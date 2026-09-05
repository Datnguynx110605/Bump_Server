# System Design

How the running components fit together and what happens at runtime. Layering rules are in
[`architecture.md`](./architecture.md); per-feature business rules are in
[`features.md`](./features.md).

## Components

### `api` — HTTP and WebSocket surface

| Component | File | Responsibility |
|---|---|---|
| 12 controllers | `src/api/controller/` | Bind HTTP, construct value objects, send a MediatR request, convert `Result` to `ActionResult`. No business logic. |
| `ResultExtensions` | `src/api/common/ResultExtensions.cs` | `Result` → `OkObjectResult` / `NoContentResult` / `AcceptedResult`; failures → `ProblemDetails`. More than one error becomes a `ValidationProblemDetails` (400). |
| `ResultStatusMapper` | `src/api/common/ResultStatusMapper.cs` | Reads the `status` metadata key off the first `IError`; defaults to 400. |
| `ImageUpload` | `src/api/common/ImageUpload.cs` | Upload trust boundary: 5 MB cap, 10-file cap, `.jpg/.jpeg/.png/.webp` allowlist, and magic-byte signature check (JPEG `FF D8 FF`, PNG signature, `RIFF`/`WEBP`). |
| `CorrelationIdMiddleware` | `src/api/middleware/` | Accepts inbound `X-Correlation-ID` if ≤64 chars matching `^[a-zA-Z0-9-]+$`, else generates a GUID. Echoes it and pushes it into the Serilog `LogContext`. |
| `PerformanceMiddleware` | `src/api/middleware/` | Logs a warning for any request over 500 ms. |
| `RateLimiterPolicy` | `src/api/common/RateLimiterPolicy.cs` | Policy name constants: `auth`, `email`, `search`, `upload`. |

### `app` — use cases

78 MediatR handlers under `src/app/feature/<area>/<command|query>/<Name>/`, each a
`Command`/`Query` record + `Handler` + optional `Validator` (34 of 72 have one). Three pipeline
behaviours run in registration order (`src/app/DependencyInjection.cs`):

1. `LoggingBehavior` — logs request name, then success or the joined error messages.
2. `PerformanceBehavior` — warns above 500 ms.
3. `ValidationBehavior` — runs all `IValidator<TRequest>` in parallel; on failure constructs
   `TResponse` via `Activator.CreateInstance` and pushes `AppError.BadRequest` errors into it,
   short-circuiting the handler.

Shared helpers: `CursorPaginator` and `EmbeddedArrayPaginator` (raw paging), their `Cached*`
wrappers, `CacheKey`, `ListCache`, `AppError`, `PostFeedEnricher`, `ReactionMembership`,
`SaveMembership`, and the `PostProjection`/`AccountProjection` field sets.

### `infra` — adapters and background workers

| Component | Responsibility |
|---|---|
| `DbContext` | Wraps `IMongoDatabase`; only member is `GetCollection<T>(name)`. |
| `RedisService` | JSON get/set/remove, `GetAndRemoveAsync` (atomic `StringGetDelete`), and raw integer `IncrementAsync`/`GetCounterAsync` for list-cache versions. |
| `RabbitMQService` | Publish and consume with per-message idempotency, retry counting, and dead-lettering. |
| `R2Service` | Uploads to a `{folder}/{guid}{ext}` key with an explicit `ContentType`; deletes by URL path. |
| `EmailService` | `POST https://api.resend.com/emails`, 10 s `HttpClient` timeout. |
| `JwtService` | HS256 access tokens carrying `accountId` and `role` claims. |
| `BCryptService`, `OtpService`, `RegistrationService`, `CurrentIdentityService` | Hashing; 6-digit OTP with 10 min TTL and 3 attempts; Data-Protection-wrapped registration tokens with 10 min TTL; `accountId` claim reader. |
| `NotificationService` | Persists a `Notification`, invalidates the recipient's list, pushes over SignalR, marks sent. |
| `NotificationHub` | `[Authorize]` SignalR hub at `/hubs/notification`; empty body — identity comes from `AccountIdUserIdProvider`. |
| 7 queue services | `FollowService`, `ReactionService`, `SaveService`, `CommentService`, `ShopRateService`, `DeleteAccountService`, `DeletePostService` — the actual work behind queued messages. |
| `QueueConsumerHost<TMessage>` | One generic `BackgroundService`, registered 14 times, one per queue. |
| `IndexInitializer`, `NormalizedFieldBackfillInitializer` | `BackgroundService`s that build indexes and backfill normalized search fields without blocking startup. |

## Component interaction

```mermaid
graph TB
    client["HTTP client / SignalR client"]

    subgraph apiLayer["api"]
        mw["CorrelationId → Performance<br/>→ Auth → RateLimiter"]
        ctrl["12 controllers"]
        hub["NotificationHub<br/>/hubs/notification"]
    end

    subgraph appLayer["app"]
        pipe["MediatR pipeline<br/>Logging → Performance → Validation"]
        handlers["72 handlers"]
    end

    subgraph infraLayer["infra"]
        svc["Services"]
        consumers["14 × QueueConsumerHost"]
        qsvc["7 queue services"]
    end

    mongo[("MongoDB")]
    redis[("Redis")]
    rabbit[["RabbitMQ"]]
    r2[("Cloudflare R2")]
    resend["Resend API"]

    client -->|"HTTP"| mw --> ctrl --> pipe --> handlers
    client <-->|"WebSocket"| hub
    handlers --> svc
    svc --> mongo
    svc --> redis
    svc --> r2
    svc --> resend
    handlers -->|"publish"| rabbit
    rabbit -->|"consume"| consumers --> qsvc
    qsvc --> mongo
    qsvc --> redis
    qsvc -->|"push"| hub
    hub <-->|"backplane"| redis
```

The three flows below are chosen to show **cross-component mechanics** — cache versioning, queue
idempotency and retry, and the token handoff between calls. They are not endpoint documentation:
every one of the 78 endpoints has its own diagram and rules in [`features.md`](./features.md).

## Flow 1 — Cached list read

`GET /api/v1/Post/latest` → `GetLatestPostHandler`. Every cached list query resolves a version
counter first; the version is part of the page key, so bumping the counter retires every cached page
and page size for that entity/signature at once.

```mermaid
sequenceDiagram
    participant C as Client
    participant Ctl as PostController
    participant H as GetLatestPostHandler
    participant P as CachedCursorPaginator
    participant R as Redis
    participant M as MongoDB
    participant E as PostFeedEnricher

    C->>Ctl: GET /api/v1/Post/latest?cursor&pageSize
    Ctl->>H: GetLatestPostQuery
    H->>H: GetAccountId() from JWT claim
    H->>P: PaginateAsync(entity=post, signature=null)
    P->>R: GET list-version:post:all
    R-->>P: version N
    P->>R: GET list:v1:post:all:{cursor}:{size}:vN
    alt cache hit
        R-->>P: cached page
    else cache miss
        P->>M: find(IsDeleted=false) sort CreatedAt,Id desc limit size+1
        M-->>P: documents
        P->>R: SET page key, TTL 2 min
    end
    P-->>H: CursorPageResult<PostSummaryDto>
    H->>E: EnrichAsync(page, accountId)
    E->>M: reacted post ids (Reactions.AccountId)
    E->>M: saved post ids (account.SavePosts)
    E-->>H: CursorPageResult<PostFeedItemDto>
    H-->>Ctl: Result.Ok
    Ctl-->>C: 200 OK
```

`PostFeedEnricher` issues **two additional Mongo queries per list request** to compute the
`IsReacted`/`IsSaved` flags, and these are never cached because they are per-viewer.

## Flow 2 — Write with async fan-out

`POST /api/v1/Reaction/post/{postId}` returns **202 Accepted** immediately; all state change happens
in the consumer. This is the pattern for every reaction, follow and save endpoint.

```mermaid
sequenceDiagram
    participant C as Client
    participant Ctl as ReactionController
    participant H as ReactPostHandler
    participant Q as RabbitMQService
    participant Host as QueueConsumerHost of ReactionMessage
    participant S as ReactionService
    participant M as MongoDB
    participant R as Redis
    participant N as NotificationService
    participant Hub as SignalR

    C->>Ctl: POST /api/v1/Reaction/post/{postId}
    Ctl->>H: ReactPostCommand
    H->>Q: PublishAsync(react-post, ReactionMessage{MessageId, AccountId, TargetId})
    H-->>Ctl: Result.Ok
    Ctl-->>C: 202 Accepted

    Q->>Host: deliver (prefetch 1, manual ack)
    Host->>R: GET rabbitmq:idempotency:react-post:{MessageId}
    alt already processed
        Host->>Q: ack, drop (DLQ if payload hash differs)
    else first delivery
        Host->>S: ProcessReactPostAsync(accountId, postId)
        S->>M: updateOne(post, not already reacted)<br/>$push Reactions, $inc ReactionAmount
        alt MatchedCount != 1
            S-->>Host: Fail "Already reacted to this post"
        else applied
            S->>R: bump post + post:reaction list versions
            S->>N: CreateAsync(PostReaction)
            N->>M: insert Notification
            N->>Hub: SendAsync("ReceiveNotification")
            N->>M: MarkAsSent
        end
        Host->>R: SET idempotency key, TTL 24 h
        Host->>Q: ack
    end
```

The idempotency key is written **after** the handler succeeds, so a crash between the work and the
key write causes reprocessing. A failure throws, the retry count in the `x-retry-count` header is
incremented, and the message is republished to the same queue until `MaxAttempts = 3`, after which
it goes to `{queue}.dlq`.

## Flow 3 — Registration

Three separate HTTP calls. The OTP proves control of the address; the registration token is the
carrier of that proof between calls.

```mermaid
sequenceDiagram
    participant C as Client
    participant EC as EmailController
    participant AC as AccountController
    participant O as OtpService
    participant Reg as RegistrationService
    participant R as Redis
    participant Mail as Resend
    participant M as MongoDB

    C->>EC: POST /Email/send-for-register {email}
    EC->>M: any account with this email and not deleted?
    alt exists
        EC-->>C: 409 "Email already exists"
    else
        EC->>O: GenerateAsync(email)
        O->>R: SET otp:{email} {Code,Attempts,ExpiresAt} TTL 10 min
        EC->>Mail: POST /emails (6-digit code)
        EC-->>C: 204 No Content
    end

    C->>EC: POST /Email/verify {email, otp}
    EC->>O: VerifyAsync
    O->>R: GETDEL otp:{email}
    alt code matches
        EC->>Reg: GenerateTokenAsync(email)
        Reg->>R: SET registration:{token} = Protect(email) TTL 10 min
        EC-->>C: 200 OK {registrationToken}
    else
        O->>R: re-SET with Attempts+1 while attempts < 3
        EC-->>C: 400 "Otp is incorrect"
    end

    C->>AC: POST /Account {registrationToken, name, password, bio}
    AC->>Reg: ExtractEmailAsync(token)
    Reg->>R: GETDEL registration:{token}
    Reg->>Reg: DataProtection Unprotect
    AC->>AC: BCrypt hash, Account.Create
    AC->>M: insertOne(account)
    AC->>R: SET account:v1:id:{id} TTL 7 days
    AC-->>C: 200 OK {AccountDto}
```

The same registration-token mechanism is reused by `POST /Account/recover`,
`PUT /Account/reset-password` and `PUT /Account/me/email`, with
`POST /Email/send-for-verify` as the entry point for existing accounts.

## Data flow

**Entry.** HTTP request bodies bind to `src/api/request/*Request.cs` records or `[FromForm]`
parameters; uploads arrive as `IFormFile`. Value objects (`Email`, `Name`, `Amount`) are constructed
**in the controller** and their `DomainResult` is unwrapped before the command is built, so an
invalid email fails before MediatR is reached.

**Processing.** The command/query flows through the three pipeline behaviours into its handler.
Handlers build MongoDB `FilterDefinition`/`UpdateDefinition`/`ProjectionDefinition` values directly
and call `IMongoDbContext.GetCollection<T>()`.

**Persistence.** MongoDB is the system of record, one collection per aggregate:
`account`, `post`, `comment`, `report`, `shop`, `refreshtoken`, `notification`
(`src/app/common/CollectionName.cs`). Deletion is **soft** everywhere: `BaseEntity` carries
`IsDeleted`, `DeletedAt`, `RecoveredAt`, and effectively every read filters `!x.IsDeleted`.

Entity state is mapped through `BsonConfiguration` (`src/infra/persistence/configuration/`), which
registers class maps that expose the private `_avatars`, `_followers`, `_followings`, `_savePosts`,
`_saveShops`, `_images` and `_reactions` backing fields under their public names, plus a
`BackingFieldBsonSerializer<T>` so those fields remain queryable. Value objects deserialize through
`MapCreator` calls into their static factories.

Counts are **denormalized onto the parent document** — `Account.FollowerAmount`,
`Account.FollowingAmount`, `Post.ReactionAmount`, `Post.CommentAmount`, `Comment.ReactionAmount` —
and maintained with `$inc` in the same update that pushes or pulls the array element.

**Caching.** Redis holds single entities (`{entity}:v1:id:{guid}`, 7-day TTL), list pages
(`list:v1:{entity}:{signature}:{cursor}:{size}:v{version}`, 2-minute TTL), list version counters
(`list-version:{entity}:{signature}`, 1-day sliding TTL), OTP entries, registration tokens,
and queue idempotency keys (24 h).

**Exit.** Entities map to `src/app/common/dto/*Dto.cs` records via 12 AutoMapper profiles. Note that
DTOs still expose the `Amount` **value object** rather than a plain `int`, so
`infra/serialization/AmountJsonConverter` (with `EmailJsonConverter` and `NameJsonConverter`, wired
in `DomainJsonOptions`) exists to flatten them for the Redis JSON payloads.

## External integrations

| System | Direction | Protocol | Wired in | Used for |
|---|---|---|---|---|
| MongoDB | out | MongoDB wire protocol | `IMongoClient` singleton → `DbContext` | All persistence |
| Redis | out | RESP | `IConnectionMultiplexer` singleton → `RedisService` | Cache, OTP, registration tokens, idempotency, SignalR backplane |
| RabbitMQ | out (publish) / in (consume) | AMQP 0-9-1 | `IConnection` singleton → `RabbitMQService` | 14 queues + 14 `.dlq` |
| Cloudflare R2 | out | S3 API over HTTPS | `IAmazonS3` singleton, `ForcePathStyle = true` | Avatars, post and comment images |
| Resend | out | HTTPS REST | `AddHttpClient<IEmailService, EmailService>` | OTP and welcome emails |
| SignalR clients | in/out | WebSocket | `MapHub<NotificationHub>("/hubs/notification")` | Live notification push |

The 14 queues (`src/app/common/QueueName.cs`): `follow`, `unfollow`, `react-post`, `unreact-post`,
`react-comment`, `unreact-comment`, `create-comment`, `recalculate-shop-rate`, `save-post`,
`unsave-post`, `save-shop`, `unsave-shop`, `delete-account`, `delete-post`.

SignalR authentication accepts the JWT from the `access_token` query string when the request path
starts with `/hubs`, configured in `JwtBearerEvents.OnMessageReceived`
(`src/api/DependencyInjection.cs`). `AccountIdUserIdProvider` maps a connection to the `accountId`
claim so `Clients.User(accountId)` addresses a specific account across replicas.
