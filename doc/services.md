# Infrastructure Services

Interfaces live in `src/app/common/interface/service/`, implementations in
`src/infra/service/`. All are registered `AddScoped` in `src/api/Program.cs`.

## IBcryptService

- `string HashPassword(string password)`
- `bool VerifyPassword(string password, string hash)`

Wraps `BCrypt.Net-Next`. Output is the standard 60-char bcrypt hash, matching
`Account.Create`'s `password.Length != 60` check. No config.

## ICurrentIdentityService

- `Guid? GetAccountId()`

Reads the `JwtService.AccountIdClaimType` ("accountId") claim off
`IHttpContextAccessor.HttpContext.User`, returns `null` if missing or
unparseable. No config. Note: nothing currently wires JWT bearer
authentication into the request pipeline, so `HttpContext.User` is never
populated yet — this service is inert until that middleware exists.

## IJwtService

- `string GenerateAccessToken(Guid accountId)`

Signs an HS256 JWT with a single custom claim, `accountId` (deliberately not
`ClaimTypes.NameIdentifier` — ASP.NET Core's inbound/outbound claim type maps
don't touch plain custom claim names, so the value round-trips regardless of
which handler validates the token later). No token *validation* method here;
that's the bearer middleware's job once it exists.

Config: `Jwt:SecretKey`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes`.

## IOtpService

- `Task<string> GenerateAsync(string identifier, CancellationToken ct)`
- `Task<bool> VerifyAsync(string identifier, string code, CancellationToken ct)`

6-digit crypto-random code, stored via `IRedisService` under
`otp:{identifier}` with a 10-minute TTL and a 3-attempt cap. Correct code or
3rd wrong attempt deletes the entry (single use / lockout). Wrong-but-not-final
attempts re-set with the *remaining* TTL, not a fresh one, so retries can't
extend the window. `identifier` is caller-defined (email, phone, etc.) — the
service doesn't assume which. No config (TTL/attempt cap are constants).

## IR2Service

- `Task<string> UploadAsync(Stream content, string fileName, CancellationToken ct)`
- `Task DeleteAsync(string url, CancellationToken ct)`

Cloudflare R2 (S3-compatible) via `IAmazonS3`. Upload generates its own key
(`{FolderName}/{Guid}{extension}`) and returns the public URL; delete recovers
the key from the URL's path. Caller is responsible for persisting the
returned URL (that's what `DeleteAsync` is later called with).

Config: `R2:AccountId`, `R2:AccessKeyId`, `R2:SecretAccessKey`,
`R2:BucketName`, `R2:FolderName`, `R2:PublicUrl`.

## IRedisService

- `Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken ct)`
- `Task<T?> GetAsync<T>(string key, CancellationToken ct)`
- `Task RemoveAsync(string key, CancellationToken ct)`

Generic JSON (`System.Text.Json`) get/set/remove over `IConnectionMultiplexer`.
`ttl: null` means no expiry — the service applies no default.

Config: `Redis:ConnectionString`.

## IEmailService

- `Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)`
- `Task SendOtpRegistrationEmailAsync(string to, string code, CancellationToken ct)`
- `Task SendWelcomeEmailAsync(string to, string name, CancellationToken ct)`

SMTP via Gmail (MailKit). Host/port (`smtp.gmail.com:587`, STARTTLS) are
hardcoded — fixed for Gmail, no reason to make them configurable. The two
`SendXxxEmailAsync` methods build their HTML body from private static
templates and delegate to `SendAsync`; `SendAsync` itself adds a plain-text
fallback (naive tag-strip) alongside the HTML part.

Config: `Email:Address` (also the SMTP username), `Email:AppPassword`
(Google App Password — requires 2-Step Verification on that account, not the
regular account password).

Operational notes: connects/authenticates/disconnects on every call (no
pooling) — fine for low volume, but a handler calling this inline pays a
few-hundred-ms-plus round trip per send. Free Gmail SMTP caps around 500
sends/day.

## IRegistrationService

- `Task<string> GenerateTokenAsync(string email, CancellationToken ct)`
- `Task<string?> ExtractEmailAsync(string registrationToken, CancellationToken ct)`

Issued after OTP verification succeeds, consumed when creating the account.
The token itself is an opaque, Base64Url-encoded random value (not
self-describing); the email is Data-Protection-encrypted
(`IDataProtector`, purpose `"RegistrationToken"`) and stored via
`IRedisService` under `registration:{token}` with a 10-minute TTL — Redis TTL
is the only expiry mechanism (no time-limited protector layered on top).
`ExtractEmailAsync` deletes the entry once the email is successfully
decrypted (single use). No config.

Deployment note: Data Protection's default key storage is the local
filesystem, per-machine. A multi-instance or containerized deployment needs
shared key storage (e.g. `PersistKeysToStackExchangeRedis`) or tokens that
Redis still holds valid will fail to decrypt on an instance that didn't mint
them — not yet configured, since no deployment target is chosen yet.

## IMongoDbContext

- `IMongoCollection<T> GetCollection<T>(string name)`
- `Task DispatchEventAsync(IEnumerable<Event> events, CancellationToken ct)`

`GetCollection<T>` is a thin pass-through to `IMongoDatabase.GetCollection<T>()`
— no repository layer; handlers are meant to work with `IMongoCollection<T>`
directly (per the outbox design draft in `docs/superpowers/specs/`, though
this simpler `DispatchEventAsync` publishes events directly through MediatR
rather than persisting them to an outbox collection first — no transactional
guarantee between the entity write and the dispatch, unlike the draft).
This is the first persistence dependency `app.csproj` has taken on
(`MongoDB.Driver`), deliberately — `GetCollection<T>`'s return type requires it.

`DispatchEventAsync` wraps each `Event` in `DomainEventNotification<TEvent>`
(`app/common/event/DomainEventNotification.cs`, `TEvent : Event`,
`INotification`) and publishes it via MediatR's `IPublisher`. Binding is
runtime-based (`foreach (dynamic @event in events)`) so the generic parameter
resolves to the event's *concrete* type — `Account.Create` raising
`AccountCreatedEvent` publishes `DomainEventNotification<AccountCreatedEvent>`,
never `DomainEventNotification<Event>`. **A handler must implement that exact
closed generic** (`INotificationHandler<DomainEventNotification<AccountCreatedEvent>>`)
to receive it — MediatR publishing to zero matching handlers is a silent
no-op, not an error. `Program.cs` now calls `AddMediatR(...)` (the project's
first runtime MediatR wiring) scanning the `app` assembly for handlers,
including the still-empty `AccountCreatedEventHandler` stub. Also note
MediatR's default publisher awaits handlers sequentially and stops at the
first one that throws, so one failing handler can block later handlers for
the same event.

Not yet handled (flagging for whoever wires up the write path): `BaseEntity.ClearEvent()`
is `protected`, so nothing outside the aggregate itself can clear `Events`
after a successful dispatch — re-dispatching the same aggregate instance
would republish every event it ever raised. Fixing that touches committed
domain code (`src/domain/core/entity/BaseEntity.cs`), which needs the
refactor-approval gate before changing.

`DbContext` is registered `AddScoped`, same as the other services, even
though `IMongoClient`/`IMongoDatabase` are themselves thread-safe and
typically singletons — the wrapper just delegates to the already-singleton
`IMongoClient`, so the scoped registration costs nothing.

Config: `MongoDB:ConnectionString`, `MongoDB:DatabaseName`.
