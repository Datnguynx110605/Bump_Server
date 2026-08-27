# Infra Operation

This document explains how the `infra` layer actually operates end to end: how each `app`-defined port gets a concrete implementation, how each implementation talks to its external system, and what its real runtime behavior and failure modes are. It does not re-explain the dependency rule or the composition root (`Program.cs` wiring) — see `doc/architecture.md` for that. For naming/DI-registration conventions, see `doc/project-rules.md`.

`infra` implements every port `domain`-independent `app` declares, using MongoDB, Redis, Cloudflare R2, MailKit/SMTP, BCrypt, JWT, ASP.NET Core Data Protection, and RabbitMQ.

## Core Patterns

### 1. Adapter Pattern (Ports & Adapters)

Every file in `infra/service` is a thin adapter: `<Capability>Service : I<Capability>Service`, translating the technology-agnostic contract `app` declares into calls against one specific external client library. None of them contain business logic — validation and business rules stay in `domain`; `infra` only ever transforms data and calls out. All ten are `internal sealed class`, consistent with `project-rules.md`'s encapsulation rule (an implementation detail invoked only through the interface, never referenced by type from outside `infra`).

### 2. Configuration via raw `IConfiguration` indexing

Every service that needs configuration takes `IConfiguration` in its constructor and reads values with the string indexer (`_configuration["Jwt:SecretKey"]`) or `GetValue<T>`, using a colon-namespaced key per external system (`Jwt:*`, `R2:*`, `Email:*`, `MongoDB:*`, `Redis:*`, `RabbitMQ:*`). There is no strongly-typed `IOptions<T>` binding anywhere in `infra` — this is a consistently-applied raw-indexer convention, not a per-service inconsistency.

### 3. Generic JSON serialization for cross-cutting stores

`IRedisService` and `IRabbitMQService` both serialize an arbitrary `T` via `System.Text.Json` rather than exposing service-specific DTOs — this is what lets `OtpService`, `RegistrationService`, and every feature handler's cache-aside logic reuse the same Redis adapter for completely different payload shapes without `infra` needing to know what they are.

### 4. Singleton external clients, scoped adapters

Long-lived client objects — `IMongoClient`, `IConnectionMultiplexer`, `IAmazonS3`, `IConnection` (RabbitMQ) — are registered once as singletons in `Program.cs` and injected into `Scoped` `<Capability>Service` adapters that use them per-call. This matches each client library's own thread-safety contract: all four are documented by their respective libraries as safe to share across the app's lifetime, so only the lightweight adapter (and, for RabbitMQ, the per-call channel — see the `RabbitMQService` profile) is re-created per request.

## Service Profiles

### `BCryptService` (`IBcryptService`)

The simplest adapter in `infra`: two one-line methods wrapping the `BCrypt.Net-Next` static API directly, with no configuration and no state.

- `HashPassword(password)` → `BCrypt.Net.BCrypt.HashPassword(password)`. The output is always exactly 60 characters — this is why `domain`'s `Account.Create`/`ChangePassword` hard-code a `password.Length != 60` guard (see `doc/domain-operation.md`): that check only makes sense because this specific hasher's output length is fixed.
- `VerifyPassword(password, hash)` → `BCrypt.Net.BCrypt.Verify(password, hash)`.
- Neither method catches anything — a malformed `hash` passed to `Verify` throws `BCrypt.Net`'s own exception, which propagates uncaught to the caller.

### `CurrentIdentityService` (`ICurrentIdentityService`)

- Depends on `IHttpContextAccessor`.
- `GetAccountId()` reads the claim named by `JwtService.AccountIdClaimType` (`"accountId"`) off `HttpContext.User`, parses it as a `Guid`, and returns `null` if the claim is missing or unparsable. This directly references `JwtService`'s public constant rather than a shared constant defined independently of either service — a small `infra`-internal coupling between two adapters.

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
- `GenerateAccessToken(accountId, role)` embeds two claims: `AccountIdClaimType` (`"accountId"`) and `RoleClaimType` (`"role"`). Both claim-type constants are `public` so `CurrentIdentityService` (see above) can reference `AccountIdClaimType` directly.
- This service only *issues* tokens — there is no method anywhere on `IJwtService` to validate a token back into a `ClaimsPrincipal`. That is ASP.NET Core's JWT bearer authentication middleware's job.

### `OtpService` (`IOtpService`)

- Built on top of `IRedisService` rather than a raw external client directly — the only place in `infra` where one adapter is composed on top of another adapter instead of a client library.
- `Ttl = 10 minutes`, `MaxAttempts = 3` (the same "3" reused as the default retry-attempt count in `RabbitMQService`'s bounded-retry policy, chosen for that precedent).
- `GenerateAsync(identifier, ct)`: generates a random 6-digit code (`RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6")`), stores a private `OtpEntry(Code, Attempts, ExpiresAt)` record under `otp:{identifier}` in Redis with the full `Ttl`.
- `VerifyAsync(identifier, code, ct)`: returns `false` if the entry is missing or its `ExpiresAt` has passed. On a correct code, it deletes the entry and returns `true` — verification is one-time-use, matching `RegistrationService`'s pattern below. On an incorrect code, it increments `Attempts` and re-stores the entry with its **remaining** TTL (not a fresh full `Ttl`), so a wrong guess never extends the attempt window; once `Attempts` reaches `MaxAttempts`, the entry is deleted outright. After that point, further calls see "no entry" — indistinguishable from "code never existed" or "already expired" from the caller's side. That's a deliberate anti-enumeration property, but it also means the caller can't tell "wrong code, try again" apart from "you're locked out" without extra state of its own.

### `R2Service` (`IR2Service`)

- Wraps `AWSSDK.S3` against Cloudflare R2 (S3-API-compatible), registered in `Program.cs` with a custom `ServiceURL` pointing at R2's endpoint and `ForcePathStyle = true` (R2 requires path-style addressing, not the AWS-default virtual-hosted-style).
- Reads `R2:BucketName` / `R2:FolderName` / `R2:PublicUrl` from configuration.
- `UploadAsync(content, fileName, ct)`: generates a brand-new key per upload — `{folder}/{Guid.NewGuid()}{extension}` — discarding the caller-supplied `fileName` except for its extension, so no user-controlled path or filename ever reaches storage and no collision is possible. Returns the public URL as `{PublicUrl}/{key}`.
- `DeleteAsync(url, ct)`: takes the full URL back (the same shape `UploadAsync` returned), recovers the object key via `new Uri(url).AbsolutePath.TrimStart('/')`, and deletes by key. This round-trips the URL back into a storage key rather than requiring the caller to track the key separately — a deliberate simplification, since callers (e.g. `Account.Avatars`) only ever hold the URL string, never a separate key.

### `RabbitMQService` (`IRabbitMQService`)

A general-purpose message broker adapter.

- **Connection lifecycle**: `IConnection` is the `Program.cs` singleton (RabbitMQ.Client guarantees it's safe to share). `IChannel` is *not* thread-safe, so every `PublishAsync`/`ConsumeAsync` call opens its own channel: `PublishAsync` disposes its channel immediately after use (`using`); `ConsumeAsync`'s channel stays open for the lifetime of its long-running consume loop and is closed on cancellation.
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

- Wraps `StackExchange.Redis`'s `IConnectionMultiplexer`/`IDatabase`. Fully generic: `SetAsync<T>`/`GetAsync<T>` serialize/deserialize via `System.Text.Json`; `RemoveAsync` deletes by key.
- `ttl` is `TimeSpan?` — `null` means no expiration is set (StackExchange.Redis's default `Expiration.Default` behavior) rather than an explicit one.
- This is the most-reused adapter in `infra`: `OtpService`, `RegistrationService`, `RabbitMQService`'s idempotency store, and every feature handler's cache-aside/write-through/invalidate-on-write logic (already documented in `project-rules.md`) all sit on top of this one service.

### `RegistrationService` (`IRegistrationService`)

Combines two `infra` mechanisms: ASP.NET Core's `IDataProtectionProvider` (to encrypt the email carried inside the token) and `IRedisService` (to give the token a server-side existence, expiry, and one-time-use guarantee).

- `GenerateTokenAsync(email, ct)`: creates a random 32-byte URL-safe token (`WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))`), encrypts `email` with a `DataProtector` created for the purpose string `"RegistrationToken"` (Data Protection's purpose isolation means only a protector created with this exact purpose string can decrypt what it encrypts), stores the encrypted email under `registration:{token}` in Redis with a 10-minute TTL, and returns the raw token.
- `ExtractEmailAsync(registrationToken, ct)`: looks up the Redis entry, returning `null` if absent (token never existed or already expired). If found, it attempts to `Unprotect` — catching `CryptographicException` specifically (e.g. if the Data Protection key ring has rotated past the key that encrypted this value) and treating that as "no email" rather than letting the exception propagate. On success, it **deletes** the Redis entry (one-time use, the same pattern `OtpService` uses on successful verification) and returns the plaintext email.
- Why two layers instead of Redis alone: the Redis entry alone already prevents reuse (deleted on first successful read) and already expires — the `DataProtector` encryption on top is specifically about never storing a plaintext email in Redis, defense in depth against a Redis-only compromise leaking user emails.

## Configuration Keys

| Service | Configuration keys read |
|---|---|
| `BCryptService` | none |
| `CurrentIdentityService` | none (reads `HttpContext`, not configuration) |
| `DbContext` | `MongoDB:DatabaseName` |
| `EmailService` | `Email:Address`, `Email:AppPassword` (SMTP host/port are hard-coded, not configured) |
| `JwtService` | `Jwt:SecretKey`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes` |
| `OtpService` | none directly (delegates to `IRedisService`) |
| `R2Service` | `R2:BucketName`, `R2:FolderName`, `R2:PublicUrl` (plus `R2:AccountId`/`R2:AccessKeyId`/`R2:SecretAccessKey`, read in `Program.cs` when constructing the singleton `IAmazonS3`) |
| `RabbitMQService` | none directly (`RabbitMQ:ConnectionString` is read in `Program.cs` when constructing the singleton `IConnection`) |
| `RedisService` | none directly (`Redis:ConnectionString` is read in `Program.cs` when constructing the singleton `IConnectionMultiplexer`) |
| `RegistrationService` | none directly (delegates to `IRedisService` and ASP.NET Core's Data Protection, configured via `AddDataProtection()` in `Program.cs`) |

None of these keys appear in `appsettings.json`/`appsettings.Development.json` — they're read from .NET User Secrets or host configuration per environment (see `doc/architecture.md`).
