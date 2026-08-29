# API Operation

This document explains how the `api` layer actually operates end to end: how an incoming HTTP request becomes a dispatched MediatR message, how authentication/authorization/rate limiting gate it, and how the `Result`/`Result<T>` a handler returns (or a rejection that never reaches a handler) becomes an HTTP response. It does not cover layer boundaries or the dependency rule (see `doc/architecture.md`) or naming conventions (see `doc/project-rules.md`, which also holds the API Layer Conventions — the status-mapping *rule* lives there; this document covers the *mechanism*). For what happens after dispatch, see `doc/app-operation.md`.

`api` is the entry-point layer and composition root: a thin ASP.NET Core Web API host with no business logic of its own. It has 10 controllers — one per `app` feature area (`account`, `auth`, `comment`, `email`, `follow`, `notification`, `post`, `reaction`, `report`, `shop`) — exposing 64 actions in total, backed by 13 request records for the actions whose body doesn't bind directly to a route/query parameter or an existing `app`/`domain` type. `api` also hosts one SignalR hub, `NotificationHub` (`infra`, mapped at `/hubs/notification`) — see "Composition Root" below.

## Core Patterns

### 1. Thin controller, MediatR dispatch

Every controller is a `[ApiController]`, `public sealed class <Feature>Controller : ControllerBase` with exactly one constructor-injected dependency, MediatR's `ISender`:

```csharp
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ShopController : ControllerBase
{
    private readonly ISender _sender;

    public ShopController(ISender sender)
    {
        _sender = sender;
    }
}
```

Every action follows the same three steps: build a command/query record from the request, `await _sender.Send(...)`, translate the returned `Result`/`Result<T>` to an `ActionResult` via one of the `ResultExtensions` methods. No controller injects `IMongoDbContext`, `IRedisService`, or any other `infra` port; `ISender` plus the three `domain` value-object factories (Pattern 4 below) are the entire outbound surface — matching `doc/architecture.md`'s rule that `api` only ever exchanges one use-case message with `app` and never reaches into `domain`/`infra` directly. There is no base controller — the nine controllers repeat this shape independently rather than sharing one through inheritance.

### 2. Routing and model binding

- **Route template**: `[Route("api/[controller]")]` on every controller, so a route is always `api/<ControllerName-minus-Controller>` (`AccountController` → `api/Account`), with per-action `[HttpGet]`/`[HttpPost]`/`[HttpPut]`/`[HttpDelete]` templates nested under it (e.g. `[HttpPut("me/name")]` → `PUT api/Account/me/name`).
- **Route-constrained ids**: every path segment carrying a `Guid` uses the `:guid` route constraint (`{postId:guid}`), so a malformed id fails model binding before the action body runs (see "What never reaches the pipeline" below), rather than reaching a handler as an unparsable string.
- **Cursor pagination parameters**: every action returning a `CursorPageResult<T>` repeats the same two `[FromQuery]` parameters — `string? cursor` and `int pageSize = 20` — and wraps them into a `new CursorPageRequest(cursor, pageSize)` passed to the query record. This is a repeated shape, not a shared parameter object, matching `app`'s own `CursorPageRequest`/`CursorPageResult<T>` pair (see `doc/app-operation.md`).
- **Multipart form binding**: the three actions that accept a file (`AccountController.ChangeAvatar`, `PostController.CreatePost`, `CommentController.CreateComment`) bind `IFormFile`/`List<IFormFile>?` parameters (with `[FromForm]` on the accompanying scalar fields for `CreatePost`/`CreateComment`, since those actions have no request record to bind the rest of the form to) rather than a JSON body.
- **No request record for path/query-only actions**: an action whose input is entirely route segments and/or query parameters (every `Delete`/`Get*` action except the ones needing a body) takes those values as plain method parameters — a request record only exists for actions that also need a JSON body.

### 3. Authorization

Every controller carries a class-level `[Authorize]`, so an action is authenticated-only-by-default; two attributes override that:

- **`[AllowAnonymous]`** (6 actions): `AccountController.CreateAccount`, `AuthController.Login`, `AuthController.RequestNewAccessToken`, `EmailController.SendForRegister`, `EmailController.SendForVerify`, `EmailController.Verify` — the only actions reachable before a caller has credentials, which is exactly the account-creation/login/OTP-verification bootstrap sequence.
- **`[Authorize(Roles = nameof(Role.Admin))]`** (8 actions): `AccountController.GetAllAccount`/`GetAccountByEmail`, `AuthController.GetAllRefreshToken`/`GetSpecificRefreshToken`, `ReportController.GetAllReport`/`GetSpecificReport`/`GetReportByTarget`, `ShopController.DeleteShop`. `nameof(Role.Admin)` reads the `domain` enum directly (see `doc/domain-operation.md`), so the attribute cannot drift from the enum's member names; it works at runtime only because `AddApi` disables inbound claim mapping (`MapInboundClaims = false`) and binds `RoleClaimType` to `JwtService.RoleClaimType` (see "Composition Root" below), so the `role` claim `JwtService` writes at login survives token validation under the exact name this attribute checks. `ReportController.CreateReport` is the one report action open to any authenticated user — reporting is not an admin-only action, only reviewing reports is.
- All 50 remaining actions require only an authenticated caller — no role check.

### 4. Domain value-object construction at the boundary

Several `app` commands/queries take a `domain` value object (`Name`, `Email`, `Amount`) rather than a primitive, because that is what the downstream `domain` factory or entity field expects. Since a value object's `private` constructor makes it uninstantiable from `api` directly (see `doc/domain-operation.md`), the controller calls the value object's own `Create` and threads the result through the same `Result`/`ResultExtensions` pipeline a handler failure would use, before a MediatR message is even built:

```csharp
var nameResult = Name.Create(request.Name);
if(!nameResult.IsSuccess)
    return Result.Fail<AccountDto>(nameResult.Error!).ToActionResult();

var result = await _sender.Send(new CreateAccountCommand(request.RegistrationToken, nameResult.Value!, request.Password, request.Bio), cancellationToken);
return result.ToActionResult();
```

This applies to `Name` (`AccountController.CreateAccount`/`ChangeName`/`GetAccountByName`), `Email` (`AccountController.GetAccountByEmail`, `AuthController.Login`, `EmailController.SendForRegister`/`SendForVerify`/`Verify`), and `Amount` (`PostController.CreatePost`/`ChangeRate`). A `domain.Create` failure and an `app` handler failure are therefore indistinguishable to the client — both surface as the same `ProblemDetails` shape through the same status-mapping rule. This is deliberately asymmetric with keyword-search actions: `GetAccountByKeyword`, `GetPostByKeyword`, `GetShopByKeyword` pass the raw `keyword` string straight into the query record with no `Name`/`Email` construction, since a keyword is matched with a regex in `app`, not parsed into a value object (see `doc/app-operation.md`).

### 5. File uploads become `(Stream, string)` tuples

`app`'s commands accept a file as `Stream`/`(Stream stream, string fileName)` — the port shape `IR2Service.UploadAsync` needs (see `doc/infra-operation.md`) — not as an ASP.NET Core `IFormFile`, since `app` cannot reference ASP.NET Core. Every uploading action converts at the boundary:

- `ChangeAvatarCommand(Stream avatars, string fileName)` ← `avatar.OpenReadStream()`, `avatar.FileName`.
- `CreatePostCommand(..., List<(Stream stream, string fileName)>? images)` / `CreateCommentCommand(..., List<(Stream stream, string fileName)>? images)` ← `images?.Select(x => (x.OpenReadStream(), x.FileName)).ToList()`.

The stream is opened in the controller, carried inside the command record through `Send`, and read by `R2Service.UploadAsync` inside the handler (see `doc/infra-operation.md`) — no action in `api` disposes it explicitly.

### 6. `Result` → HTTP response

`api/common/ResultExtensions.cs` is the single place a `FluentResults.Result`/`Result<T>` becomes an `ActionResult`, via three extension methods:

| Method | On success | On failure |
|---|---|---|
| `ToActionResult<T>()` | `200 OK` with `result.Value` | `ProblemDetails`/`ValidationProblemDetails`, see below |
| `ToActionResult()` (non-generic) | `204 No Content` | same |
| `ToAcceptedResult()` | `202 Accepted` | same |

`ToAcceptedResult()` is used by exactly six actions — `FollowController.Follow`/`Unfollow`, `ReactionController.ReactPost`/`UnreactPost`/`ReactComment`/`UnreactComment` — because their handlers only publish a message to RabbitMQ and return `Result.Ok()` immediately; the actual follow/react mutation happens later, out-of-band, in `FollowService`/`ReactionService` (see `doc/infra-operation.md`). `202 Accepted` is the accurate status for "the request was accepted for processing, not yet applied," where `204 No Content` (used by every other void-returning action) would imply the mutation already committed. No action in the codebase constructs a `201 Created` response.

On failure, `ToProblemResult` branches on error count:

- **One error** (the normal case — a handler returns on its first failure, see `doc/app-operation.md`): the message is classified by `ResultStatusMapper.Resolve` into a status code, wrapped in a `ProblemDetails { Status, Title, Detail = message }`.
- **More than one error**: this only happens when `ValidationBehavior` (not a handler) rejected the request, since only FluentValidation accumulates multiple `Reasons` on one `Result` — the response becomes a `ValidationProblemDetails` with every message folded into `ModelStateDictionary` under an empty key, always `400 Bad Request` regardless of what the individual messages say.

**Validator wording participates in this same status contract, not just handler wording** — `ResultStatusMapper.Resolve` runs on any *single*-error failure, and a validator that fails alone (no other rule on the same command also fails) produces exactly one error. Two validators currently collide with the `"token"` keyword this way: `CreateAccountValidator`/`ChangeEmailValidator`'s `"RegistrationToken is required"` and `RequestNewAccessTokenValidator`'s `"RefreshToken is required"` — a request missing that one field returns `401 Unauthorized`, not `400 Bad Request`, even though nothing about the request was actually unauthenticated. This is an accurate description of current behavior, not a proposed fix.

### 7. What never reaches this pipeline

Several failure paths short-circuit before an action body — and therefore `ResultExtensions`/`ResultStatusMapper` — ever runs:

- **Model-binding/validation failure**: `[ApiController]` triggers ASP.NET Core's automatic `400 Bad Request` for a malformed request body or a missing required parameter, before the action executes.
- **Route-constraint miss**: a non-`Guid` value against a `{x:guid}` segment doesn't match the route at all, so it falls through to ASP.NET Core's default `404 Not Found` rather than reaching an action with an unparsed string.
- **Missing/invalid/expired bearer token, or a role mismatch**: the JWT bearer authentication/authorization middleware (registered in `AddApi`, run in `Program.cs` before `MapControllers`) returns `401 Unauthorized`/`403 Forbidden` on its own, before an action body runs.
- **Rate limit exceeded**: `UseRateLimiter()` rejects the request with `429 Too Many Requests` before the action body runs — see Pattern 8 below. Its response is still a `ProblemDetails` body for consistency, but it is built directly in `AddRateLimiter`'s `OnRejected` callback, not by `ResultExtensions`, because there is no `Result` to translate.
- **Unhandled exceptions**: `Program.cs` registers no `UseExceptionHandler`, no `AddProblemDetails`, and no custom exception-handling middleware of any kind. An exception thrown inside an action or a handler therefore propagates to ASP.NET Core's/Kestrel's built-in default, not to a documented `api`-owned response shape.

### 8. Rate limiting

`AddApi` calls `ConfigureRateLimiting` (`api/DependencyInjection.cs`), which registers .NET's built-in `Microsoft.AspNetCore.RateLimiting` middleware with two tiers that both apply to a request at once — a request must pass the global limiter *and*, if the action carries `[EnableRateLimiting]`, its named policy:

- **Global limiter** — a `FixedWindowRateLimiter`, `200` permits per `1` minute, applied to every request regardless of attributes via `options.GlobalLimiter`. Fixed window is deliberately chosen here over sliding: this tier is blanket anti-spam, not anti-brute-force, so the boundary-burst behavior fixed window is known for (up to ~2x the limit for one second at a window edge) is an acceptable cost for the cheaper per-partition primitive — and partition count at this tier is unbounded (every distinct IP/account hitting the API), which is exactly where that cheapness matters.
- **Named policies** (`RateLimiterPolicy` in `api/common/`, `const string` — not the mutable `static string` shape `CollectionName`/`QueueName` use, because `[EnableRateLimiting(...)]`'s argument must be a compile-time constant) — all four are `SlidingWindowRateLimiter`s (`SegmentsPerWindow = 6`), chosen because their limits are tight enough that the fixed-window boundary trick would be a real bypass, not noise:

  | Policy | Limit | Applied to |
  |---|---|---|
  | `RateLimiterPolicy.Auth` | 10/min | `AccountController.CreateAccount`, `AuthController.Login`/`RequestNewAccessToken` |
  | `RateLimiterPolicy.Email` | 5/min | `EmailController` (class-level — all three actions) |
  | `RateLimiterPolicy.Search` | 20/min | `AccountController.GetAccountByKeyword`, `PostController.GetPostByKeyword`, `ShopController.GetShopByKeyword` — the three keyword-search actions `doc/infra-operation.md` documents as an unindexed regex triggering a full collection scan |
  | `RateLimiterPolicy.Upload` | 10/min | `AccountController.ChangeAvatar`, `PostController.CreatePost`, `CommentController.CreateComment` — the three actions that write to R2 storage (see Pattern 5 above) |

- **Partition key**: `Auth`/`Email` always partition by IP, since their actions are `[AllowAnonymous]` — there is no reliable identity yet. The global limiter and `Search`/`Upload` partition by the `accountId` claim when the caller is authenticated, falling back to IP otherwise (`ResolveClientKey`) — this keeps one heavy authenticated user's usage from being blamed on everyone sharing their IP (e.g. mobile carrier NAT), and vice versa. All IP/account resolution reads `HttpContext.Connection.RemoteIpAddress`/`HttpContext.User`, both of which depend on `UseForwardedHeaders()`/`UseAuthentication()` having already run — see "Composition Root" below for why ordering matters here.
- **Rejection behavior**: `RejectionStatusCode = 429`, `QueueLimit = 0` on every limiter (fail fast — a spam-prevention limiter should reject immediately, not queue and delay the request). `OnRejected` sets a `Retry-After` header from the lease's `MetadataName.RetryAfter` metadata when present, logs a structured warning (`ClientKey`, `Method`, `Path`) via a `"RateLimiting"`-categorized `ILogger`, and writes a `ProblemDetails` body (`Status: 429, Title: "Too Many Requests"`) with `contentType: "application/problem+json"` explicitly passed to `WriteAsJsonAsync` — the parameterless overload would otherwise default to plain `application/json`, breaking the "same shape as every other error" goal this exists for.

### 9. Observability: correlation IDs, structured logging, and request timing

Three additions sit alongside rate limiting, all for making a rejected or slow request traceable in production rather than changing request handling itself:

- **Structured logging via Serilog**: `Program.cs` bootstraps a minimal `Log.Logger` (Console sink) before `WebApplication.CreateBuilder` runs, so startup failures are still logged, then replaces it via `builder.Host.UseSerilog(...)` with one read from the `Serilog` section of `appsettings.json`/`appsettings.Development.json` (`ReadFrom.Configuration`) plus `.Enrich.FromLogContext()`. Nothing elsewhere in the codebase talks to Serilog's API directly — `LoggingBehavior` (`app`, see `doc/app-operation.md`) and every other `ILogger<T>` injection keep using `Microsoft.Extensions.Logging`'s abstraction unchanged; Serilog only replaces the provider underneath it. `app.UseSerilogRequestLogging()` logs one structured line per request (method, path, status code, elapsed time) — this is what actually satisfies "log every request," and it's why `api` has no separate hand-written request-logging middleware: it would duplicate this.
- **`CorrelationIdMiddleware`** (`api/middleware/`): reads an inbound `X-Correlation-ID` header if present and well-formed (alphanumeric/hyphen only, ≤64 characters — anything else is treated as absent rather than trusted, since it's unauthenticated client input that gets echoed back and written into every log line for the request), otherwise generates a `Guid.NewGuid()`. It echoes the value on the response header and wraps the rest of the pipeline in `LogContext.PushProperty("CorrelationId", correlationId)`, so every log line for that request — including `UseSerilogRequestLogging`'s own completion line, `PerformanceMiddleware`'s warning, and a rate-limit rejection's warning — carries the same id. This only works because of ordering: `UseCorrelationId()` must run *before* `UseSerilogRequestLogging()`/`UsePerformance()` in `Program.cs`, since `LogContext` is an ambient (`AsyncLocal`-backed) scope that has to already be pushed by the time those middleware capture it.
- **`PerformanceMiddleware`** (`api/middleware/`): times the rest of the pipeline with a `Stopwatch` and logs one `Warning` when it exceeds `WarningThresholdMilliseconds` (500 ms) — the HTTP-layer counterpart to `app`'s `PerformanceBehavior` (`doc/app-operation.md`, same 500 ms threshold, same "warn only past the threshold" shape), registered right after `UseCorrelationId()` so it wraps everything downstream: rate limiting, authentication/authorization, model binding, and the MediatR pipeline `PerformanceBehavior` already times on its own. The two are deliberately not the same measurement — `PerformanceBehavior` isolates handler+validation time; `PerformanceMiddleware` catches the full request, so a slow multipart file upload (`ChangeAvatar`/`CreatePost`/`CreateComment`) or a slow rate-limiter/auth check shows up here even when the MediatR pipeline itself was fast.

## Composition Root

`api.DependencyInjection.AddApi(configuration)` is the outermost composition step, called once from `Program.cs`:

1. `services.AddApp()` and `services.AddInfra(configuration)` — wires the two inner layers (see `doc/architecture.md`).
2. `services.AddControllers()` and `services.AddOpenApi()`.
3. JWT bearer authentication: `AddJwtBearer` reads `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SecretKey` from `IConfiguration` and validates issuer, audience, lifetime, and signing key. `configuration["Jwt:SecretKey"]!` is null-forgiven — a missing key at startup throws a `NullReferenceException` from `Encoding.UTF8.GetBytes(null)` rather than failing with a configuration-specific error message. `MapInboundClaims = false` and `RoleClaimType = JwtService.RoleClaimType` are what make `[Authorize(Roles = nameof(Role.Admin))]` match the `role` claim `JwtService` writes (see Pattern 3 above).
4. `services.AddAuthorization()`.
5. `ConfigureForwardedHeaders(services, configuration)`: registers `ForwardedHeadersMiddleware` options, trusting `X-Forwarded-For`/`X-Forwarded-Proto` only from proxy addresses/networks listed in `ForwardedHeaders:KnownProxies`/`ForwardedHeaders:KnownNetworks` (both `string[]`, unset by default). With nothing configured, ASP.NET Core's own default (loopback only) applies, so this is inert until a real reverse proxy's address is supplied — deliberately, since trusting a forwarded header from an untrusted peer lets a client spoof its own IP and dodge IP-partitioned rate limits.
6. `ConfigureRateLimiting(services)`: registers the global limiter and the four named policies described in Pattern 8 above.

None of the `Jwt:*`/`R2:*`/`Redis:*`/`ForwardedHeaders:*`/etc. keys `AddApi`/`AddInfra` read live in `appsettings.json`/`appsettings.Development.json` (both hold only a `Serilog` section and `AllowedHosts`) — they come from .NET User Secrets or host configuration per environment, consistent with `doc/infra-operation.md`.

`Program.cs` calls `AddApi` and registers nothing of its own beyond the Serilog bootstrap described in Pattern 9; the middleware pipeline order is: `MapOpenApi()` (Development only) → `UseForwardedHeaders()` → `UseHttpsRedirection()` → `UseCorrelationId()` → `UsePerformance()` → `UseSerilogRequestLogging()` → `UseAuthentication()` → `UseRateLimiter()` → `UseAuthorization()` → `MapControllers()` → `MapHub<NotificationHub>("/hubs/notification")`. The ordering is load-bearing in three places: `UseForwardedHeaders()` must precede anything reading `Connection.RemoteIpAddress` (HTTPS redirection's scheme check, the rate limiter's partition key); `UseCorrelationId()` must precede `UsePerformance()`/`UseSerilogRequestLogging()` so their log lines are tagged with the correlation id (Pattern 9); `UseRateLimiter()` must follow `UseAuthentication()` so its partition-key resolution can read the `accountId` claim, and precede `UseAuthorization()` since a rejected request should never reach role enforcement.

`MapHub<NotificationHub>` is the one place `Program.cs` names an `infra` type directly rather than going through `AddApi`/`AddInfra` — `NotificationHub` (`infra/hub`, see `doc/infra-operation.md`) is a SignalR `Hub`, and SignalR hubs are mapped on the endpoint-routing surface built by `MapControllers()`, not registered inside `AddInfra`'s `IServiceCollection` wiring like every other `infra` type. It runs `[Authorize]` and `UseAuthentication()`/`UseAuthorization()` already precede `MapControllers()` in the pipeline above, so a hub connection is authenticated the same way an HTTP request is; `ICurrentIdentityService`/the JWT bearer middleware are what the hub's `[Authorize]` relies on, same as any controller action.

## Controllers

Each table lists every action's HTTP verb + route, who may call it, its rate-limit policy (beyond the global 200/min every action gets — see Pattern 8 above), the `app` message it dispatches, and its success response.

### AccountController — `api/Account`

| Verb + route | Access | Rate limit | Dispatches | Success |
|---|---|---|---|---|
| `POST /` | Anonymous | `Auth` 10/min | `CreateAccountCommand` (after `Name.Create`) | 200 `AccountDto` |
| `GET /me` | Authenticated | Global | `GetMyAccountQuery` | 200 `AccountSummaryDto` |
| `PUT /me/name` | Authenticated | Global | `ChangeNameCommand` (after `Name.Create`) | 204 |
| `PUT /me/bio` | Authenticated | Global | `ChangeBioCommand` | 204 |
| `PUT /me/password` | Authenticated | Global | `ChangePasswordCommand` | 204 |
| `PUT /me/email` | Authenticated | Global | `ChangeEmailCommand` | 204 |
| `POST /me/avatar` | Authenticated | `Upload` 10/min | `ChangeAvatarCommand` (file upload) | 204 |
| `DELETE /me/avatar` | Authenticated | Global | `RemoveAvatarCommand` | 204 |
| `DELETE /me` | Authenticated | Global | `DeleteAccountCommand` | 204 |
| `POST /me/recover` | Authenticated | Global | `RecoverAccountCommand` | 204 |
| `GET /` | Admin | Global | `GetAllAccountQuery` | 200 `CursorPageResult<AccountSummaryDto>` |
| `GET /{accountId:guid}` | Authenticated | Global | `GetSpecificAccountQuery` | 200 `AccountSummaryDto` |
| `GET /by-email` | Admin | Global | `GetAccountByEmailQuery` (after `Email.Create`) | 200 `AccountSummaryDto` |
| `GET /by-name` | Authenticated | Global | `GetAccountByNameQuery` (after `Name.Create`) | 200 `CursorPageResult<AccountSummaryDto>` |
| `GET /search` | Authenticated | `Search` 20/min | `GetAccountByKeywordQuery` | 200 `CursorPageResult<AccountSummaryDto>` |

### AuthController — `api/Auth`

| Verb + route | Access | Rate limit | Dispatches | Success |
|---|---|---|---|---|
| `POST /login` | Anonymous | `Auth` 10/min | `LoginCommand` (after `Email.Create`) | 200 `AuthResult` |
| `POST /refresh` | Anonymous | `Auth` 10/min | `RequestNewAccessTokenCommand` | 200 `AuthResult` |
| `GET /refresh-token` | Admin | Global | `GetAllRefreshTokenQuery` | 200 `CursorPageResult<RefreshTokenDto>` |
| `GET /refresh-token/{refreshTokenId:guid}` | Admin | Global | `GetSpecificRefreshTokenQuery` | 200 `RefreshTokenDto` |

### CommentController — `api/Comment`

| Verb + route | Access | Rate limit | Dispatches | Success |
|---|---|---|---|---|
| `POST /` | Authenticated | `Upload` 10/min | `CreateCommentCommand` (multipart form, file upload) | 200 `CommentDto` |
| `PUT /{commentId:guid}` | Authenticated | Global | `ChangeContentCommand` | 204 |
| `DELETE /{commentId:guid}` | Authenticated | Global | `DeleteCommentCommand` | 204 |
| `GET /{commentId:guid}` | Authenticated | Global | `GetSpecificCommentQuery` | 200 `CommentSummaryDto` |
| `GET /post/{postId:guid}` | Authenticated | Global | `GetCommentByPostQuery` | 200 `CursorPageResult<CommentSummaryDto>` |
| `GET /mine` | Authenticated | Global | `GetMyCommentHistoryQuery` | 200 `CursorPageResult<CommentSummaryDto>` |

### EmailController — `api/Email`

`[EnableRateLimiting(RateLimiterPolicy.Email)]` is applied at the class level — all three actions share it, since every one of them is anonymous and OTP-adjacent.

| Verb + route | Access | Rate limit | Dispatches | Success |
|---|---|---|---|---|
| `POST /send-for-register` | Anonymous | `Email` 5/min | `SendEmailForRegisterCommand` (after `Email.Create`) | 204 |
| `POST /send-for-verify` | Anonymous | `Email` 5/min | `SendEmailForVerifyCommand` (after `Email.Create`) | 204 |
| `POST /verify` | Anonymous | `Email` 5/min | `VerifyEmailCommand` (after `Email.Create`) | 200 `string` (registration token) |

### FollowController — `api/Follow`

| Verb + route | Access | Rate limit | Dispatches | Success |
|---|---|---|---|---|
| `POST /{followingId:guid}` | Authenticated | Global | `FollowCommand` (publishes to RabbitMQ) | 202 |
| `DELETE /{followingId:guid}` | Authenticated | Global | `UnfollowCommand` (publishes to RabbitMQ) | 202 |
| `GET /follower/{accountId:guid}` | Authenticated | Global | `GetFollowerByAccountQuery` | 200 `CursorPageResult<FollowerDto>` |
| `GET /following/{accountId:guid}` | Authenticated | Global | `GetFollowingByAccountQuery` | 200 `CursorPageResult<FollowingDto>` |
| `GET /me/follower` | Authenticated | Global | `GetMyFollowerQuery` | 200 `CursorPageResult<FollowerDto>` |
| `GET /me/following` | Authenticated | Global | `GetMyFollowingQuery` | 200 `CursorPageResult<FollowingDto>` |

### NotificationController — `api/Notification`

| Verb + route | Access | Rate limit | Dispatches | Success |
|---|---|---|---|---|
| `GET /{notificationId:guid}` | Authenticated | Global | `GetSpecificNotificationQuery` | 200 `NotificationSummaryDto` |
| `GET /mine` | Authenticated | Global | `GetMyNotificationQuery` | 200 `CursorPageResult<NotificationSummaryDto>` |

`NotificationController` has no `POST`/create action, for the same underlying reason `ShopController` has no create action but a different mechanism: a `Notification` is never created through this controller, or through any MediatR command — `infra`'s `NotificationService` constructs one directly whenever `FollowService`/`ReactionService`/`CommentService` decide a recipient needs telling (see `doc/infra-operation.md`, `doc/project-rules.md`). Real-time delivery for an online recipient goes over `NotificationHub` (SignalR, `/hubs/notification`), not through this controller either — these two `GET` actions exist only to read back a notification's history/current state.

### PostController — `api/Post`

| Verb + route | Access | Rate limit | Dispatches | Success |
|---|---|---|---|---|
| `POST /` | Authenticated | `Upload` 10/min | `CreatePostCommand` (multipart form, after `Amount.Create`, file upload) | 200 `PostDto` |
| `PUT /{postId:guid}/caption` | Authenticated | Global | `ChangeCaptionCommand` | 204 |
| `PUT /{postId:guid}/rate` | Authenticated | Global | `ChangeRateCommand` (after `Amount.Create`) | 204 |
| `DELETE /{postId:guid}` | Authenticated | Global | `DeletePostCommand` | 204 |
| `GET /{postId:guid}` | Authenticated | Global | `GetSpecificPostQuery` | 200 `PostSummaryDto` |
| `GET /mine` | Authenticated | Global | `GetMyPostQuery` | 200 `CursorPageResult<PostSummaryDto>` |
| `GET /account/{accountId:guid}` | Authenticated | Global | `GetPostByAccountQuery` | 200 `CursorPageResult<PostSummaryDto>` |
| `GET /shop/{shopId:guid}` | Authenticated | Global | `GetPostByShopQuery` | 200 `CursorPageResult<PostSummaryDto>` |
| `GET /search` | Authenticated | `Search` 20/min | `GetPostByKeywordQuery` | 200 `CursorPageResult<PostSummaryDto>` |
| `GET /latest` | Authenticated | Global | `GetLatestPostQuery` | 200 `CursorPageResult<PostSummaryDto>` |
| `GET /most-reacted` | Authenticated | Global | `GetMostReactedPostQuery` | 200 `CursorPageResult<PostSummaryDto>` |

### ReactionController — `api/Reaction`

| Verb + route | Access | Rate limit | Dispatches | Success |
|---|---|---|---|---|
| `POST /post/{postId:guid}` | Authenticated | Global | `ReactPostCommand` (publishes to RabbitMQ) | 202 |
| `DELETE /post/{postId:guid}` | Authenticated | Global | `UnreactPostCommand` (publishes to RabbitMQ) | 202 |
| `POST /comment/{commentId:guid}` | Authenticated | Global | `ReactCommentCommand` (publishes to RabbitMQ) | 202 |
| `DELETE /comment/{commentId:guid}` | Authenticated | Global | `UnreactCommentCommand` (publishes to RabbitMQ) | 202 |
| `GET /post/{postId:guid}` | Authenticated | Global | `GetReactionByPostQuery` | 200 `CursorPageResult<ReactionDto>` |
| `GET /comment/{commentId:guid}` | Authenticated | Global | `GetReactionByCommentQuery` | 200 `CursorPageResult<ReactionDto>` |

### ReportController — `api/Report`

| Verb + route | Access | Rate limit | Dispatches | Success |
|---|---|---|---|---|
| `POST /` | Authenticated | Global | `CreateReportCommand` | 200 `ReportDto` |
| `GET /` | Admin | Global | `GetAllReportQuery` | 200 `CursorPageResult<ReportDto>` |
| `GET /{reportId:guid}` | Admin | Global | `GetSpecificReportQuery` | 200 `ReportDto` |
| `GET /target` | Admin | Global | `GetReportByTargetQuery` | 200 `CursorPageResult<ReportDto>` |

### ShopController — `api/Shop`

| Verb + route | Access | Rate limit | Dispatches | Success |
|---|---|---|---|---|
| `DELETE /{shopId:guid}` | Admin | Global | `DeleteShopCommand` | 204 |
| `GET /{shopId:guid}` | Authenticated | Global | `GetSpecificShopQuery` | 200 `ShopDto` |
| `GET /` | Authenticated | Global | `GetAllShopQuery` | 200 `CursorPageResult<ShopSummaryDto>` |
| `GET /search` | Authenticated | `Search` 20/min | `GetShopByKeywordQuery` | 200 `CursorPageResult<ShopSummaryDto>` |
| `GET /by-name` | Authenticated | Global | `GetShopByNameQuery` | 200 `CursorPageResult<ShopSummaryDto>` |
| `GET /by-address` | Authenticated | Global | `GetShopByAddressQuery` | 200 `CursorPageResult<ShopSummaryDto>` |
| `GET /most-rated` | Authenticated | Global | `GetMostRatedShopQuery` | 200 `CursorPageResult<ShopSummaryDto>` |

There is no `ShopController` create action — every `Shop` comes into existence as a side effect of `PostController.CreatePost` (see `doc/project-rules.md`).

## Request Records

`api/request/` holds the 13 records used for actions with a JSON body that doesn't already match an existing `app`/`domain` type one-for-one:

| Request record | Fields | Used by |
|---|---|---|
| `CreateAccountRequest` | `RegistrationToken, Name, Password, Bio?` | `AccountController.CreateAccount` |
| `ChangeNameRequest` | `Name` | `AccountController.ChangeName` |
| `ChangeBioRequest` | `Bio?` | `AccountController.ChangeBio` |
| `ChangePasswordRequest` | `CurrentPassword, NewPassword` | `AccountController.ChangePassword` |
| `ChangeEmailRequest` | `RegistrationToken` | `AccountController.ChangeEmail` |
| `LoginRequest` | `Email, Password` | `AuthController.Login` |
| `RequestNewAccessTokenRequest` | `RefreshToken` | `AuthController.RequestNewAccessToken` |
| `ChangeContentRequest` | `Content` | `CommentController.ChangeContent` |
| `SendEmailRequest` | `Email` | `EmailController.SendForRegister`/`SendForVerify` |
| `VerifyEmailRequest` | `Email, Otp` | `EmailController.Verify` |
| `ChangeCaptionRequest` | `Caption` | `PostController.ChangeCaption` |
| `ChangeRateRequest` | `Rate` | `PostController.ChangeRate` |
| `CreateReportRequest` | `PostId?, CommentId?, AccountId?, Reason` | `ReportController.CreateReport` |

Every other action binds its input entirely from the route and/or query string (`[FromRoute]`/`[FromQuery]`, largely implicit for simple types), or — for `CreatePost`/`CreateComment`/`ChangeAvatar` — from `[FromForm]`/`IFormFile` multipart fields with no backing record (see Pattern 5 above).
