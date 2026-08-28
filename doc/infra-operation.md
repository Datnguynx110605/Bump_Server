# Infra Operation

This document explains how the `infra` layer actually operates end to end: how each `app`-defined port gets a concrete implementation, how each implementation talks to its external system, and what its real runtime behavior and failure modes are. It does not re-explain the dependency rule or the composition root — see `doc/architecture.md` for that. For naming/DI-registration conventions, see `doc/project-rules.md`.

`infra` implements every port `domain`-independent `app` declares, using MongoDB, Redis, Cloudflare R2, MailKit/SMTP, BCrypt, JWT, ASP.NET Core Data Protection, and RabbitMQ.

## Core Patterns

### 1. Adapter Pattern (Ports & Adapters)

Every file in `infra/service` is a thin adapter: `<Capability>Service : I<Capability>Service`, translating the technology-agnostic contract `app` declares into calls against one specific external client library. None of them contain business logic — validation and business rules stay in `domain`; `infra` only ever transforms data and calls out. All nine are `public sealed class`: `api` names `JwtService` directly to read its claim-type constants, and the nine share one visibility rather than singling that class out. Callers still only ever hold the interface — the visibility exists for composition, not for use by type.

### 2. Configuration via raw `IConfiguration` indexing

Every service that needs configuration takes `IConfiguration` in its constructor and reads values with the string indexer (`_configuration["Jwt:SecretKey"]`) or `GetValue<T>`, using a colon-namespaced key per external system (`Jwt:*`, `R2:*`, `Email:*`, `MongoDB:*`, `Redis:*`, `RabbitMQ:*`). There is no strongly-typed `IOptions<T>` binding anywhere in `infra` — this is a consistently-applied raw-indexer convention, not a per-service inconsistency.

### 3. Generic JSON serialization for cross-cutting stores

`IRedisService` and `IRabbitMQService` both serialize an arbitrary `T` via `System.Text.Json` rather than exposing service-specific DTOs — this is what lets `OtpService`, `RegistrationService`, and every feature handler's cache-aside logic reuse the same Redis adapter for completely different payload shapes without `infra` needing to know what they are.

### 4. Singleton external clients, scoped adapters

Long-lived client objects — `IMongoClient`, `IConnectionMultiplexer`, `IAmazonS3`, `IConnection` (RabbitMQ) — are registered once as singletons in `AddInfra` and injected into `Scoped` `<Capability>Service` adapters that use them per-call. This matches each client library's own thread-safety contract: all four are documented by their respective libraries as safe to share across the app's lifetime, so only the lightweight adapter (and, for RabbitMQ, the per-call channel — see the `RabbitMQService` profile) is re-created per request.

## Service Profiles

### `BCryptService` (`IBcryptService`)

The simplest adapter in `infra`: two one-line methods wrapping the `BCrypt.Net-Next` static API directly, with no configuration and no state.

- `HashPassword(password)` → `BCrypt.Net.BCrypt.HashPassword(password)`. The output is always exactly 60 characters — this is why `domain`'s `Account.Create`/`ChangePassword` hard-code a `password.Length != 60` guard (see `doc/domain-operation.md`): that check only makes sense because this specific hasher's output length is fixed.
- `VerifyPassword(password, hash)` → `BCrypt.Net.BCrypt.Verify(password, hash)`.
- Neither method catches anything — a malformed `hash` passed to `Verify` throws `BCrypt.Net`'s own exception, which propagates uncaught to the caller.

### `CurrentIdentityService` (`ICurrentIdentityService`)

- Depends on `IHttpContextAccessor`.
- `GetAccountId()` reads the claim named by `JwtService.AccountIdClaimType` (`"accountId"`) off `HttpContext.User`, parses it as a `Guid`, and returns `null` if the claim is missing or unparsable. This directly references `JwtService`'s public constant rather than a shared constant defined independently of either service.

### `DbContext` (`IMongoDbContext`) — persistence

- Constructed from `IMongoClient` + `IConfiguration` (`MongoDB:DatabaseName`) to obtain an `IMongoDatabase`.
- `GetCollection<T>(name)` is a direct pass-through to `_database.GetCollection<T>(name)` — no repository abstraction, matching the convention already documented in `project-rules.md`.
- `DispatchEventAsync(events, cancellationToken)` loops `foreach(dynamic @event in events)` and calls a private generic `PublishEvent<TEvent>(@event, ct)`, which wraps the event in `DomainEventNotification<TEvent>` and publishes it via MediatR's `IPublisher`. The `dynamic` here is deliberate, not sloppy: `entity.Events` is typed as `IReadOnlyCollection<Event>` (the base type), but MediatR needs the event's *concrete* runtime type as the generic argument to route to the right `INotificationHandler<DomainEventNotification<TConcreteEvent>>` — a handler registered for `AccountCreatedEvent` will never be invoked for a `DomainEventNotification<Event>`. `dynamic` re-resolves the generic argument to each event's actual runtime type at the call site, which is what makes per-event-type handler routing work at all.
- There is no error handling around this loop: if any registered notification handler throws, the exception propagates out of `DispatchEventAsync` to whatever `app` handler called it — *after* the MongoDB write that produced the event has already committed.

### `EmailService` (`IEmailService`)

- Wraps MailKit's `SmtpClient`, hard-coded to Gmail's SMTP endpoint via `private const string Host = "smtp.gmail.com"` and `private const int Port = 587` — unlike every other external endpoint in `infra` (Mongo, Redis, R2, RabbitMQ), the SMTP host/port are not read from configuration.
- Reads `Email:Address` / `Email:AppPassword` from configuration for the sender address and SMTP credentials.
- `SendAsync(to, subject, htmlBody, ct)`: builds a `MimeMessage`, derives a plain-text fallback body by stripping HTML tags with a regex (`Regex.Replace(htmlBody, "<.*?>", string.Empty)`), then does one full connect → `StartTls` → authenticate → send → disconnect cycle per call — no connection pooling or client reuse across calls.
- `SendOtpRegistrationEmailAsync`/`SendWelcomeEmailAsync` both delegate to `SendAsync` with a fixed subject and an HTML body built by a private static method using C# raw string literals. `SendWelcomeEmailAsync` HTML-encodes the caller-supplied `name` (`WebUtility.HtmlEncode`) before embedding it, since a name can contain arbitrary user input; `SendOtpRegistrationEmailAsync` does not encode `code`, because that value is always server-generated (see `OtpService` below), never user input.

### `JwtService` (`IJwtService`)

- Wraps `System.IdentityModel.Tokens.Jwt` to issue HMAC-SHA256-signed access tokens.
- Reads `Jwt:SecretKey` / `Jwt:Issuer` / `Jwt:Audience` / `Jwt:ExpiryMinutes` from configuration.
- `GenerateAccessToken(accountId, role)` embeds two claims: `AccountIdClaimType` (`"accountId"`) and `RoleClaimType` (`"role"`). Both claim-type constants are `public` so `CurrentIdentityService` (see above) can reference `AccountIdClaimType`, and so `api` can bind `RoleClaimType` to the JWT bearer middleware's `TokenValidationParameters`.
- This service only *issues* tokens — there is no method anywhere on `IJwtService` to validate a token back into a `ClaimsPrincipal`. That is ASP.NET Core's JWT bearer authentication middleware's job, configured in `api` against the same four `Jwt:*` keys this service signs with.

### `OtpService` (`IOtpService`)

- Built on top of `IRedisService` rather than a raw external client directly — the only place in `infra` where one adapter is composed on top of another adapter instead of a client library.
- `Ttl = 10 minutes`, `MaxAttempts = 3` (the same "3" reused as the default retry-attempt count in `RabbitMQService`'s bounded-retry policy, chosen for that precedent).
- `GenerateAsync(identifier, ct)`: generates a random 6-digit code (`RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6")`), stores a private `OtpEntry(Code, Attempts, ExpiresAt)` record under `otp:{identifier}` in Redis with the full `Ttl`.
- `VerifyAsync(identifier, code, ct)`: fetches and deletes the entry atomically via `IRedisService.GetAndRemoveAsync<T>` (Redis `GETDEL`, one round trip), so the entry is consumed the moment it's read regardless of outcome. Returns `false` if the entry was missing or its `ExpiresAt` had already passed. On a correct code, returns `true` — the entry is already gone by this point, so verification is one-time-use, matching `RegistrationService`'s pattern below. On an incorrect code with attempts remaining, it re-`SetAsync`s the entry with `Attempts + 1` and its **remaining** TTL (not a fresh full `Ttl`), so a wrong guess never extends the attempt window; once `Attempts` would reach `MaxAttempts`, it simply isn't re-stored, so the entry stays deleted. After that point, further calls see "no entry" — indistinguishable from "code never existed" or "already expired" from the caller's side. That's a deliberate anti-enumeration property, but it also means the caller can't tell "wrong code, try again" apart from "you're locked out" without extra state of its own.

### `R2Service` (`IR2Service`)

- Wraps `AWSSDK.S3` against Cloudflare R2 (S3-API-compatible), registered in `AddInfra` with a custom `ServiceURL` pointing at R2's endpoint and `ForcePathStyle = true` (R2 requires path-style addressing, not the AWS-default virtual-hosted-style).
- Reads `R2:BucketName` / `R2:FolderName` / `R2:PublicUrl` from configuration.
- `UploadAsync(content, fileName, ct)`: generates a brand-new key per upload — `{folder}/{Guid.NewGuid()}{extension}` — discarding the caller-supplied `fileName` except for its extension, so no user-controlled path or filename ever reaches storage and no collision is possible. Returns the public URL as `{PublicUrl}/{key}`.
- `DeleteAsync(url, ct)`: takes the full URL back (the same shape `UploadAsync` returned), recovers the object key via `new Uri(url).AbsolutePath.TrimStart('/')`, and deletes by key. This round-trips the URL back into a storage key rather than requiring the caller to track the key separately — a deliberate simplification, since callers (e.g. `Account.Avatars`) only ever hold the URL string, never a separate key.

### `RabbitMQService` (`IRabbitMQService`)

A general-purpose message broker adapter.

- **Connection lifecycle**: `IConnection` is the `AddInfra` singleton (RabbitMQ.Client guarantees it's safe to share). `IChannel` is *not* thread-safe, so every `PublishAsync`/`ConsumeAsync` call opens its own channel: `PublishAsync` disposes its channel immediately after use (`using`); `ConsumeAsync`'s channel stays open for the lifetime of its long-running consume loop and is closed on cancellation.
- **`PublishAsync<T>(queueName, message, ct)`**: declares `queueName` as a durable queue (idempotent — safe to call repeatedly), serializes `message` to JSON, and publishes with `Persistent = true` so the message survives a broker restart while it sits in the queue.
- **`ConsumeAsync<T>(queueName, getIdempotencyKey, onMessage, ct)`**: declares both `queueName` and its dead-letter queue `{queueName}.dlq` (also durable), sets QoS to `prefetchCount: 1` (one unacknowledged message per consumer at a time), and registers an `AsyncEventingBasicConsumer`. For every delivered message:
  1. Deserialize to `T`. If deserialization fails (`null`), the raw body is republished to `{queueName}.dlq` unchanged and the original is acked — a malformed message is never retried, since retrying can't fix a shape it will never parse.
  2. Compute `payloadHash = SHA256(rawBody)` and `idempotencyKey = getIdempotencyKey(message)`, looked up in Redis as `rabbitmq:idempotency:{queueName}:{idempotencyKey}`.
  3. **Three idempotency outcomes**, matching the design confirmed for this service:
     - **No stored hash** → never seen this key before; proceed to step 4.
     - **Stored hash equals `payloadHash`** → genuine duplicate delivery of the same message (at-least-once redelivery, not a new operation) → skip `onMessage` entirely, ack immediately.
     - **Stored hash differs from `payloadHash`** → the same idempotency key was reused for different content — a conflict, not a retry. This is rejected outright: routed straight to `{queueName}.dlq` without incrementing the retry counter or calling `onMessage`, since retrying an integrity conflict cannot resolve it.
  4. Call `onMessage(message, ct)`. On success: store `payloadHash` at the idempotency key with a 24-hour TTL, then ack. On any exception: read the current retry count from the `x-retry-count` message header (default `0`); if the next attempt would reach `MaxAttempts` (3), republish to `{queueName}.dlq`, otherwise republish to `queueName` with the header incremented — either way, ack the original delivery so it's removed from the source queue (the republished copy, with its updated header, is what gets redelivered next).
- A message routed to `{queueName}.dlq` sits there for manual/ops inspection — there is no automatic reprocessing or alerting built into `RabbitMQService` itself.

### `RedisService` (`IRedisService`)

- Wraps `StackExchange.Redis`'s `IConnectionMultiplexer`/`IDatabase`. Fully generic: `SetAsync<T>`/`GetAsync<T>` serialize/deserialize via `System.Text.Json`; `RemoveAsync` deletes by key; `GetAndRemoveAsync<T>` does both atomically via Redis's `GETDEL` (`StringGetDeleteAsync`) in one round trip, so a value can only ever be read by one caller.
- `ttl` is `TimeSpan?` — `null` means no expiration is set (StackExchange.Redis's default `Expiration.Default` behavior) rather than an explicit one.
- This is the most-reused adapter in `infra`: `OtpService`, `RegistrationService`, `RabbitMQService`'s idempotency store, and every feature handler's cache-aside/write-through/invalidate-on-write logic (already documented in `project-rules.md`) all sit on top of this one service.

### `RegistrationService` (`IRegistrationService`)

Combines two `infra` mechanisms: ASP.NET Core's `IDataProtectionProvider` (to encrypt the email carried inside the token) and `IRedisService` (to give the token a server-side existence, expiry, and one-time-use guarantee).

- `GenerateTokenAsync(email, ct)`: creates a random 32-byte URL-safe token (`WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))`), encrypts `email` with a `DataProtector` created for the purpose string `"RegistrationToken"` (Data Protection's purpose isolation means only a protector created with this exact purpose string can decrypt what it encrypts), stores the encrypted email under `registration:{token}` in Redis with a 10-minute TTL, and returns the raw token.
- `ExtractEmailAsync(registrationToken, ct)`: looks up and deletes the Redis entry in one atomic `GetAndRemoveAsync` call (see `RedisService` above), returning `null` if it was absent (token never existed or already expired) — the entry is consumed either way, since a token that fails to decrypt is just as dead as one that never existed. If a value was found, it attempts to `Unprotect` — catching `CryptographicException` specifically (e.g. if the Data Protection key ring has rotated past the key that encrypted this value) and treating that as "no email" rather than letting the exception propagate. On success, returns the plaintext email. Only one caller can ever see a non-null value for a given token, since the read and the delete are one atomic operation.
- Why two layers instead of Redis alone: the Redis entry alone already prevents reuse (deleted on first successful read) and already expires — the `DataProtector` encryption on top is specifically about never storing a plaintext email in Redis, defense in depth against a Redis-only compromise leaking user emails.

## Persistence Configuration (`infra/persistence/configuration`)

`domain` takes no package references, so it carries no BSON attributes: nothing in `Account.cs` says that `Id` is `_id`, that `Email` is a subdocument, or that `Avatars` is backed by a private list. Every one of those decisions is declared here instead, in two static entry points called once at startup — `BsonConfiguration.Register()`, called directly by `AddInfra` while the container is still being built, and `IndexConfiguration.CreateAsync(...)`, called by the `IndexInitializer` hosted service the host awaits during start-up.

### `BsonConfiguration` — how a domain object becomes a document

`Register()` does four things, in order:

1. **Guid representation** — registers `GuidSerializer(GuidRepresentation.Standard)`, so every `Guid` is written as BSON binary subtype 4 rather than the driver's legacy subtype 3. This must be explicit; it decides the on-disk layout of every `_id` in the database.
2. **Value objects** — `Amount`, `Email` and `Name` each get a class map that maps the read-only `Value` property and supplies a creator delegate calling the domain factory (`map.MapCreator(new Func<string, Name>(value => Name.Create(value).Value!), nameof(Name.Value))`). A value object therefore round-trips **through its own validation**: nothing enters the domain from the database without passing `Name.Create`.
3. **Entities** — `BaseEntity` is mapped first (so `Id` becomes `_id` once, for all six aggregates, and `IgnoreExtraElements` is inherited), then each aggregate auto-maps its own members and explicitly maps the private list behind each read-only collection: `map.MapField("_avatars").SetElementName(nameof(Account.Avatars))`. The element name is deliberately the *property* name, not the field name, because `EmbeddedArrayPaginator` builds its aggregation stages from raw member-name strings (`"Followers"`, `"Reactions"`) — the mapped element name and the C# property name have to agree or that pipeline silently matches nothing.
4. **Backing-field serializers** — see below.

Consequences worth knowing before changing anything:

- **A value object is a subdocument, not a scalar**: `{ "Name": { "Value": "someone" } }`. This is forced by existing query code, not a stylistic choice — `Regex(x => x.Name.Value, …)`, `Inc(x => x.FollowerAmount.Value, 1)` and `SortByDescending(x => x.Rate.Value)` all address the nested path, and they can only be translated if `Value` is a real mapped member. `Eq(x => x.Name, name)` keeps working alongside them, as a whole-subdocument equality match.
- **`Events` never persists.** `BaseEntity.Events` is read-only and `_events` is private and unmapped, so raised-but-undispatched events are not written to MongoDB and do not survive a round trip.
- **Timestamps lose sub-millisecond precision.** BSON dates are milliseconds since epoch, so a `CreatedAt` read back from MongoDB is a truncation of the `DateTime.UtcNow` that was written. Cursor pagination is unaffected: `CursorCodec` encodes a value that was itself read from a document, so the cursor and the stored value are truncated identically.
- **`Role` is stored as its integer value** (`Role.User` → `0`), the driver's default enum representation. Reordering the `Role` enum would silently reinterpret existing rows.
- **`Account.SavePosts`/`SaveShops` embed whole `Post`/`Shop` documents**, because that is what the domain model declares. They are mapped for fidelity — no use case reads or writes them yet.

### `BackingFieldBsonSerializer<TEntity>` — why a wrapper serializer exists

Mapping the private field solves persistence but breaks queries. When the driver translates `AnyEq(x => x.Avatars.Select(a => a.Url), url)` (`ChangeAvatarHandler`, `RemoveAvatarHandler`, `FollowService`, `ReactionService`) or `Include(x => x.Images)` (`PostProjection`), it asks the document serializer for a member literally named `Avatars` — and the class map knows that member as `_avatars`, so the lookup fails with `ExpressionNotSupportedException: Expression not supported: x.Avatars`.

`BackingFieldBsonSerializer<TEntity>` closes that gap without touching a single handler: it delegates every serialization concern to a `BsonClassMapSerializer<TEntity>` and extends only `TryGetMemberSerializationInfo`, falling back to a lookup **by element name** when the lookup by member name misses. `Avatars` then resolves to the `_avatars` member map, and the query renders as `{ "Avatars.Url": … }`. It is registered for the three aggregates that actually have such properties — `Account`, `Comment`, `Post`. An entity that later gains a read-only collection property needs the same registration, or its queries will fail at translation time rather than compile time.

### `IndexConfiguration` — index strategy

`CreateAsync` resolves the database from the singleton `IMongoClient` plus `MongoDB:DatabaseName` and issues one `CreateManyAsync` per collection. `createIndexes` is idempotent for an identical specification, so this runs safely on every boot — but it does mean **the process reaches MongoDB during startup and fails fast if the database is unreachable**.

Every list index ends in `CreatedAt: -1, _id: -1`, in that order, so its trailing keys match `CursorPaginator`'s `SortByDescending(CreatedAt).ThenByDescending(Id)` exactly — the shape a keyset page needs if the sort is to be answered from the index rather than in memory. The index keys and the rendered query shapes have been verified to agree; the plans MongoDB actually chooses have not been measured against a live server, so treat the "Serves" column as intent, and confirm with `explain()` before relying on any of it under load.

| Collection | Keys | Options | Serves |
|---|---|---|---|
| `account` | `Email` | unique, partial `IsDeleted: false` | `GetAccountByEmail`, `LoginHandler`, `SendEmailForRegister`/`SendEmailForVerify` |
| `account` | `Name, IsDeleted, CreatedAt, _id` | | `GetAccountByName` |
| `account` | `IsDeleted, CreatedAt, _id` | | `GetAllAccount` |
| `post` | `AccountId, IsDeleted, CreatedAt, _id` | | `GetMyPost`, `GetPostByAccount` |
| `post` | `ShopId, IsDeleted, CreatedAt, _id` | | `GetPostByShop` |
| `post` | `IsDeleted, CreatedAt, _id` | | `GetLatestPost` |
| `post` | `IsDeleted, ReactionAmount.Value, CreatedAt, _id` | | `GetMostReactedPost` |
| `comment` | `PostId, IsDeleted, CreatedAt, _id` | | `GetCommentByPost` |
| `comment` | `AccountId, IsDeleted, CreatedAt, _id` | | `GetMyCommentHistory` |
| `shop` | `Name, Address` | unique, partial `IsDeleted: false` | `CreatePostHandler`'s resolve-or-create lookup |
| `shop` | `Name, IsDeleted, CreatedAt, _id` | | `GetShopByName` |
| `shop` | `Address, IsDeleted, CreatedAt, _id` | | `GetShopByAddress` |
| `shop` | `IsDeleted, CreatedAt, _id` | | `GetAllShop` |
| `shop` | `IsDeleted, Rate.Value, CreatedAt, _id` | | `GetMostRatedShop` |
| `report` | `IsDeleted, CreatedAt, _id` | | `GetAllReport` |
| `report` | `PostId` / `CommentId` / `AccountId` / `ReporterId`, each + `IsDeleted, CreatedAt, _id` | | `GetReportByTarget`, one index per `ReportTarget` branch |
| `refreshtoken` | `Token` | unique | `RequestNewAccessTokenHandler` |
| `refreshtoken` | `ExpiryDate` | TTL, `expireAfterSeconds: 0` | expired-token cleanup |
| `refreshtoken` | `IsDeleted, CreatedAt, _id` | | `GetAllRefreshToken` |

The unique and TTL indexes change runtime behavior, deliberately:

- **The unique indexes are partial, filtered to `IsDeleted: false`.** A plain unique index would make a soft delete permanent: no one could ever re-register a deleted account's email, and `CreatePostHandler` would throw a duplicate-key error the first time someone posted about a shop that had been deleted (its resolve-or-create lookup filters on `IsDeleted: false`, so it would try to insert a second row). Filtering the constraint to live documents matches what every handler already queries. The trade-off is that a partial index is only usable by a query whose predicate implies the filter — every affected handler does filter `IsDeleted == false`, so the index is expected to serve them, but that is a planner decision to confirm with `explain()` rather than something the code guarantees.
- **Duplicate keys surface as unhandled exceptions.** `CreateAccountHandler` and `CreatePostHandler` check-then-insert and do not catch `MongoWriteException`, so the index closes the race between the check and the insert by failing the request rather than by returning a `Result.Fail`. That is the intended trade: a rejected duplicate beats a silently duplicated account.
- **The TTL index deletes expired refresh tokens.** MongoDB's background task removes any `RefreshToken` once `ExpiryDate` passes, so `GetAllRefreshToken` returns live tokens only and expired rows never accumulate. Revoked-but-unexpired tokens stay until their expiry.

Three query shapes are deliberately left unindexed:

- **The `GetXByKeyword` handlers** (`account`, `post`, `shop`) filter with an unanchored, case-insensitive `BsonRegularExpression`, which no B-tree index can serve — MongoDB would scan regardless. They fall back to the collection scan bounded by their `IsDeleted, CreatedAt, _id` index. A text index would be a different feature (word matching, not substring matching), not a drop-in speed-up.
- **The embedded-array queries** (`follow` and `reaction` features, plus `FollowService`/`ReactionService`'s `AnyEq` guards) always match their parent by `_id` first, which the default index already covers; the `$unwind`/`$sort` happens on a single document's array.
- **`Shop` name+address lookup has no dedicated non-unique index** — the unique partial index above already answers it.

### `IndexInitializer` — when index creation runs

`CreateAsync` is asynchronous, and DI registration cannot await it, so `AddInfra` registers `IndexInitializer` — an `IHostedService` holding the singleton `IMongoClient` and `IConfiguration` — instead of calling it inline. The host awaits `StartAsync` during start-up, before the server begins accepting requests, so every index is declared by the time the first handler runs. `StopAsync` does nothing. Because start-up awaits it, an unreachable MongoDB surfaces as a failure to start the host rather than as a failure on the first request.

## Queue Consumers (`infra/queue`)

`FollowService` and `ReactionService` implement the ports in `app/common/interface/queue` and are written to be driven by `RabbitMQService.ConsumeAsync`, not called directly from a request. Three consequences follow from that, none of which are obvious from reading the classes on their own.

**They take every identifier as a parameter and depend on no ambient identity.** Neither service injects `ICurrentIdentityService`: it resolves the account from `HttpContext.User`, and a consume loop has no `HttpContext`, so it would return `null` for every message. The caller — a request handler, or the consumer wiring — resolves the acting account and passes it in explicitly (`ProcessFollowAsync(followerId, followingId, ct)`).

**A failure that should be retried must throw; a failure that should be accepted returns `Result.Fail`.** `ConsumeAsync`'s `onMessage` delegate is `Func<T, CancellationToken, Task>` — it has no `Result` in its signature, and the retry/dead-letter logic keys off an *exception*. A normal return, including one that produced a failed `Result`, is treated as success: the payload hash is stored and the message is acked. So the two kinds of failure are deliberately signalled differently:

- **Terminal** — the operation is a genuine duplicate or is not applicable (`MatchedCount == 0`: already following, not following, already reacted). Retrying cannot change the outcome, so these return `Result.Fail` and the message is acked.
- **Retryable** — a multi-document `BulkWriteAsync` applied to only one of its two target documents (`MatchedCount == 1`). A follow/unfollow spans two `Account` documents and the batch is *not* atomic, so this leaves the relationship asymmetric. These throw `InvalidOperationException`, which is what lets the consumer redeliver. The `AnyEq` / `Not(AnyEq)` guards on each side make every operation idempotent, so a redelivery repairs the half-applied write rather than double-applying it.

`ReactionService` has no retryable case: each of its four methods is a single `UpdateOneAsync` against one document, which is atomic, so there is no partial state to repair and `MatchedCount == 0` is always terminal.

**Follow and unfollow must not share a queue.** The idempotency entry is keyed `rabbitmq:idempotency:{queueName}:{idempotencyKey}`. Two operations on the same pair of accounts naturally produce the same idempotency key, so if they also share a queue the second one arrives with a matching key but a different payload hash — which `RabbitMQService` treats as an integrity conflict and routes straight to the dead-letter queue without retrying. Separate queues namespace the key apart; sharing one requires folding the operation name into the idempotency key.

**Consumers must open a scope per message.** `IMongoDbContext` and `IRedisService` are registered `AddScoped`, while a consume loop lives for the lifetime of the process. Injecting these services directly into a long-running consumer captures a single scope forever; the consumer should take `IServiceScopeFactory` and resolve the queue service inside a fresh scope for each message.

## Configuration Keys

| Service | Configuration keys read |
|---|---|
| `BCryptService` | none |
| `CurrentIdentityService` | none (reads `HttpContext`, not configuration) |
| `DbContext` | `MongoDB:DatabaseName` |
| `IndexConfiguration` | `MongoDB:DatabaseName` (read directly, not via `DbContext`, since index creation runs before any scoped service exists) |
| `EmailService` | `Email:Address`, `Email:AppPassword` (SMTP host/port are hard-coded, not configured) |
| `JwtService` | `Jwt:SecretKey`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes` |
| `OtpService` | none directly (delegates to `IRedisService`) |
| `R2Service` | `R2:BucketName`, `R2:FolderName`, `R2:PublicUrl` (plus `R2:AccountId`/`R2:AccessKeyId`/`R2:SecretAccessKey`, read in `AddInfra` when constructing the singleton `IAmazonS3`) |
| `RabbitMQService` | none directly (`RabbitMQ:ConnectionString` is read in `AddInfra` when constructing the singleton `IConnection`) |
| `RedisService` | none directly (`Redis:ConnectionString` is read in `AddInfra` when constructing the singleton `IConnectionMultiplexer`) |
| `RegistrationService` | none directly (delegates to `IRedisService` and ASP.NET Core's Data Protection, configured via `AddDataProtection()` in `AddInfra`) |

None of these keys appear in `appsettings.json`/`appsettings.Development.json` — they're read from .NET User Secrets or host configuration per environment (see `doc/architecture.md`).
