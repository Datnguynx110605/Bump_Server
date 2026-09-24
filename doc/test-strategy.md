# Test Strategy

**Status: in progress.** Bootstrap (§2), `domain` (§3.1), the `app` pure parts (§3.2), the `infra`
adapters (§3.4), the queue transport contract (§3.5), §3.6's three generic sweeps, its rate-limit
policy tests and its pipeline tests (§3.6.5) are fully implemented and green, along with a growing set
of feature areas across §3.3 and §3.6's per-endpoint tables. Sections or rows not yet backed by a test
file are still a plan — where one of those says a test "asserts" something, read it as *the test to be
written asserts*, not as a claim that a suite passes for it.

The strategy is derived from what the code actually does, not from a generic layer template. Section 1
explains the one property of this codebase that decides the whole shape; sections 2–4 give the plan per
layer; section 5 maps all 83 features in [`features.md`](./features.md) to the tests that cover them;
section 7 defines what "passes for production" is allowed to mean — including, in §7.2, what a fully
green suite would and would not prove.

Related: [`architecture.md`](./architecture.md) for the layering rules the test projects must respect,
[`developer-guide.md`](./developer-guide.md) for the queue topology and serialization mechanics under
test, [`project-rules.md`](./project-rules.md) for the naming conventions new test files must follow.

---

## 1. What shapes this strategy

### 1.1 The constraint that decides everything

`IMongoDbContext` exposes exactly one member:

```csharp
IMongoCollection<T> GetCollection<T>(string name);
```

The inversion boundary stops at the driver. Everything a handler or worker decides is expressed *in*
`FilterDefinition<T>`, `UpdateDefinition<T>` and aggregation stages that only a real MongoDB server can
evaluate. Three examples, all load-bearing:

- `FollowService.ProcessFollowAsync` writes both sides of the edge in one `BulkWriteAsync` where each
  side's filter excludes the state it is about to write, then branches on `bulkResult.MatchedCount`:
  `0` means already following, `1` means a previously half-applied follow was just repaired. A mocked
  collection returns whatever `MatchedCount` the test author decides, which asserts the test, not the code.
- `CreatePostHandler` catches `MongoWriteException` with
  `ex.WriteError.Category == ServerErrorCategory.DuplicateKey` and re-reads the row the partial unique
  index rejected it for. That behaviour only exists when a real partial unique index rejects a real
  concurrent insert.
- `SaveMembership.SavedPostIdsAsync` hand-builds a `$project`/`$filter`/`$map` stage and writes its
  Guids as explicit `BsonBinaryData(id, GuidRepresentation.Standard)`. Its own comment states the
  failure mode: a representation mismatch *"does not fail — it matches nothing, and the feed quietly
  reports every post as unsaved."* A mock cannot produce a silent mismatch.

**Therefore there is no meaningful mock-based unit tier for the `app` handlers or the `infra` workers.**
A suite of handler tests over a mocked `IMongoCollection<T>` would be green while every one of those
three behaviours was broken. This document does not propose one.

### 1.2 Consequence — two test projects, not one per layer

| Project | Backing services | Target runtime | Covers |
|---|---|---|---|
| `test/unit` | none | seconds for the whole project | `domain` in full; the pure parts of `app` (cursor codec, page-size clamp, cache-key policy, behaviors, all validators, AutoMapper configuration); the pure parts of `api` (`ImageUpload`, `ResultExtensions`, `ResultStatusMapper`) |
| `test/integration` | Mongo, Redis, RabbitMQ, MinIO — via Testcontainers, **one set per assembly** | minutes | `app` handlers, `infra` adapters and queue workers, `api` endpoints through `WebApplicationFactory` |

The single-fixture rule for `test/integration` is not a preference. Twelve `QueueConsumerHost`
singletons, four other hosted services and a blocking RabbitMQ connection factory make per-class
containers slow enough that the suite stops being run, which is the same as not having it.

A third project per layer buys nothing here: the layers already live in separate assemblies, and the
split that matters for feedback speed is "needs Docker" versus "does not".

### 1.3 Deliberately out of scope

- Live calls to Resend, APNs or Apple Maps. `docker-compose.yml` and
  [`infrastructure.md`](./infrastructure.md) already record that these three have no local emulator;
  they are stubbed (§2.3).
- The Serilog `ExpressionTemplate` output shape in `Program.cs`.
- Third-party library behaviour (MediatR dispatch, FluentValidation's own rules, the S3 SDK).
- `Dockerfile` / `railway.json` deployment behaviour. Deploy-time verification is `GET /health`, which
  already exists and is exercised by one endpoint test.

---

## 2. Bootstrap — step zero

Done — `test/unit` and `test/integration` exist, are registered in `src/Bump.sln`, and the visibility
edits and test host surgery below are in place. Left here as the reference for what bootstrap required.

### 2.1 Projects

```
test/
├── unit/
│   └── unit.csproj            net9.0, references domain, app, api
└── integration/
    └── integration.csproj     net9.0, references api (which transitively pulls infra → app → domain)
```

Both are added to `src/Bump.sln`. Naming follows the house style in `project-rules.md`: lowercase
single-word project names, `namespace unit;` / `namespace integration;`, file-scoped, flat.

Packages, kept to the minimum that works:

| Project | Packages |
|---|---|
| both | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` |
| `integration` | `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MongoDb`, `Testcontainers.Redis`, `Testcontainers.RabbitMq`, `Testcontainers.Minio` |

No mocking library and no assertion library initially. The unit tier's fakes are three-line classes
(`ICurrentIdentityService` returning a fixed Guid); FluentValidation ships `InlineValidator<T>` for the
`ValidationBehavior` test. Add NSubstitute when a hand-rolled fake first exceeds a screen, not before.

### 2.2 Visibility edits the tests require

Three edits to production code, each a one-liner, each necessary or the suite will not compile:

| Edit | Why |
|---|---|
| `InternalsVisibleTo("unit")` and `InternalsVisibleTo("integration")` in `app.csproj` | handlers, validators and `CursorCodec` are `internal sealed` by convention |
| the same two in `api.csproj` | `ImageUpload` and `ResultStatusMapper` are `internal static`, and `WebApplicationFactory<T>` needs an entry-assembly marker |
| a marker type for `WebApplicationFactory` | `Program.cs` uses top-level statements and declares no namespace, so there is no `Program` type to bind to. Either `public partial class Program { }` at the end of `Program.cs`, or rely on the `InternalsVisibleTo` above and bind to the compiler-generated internal `Program`. The explicit partial class is the clearer of the two. |

These are additive and change no behaviour, but they are production-file edits and belong in the same
approval as the first slice of tests.

### 2.3 Test host surgery

`AddApi` cannot be booted as-is against containers. The specific obstacles, all read out of
`api/DependencyInjection.cs` and `infra/DependencyInjection.cs`:

| Obstacle | Handling in the test host |
|---|---|
| `RequireJwtConfiguration` throws unless `Jwt:SecretKey`, `Jwt:Issuer`, `Jwt:Audience` are non-empty and `Jwt:ExpiryMinutes > 0` | supply all four through `ConfigureAppConfiguration` in-memory values |
| `AddSignalR().AddStackExchangeRedis(configuration["Redis:ConnectionString"]!)` is null-forgiving | point at the Redis container |
| 12 `QueueConsumerHost<T>` singletons registered as `IHostedService`, each resolving `IRabbitMQService` | keep them **only** in the worker tests; in endpoint tests remove them so a 202 test is not racing a consumer. `AddQueueConsumer` registers through a **factory**, so those descriptors have a null `ImplementationType` while the four typed `AddHostedService<T>()` registrations do not — the filter is `ServiceType == typeof(IHostedService) && ImplementationType is null`. The obvious `ImplementationType == typeof(QueueConsumerHost<>)` filter matches nothing and leaves every consumer running |
| `IndexInitializer`, `NormalizedFieldBackfillInitializer`, `AdminSeeder`, `SoftDeletePurgeHost` | keep `IndexInitializer` — the partial unique indexes are the thing under test in the shop race. Remove `SoftDeletePurgeHost` (it deletes rows on a timer) and `NormalizedFieldBackfillInitializer`. Keep or remove `AdminSeeder` per fixture: an admin account is needed for the 20 admin-tier endpoints, so keeping it is simpler than minting one |
| `IAmazonS3` singleton interpolates `ServiceURL` as `https://{R2:AccountId}.r2.cloudflarestorage.com` | **replace the descriptor**, do not reconfigure it — configuration alone cannot reach MinIO |
| `IConnection` for RabbitMQ blocks on `.CreateConnectionAsync().GetAwaiter().GetResult()` at resolution | the container must be healthy before the host is built; this is why the fixture is assembly-scoped |

The three external SaaS adapters have a clean seam that needs no interface substitution:
`IEmailService`, `IPlaceService` and `IApnsService` are all registered with `AddHttpClient<,>`, so
`ConfigurePrimaryHttpMessageHandler` with a scripted `HttpMessageHandler` stubs Resend, Apple Maps and
APNs while leaving the real `EmailService`, `ApplePlaceService` and `ApnsService` code under test —
including their error handling, which is where the interesting behaviour is (`ApplePlaceService`'s
network-failure path is the subject of the most recent commit on this branch).

---

## 3. Layer strategies

### 3.1 `domain` — pure unit tests, exhaustive

The only layer with no dependencies at all, so it is tested exhaustively and cheaply. Every branch of
every `Create` factory and every state transition.

| Target | What the tests pin |
|---|---|
| `Amount` | negative rejected; zero and positive accepted |
| `Email` | empty, missing `@`, over 200 chars rejected |
| `Name` | empty, over 200 chars, and any uppercase or non-alphanumeric character rejected — the `char.IsLower(c) \|\| char.IsDigit(c)` rule is strict and surprising, so it is pinned explicitly |
| `BaseEntity` | `Update`/`Delete` refuse on a deleted entity; `Recover` refuses on a live one; `Delete` sets `DeletedAt`, `Recover` clears it and sets `RecoveredAt` |
| `Account` | password not exactly 60 chars rejected (`PasswordHashLength`); bio over 2000 rejected; `ChangeEmail`/`ChangeName` refuse an identical value; `PromoteToAdmin` refuses an existing admin |
| `Post` | empty `accountId` or `shopId` rejected; `CaptionNormalized` derived on create and on `ChangeCaption`; null caption leaves it null |
| `Shop` | half-supplied lat/long pair rejected; latitude and longitude range bounds; `Location` stored as `[longitude, latitude]` — the reversed order is the single most breakable invariant in the entity; `AdoptPlace` refuses an empty id and refuses to overwrite an existing one |
| `TextNormalizer` | `đ`/`Đ` → `d`/`D`; Vietnamese diacritics folded; result lowercased; empty input returns empty |
| `DomainResult` | `Failure` carries the message; `Success<T>` carries the value; `Failure<T>` leaves `Value` default |

### 3.2 `app` — the pure parts, unit tests

Everything in `app` that does not touch `IMongoCollection<T>`:

| Target | What the tests pin |
|---|---|
| `CursorCodec` | encode→decode round-trips `DateTime` (round-trip kind) and `Guid`; malformed base64, a missing `\|` separator and an unparseable date all return `null` rather than throwing |
| `CursorPageRequest` | `PageSize` clamps to `[1, 100]` in the **record itself** — the comment records that an unclamped value let a caller mint unbounded cache keys, so `999999 → 100` and `0 → 1` are pinned here as well as in the paginators |
| `CacheKey` | `List` composes entity/signature/cursor/pageSize/version in that order; `KeywordSignature` hashes rather than interpolates — the regression test feeds a keyword shaped like `foo:hidden:<16 hex>` and asserts it cannot collide with a `BlockedAccountSet.Signature` output; `ListVersionTTL > DefaultListTTL` |
| `BlockedAccountSet.Signature` | empty hidden set returns the caller's signature unchanged (so non-blocking viewers share one page); two differently-ordered lists of the same ids produce the same signature |
| `AppError` | each factory attaches the right status in metadata under `StatusKey` |
| `ValidationBehavior` | no validators registered → `next` is invoked; failures are converted to `AppError.BadRequest` and `next` is **not** invoked |
| `WriteCancellationBehavior` | a `*Command` receives `CancellationToken.None` even when the caller's token is already cancelled; a `*Query` receives the caller's token unchanged. **Plus a reflection test** over every `IRequest`/`IRequest<>` type in the `app` assembly asserting each name ends in `Command` or `Query` — the behaviour switches on `typeof(TRequest).Name.EndsWith("Command")` and its own comment warns that a use case named outside the convention silently keeps the caller's token |
| AutoMapper | one test calling `AssertConfigurationIsValid()` over a configuration built from all 13 profiles. This covers every DTO mapping at once and fails on the next DTO added without a profile entry |
| validators | one test class per validator, one case per rule plus one valid case. 46 validators exist today (`project-rules.md`: 37 of 83 use cases have none) |

### 3.3 `app` — handlers, integration tests

Handlers run against real Mongo and real Redis, with `ICurrentIdentityService` faked to a fixed account
id (it is the only `app` dependency that legitimately has no infrastructure behind it — it reads
`HttpContext`). The handler is resolved from the container so the MediatR pipeline, validators and
AutoMapper profiles are all in play.

What a handler test asserts, in order of importance:

1. **The `Result`** — success value, or the failure with its `AppError` status. `Unauthorized` when
   identity is missing is a one-line case every handler shares and is worth a shared theory.
2. **The Mongo state after the write** — read the document back and assert the fields, not the
   `UpdateDefinition`. This is what makes the filter and `$set` real.
3. **The cache effect** — the list version counter for the right `(entity, filterSignature)` pair was
   incremented, and the entity key was set or removed. `ListCache.InvalidateAsync` bumping the wrong
   signature is invisible to every other assertion and is the most common way a cached list goes stale.
4. **The published message** — for handlers that only publish (block, follow, react, save), the queue
   holds a message with the expected payload. The write itself is the worker's test, not the handler's.

Three handlers deserve named, non-obvious cases:

- **`CreatePostHandler`** — the widest handler in the codebase. Cases: place resolution failing returns
  400 *before* any upload; a verified place overrides client-supplied name/address/coordinates
  entirely; an existing shop matched by `PlaceId` wins over one matched by name+address; a shop matched
  by name+address with no `PlaceId` is adopted; a concurrent insert that trips the partial unique index
  is recovered by re-reading rather than surfacing a 500; and every failure path between the upload and
  the post insert removes the R2 objects (assert against MinIO, not against a mock).
- **`GetRelatedPostHandler`** — the affinity aggregation, its 1-hour cache, the trusted-account
  fallback tier and the 300-id cap.
- **`GetBestShopAroundHandler`** — `$nearSphere` against the 2dsphere index, which requires
  `IndexInitializer` to have run and requires `Location` to be `[longitude, latitude]`. A shop with a
  null `Location` must not appear rather than appearing at (0, 0).

Read-side handlers share one parameterized shape: seed N+1 rows, request a page of N, assert
`HasMore` is true and the cursor is non-null, follow the cursor, assert the second page and that the
two pages do not overlap. Filtered reads add a blocked-account case (§3.6).

### 3.4 `infra` — adapters, integration tests

Done — every row below is implemented and green in `test/integration/infra` (`JwtService` and
`BCryptService` in `test/unit/infra` per their own row). Two rows were corrected against the adapters'
actual code while writing their tests, same as the §3.5 worker rows below: `EmailService`/
`ApplePlaceService`/`ApnsService`'s "degrades rather than throwing" claim, and `RabbitMQService`'s
undeserializable-body claim in §3.5.

| Adapter | Against | What the tests pin |
|---|---|---|
| `RedisService` | Redis container | get/set/remove round-trip with the value-object JSON converters; TTL applied; `IncrementAsync` creates and expires the counter; `GetCounterAsync` returns 0 for a missing key |
| `RabbitMQService` | RabbitMQ + Redis | the idempotency, retry and dead-letter contract — see §3.5 |
| `R2Service` | MinIO | upload returns a `{PublicUrl}/{folder}/{guid}{ext}` URL; the object exists with the mapped content type; an extension outside the allowlist is stored as `application/octet-stream`; delete removes it |
| `OtpService` | Redis | a 6-digit code is issued; the 6th generation inside an hour returns `null` (`MaxGenerationsPerWindow`); a correct code still verifies a second time within its TTL (deliberate — a lost response must be recoverable); the 4th wrong guess fails (`MaxAttempts`); an expired entry fails |
| `RegistrationService` | Redis | `ExtractEmailAsync` does **not** consume the token — the token survives a failed downstream write, which is the whole reason the read and the consume are separate calls; a tampered payload returns `null` rather than throwing; `ConsumeTokenAsync` removes it |
| `JwtService` | none (unit) | the token carries `accountId` and `role` claims, the configured issuer/audience, and an expiry `Jwt:ExpiryMinutes` in the future. Can live in `test/unit` with an in-memory `IConfiguration` |
| `BCryptService` | none (unit) | hash length is 60 — the number `Account.Create` hard-codes as `PasswordHashLength`; verify succeeds for the right password and fails for the wrong one |
| `EmailService`, `ApplePlaceService`, `ApnsService` | scripted `HttpMessageHandler` | a success response parses into the expected result. Corrected against the actual code, not all three degrade the same way: `EmailService` throws `InvalidOperationException` on any non-success status by design, which is its documented failure mode, not a bug to guard against; `ApplePlaceService` returns `null` on a network failure, an unknown place id, and a non-success status, but a malformed 200 body throws `JsonException` uncaught — nothing between `ReadAsStringAsync` and the `Deserialize` call catches it; `ApnsService` swallows an ordinary failure status and a malformed *reason* body (logs a warning, retires a dead token, retries once on `ExpiredProviderToken`), but a network failure propagates out of `SendAsync` uncaught — `ApnsTokenProvider`'s own comment says that is swallowed by `NotificationService`, one layer up, not here |
| `BsonConfiguration` / `BackingFieldBsonSerializer` | Mongo container | an `Account` with populated `_avatars`, `_followers`, `_blocks` and `_blockedBy` round-trips through insert and read with the private backing fields intact, and `Email`/`Name`/`Amount` deserialize through their private constructors |
| `IndexConfiguration` / `IndexInitializer` | Mongo container | after the initializer runs, the expected indexes exist; specifically the two partial unique indexes on `Shop` (`PlaceId`, and `Name`+`Address`) that `CreatePostHandler`'s race recovery depends on, and the 2dsphere index on `Location` |
| health checks | all three containers | healthy when up. The unhealthy case is left alone: stopping a shared container to test it would break every other test in the assembly |

### 3.5 `infra` — queue workers and the queue contract

Two distinct things to test, and conflating them is the usual mistake.

**The transport contract** is done, in `test/integration/infra/queue/RabbitMQTransportContractTests.cs`,
tested once against the real `RabbitMQService`/`QueueConsumerHost`:

- a message is consumed and the handler invoked;
- redelivery of the same `MessageId` with the same payload is skipped (the Redis idempotency key);
- redelivery of the same `MessageId` with a *different* payload is dead-lettered, not silently acked;
- corrected against the actual code while writing this: a body only goes straight to `{queue}.dlq`
  when it deserializes to JSON `null` — that is the one case `ConsumeAsync`'s own `message is null`
  check dead-letters directly. Genuinely malformed JSON makes `JsonSerializer.Deserialize` throw, which
  the surrounding `catch` treats exactly like a throwing handler: it goes to `{queue}.retry` first and
  only reaches `{queue}.dlq` on the third attempt, same as any other handler exception;
- a throwing handler republishes to `{queue}.retry` with `x-retry-count` incremented, and the third
  attempt lands in `{queue}.dlq` (`MaxAttempts = 3`). The 30-second `RetryDelay` makes a literal
  end-to-end retry test slow — assert on the queue the message was published to and the header value,
  not on waiting out the TTL;
- a worker returning a **failed** `Result` is acked and logged, *not* retried. This is the sharpest
  edge in the system: `QueueConsumerHost.HandleAsync` only logs a warning, so a failed `Result` never
  reaches the retry or dead-letter tiers. A test pinning it is what stops someone "fixing" a worker by
  returning `Result.Fail` where they meant to throw.

**Each of the 12 workers**, against Mongo and Redis directly — invoking the service method rather than
going through the broker, since the transport is already covered above.

> **Provenance.** Of the twelve workers, only `FollowService` was read in full while writing this
> document. The other eleven rows below are derived from `developer-guide.md` §3.2, `features.md` and
> the `AddQueueConsumer` registrations in `infra/DependencyInjection.cs`. Read the worker before
> writing its tests and correct the row if it disagrees — the row is a starting point, never the
> specification.


| Worker | Cases beyond the happy path |
|---|---|
| `FollowService` | self-follow refused; either account missing → `NotFound`; blocked in either direction → refused silently; `MatchedCount == 0` → `Conflict("Already following")`; a pre-broken half-applied edge is **repaired** rather than throwing; both `FollowerAmount` and `FollowingAmount` move by exactly 1; trust granted only at ≥500 followers **and** ≥30 days old **and** no live reports, and never revoked by unfollow |
| `BlockService` | both arrays written; existing follow edges severed in both directions; reactions severed; the blocked-set cache key deleted for both accounts; **and, like `FollowService`, a pre-broken half-applied block (`Blocks` already pushed, nothing else run) falls through and still severs follows/reactions rather than exiting on the first `MatchedCount == 0`** — found while writing the Block area's tests, not called out when this row was first drafted; `ProcessBlockAsync`'s own comment records the bug class the fall-through fixed |
| `ReactPostService` / `ReactCommentService` | double-react is a no-op; `ReactionAmount` never goes negative on a double-unreact; the reaction snapshots the actor's name and latest avatar |
| `SaveService` | the four save/unsave paths; **a double-save is not a no-op** — `ProcessSavePostAsync`/`ProcessSaveShopAsync`'s conditional filter excludes an id already saved, so the second call matches nothing and returns `AppError.Conflict`, not `Result.Ok()` (corrected from this row's original guess, written before the handler was read). The array does stay at one entry either way, which is the part "no-op" got right. The two failure paths are also asymmetric: save's `Conflict` carries status metadata, unsave's bare `Result.Fail("Not saved this post"/"Not saved this shop")` does not — the same class of inconsistency §5 row 5 already pins for `ChangePasswordHandler` |
| `CommentService` | create publishes the notification and increments `CommentAmount`; delete removes the comment's images from R2 |
| `PostService` | delete cascades to comments and removes post images from R2 |
| `AccountService` | the delete cascade across every collection that references the account |
| `ProfileService` | a rename fans out to the copied name/avatar in `Follower`, `Following`, `Block`, `Reaction`, `Comment` and `Notification` subdocuments |
| `ShopRateService` | the recalculated rate equals the mean of live posts' rates; deleted posts excluded; a shop with no posts |

Inverse pairs share a queue precisely so they apply in order (`QueueName`'s own comment). One ordering
test — follow then unfollow delivered back to back leaves no edge and counters at zero — pins the
property that registration choice exists to protect.

### 3.6 `api` — endpoint tests through `WebApplicationFactory`

**Every endpoint is exercised across its whole input space: valid, invalid, missing, malformed and
boundary.** Not a happy path plus an auth check. This is the largest single block of tests in the plan,
and §3.6.3 is how it is written without hundreds of near-identical test methods.

#### 3.6.1 Why input cases belong here and not only in the validator unit tests

Three independent layers reject input before a handler ever runs, and **they answer in different
shapes**. A validator unit test sees only one of the three.

| Layer | Rejects | Produces |
|---|---|---|
| 1 — model binding (`[ApiController]`) | a missing required query parameter, an unparseable `double`/`int`/`Guid`, an absent or malformed body | 400 `ValidationProblemDetails`; the controller body never runs |
| 2 — the controller itself | value objects built before dispatch: `Amount.Create(rate)`, `Email.Create(request.Email)`, `ImageUpload.ToStreams(images)` | 400 `ProblemDetails` via `ToActionResult`; MediatR is never reached |
| 3 — `ValidationBehavior` in the pipeline | the 46 FluentValidation validators | `AppError.BadRequest` → **one error becomes `ProblemDetails`, two or more become `ValidationProblemDetails`** (`ResultExtensions`) |

The `rate` field on `POST /Post` shows why this matters. It has **three** outcomes on one field:

- `rate=-1` — rejected by layer 2, `Amount.Create`, message *"Amount must be a non-negative integer"*;
- `rate=0` — passes layer 2, rejected by layer 3, `CreatePostValidator`, message *"Rate must be
  between 1 and 5"*;
- `rate=3` — passes both.

Two different rejection paths and two different messages for the same field, and a unit test of
`CreatePostValidator` sees only the middle one. The same split applies to `Email` on `POST /Auth/login`
and to every image on the two upload endpoints.

#### 3.6.2 The case taxonomy

Applied to each of the 83 endpoints, taking the categories that apply to its signature:

| Category | Where it applies | Example | Expected |
|---|---|---|---|
| valid, minimal | all | only required fields | 200 / 204 / 202 |
| valid, full | all | every optional field populated | 200 / 204 / 202 |
| route param malformed | the `{id:guid}` routes | `/api/v1/Post/not-a-guid` | **404 from routing, not 400** — the `:guid` constraint fails the route match, so no filter or handler runs. Surprising, and worth pinning so nobody "fixes" it |
| route param well-formed, absent row | same | an unused Guid | the handler's 404 |
| required query param missing | `keyword`, `name`, `address`, `email`, `url`, `latitude`, `longitude` — all declared non-nullable | `GET /Shop/search` with no `keyword` | 400, layer 1 |
| query param unparseable | `latitude=abc`, `pageSize=abc`, `windowHours=abc` | | 400, layer 1 |
| query param out of range, **validated** | `latitude=91`, `longitude=181` | | 400, layer 3, message from `GetBestShopAroundValidator` |
| query param out of range, **clamped** | `pageSize=0`, `pageSize=-5`, `pageSize=999999` | | **200** — `CursorPageRequest` clamps silently to `[1,100]`. Pinned explicitly because it is the opposite of the row above and looks like a bug to anyone who has not read the record |
| body absent / empty / malformed | the 16 body endpoints | no body, `{}`, `{` | **no body (no Content-Type header at all) → 415**, not 400 — ASP.NET Core can't pick a formatter without one. `{}` and `{` (a real `application/json` body, just empty or truncated) are 400, layer 1, as originally stated. All three confirmed against `POST /Account` |
| body field missing where non-nullable | `LoginRequest` without `Password` | | 400, layer 1 |
| body field empty string | `""` | | per validator — pinned per endpoint, since `NotEmpty` is not applied uniformly |
| length boundary, both sides | bio 2000 / 2001, caption 2000 / 2001, `placeId` 200 / 201, `shopName` 200 / 201 | | at the limit 200/204, over it 400 |
| value object rejected at the controller | `rate=-1`, a malformed email on login | | 400, layer 2, handler never invoked |
| conditionally required field | `CreatePost`: `placeId` absent → `shopName` and `shopAddress` required; `placeId` present → both ignored | | both directions tested — the `When` block in `CreatePostValidator` is a real branch |
| paired fields | `latitude` without `longitude` | | 400 *"Latitude and longitude must be provided together"* |
| **two or more fields wrong at once** | `CreatePost` with neither `shopName` nor `shopAddress` | | 400 **`ValidationProblemDetails`** — the response *shape* changes, and this is only observable here |
| wrong content type | JSON posted to the `[FromForm]` endpoints | | **415 only when the action takes an `IFormFile` directly** (`ChangeAvatar`) — confirmed empirically. `CreatePost` binds every `[FromForm]` parameter as an individually-optional scalar, so a JSON body just leaves them all at their defaults (`shopName`/`shopAddress` null, `rate` 0) and the request fails at validation instead, as 400 `ValidationProblemDetails`, never 415. Check which shape an endpoint has before assuming 415 |
| upload cases | empty file, > 5 MB, an 11th file, disallowed extension, HTML renamed `.jpg` | | 400, layer 2 |
| unauthenticated / wrong role / over limit | all | | 401 / 403 / 429 |

Every case asserts **status and body**, never status alone: the status does not say which of the three
layers answered, and that is the distinction the whole section exists to make. For a `ProblemDetails`
the assertion is on `Detail`; for a `ValidationProblemDetails`, on the set of `Errors` keys.

#### 3.6.3 How this is written without ~800 test methods

Data-driven, so the volume lives in tables that are read rather than in code that is maintained.

**Three generic sweeps**, one `[Theory]` each, covering what applies to whole classes of route — done,
all three in `test/integration`:

- the auth tier over all 83 routes (`AuthTierSweepTests`) — anonymous → 401, user token on an admin
  route → 403, the 8 `[AllowAnonymous]` routes reachable without a token. The tier is an attribute, and
  an attribute is exactly what a refactor drops silently;
- the malformed-`{id:guid}` case over the route table (`RouteSweepTests`);
- the absent/malformed-body case (`BodyShapeSweepTests`), over what turned out to be **17** JSON-body
  endpoints once actually counted, not 16 — `PUT /Comment/{id}` was missing from the original count.
  `{}` (an empty JSON object) 400s at every one of them except `PUT /Account/me/bio`:
  `ChangeBioRequest`'s only field is the nullable `Bio`, so `{}` deserializes to a request that
  legitimately clears the bio (204, per that endpoint's own input matrix) rather than failing to bind.

**One `[Theory]` per endpoint** with its own `MemberData` table for the field-specific cases. A row is
`(description, request, expectedStatus, expectedMessageFragment)`, and **row one is always the valid
case**, so valid and invalid stay visibly paired in the same table. Eighty-three tables of five to
twelve rows is data to review in a diff, not code to maintain.

**Rate-limit policies** — done, `RateLimiterPolicyTests`, one test each (`auth` 10/min, `email` 5/min,
`search` 20/min, `upload` 10/min, `report` 10/min, global 200/min), asserting 429 with a
`ProblemDetails` body and that partitioning actually isolates budgets: two different IPs (`auth`,
`email`) or two different accounts (`search`, `report`, `upload`) never share one. Corrected against
the actual runtime behaviour while writing these: only the global policy's 429 carries a `Retry-After`
header. `ConfigureRateLimiting`'s `OnRejected` reads it from `Lease.TryGetMetadata(MetadataName.RetryAfter, ...)`,
and `System.Threading.RateLimiting`'s `SlidingWindowRateLimiter` — every named policy uses it — never
populates that metadata on a rejected lease, confirmed directly against the library; only
`FixedWindowRateLimiter`, backing the global limiter, does. A 429 from `auth`/`email`/`search`/
`report`/`upload` therefore has no `Retry-After` header at all.

#### 3.6.4 Still unit-tested, because they are pure

`ImageUpload` and `ResultExtensions`/`ResultStatusMapper` keep their `test/unit` coverage from §3.2 —
the endpoint cases above prove they are *wired in*, the unit tests prove they are *right*. Specifically:
`ImageUpload` over every rejection reason and all three accepted signatures; `ResultExtensions` over
the single-error, multi-error and missing-metadata shapes.

#### 3.6.5 Pipeline tests that belong nowhere else

Done — all three, in `test/integration`:

- `GET /health` returns 200 (`HealthCheckTests`, §3.4 — one test covers both rows: it is the same
  endpoint);
- `CorrelationIdMiddlewareTests`: `CorrelationIdMiddleware` echoes a valid inbound id, and **replaces**
  one that is over 64 characters or contains anything outside `[a-zA-Z0-9-]` — an injection guard on a
  value that reaches every log line, so the rejection cases carry more weight than the happy one;
- `NotificationHubTests`: a client connecting with `?access_token=` reaches `NotificationHub` (the
  `OnMessageReceived` event in `AddApi` exists for exactly that — confirmed a bare connection attempt
  with no token is rejected, the property that makes the query-param case meaningful), and a
  notification created for that account is delivered over it, asserted against a real `HubConnection`
  forced onto the `LongPolling` transport since `WebApplicationFactory`'s `TestServer` has no socket to
  upgrade. The Redis backplane's cross-replica behaviour is not tested — one test host is one replica.

### 3.7 The block model — its own cross-cutting suite

Blocking is the one feature whose correctness is spread across every read path in the system
(`features.md` gives it two explanatory sections before its three endpoints), so it is tested as a
property rather than as three endpoints:

- `BlockGuard` refuses in **both** directions — the actor blocked the target, and the target blocked
  the actor — and is uncapped, unlike `BlockedAccountSet`;
- `BlockedAccountSet` returns the union, sorted, capped at 300, and is served from cache on the second
  call;
- for each filtered read path (`GetAllPost`, `GetPostByKeyword`, `GetRelatedPost`, `GetCommentByPost`,
  reaction lists, follower/following lists): a blocked account's rows are absent, **and** the page comes
  back full — the filtering happens in Mongo before `$limit`, so a page of 20 with 5 blocked authors
  must still return 20 items, not 15. **`GetMySavedPosts` (§5 row 61) is the one exception**, found
  while writing the Save area's tests: `SavedPost` holds only `PostId`/`SavedAt`, never the author, so
  the `$nin` cannot ride `EmbeddedArrayPaginator`'s query the way it rides every filter above — the
  author is only known once the saved ids are looked up against `Post` in a second query, after
  `HasMore`/cursor are already fixed over the *unfiltered* saved set. A page here comes back **short**
  when a saved post's author is blocked, not backfilled to stay full;
- the cache-key split that `CachedCursorPaginator` documents: the version counter is read under the
  *unsuffixed* signature and the page under the suffixed one. The regression test is a write by a
  non-blocked author landing visibly for a viewer who has blocked someone else — folding the hidden
  digest into the counter key would hide that write from exactly that viewer.

---

## 4. Fixtures, data and isolation

**One assembly-scoped fixture** (`ICollectionFixture`) owns all four containers and the
`WebApplicationFactory`. Every integration test class joins that collection.

**Isolation is per-test, by data, not by container.** Each test seeds the accounts, posts and shops it
needs with fresh `Guid`s and asserts only on those. Dropping the database between tests would serialize
the suite and would force `IndexInitializer` to re-run; a shared database with disjoint ids does not.

**`GetDashboardStats` does not need the clean slate this section originally proposed.** Every integration
test class already joins the one `IntegrationCollection`, and xUnit runs the test methods of classes in
one collection sequentially by default — the whole suite is already the "non-parallel collection" this
carve-out asked for, so nothing separate had to be added. What changed is the test itself: rather than
dropping and reseeding `Account`/`Post`/`Comment` (which would also drop `IndexInitializer`'s partial
unique indexes, and delete whatever `AdminSeeder` wrote at boot — `IntegrationTestFixture` only removes
`NormalizedFieldBackfillInitializer` and `SoftDeletePurgeHost`, §2.3), the test reads `/Dashboard/stats`,
seeds a known number of accounts/posts/comments, soft-deletes one of each, reads again, and asserts the
**delta** is exactly `(seeded − 1, seeded − 1, seeded − 1)`. Because the two reads happen back to back in
one already-serialized test, nothing else can insert between them, so the delta is as exact as a wipe
would have been, without the risk to everything a wipe would have destroyed.

**Redis** is shared the same way: keys are namespaced by the entity ids under test, and the few tests
that assert a counter's absolute value flush only their own keys.

**A seed builder** (`AccountBuilder`, `PostBuilder`, `ShopBuilder`) writing straight to Mongo keeps
arrange blocks to one or two lines. Seeding through the API instead would make every test depend on
`CreateAccount` and `CreatePost` still working, which turns one bug into a hundred red tests.

**Time** — `BaseEntity` stamps `DateTime.UtcNow` directly, so nothing can be frozen without changing
production code. Two consequences the tests must live with: the trust-grant rule's 30-day account age
is tested by seeding `CreatedAt` in the past through Mongo directly, and TTL expiry is tested with
short explicit TTLs rather than by waiting. Introducing an `IClock` is a refactor, not a test change,
and is listed in §8.

---

## 5. Feature coverage matrix

Every feature in [`features.md`](./features.md), 1–83. Columns: the tier that covers it, and the
behaviour the test exists to pin **beyond** the §3.6 input matrix, which every endpoint gets in full —
valid, invalid, missing, malformed, boundary, auth tier and rate limit — without being restated per row. Legend: **U** `test/unit`, **H** handler integration, **W** worker integration,
**E** endpoint integration.

> **Provenance — read this before trusting a cell.** The *tier* columns are complete and derived from
> the endpoint list, so they can be relied on. The *pinned behaviour* column is not uniformly measured.
> Handlers read in full while writing this document: `CreatePostHandler`, `LoginHandler`,
> `RequestNewAccessTokenHandler`, `ChangePasswordHandler`, `CreateAccountHandler`,
> `CreateReportHandler`, `RegisterDeviceHandler`, plus `FollowService` and all of `app/common`,
> `domain`, `PostController` and `AuthController`. Also read while writing the Account area's tests
> (rows 1–16 and 72): `ChangeNameHandler`, `ChangeBioHandler`, `ChangeEmailHandler`,
> `ChangeAvatarHandler`, `RemoveAvatarHandler`, `DeleteAccountHandler`, `RecoverAccountHandler`,
> `ResetPasswordHandler`, `GetAllAccountHandler`, `GetSpecificAccountHandler`,
> `GetAccountByEmailHandler`, `GetAccountByNameHandler`, `GetAccountByKeywordHandler`,
> `AdminDeleteAccountHandler` and every Account validator — two of their cells were wrong (rows 3
> and 15, corrected above) precisely because the earlier version of this row was a features.md guess.
> Also read while writing the Auth area's tests (rows 17–20 and 75): `GetAllRefreshTokenHandler`,
> `GetSpecificRefreshTokenHandler`, `RevokeRefreshTokenHandler` and every Auth validator (no
> corrections needed — `LoginHandler`/`RequestNewAccessTokenHandler` were already in this list and
> their cells held up). Also read while writing the Email area's tests (rows 21–23):
> `SendEmailForRegisterHandler`, `SendEmailForVerifyHandler`, `VerifyEmailHandler`, `OtpService` and
> every Email validator — row 22 was wrong (corrected above): the endpoint answers the opposite of
> what the row said. Also read while writing the Post area's tests (rows 24–32, 71, 73, 77):
> `ChangeCaptionHandler`, `ChangeRateHandler`, `DeletePostHandler`, `AdminDeletePostHandler`,
> `GetSpecificPostHandler`, `GetMyPostHandler`, `GetPostByAccountHandler`, `GetPostByShopHandler`,
> `GetPostByKeywordHandler`, `GetAllPostHandler`, `GetRelatedPostHandler` and every Post validator —
> row 25 was corrected above (404, never 403). Also read while writing the Comment area's tests
> (rows 33–38, 70, 74): `CreateCommentHandler`, `ChangeContentHandler`, `DeleteCommentHandler`,
> `AdminDeleteCommentHandler`, `GetSpecificCommentHandler`, `GetCommentByPostHandler`,
> `GetMyCommentHistoryHandler`, `GetAllCommentHandler`, `CommentService` (the `CreateComment` and
> `DeleteComment` worker) and every Comment validator — row 33 was wrong (corrected above): the
> `CommentAmount` increment is synchronous in the handler, not a worker responsibility; the worker
> only sends the notification. Also read while writing the Shop area's tests (rows 39–44, 78):
> `DeleteShopHandler`, `GetSpecificShopHandler`, `GetAllShopHandler`, `GetShopByKeywordHandler`,
> `GetShopByNameHandler`, `GetShopByAddressHandler`, `GetBestShopAroundHandler`, `Shop.Create`,
> `IndexConfiguration`'s Shop indexes and every Shop validator — rows 43 and 44 were wrong
> (corrected above): `/by-name` and `/by-address` are exact-match on the raw field, not normalized,
> the same class of gap as Account row 15/16. Also read while writing the Reaction area's tests
> (rows 45–50): `ReactPostHandler`/`UnreactPostHandler`/`ReactCommentHandler`/`UnreactCommentHandler`
> (all four are publish-only — the real behaviour lives in the workers), `ReactPostService`,
> `ReactCommentService`, `GetReactionByPostHandler`, `GetReactionByCommentHandler`, `BlockGuard` and
> every Reaction validator — no cell was wrong, but rows 45–48's H tier is trivial by design and the
> W tier (called directly, not through the queue, since nothing races a call nobody queued) is where
> rows 45/46/47/48's actual pinned behaviour lives. Also read while writing the Follow area's tests
> (rows 51–56): `FollowHandler`/`UnfollowHandler` (unlike Reaction's, these check self-follow
> synchronously at the H tier before anything is queued), `GetFollowerByAccountHandler`,
> `GetFollowingByAccountHandler`, `GetMyFollowerHandler`, `GetMyFollowingHandler` and every Follow
> validator — no cell was wrong; `FollowService` itself was already fully read (see the note above)
> and every case in its §3.5 row is now pinned by a test. Also read while writing the Save area's tests
> (rows 57–62): `SavePostHandler`/`SaveShopHandler`/`UnsavePostHandler`/`UnsaveShopHandler`,
> `GetMySavedPostsHandler`, `GetMySavedShopsHandler`, `SaveService` and every Save validator — rows
> 57/58 were wrong (corrected above: a double-save is `AppError.Conflict`, not a no-op) and row 61 was
> describing the wrong code path entirely (corrected above, and in §3.7). Also read while writing the
> Block area's tests (rows 79–81): `BlockAccountHandler`/`UnblockAccountHandler` (self-block checked
> synchronously at the H tier, the same pattern as Follow), `GetMyBlockedAccountHandler`, `BlockService`
> and every Block validator — no cell was wrong, but `BlockService`'s own half-applied-block repair
> (the same bug class `FollowService`'s repair fixes) had no test until this pass; row 79 now names it.
> Also read while writing the Notification area's tests (rows 67–68, 82–83): `RegisterDeviceHandler`,
> `UnregisterDeviceHandler`, `GetSpecificNotificationHandler`, `GetMyNotificationHandler`,
> `NotificationService`, `ApnsService`, `ApnsTokenProvider` and the Notification validators — row 83 was
> under-specified (corrected above: matched on the caller's own `AccountId`, not the token alone) and
> row 67 was missing the warm-path (cached-DTO) ownership check, a separate branch from the cold-path
> filter. Also read while writing the Report area's tests (rows 63–66): `CreateReportHandler`,
> `GetAllReportHandler`, `GetSpecificReportHandler`, `GetReportByTargetHandler` and every Report
> validator — row 66 was wrong (corrected above, and in §8): `IsInEnum()` is unreachable through the
> endpoint because ASP.NET Core's enum query binder already rejects an undefined value at layer 1. Also
> read while writing the Dashboard area's tests (rows 69, 76): `GetDashboardStatsHandler`,
> `GetDashboardActivityHandler` — row 69's "isolated collection" cell was wrong (corrected above, and in
> §4: a before/after delta in the shared serial collection, not a wipe) and `GetDashboardActivityQuery`
> has no validator at all (`windowHours` is a plain guard clause, the first statement in `Handle`, not a
> FluentValidation rule), which `test/unit/app/dashboard/GetDashboardActivityHandlerTests` pins directly
> against the handler rather than against a validator that does not exist. Every other cell is **derived
> from [`features.md`](./features.md), not read from
> the handler.** When
> writing a row's test, open the handler first: if it disagrees with the cell, the handler is right and
> the cell is corrected here. A cell must never be treated as a specification the handler should be
> changed to satisfy.

### Account

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 1 | `POST /Account` | anon | auth | U H E | missing or expired registration token → 401 `Unauthenticated`; duplicate email → 409 `Email already exists`; the token is consumed only **after** the insert commits, so a failed create leaves it reusable; password hashed to 60 chars |
| 2 | `GET /Account/me` | user | — | H E | served from the account cache on the second call |
| 3 | `PUT /Account/me/name` | user | — | U H E | identical name → **409** `New name must be different` (`ChangeNameHandler` wraps the domain refusal in `AppError.Conflict`, not a bare `Result.Fail` — corrected from an earlier, unread-handler guess of 400); `ProfileChanged` published |
| 4 | `PUT /Account/me/bio` | user | — | U H E | 2000-char ceiling |
| 5 | `PUT /Account/me/password` | user | — | U H E | wrong current password → **400** (`Result.Fail` with no status metadata, so it takes the default — worth pinning precisely because it is inconsistent with the 401s around it); an unchanged password → 409; every live refresh token for the account is **deleted**, not flagged, and one fresh pair is returned |
| 6 | `PUT /Account/me/email` | user | — | U H E | requires a verified OTP token; email uniqueness |
| 7 | `POST /Account/me/avatar` | user | upload | U H E | magic-byte rejection; R2 object created; `ProfileChanged` published |
| 8 | `DELETE /Account/me/avatar?url=` | user | — | H E | R2 object removed; a url not owned by the caller → 400/404 |
| 9 | `DELETE /Account/me` | user | — | H W E | soft delete; `DeleteAccount` message published; the cascade worker |
| 10 | `POST /Account/recover` | anon | auth | U H E | `Recover` refuses a live account; requires a verified OTP token |
| 11 | `PUT /Account/reset-password` | anon | auth | U H E | requires a verified OTP token; token single-use |
| 12 | `GET /Account` | admin | — | H E | pagination; soft-deleted excluded |
| 13 | `GET /Account/{accountId}` | user | — | H E | deleted account → 404 |
| 14 | `GET /Account/by-email` | admin | — | H E | — |
| 15 | `GET /Account/by-name` | user | search | H E | **not** a normalized match — `GetAccountByNameHandler` filters with `Builders<Account>.Filter.Eq(x => x.Name, request.name)`, an exact match on the stored Name; no `TextNormalizer` call exists on this path. That behaviour belongs to row 16 (`/Account/search`) only; the entry here was an unread-handler guess that conflated the two. A near match in casing or diacritics finds nothing, and — unlike `GetSpecificAccount` — no match is a 200 with an empty page, not a 404 |
| 16 | `GET /Account/search` | user | search | U H E | keyword signature hashed, not interpolated; **found gap, not a spec**: `char.IsLower('à')` is `true` in .NET (`NameTests.LowercaseDiacritic_IsAccepted`), so `Name.Create` stores diacritics as-is, but this handler only normalizes the incoming keyword — a diacritic name is unreachable by any search that doesn't type the exact diacritics back (`GetAccountByKeyword_DiacriticName_IsNotFoundByItsNormalizedForm` pins the current, unfixed behaviour). Fixing it is a production change (normalize on write, or reject diacritics in `Name.Create`) needing its own approval, same class of finding as the queue-boundary correlation-id gap in §7.3/§8 |

### Auth

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 17 | `POST /Auth/login` | anon | auth | U H E | unknown email, soft-deleted account and wrong password all → 401 `Invalid credentials`, byte-identical, so the endpoint does not enumerate accounts; access + refresh issued; `accountId` and `role` claims present |
| 18 | `POST /Auth/refresh` | anon | auth | H E | **reuse detection**: presenting an already-revoked token revokes *every* live token for that account and answers 401 — the highest-value test on this endpoint; expired and unknown → 401; rotation revokes the presented token under a filter requiring `IsRevoked == false`, so a concurrent second use matches nothing and gets 401; deleted account → 404 |
| 19 | `GET /Auth/refresh-token` | admin | — | H E | — |
| 20 | `GET /Auth/refresh-token/{id}` | admin | — | H E | — |
| 75 | `POST /Auth/refresh-token/{id}/revoke` | admin | — | H E | a revoked token cannot refresh |

### Email

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 21 | `POST /Email/send-for-register` | anon | email | H E | existing email refused; 6th send in an hour → 429 from `OtpService`, not the edge limiter |
| 22 | `POST /Email/send-for-verify` | anon | email | H E | **opposite of "refused"** — `SendEmailForVerifyHandler`'s own comment records that this endpoint used to answer 404 for an unknown address and was deliberately changed: an unknown email now returns 204, identically to a known one, so an unauthenticated caller on the password-reset path cannot use this endpoint to learn whether an account exists. A soft-deleted account is also *not* filtered out here (the recover flow needs it reachable), unlike every other account read in the codebase |
| 23 | `POST /Email/verify` | anon | email | H E | wrong code 3× then refused; correct code returns a registration token and stays verifiable within its TTL |

### Post

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 24 | `POST /Post` | user | upload | U H E | the six `CreatePostHandler` cases in §3.3 |
| 25 | `PUT /Post/{id}/caption` | user | — | U H E | not the author → **404, never 403** (`ChangeCaptionHandler` looks up the post scoped to the caller's id, so a mismatched author looks identical to a missing post); `CaptionNormalized` re-derived |
| 26 | `PUT /Post/{id}/rate` | user | — | U H E | `Amount` bounds rejected at the controller; `RecalculateShopRate` published |
| 27 | `DELETE /Post/{id}` | user | — | H W E | soft delete; cascade worker removes comments and images |
| 28 | `GET /Post/{id}` | user | — | H E | `IsReacted` / `IsSaved` reflect the viewer |
| 29 | `GET /Post/mine` | user | — | H E | cursor pagination |
| 30 | `GET /Post/account/{id}` | user | — | H E | blocked author → 404/empty |
| 31 | `GET /Post/shop/{id}` | user | — | H E | cursor pagination |
| 32 | `GET /Post/search` | user | search | H E | normalized keyword; blocked authors filtered, page still full |
| 77 | `GET /Post/related` | user | search | H E | affinity tier, trusted fallback tier, 1-hour cache, 300-id cap |

### Comment

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 33 | `POST /Comment` | user | upload | U H W E | images validated via `ImageUpload.ToStreams`; `CommentAmount` incremented **synchronously** in the handler (corrected — the earlier cell had this in the worker); the `CreateComment` worker only sends the notification, and skips it entirely when the commenter is the post's own author |
| 34 | `PUT /Comment/{id}` | user | — | U H E | not the author → **404, never 403** (same lookup-scoped-to-caller pattern as row 25) |
| 35 | `DELETE /Comment/{id}` | user | — | H W E | counter decremented; images removed |
| 36 | `GET /Comment/{id}` | user | — | H E | — |
| 37 | `GET /Comment/post/{postId}` | user | — | H E | blocked commenters filtered, page still full |
| 38 | `GET /Comment/mine` | user | — | H E | — |

### Shop

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 39 | `DELETE /Shop/{id}` | admin | — | H E | soft delete; excluded from the partial unique indexes afterwards |
| 40 | `GET /Shop/{id}` | user | — | H E | `shop:v2:` cache key carries `PlaceId` |
| 41 | `GET /Shop` | user | — | H E | — |
| 42 | `GET /Shop/search` | user | search | H E | normalized keyword |
| 43 | `GET /Shop/by-name` | user | search | H E | **exact match, not normalized** (`GetShopByNameHandler` filters on `x.Name`, not `x.NameNormalized` — corrected from an earlier, unread-handler guess; same class of gap as Account row 15/16: a diacritic or differently-cased name will not match) |
| 44 | `GET /Shop/by-address` | user | search | H E | **exact match, not normalized**, same gap as row 43 (`GetShopByAddressHandler` filters on `x.Address`) |
| 78 | `GET /Shop/around` | user | search | U H E | lat/long out of range → 400 (one of the two validated queries); `$nearSphere` ordering; null-`Location` shops absent |

### Reaction

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 45 | `POST /Reaction/post/{id}` → 202 | user | — | H W E | 202 + message published; worker pushes the reaction and increments |
| 46 | `DELETE /Reaction/post/{id}` → 202 | user | — | H W E | worker pulls and decrements; double-unreact leaves the counter at 0 — not via a floor guard like `DeleteCommentHandler`'s `Gt(..., 0)`, but because the pull's own filter requires `AnyEq(Reactions, accountId)`, so a second unreact matches no document at all and array/counter can't drift apart |
| 47 | `POST /Reaction/comment/{id}` → 202 | user | — | H W E | same, on the comment target |
| 48 | `DELETE /Reaction/comment/{id}` → 202 | user | — | H W E | same mechanism as row 46 |
| 49 | `GET /Reaction/post/{id}` | user | — | H E | embedded-array pagination; blocked reactors filtered |
| 50 | `GET /Reaction/comment/{id}` | user | — | H E | same |

### Follow

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 51 | `POST /Follow/{id}` → 202 | user | — | H W E | the full `FollowService` case list in §3.5 |
| 52 | `DELETE /Follow/{id}` → 202 | user | — | H W E | half-applied unfollow repaired; ordering with 51 on the shared queue |
| 53 | `GET /Follow/follower/{accountId}` | user | — | H E | embedded-array pagination; blocked filtered |
| 54 | `GET /Follow/following/{accountId}` | user | — | H E | same |
| 55 | `GET /Follow/me/follower` | user | — | H E | same |
| 56 | `GET /Follow/me/following` | user | — | H E | same |

### Block

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 79 | `POST /Block/{id}` → 202 | user | — | H W E | both arrays written; follow edges and reactions severed; blocked-set cache deleted; a pre-broken half-applied block falls through and still severs rather than exiting early (§3.5's `BlockService` row) |
| 80 | `DELETE /Block/{id}` → 202 | user | — | H W E | arrays cleared; cache deleted; edges **not** restored |
| 81 | `GET /Block/me` | user | — | H E | — |

Plus the cross-cutting block suite in §3.7, which is where the feature's real risk lives.

### Save

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 57 | `POST /Save/post/{id}` → 202 | user | — | H W E | worker pushes to `SavePosts`; **a double-save is not a no-op** — it returns `AppError.Conflict`, corrected from this row's original guess (§3.5's `SaveService` row) |
| 58 | `DELETE /Save/post/{id}` → 202 | user | — | H W E | worker pulls; unsaving something never saved fails with a bare, status-less `Result.Fail`, unlike save's `Conflict` |
| 59 | `POST /Save/shop/{id}` → 202 | user | — | H W E | same on `SaveShops` |
| 60 | `DELETE /Save/shop/{id}` → 202 | user | — | H W E | same |
| 61 | `GET /Save/me/post` | user | — | H E | **corrected — this row originally described `SaveMembership.SavedPostIdsAsync`'s `$project`/`$filter`/`$map` stage, which is a different code path** (feed enrichment's `IsSaved` flag, exercised by the Post area's tests, e.g. `GetAllPost`/`GetMyPost`) and does not run here. `GetMySavedPostsHandler` instead pages `SavePosts` with `EmbeddedArrayPaginator` and resolves the posts in a second query; because `SavedPost` never stores the author, the block filter can only apply to that second query, after `HasMore`/cursor are already fixed over the unfiltered saved set. A blocked author's saved post is therefore excluded but **not backfilled** — the one exception to §3.7's "page comes back full" property, found while writing this area's tests |
| 62 | `GET /Save/me/shop` | user | — | H E | ordinary pagination — `GetMySavedShopsHandler` filters no blocked authors at all, unlike row 61 |

### Report

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 63 | `POST /Report` | user | report | U H E | missing target → 404; reporting the same target twice → 409; a live report blocks trust grants (asserted in the `FollowService` trust case) |
| 64 | `GET /Report` | admin | — | H E | — |
| 65 | `GET /Report/{id}` | admin | — | H E | — |
| 66 | `GET /Report/target` | admin | — | U H E | **found gap, not a spec**: `GetReportByTargetValidator`'s `IsInEnum()` rule is unreachable through this endpoint. ASP.NET Core's own enum query binder rejects an undefined numeric value (`target=99`) exactly like it rejects an unparseable name (`target=nonsense`) — both fail model binding (layer 1, `ValidationProblemDetails`) before the validator (layer 3) ever runs. `IsInEnum()` is only exercised directly, by `GetReportByTargetValidatorTests` in `test/unit`. See §8 |

### Notification

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 67 | `GET /Notification/{id}` | user | — | H E | another account's notification → 404, on both the cold path (a Mongo read filtered to the caller's `ToAccountId`) and the warm path (`GetSpecificNotificationHandler` checks the *cached* DTO's `ToAccountId` too — a separate branch from the cold-path filter, so it needs its own test: read as the owner to populate the cache, then read the same id as a different account) |
| 68 | `GET /Notification/mine` | user | — | H E | pagination |
| 82 | `POST /Notification/device` | user | — | U H E | an upsert keyed on the token, not an insert: re-registering the same token from a second account **reassigns** it rather than throwing on the unique index, and clears `IsDeleted` so a reinstall revives a pruned row; `isSandbox` maps to `ApnsEnvironment` |
| 83 | `DELETE /Notification/device/{token}` | user | — | H E | soft-deletes the row (matched on token **and** the caller's own `AccountId`, so a token another account owns is untouched); APNs no longer targeted; a miss is still success by design |

Delivery itself is covered once, not per notification type: `NotificationService.CreateAsync` is called
directly (not through one of the workers that use it) and pins that it writes the row, marks it sent,
invalidates the recipient's list, and that a push failure is swallowed by design — both with no device
token registered (`ApnsService`'s own early return, not a failure) and with one registered. **Corrected**:
`Apns:PrivateKey` is no longer deliberately invalid — §3.4's `ApnsServiceTests` need
`ApnsTokenProvider.Current()` to actually succeed so its HTTP-degrade-path tests can run against a
scripted response, so the fixture now generates a real key the same way it already did for
`Maps:PrivateKey`. The registered-device case here forces the failure the same way §3.4 pins
`ApnsService` doing it: a scripted network failure propagates uncaught out of `ApnsService.SendAsync`,
and `NotificationService`'s `try`/`catch` around `_apnsService.SendAsync` is what stops it from failing
the whole notification. The SignalR-connected push itself — a client connecting with `?access_token=`
and receiving a live notification — is `NotificationHub`'s own pipeline test in §3.6.5, done
(`NotificationHubTests`).

### Dashboard and administrator additions

One grouping note: [`features.md`](./features.md)'s Contents table counts Dashboard as 1 endpoint and
Administrator additions as 7, placing #76 under the latter. Both are listed together below. The set of
83 is identical either way — no row is missing.

| # | Endpoint | Auth | Limit | Tiers | Pinned behaviour |
|---|---|---|---|---|---|
| 69 | `GET /Dashboard/stats` | admin | — | H E | counts exclude soft-deleted rows. Tested as a **delta**, not against an isolated collection — see §4's corrected carve-out |
| 76 | `GET /Dashboard/activity` | admin | — | U H E | `windowHours` bounds |
| 70 | `GET /Comment` | admin | — | H E | — |
| 71 | `GET /Post` | admin | — | H E | — |
| 72 | `DELETE /Account/admin/{id}` | admin | — | H W E | same cascade as 9, initiated by an admin |
| 73 | `DELETE /Post/admin/{id}` | admin | — | H W E | same cascade as 27 |
| 74 | `DELETE /Comment/admin/{id}` | admin | — | H W E | same cascade as 35 |

---

## 6. Non-functional checks

Done — all six, in `test/unit/ArchitectureTests.cs` except the last, `test/integration/QueueRegistrationTests.cs`. Small, cheap, and each one guards a rule that is currently held by hand:

| Check | Where | Rule from |
|---|---|---|
| `domain` has no project or package references | `test/unit`, reflection over `typeof(Account).Assembly.GetReferencedAssemblies()` | `architecture.md` §1.1 |
| `app` references no `infra` type | same, asserting no referenced assembly named `infra` (with a positive control: `app` *does* reference `domain`, or a typo'd forbidden name could never fail this) | the inversion boundary |
| every `IRequest` type in `app` is named `*Command` or `*Query` | `test/unit` | `WriteCancellationBehavior` depends on it silently |
| every `*Command`/`*Query` has a registered handler | `test/unit`, inspecting the `IServiceCollection` `AddApp` builds for a matching closed `IRequestHandler<,>` descriptor — **not** by resolving from the container, which would fail on missing infra dependencies (`IMongoDbContext`, `IRedisService`) for reasons that have nothing to do with handler registration | a missing registration is a runtime 500 |
| every controller action is either `[AllowAnonymous]` or covered by the class-level `[Authorize]` | `test/unit`, reflection over `api` | the auth tier is attribute-held |
| every `QueueName` member has exactly one `AddQueueConsumer` registration | `test/integration`, reflecting the private `_queueName` field off every resolved `QueueConsumerHost<>` `IHostedService` instance — grouping by closed generic type first would collapse `ReactionMessage`'s two registrations (react-post, react-comment; `SaveMessage` and `CommentMessage` are each shared the same way) into one and hide a missing consumer | a queue nobody consumes fills silently |

Performance is **not** part of the gate. `PerformanceBehavior` warns above 500 ms and
`PerformanceMiddleware` logs per request; that telemetry is the production signal, and asserting a
latency budget inside a container-backed test suite produces flaky failures rather than information.

---

## 7. CI gate — what "passes for production" means

The phrase cannot mean "the tests pass", because none exist. It is defined here as a gate:

```bash
dotnet build src/Bump.sln -c Release          # must succeed
dotnet test test/unit -c Release              # every push, no Docker required
dotnet test test/integration -c Release       # every push where Docker is available; required on main
```

Rules for the gate:

1. **A red test blocks the merge.** No skipped, no `[Fact(Skip=…)]` left behind — a test that cannot
   pass is deleted along with the claim it was making.
2. **`TreatWarningsAsErrors` is explicitly not part of the gate.** The build currently emits 55 nullable
   warnings (CS8600/CS8604/CS8618), largely because `DomainResult<T>.Value` is `T?` with no flow
   attribute (`project-rules.md`). Turning warnings into errors is a refactor with its own approval
   requirement under `AGENTS.md`, not a side effect of adding tests.
3. **Coverage is measured on every run, but only three things about it are enforced** — see §7.1. A
   single global percentage threshold is not one of them.
4. **The integration suite must be runnable locally with one command.** Note that it does **not** need
   `docker compose up -d` — Testcontainers starts and disposes its own containers per run, from the
   same images `docker-compose.yml` names, and needs only a reachable Docker daemon. The compose file
   stays what `developer-guide.md` §6 describes: the persistent environment for `dotnet run`. On this
   machine the daemon is Colima-backed, and starting Colima is the developer's own step (`AGENTS.md`).
   If a developer cannot run the suite, only CI will, and it will rot.
5. **Any new feature adds its row to §5 in the same change.** This is the mechanism that keeps "every
   feature is tested" true a year from now, and it is the only part of this document that is a standing
   obligation rather than a one-time build.

### 7.1 Coverage

**Coverage and the §5 matrix answer different questions, and neither substitutes for the other.** The
matrix answers *is feature 52 tested at all* — it catches a whole endpoint nobody wrote a test for.
Coverage answers *of the code feature 52 executes, how much did the test actually walk* — it catches
the four refusal branches inside `ProcessUnfollowAsync` that the happy-path test never reaches. A
feature can have a matrix row and 30% branch coverage; that is a real gap and only coverage sees it.

#### Branch coverage is the number; line coverage is decoration

This matters more here than in an average codebase. `CreatePostHandler` is ~250 lines containing 19
branch points. One happy-path integration test executes most of those lines and reports a healthy
line-coverage figure for the file — while every one of its six documented failure paths (place
unresolved, `Shop.Create` invalid, the duplicate-key race, `Post.Create` invalid, the insert throw, the
adopt collision) stays dark, and those failure paths are the entire reason the handler is written the
way it is. `FollowService` has the same shape: 13 branch points, of which the interesting ones are
`MatchedCount == 0` and `MatchedCount == 1`.

**Every target below is branch coverage.** Line coverage is reported because the tooling produces it
free, and is not a gate.

#### Tooling

Nothing beyond what the xUnit template already ships. The exclusion filter below is done —
`coverlet.runsettings` at the repository root:

```bash
dotnet test test/unit        -c Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings
dotnet test test/integration -c Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings

reportgenerator -reports:"**/coverage.cobertura.xml"                 -targetdir:coverage                 -reporttypes:"Html;Cobertura;MarkdownSummaryGithub"
```

`coverlet.collector` as a `PackageReference` in both test projects (already present),
`dotnet-reportgenerator-globaltool` as a local tool (not installed — the merge-and-report step is a CI
concern, per §7's own "don't invent a pipeline" boundary; `coverlet.runsettings` alone is verified to
work by running the two `dotnet test` lines above and inspecting the resulting `coverage.cobertura.xml`).
**The two runs must be merged before the number means anything** — `test/unit` alone reports
`app/feature` at near zero, because by §1.1 no handler has a unit tier at all.

#### What is excluded, and why not by attribute

Done. 62 of the 415 `.cs` files in `src/` are declarations with no executable branch: 21 DTOs, 13
AutoMapper profiles, 9 queue messages, 16 request records, 3 enums — counts confirmed by directory
listing while writing `coverlet.runsettings`, matching this row exactly. Including them inflates the
percentage by roughly 15% of the file count for zero information. Excluded alongside them: `Program.cs`
and the four `DependencyInjection.cs` composition roots, which the boot smoke test walks once and which
say nothing as a ratio.

These go in `coverlet.runsettings`'s `ExcludeByFile` filter (`**/dto/*.cs`, `**/mapping/*.cs`,
`**/message/*.cs`, `**/request/*.cs`, `**/enum/*.cs`, `**/Program.cs`, `**/DependencyInjection.cs`),
**not** as `[ExcludeFromCodeCoverage]` attributes — the attribute route is 67 production-file edits to
serve a reporting concern, and `project-rules.md` has no precedent for attributes on those types.
Verified directly: running `dotnet test test/unit` with and without `--settings coverlet.runsettings`
and diffing the two `coverage.cobertura.xml` files shows every excluded path absent from the filtered
run and present in the unfiltered one, and nothing else moves.

#### Targets per layer

| Scope | Branch target | Why that number |
|---|---|---|
| `domain` | **100%** | Pure, no I/O, and every branch is a `DomainResult.Failure` a caller can actually reach. Anything less is a genuinely untested invariant, and §3.1 already enumerates every one of them |
| `app/common` (pure) | ≥ 90% | `CursorCodec`, the page-size clamp, `CacheKey`, `BlockedAccountSet.Signature`, the four behaviors — all unit-testable, so the residual should be small |
| `app/feature` handlers | ≥ 75% | Every `AppError.*` return is a branch and should be covered. The residual is Mongo-throw paths that are awkward to provoke even against a real server |
| `infra/queue` workers | ≥ 80% | The refusal branches *are* the feature — already following, already blocked, already reacted, the half-applied repair. A worker at 50% is one whose refusals are untested |
| `infra/service` adapters | ≥ 70% | Degrade-on-failure paths are reachable through the scripted `HttpMessageHandler`; the full S3 and RabbitMQ SDK error surface is not, and chasing it is not worth the fixtures |
| `api` controllers | ≥ 90% | Thin, but not branchless: the value-object construction in §3.6.1's layer 2 is a real branch per action, and the §3.6.2 case tables reach all of them. A low number here means input cases are missing |

#### What is actually enforced

In the order it should be adopted, because the order is what makes it survivable:

1. **`domain` at 100% branch, from the first slice.** A hard floor
   (`/p:Threshold=100 /p:ThresholdType=branch` scoped to that assembly). It is achievable on day one —
   §9's slice 2 delivers exactly this — so it never needs a grace period.
2. **Diff coverage on changed lines: ≥ 80% branch, for `app` and `infra`.** This is the real gate. New
   and modified lines must be covered; the existing untested mass is not held against the change that
   touches a file next to it. It is the only rule that stops erosion without blocking every merge on
   day one.
3. **A ratchet everywhere else.** The merged branch percentage per assembly may not fall below the base
   commit's, and the current floors are committed to the repository so they rise as work lands.

**Explicitly not enforced: a fixed global percentage.** On day one it is unmeetable and gets waived; a
waived gate teaches everyone that gates are advisory. After the suite lands it is trivially met and
stops carrying information. The ratchet does the same job without either failure mode.

#### What coverage cannot see

Stated plainly, because a green percentage is the most confidently wrong signal available:

- **A wrong Mongo filter is the same line as the right one.** `SaveMembership.SavedPostIdsAsync` at
  100% branch coverage is still silently broken if the Guid representation is wrong — it matches
  nothing, throws nothing, and reports every post as unsaved. Only an assertion on the returned set
  catches it.
- **A missing index is not a line at all.** `GetBestShopAround` covered at 100% still fails in
  production if the 2dsphere index was never created.
- **Coverage of the cache-invalidation call does not check the key it invalidated.** Bumping the wrong
  `(entity, filterSignature)` pair executes the same statement.

That is why coverage is third in this document's hierarchy, behind the §5 feature matrix and the §6
structural checks — it measures how much of the code ran, never whether what ran was correct.

### 7.2 What a fully green suite does and does not establish

Worth stating flatly, because "the tests pass" is routinely read as a stronger claim than it is.

**What it establishes.** Each of the 83 features behaves as its §5 row says *on the inputs the tests
supply*; the domain invariants hold on every branch; the layer contracts hold (statuses, cursor shape,
cache keys, queue payloads); the refusal paths return the statuses the API promises; and the two
concurrency races written down in §3.3 and §3.5 resolve the way the code's comments claim.

**What it cannot establish, however green.** Tests falsify; they do not prove. The specific holes in
*this* system, none of which shrink by adding more tests of the kind above:

- **A wrong Mongo filter, index or Guid representation is invisible** — §7.1 covers this class. It is
  the largest one, because by §1.1 that is where most of the logic lives.
- **The two-sided writes are not transactional.** `FollowService` and `BlockService` repair
  half-applied state precisely because Mongo is not asked for a transaction. A test can prove the
  repair works when drift exists; nothing can prove drift never occurs.
- **Queue delivery is at-least-once with a 24-hour Redis idempotency key.** If that key is evicted or
  Redis is flushed, a redelivery re-applies. The suite pins the dedupe behaviour while the key is
  present; the window where it is not is a property of the design, not a test gap.
- **A worker returning a failed `Result` is terminal** — acked, logged at warning, never retried,
  never dead-lettered (§3.5). Tests pin that this is what happens. They cannot make it safe, and a
  worker that begins refusing every message is visible only as a *rate* of that warning.
- **Scale-dependent behaviour is not reachable.** The 300-account cap in `BlockedAccountSet`, unbounded
  `SavePosts`/`Followers` arrays, and the affinity aggregation's cost over a large reaction history all
  behave correctly at test sizes by construction.
- **Multi-replica, real SaaS contracts and wall-clock behaviour** — §8.

### 7.3 Verifying the system can be debugged

Debuggability is an observability property, not a test property: the suite does not create it. What the
suite *can* do is prove the diagnostic surface still works, which matters because that surface is
exactly what nobody notices is broken until the night they need it.

What exists in the code today, and the test that pins each. Done except the JSON template row,
excluded by §1.3:

| Affordance | Where | Test |
|---|---|---|
| `X-Correlation-ID` echoed on every response; an inbound value accepted only if ≤64 chars and `^[a-zA-Z0-9-]+$`, else a fresh Guid is minted | `CorrelationIdMiddleware` | `CorrelationIdMiddlewareTests`: a valid id is echoed back; an over-long or punctuation-bearing id is **replaced**, not echoed — this is an injection guard on a value that lands in every log line, so the rejection cases matter more than the happy one |
| That id pushed into Serilog `LogContext`, so every event inside the request carries `CorrelationId` | same | `CorrelationIdMiddlewareTests`: a `CapturingLogEventSink` registered as a singleton `ILogEventSink` is picked up by `Program.cs`'s own `.ReadFrom.Services(services)` call with no production change, and the id is asserted present on the "Handling `LoginCommand`" event `LoggingBehavior` logs from inside the MediatR pipeline — not only the response header |
| One-line JSON per event with properties flattened alongside `message`/`level` | `Program.cs` `LogTemplate` | not tested (§1.3) — the template is a formatting concern |
| Slow-request and slow-handler warnings above 500 ms | `PerformanceMiddleware`, `PerformanceBehavior` | `PerformanceBehaviorTests`: a real delayed `next` (no injected clock exists, per §4/§8), above threshold warns, below does not. Scoped to the behavior only, per this row's own wording — `PerformanceMiddleware` is untested |
| A queue message that did not apply, logged with `QueueName`, `MessageId` and the error | `QueueConsumerHost.HandleAsync` | `RabbitMQTransportContractTests.QueueConsumerHost_FailedResult_...`: a refusal emits exactly one warning carrying all three, asserted directly against a real `QueueConsumerHost<>` rather than through `FollowService` specifically — the mechanism is generic to every worker. This is the only trace such a message ever leaves, so a silent regression here is a blind spot, not a cosmetic one |
| Half-applied follow/unfollow repair warnings | `FollowService` | `FollowEndpointTests.ProcessFollow_HalfAppliedEdge_IsRepaired` asserts the warning as well as the state |
| `GET /health` with three named checks | `infra/health/` | **Corrected**: `MapHealthChecks("/health")` with no `ResponseWriter` returns the bare string `"Healthy"` — the three check names are not in the body. `HealthCheckTests` (§3.4) asserts 200 and the body correctly; asserting names present would require changing `Program.cs`, which this plan does not do without its own approval |

**The gap this question exposes, and it is a real one:** the correlation id does not survive the queue
boundary. `LogContext.PushProperty` is scoped to the request, and every `*Message` record carries a
`MessageId` and nothing else — there is no correlation field on `FollowMessage`, `BlockMessage`,
`ReactionMessage`, `SaveMessage`, `CommentMessage`, `PostMessage`, `AccountMessage`, `ProfileMessage`
or `RecalculateShopRateMessage`. So for the twelve endpoints that answer 202, the HTTP request can be
traced and the worker can be traced, but **nothing joins them**: given a user reporting that their
follow never took effect, there is no query that goes from their request to the worker log line that
refused it.

Carrying a correlation id on the message records and re-pushing it in `QueueConsumerHost.HandleAsync`
would close it. That is a production change across ten message records and the consumer host, so under
`AGENTS.md` it needs its own proposal and approval — it is recorded here as a finding, not scheduled as
part of this plan.

---

## 8. Known gaps this plan does not close

Stated rather than hidden, because each is a real limit on what the suite can claim:

- **Time is not injectable.** `BaseEntity` and the services stamp `DateTime.UtcNow` directly, so
  expiry-boundary behaviour is tested by seeding past timestamps, not by controlling the clock.
  An `IClock` abstraction is a refactor needing its own approval.
- **Multi-replica behaviour is untestable in one host.** The SignalR Redis backplane and the
  per-process RabbitMQ channel cache both exist because there is more than one replica in production;
  a single test host cannot exercise either.
- **The unhealthy branch of the three health checks** is not tested, because stopping a shared container
  breaks the rest of the assembly.
- **Real Resend, APNs and Apple Maps responses** are stubbed. The stubs encode what the providers are
  believed to return; a provider changing its contract is invisible to this suite.
- **`SoftDeletePurgeHost`'s retention sweep** is tested by invoking it directly rather than by waiting
  for its timer.
- **No trace survives the queue boundary.** See §7.3: the request correlation id is not carried on any
  queue message, so asynchronous work cannot be tied back to the request that caused it. This is a
  property of the system, not of the suite, and closing it is a production change.
- **A diacritic name is unsearchable.** Found while writing the Account tests, not designed for: `Name`
  accepts Vietnamese diacritics (`char.IsLower('à')` is `true`), but `GetAccountByKeywordHandler`
  normalizes only the incoming keyword, never the stored field — see §5 row 16. A production fix
  (normalize `Name` on write, or reject diacritics at `Name.Create`) needs its own approval; the suite
  only pins that this is the current behaviour.
- **`GET /Shop/by-name` and `/by-address` are exact-match, unlike their own `/search` sibling.**
  Found while writing the Shop tests: `GetShopByNameHandler`/`GetShopByAddressHandler` filter on the
  raw `Name`/`Address` fields, not `NameNormalized`/`AddressNormalized` — see §5 rows 43–44. A caller
  who reasonably expects the same case/diacritic tolerance `GET /Shop/search` gives will get an empty
  page instead. Whether this is intentional (an exact lookup for a client that already has the
  canonical string) or an oversight is a product decision; the suite only pins the current behaviour.
- **`GetReportByTargetValidator`'s `IsInEnum()` rule is dead code on `GET /Report/target`.** Found while
  writing the Report tests: ASP.NET Core's own enum query binder already rejects a `target` value that
  is not a defined `ReportTarget` member — a numeric value like `99` fails model binding with "The value
  '99' is invalid" exactly like an unparseable name does, so both are layer-1 `ValidationProblemDetails`
  and the request never reaches the validator. `IsInEnum()` can only be exercised by calling
  `GetReportByTargetValidator` directly, which `test/unit/app/report/GetReportByTargetValidatorTests`
  does — see §5 row 66. Not a bug to fix, just a rule that adds test surface without adding a reachable
  code path; removing it would be a validator simplification, not a behaviour change.
- **`GetMySavedPosts` is the one filtered read where blocking shortens a page instead of backfilling it.**
  Found while writing the Save tests, and the one correction to §3.7's own stated property: see §5 row 61
  and §3.7.
- **A double-save is a failed `Result`, not a silent no-op.** Found while writing the Save tests: earlier
  drafts of §3.5 and §5 rows 57/59 described it as a no-op before the handler was read; `SaveService`
  returns `AppError.Conflict` instead. The state ends up identical either way, so nothing production-side
  needs fixing — only this document's earlier guess was wrong.

---

## 9. Suggested first slice

Not the whole matrix at once. The order that gets the most signal per unit of work:

1. **Bootstrap** (§2) — two projects, the `InternalsVisibleTo` edits, the `Program` marker, the
   container fixture. Verify: `dotnet test` runs and reports zero tests.
2. **All of `domain`** (§3.1) — no containers, no fixtures, and it locks down every invariant the rest
   of the system trusts. Verify: green; branch coverage on the `domain` assembly reports 100%, which
   is the §7.1 floor going live; and mutating `Shop.Create`'s lat/long pair check turns it red.
3. **One integration test of `CreatePostHandler`'s shop-adoption and duplicate-key race** (§3.3) — the
   single most valuable integration test in the plan, because it exercises place resolution, the R2
   cleanup path, the partial unique index and cache invalidation in one flow. Verify: green against
   real containers, and red when `IndexInitializer` is removed from the host.
4. **The auth-tier table over all 83 routes** (§3.6) — one test, complete coverage of the attribute
   that protects everything else.

Everything after that is the matrix, area by area, and can be split however the work is scheduled.
