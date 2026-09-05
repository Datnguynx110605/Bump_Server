# Project Rules

Conventions as they actually exist in the repository, measured rather than assumed. Every rule below
is followed by the evidence it was derived from.

**Every rule here is convention held by hand, not enforced by tooling**, so it can drift silently.
The conventions were derived by measuring the tree; where a count is given, it is the count across
all `.cs` files in `src/`.

## Folder tree and responsibilities

Every folder in the repository and what belongs in it. Counts in brackets are `.cs` files directly in
that folder, not recursive.

```
Bump_Server/
├── AGENTS.md                       agent operating rules — session order, git limits, refactor gate
├── Dockerfile                      multi-stage build; restores api.csproj, binds $PORT at runtime
├── railway.json                    the only platform config — builder, health check, restart policy
├── .dockerignore                   build-context excludes; tracked, and must stay tracked
├── .gitignore                      root ignores only .DS_Store, .dockerignore, .claude/, .env
├── .env                            untracked; 16 secrets in A__B form
├── doc/                            project documentation
└── src/
    ├── Bump.sln                    solution — 4 projects
    │
    ├── domain/                     [0]  no project or package dependencies at all
    │   ├── common/                 [2]  DomainResult / DomainResult<T>; TextNormalizer (diacritic folding)
    │   └── core/
    │       ├── entity/             [15] aggregates + embedded types; private ctor, static Create factory
    │       ├── enum/               [2]  Role, NotificationType
    │       └── valueobject/        [3]  Amount, Email, Name — validated wrappers over a primitive
    │
    ├── app/                        [1]  DependencyInjection.cs — AddApp: MediatR, AutoMapper, validators
    │   ├── common/                 [3]  CollectionName, QueueName, AppError
    │   │   ├── behavior/           [3]  MediatR pipeline: Logging → Performance → Validation
    │   │   ├── cache/              [2]  CacheKey (key shapes + TTLs), ListCache (version bump/read)
    │   │   ├── dto/                [19] response and cache shapes; PascalCase record parameters
    │   │   ├── interface/               contracts implemented in infra — the inversion boundary
    │   │   │   ├── persistence/    [1]  IMongoDbContext
    │   │   │   ├── queue/          [7]  one per queue worker (IFollowService, IReactionService, …)
    │   │   │   └── service/        [10] adapters (IRedisService, IR2Service, IJwtService, …)
    │   │   ├── mapping/            [12] one AutoMapper Profile per aggregate
    │   │   ├── membership/         [3]  per-viewer flags: ReactionMembership, SaveMembership, PostFeedEnricher
    │   │   ├── message/            [7]  queue payload records; every one carries a MessageId
    │   │   └── pagination/         [7]  cursor codec, paginators, and their cached wrappers
    │   └── feature/                     78 use cases — one folder each, listed in full below
    │       ├── account/            [1]  AccountProjection.cs — area-shared
    │       │   ├── command/
    │       │   │   ├── ChangeAvatar/     ChangeAvatar{Command, Handler, Validator}
    │       │   │   ├── ChangeBio/        ChangeBio{Command, Handler, Validator}
    │       │   │   ├── ChangeEmail/      ChangeEmail{Command, Handler, Validator}
    │       │   │   ├── ChangeName/       ChangeName{Command, Handler, Validator}
    │       │   │   ├── ChangePassword/   ChangePassword{Command, Handler, Validator}
    │       │   │   ├── AdminDelete/      AdminDeleteAccount{Command, Handler}  ⚠ no validator
    │       │   │   ├── Create/           CreateAccount{Command, Handler, Validator}
    │       │   │   ├── Delete/           DeleteAccount{Command, Handler}  ⚠ no validator
    │       │   │   ├── Recover/          RecoverAccount{Command, Handler, Validator}
    │       │   │   ├── RemoveAvatar/     RemoveAvatar{Command, Handler, Validator}
    │       │   │   └── ResetPassword/    ResetPassword{Command, Handler, Validator}
    │       │   └── query/
    │       │       ├── GetAccountByEmail/    GetAccountByEmail{Query, Handler}
    │       │       ├── GetAccountByKeyword/  GetAccountByKeyword{Query, Handler}
    │       │       ├── GetAccountByName/     GetAccountByName{Query, Handler}
    │       │       ├── GetAllAccount/        GetAllAccount{Query, Handler}
    │       │       ├── GetMyAccount/         GetMyAccount{Query, Handler}
    │       │       └── GetSpecificAccount/   GetSpecificAccount{Query, Handler}
    │       ├── auth/               [1]  AuthResult.cs — area-shared
    │       │   ├── command/
    │       │   │   ├── Login/                    Login{Command, Handler, Validator}
    │       │   │   ├── RequestNewAccessToken/    RequestNewAccessToken{Command, Handler, Validator}
    │       │   │   └── RevokeRefreshToken/       RevokeRefreshToken{Command, Handler}  ⚠ no validator
    │       │   └── query/
    │       │       ├── GetAllRefreshToken/       GetAllRefreshToken{Query, Handler}
    │       │       └── GetSpecificRefreshToken/  GetSpecificRefreshToken{Query, Handler}
    │       ├── comment/            [1]  CommentProjection.cs — area-shared
    │       │   ├── command/
    │       │   │   ├── AdminDelete/      AdminDeleteComment{Command, Handler}  ⚠ no validator
    │       │   │   ├── ChangeContent/    ChangeContent{Command, Handler, Validator}
    │       │   │   ├── Create/           CreateComment{Command, Handler, Validator}
    │       │   │   └── Delete/           DeleteComment{Command, Handler, Validator}
    │       │   └── query/
    │       │       ├── GetAllComment/        GetAllComment{Query, Handler}
    │       │       ├── GetCommentByPost/     GetCommentByPost{Query, Handler}
    │       │       ├── GetMyCommentHistory/  GetMyCommentHistory{Query, Handler}
    │       │       └── GetSpecificComment/   GetSpecificComment{Query, Handler}
    │       ├── dashboard/
    │       │   └── query/
    │       │       ├── GetDashboardActivity/ GetDashboardActivity{Query, Handler}
    │       │       └── GetDashboardStats/    GetDashboardStats{Query, Handler}
    │       ├── email/                   ⚠ no command/query level — use cases sit directly here
    │       │   ├── SendEmailForRegister/ SendEmailForRegister{Command, Handler, Validator}
    │       │   ├── SendEmailForVerify/   SendEmailForVerify{Command, Handler, Validator}
    │       │   └── VerifyEmail/          VerifyEmail{Command, Handler, Validator}
    │       ├── follow/
    │       │   ├── command/
    │       │   │   ├── Follow/           Follow{Command, Handler, Validator}
    │       │   │   └── Unfollow/         Unfollow{Command, Handler, Validator}
    │       │   └── query/
    │       │       ├── GetFollowerByAccount/   GetFollowerByAccount{Query, Handler}
    │       │       ├── GetFollowingByAccount/  GetFollowingByAccount{Query, Handler}
    │       │       ├── GetMyFollower/          GetMyFollower{Query, Handler}
    │       │       └── GetMyFollowing/         GetMyFollowing{Query, Handler}
    │       ├── notification/       [1]  NotificationProjection.cs — area-shared
    │       │   └── query/
    │       │       ├── GetMyNotification/       GetMyNotification{Query, Handler}
    │       │       └── GetSpecificNotification/ GetSpecificNotification{Query, Handler}
    │       ├── post/               [1]  PostProjection.cs — area-shared
    │       │   ├── command/
    │       │   │   ├── AdminDelete/      AdminDeletePost{Command, Handler}  ⚠ no validator
    │       │   │   ├── ChangeCaption/    ChangeCaption{Command, Handler, Validator}
    │       │   │   ├── ChangeRate/       ChangeRate{Command, Handler, Validator}
    │       │   │   ├── Create/           CreatePost{Command, Handler, Validator}
    │       │   │   └── Delete/           DeletePost{Command, Handler, Validator}
    │       │   └── query/
    │       │       ├── GetLatestPost/        GetLatestPost{Query, Handler}
    │       │       ├── GetMostReactedPost/   GetMostReactedPost{Query, Handler}
    │       │       ├── GetMyPost/            GetMyPost{Query, Handler}
    │       │       ├── GetPostByAccount/     GetPostByAccount{Query, Handler}
    │       │       ├── GetPostByKeyword/     GetPostByKeyword{Query, Handler}
    │       │       ├── GetPostByShop/        GetPostByShop{Query, Handler}
    │       │       └── GetSpecificPost/      GetSpecificPost{Query, Handler}
    │       ├── reaction/
    │       │   ├── command/
    │       │   │   ├── ReactComment/     ReactComment{Command, Handler, Validator}
    │       │   │   ├── ReactPost/        ReactPost{Command, Handler, Validator}
    │       │   │   ├── UnreactComment/   UnreactComment{Command, Handler, Validator}
    │       │   │   └── UnreactPost/      UnreactPost{Command, Handler, Validator}
    │       │   └── query/
    │       │       ├── GetReactionByComment/ GetReactionByComment{Query, Handler}
    │       │       └── GetReactionByPost/    GetReactionByPost{Query, Handler}
    │       ├── report/
    │       │   ├── command/
    │       │   │   └── Create/           CreateReport{Command, Handler, Validator}
    │       │   └── query/
    │       │       ├── GetAllReport/         GetAllReport{Query, Handler}
    │       │       ├── GetReportByTarget/    GetReportByTarget{Query, Handler, Validator}
    │       │       │                         + ReportTarget.cs — use-case-local enum
    │       │       └── GetSpecificReport/    GetSpecificReport{Query, Handler}
    │       ├── save/
    │       │   ├── command/
    │       │   │   ├── SavePost/         SavePost{Command, Handler, Validator}
    │       │   │   ├── SaveShop/         SaveShop{Command, Handler, Validator}
    │       │   │   ├── UnsavePost/       UnsavePost{Command, Handler, Validator}
    │       │   │   └── UnsaveShop/       UnsaveShop{Command, Handler, Validator}
    │       │   └── query/
    │       │       ├── GetMySavedPosts/      GetMySavedPosts{Query, Handler}
    │       │       └── GetMySavedShops/      GetMySavedShops{Query, Handler}
    │       └── shop/               [1]  ShopProjection.cs — area-shared
    │           ├── command/
    │           │   ├── Create/           ⚠ empty, untracked
    │           │   └── Delete/           DeleteShop{Command, Handler, Validator}
    │           └── query/
    │               ├── GetAllShop/           GetAllShop{Query, Handler}
    │               ├── GetMostRatedShop/     GetMostRatedShop{Query, Handler}
    │               ├── GetShopByAddress/     GetShopByAddress{Query, Handler}
    │               ├── GetShopByKeyword/     GetShopByKeyword{Query, Handler}
    │               ├── GetShopByName/        GetShopByName{Query, Handler}
    │               └── GetSpecificShop/      GetSpecificShop{Query, Handler}
    │
    ├── infra/                      [1]  DependencyInjection.cs — AddInfra: singletons, services, hosted services
    │   ├── health/                 [3]  one IHealthCheck per backing service, surfaced at /health
    │   ├── hub/                    [2]  NotificationHub, AccountIdUserIdProvider (accountId → connection)
    │   ├── persistence/
    │   │   ├── configuration/      [5]  BSON class maps, backing-field serializer, indexes, initializers
    │   │   └── dbcontext/          [1]  DbContext — the only IMongoDbContext implementation
    │   ├── queue/                  [1]  QueueConsumerHost<TMessage> — the single generic consumer host
    │   │   ├── account/            [1]  DeleteAccountService — the delete cascade
    │   │   ├── comment/            [1]  CommentService — comment notification
    │   │   ├── follow/             [1]  FollowService — both sides of the edge + counters
    │   │   ├── post/               [1]  DeletePostService — comment and image cleanup
    │   │   ├── reaction/           [1]  ReactionService — post and comment reactions
    │   │   ├── save/               [1]  SaveService — save/unsave post and shop
    │   │   └── shop/               [1]  ShopRateService — rate recalculation
    │   ├── serialization/          [4]  value-object JSON converters + DomainJsonOptions
    │   └── service/                [10] adapters: Redis, RabbitMQ, R2, email, JWT, BCrypt, OTP, identity…
    │
    └── api/                        [2]  Program.cs (pipeline order), DependencyInjection.cs (AddApi)
        ├── Properties/             [0]  launchSettings.json only
        ├── common/                 [4]  ResultExtensions, ResultStatusMapper, RateLimiterPolicy, ImageUpload
        ├── controller/             [12] one per resource; bind → send → ToActionResult, no logic
        ├── middleware/             [4]  CorrelationId + Performance, each with its UseX extension
        └── request/                [15] HTTP body records; PascalCase parameters
```

One folder holds no `.cs` files: `api/Properties/`, which exists for `launchSettings.json`. The
empty `app/feature/shop/command/Create/` directory that used to sit alongside `Delete/` has been
removed — shops are created as a side effect of `CreatePostHandler`, so no shop-create command
exists.

### Responsibility rules the tree encodes

- **A folder named for a technical role holds only that role.** `dto/` holds only DTOs, `mapping/`
  only `Profile` classes, `behavior/` only `IPipelineBehavior` implementations.
- **`interface/` mirrors the shape of what implements it.** `interface/queue/` ↔ `infra/queue/`,
  `interface/service/` ↔ `infra/service/`, `interface/persistence/` ↔ `infra/persistence/`.
- **`infra/queue/<area>/` holds the worker, never the host.** Every queue is served by the single
  `QueueConsumerHost<TMessage>` one level up; area folders contain only the `*Service` doing the work.
- **Area-level files in `app/feature/<area>/` are shared across that area's use cases** — the six
  `*Projection.cs` files and `AuthResult.cs`. Anything used by exactly one use case belongs inside
  that use case's folder instead.
- **`api/common/` is for cross-controller plumbing**, not helpers for a single controller.

**Structural folders are lowercase, single word.** All 40 of them: `account`, `api`, `app`, `auth`,
`behavior`, `cache`, `command`, `comment`, `common`, `configuration`, `controller`, `core`,
`dashboard`, `dbcontext`, `domain`, `dto`, `email`, `entity`, `enum`, `feature`, `follow`, `health`,
`hub`, `infra`, `interface`, `mapping`, `membership`, `message`, `middleware`, `notification`,
`pagination`, `persistence`, `post`, `query`, `queue`, `reaction`, `report`, `request`, `save`,
`serialization`, `service`, `shop`, `valueobject`. Note `valueobject` and `dbcontext` are run
together rather than hyphenated or split.

**Use-case folders are PascalCase** and named for the operation: `Create`, `Delete`, `ChangeBio`,
`ChangeCaption`, `GetLatestPost`, `GetMostRatedShop`, `GetReportByTarget`.

The `app/feature` tree nests as `feature/<area>/<command|query>/<UseCase>/`, except **`email`, which
omits the `command`/`query` level** — `app/feature/email/VerifyEmail/` sits directly under the area.
That is the one structural inconsistency in the feature tree.

## Namespaces

**File-scoped, lowercase, one per project — 352 files, zero exceptions**, and no block-bodied
namespace anywhere:

| Namespace | Files |
|---|---|
| `namespace app;` | 260 |
| `namespace api;` | 36 |
| `namespace infra;` | 34 |
| `namespace domain;` | 22 |

Namespaces are **flat**: a type in `app/feature/post/query/GetLatestPost/` is still `namespace app;`.
Folders carry no namespace meaning, so every type name must be unique within its project.

Lowercase namespaces are contrary to the .NET convention of PascalCase (`Bump.Application`), and the
project name/assembly name matches (`app.csproj` → assembly `app`). This is consistent within the
repo; treat it as the house style rather than an error.

## File naming

One public type per file, filename identical to the type name, PascalCase, `.cs`.

Suffixes are load-bearing and consistent:

| Suffix | Meaning | Example |
|---|---|---|
| `*Command.cs` | MediatR request that mutates | `CreatePostCommand.cs` |
| `*Query.cs` | MediatR request that reads | `GetLatestPostQuery.cs` |
| `*Handler.cs` | `IRequestHandler` implementation | `CreatePostHandler.cs` |
| `*Validator.cs` | `AbstractValidator<T>` | `CreatePostValidator.cs` |
| `*Request.cs` | HTTP request body record (`api` only) | `ChangeRateRequest.cs` |
| `*Dto.cs` | Response/cache shape | `PostSummaryDto.cs` |
| `*Profile.cs` | AutoMapper profile | `PostProfile.cs` |
| `*Controller.cs` | MVC controller | `PostController.cs` |
| `*Service.cs` | Adapter or queue worker | `RedisService.cs` |
| `*Projection.cs` | Reusable Mongo projection | `PostProjection.cs` |
| `*Message.cs` | Queue payload record | `FollowMessage.cs` |
| `*Behavior.cs` | MediatR pipeline behaviour | `ValidationBehavior.cs` |
| `*Middleware.cs` + `*MiddlewareExtensions.cs` | Middleware and its `UseX` extension | `PerformanceMiddleware.cs` |
| `*HealthCheck.cs` | `IHealthCheck` | `RedisHealthCheck.cs` |
| `*JsonConverter.cs` | `JsonConverter<T>` | `AmountJsonConverter.cs` |

## Type and member naming

**Types are PascalCase and `sealed` by default** — 305 `sealed class`/`sealed record` declarations
against only 4 non-sealed types, each deliberately open:
`BaseEntity` (abstract), `DomainResult`, `DomainResult<T>` (inherited from), and `DbContext`.

**Interfaces take an `I` prefix.** One casing inconsistency exists: `IBcryptService` (lowercase `c`)
is implemented by `BCryptService` (uppercase `C`).

**Private fields are `_camelCase`**: `_dbContext`, `_redisService`, `_scopeFactory`. Constructor
parameters are the same name without the underscore.

**Constants are PascalCase**: `PasswordHashLength`, `BioMaxLength`, `MaxAttempts`,
`WarningThresholdMilliseconds`, `RetryCountHeader`.

### Record parameter casing is inconsistent — the one rule to know

Two opposite conventions coexist, split by layer:

```csharp
// app — commands and queries use camelCase parameters
public sealed record LoginCommand(
   Email email, 
   string password);
public sealed record GetLatestPostQuery(CursorPageRequest pageRequest);
public sealed record ChangeRateCommand(
   Guid postId, 
   Amount rate);

// app — DTOs use PascalCase parameters
public sealed record PostSummaryDto(
   Guid Id, 
   Guid AccountId, /* … */ 
   DateTime CreatedAt);

// app — queue messages use PascalCase parameters
public sealed record FollowMessage(
   Guid MessageId, 
   Guid FollowerId, 
   Guid FollowingId);

// api — request records use PascalCase parameters
public sealed record ChangeRateRequest(int Rate);
public sealed record LoginRequest(
   string Email, 
   string Password);
```

camelCase record parameters become camelCase **public properties**, which is why handlers read
`request.postId` rather than `request.PostId`. Follow the local convention: camelCase for
commands/queries, PascalCase for everything else.

### Static-holder types are declared as `record`

`CollectionName`, `QueueName`, `CacheKey`, `RateLimiterPolicy`, `PostProjection` and
`AccountProjection` are `public sealed record` types containing only static members and never
instantiated. `CollectionName` and `QueueName` additionally use **mutable `public static` fields**
rather than `const` or `static readonly`:

```csharp
public sealed record CollectionName
{
    public static string Account = "account";   // assignable at runtime
}
```

`RateLimiterPolicy` uses `public const string` correctly, so the two styles are mixed. New static
holders should use `static class` with `const`.

## Formatting

Derived by counting the whole tree; there is no config file to cite.

| Rule | Evidence |
|---|---|
| 4-space indentation, **no tabs** | 0 files contain a leading tab |
| **Allman braces** — `{` always on its own line | 0 occurrences of `) {` at end of line |
| **No space after control keywords**: `if(`, `foreach(` | `if(` 258 / `if (` 0; `foreach(` 21 / `foreach (` 0 |
| File-scoped namespaces | 352 / 0 |
| `using` directives at file top, framework and package usings mixed, roughly alphabetical | e.g. `using app; using AutoMapper; using domain; using FluentResults;` |
| Multi-line argument lists: one argument per line, closing `)` on the last argument's line | pervasive in handlers |
| Named arguments used for clarity on optional Mongo parameters | `cancellationToken : cancellationToken` — note the **spaces around the colon**, which is non-standard |
| `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` | all five `.csproj` files |

The build emits **50 nullable warnings** (CS8600/CS8604/CS8618), largely because
`DomainResult<T>.Value` is `T?` with no flow attribute, so `result.Value` after an `IsSuccess` check
still warns. Warnings are not treated as errors.

Some files begin with a UTF-8 BOM and others do not (`Account.cs` has one; `SavedPost.cs` does not) —
cosmetic, but visible in diffs.

## Scaffolding a new feature

Comparing `post/command/Create`, `comment/command/Create` and `account/command/ChangeBio` gives an
identical three-file shape; comparing `post/query/GetLatestPost`, `shop/query/GetAllShop` and
`report/query/GetReportByTarget` gives a two-file shape plus optional extras.

**A new command** — `src/app/feature/<area>/command/<UseCase>/`:

1. `<UseCase>Command.cs` — `public sealed record XCommand(camelCase params) : IRequest<Result>` or
   `IRequest<Result<TDto>>`.
2. `<UseCase>Handler.cs` — `internal sealed class XHandler : IRequestHandler<XCommand, …>`,
   constructor-injected dependencies as `_camelCase` fields, and a `Handle` body that:
   resolves identity → loads with a projection → mutates via the entity method → persists with an
   explicit filter + `$set` → invalidates cache → publishes any queue message → returns `Result`.
3. `<UseCase>Validator.cs` — `internal sealed class XValidator : AbstractValidator<XCommand>`.

**A new query** — `src/app/feature/<area>/query/<UseCase>/`:

1. `<UseCase>Query.cs` — `public sealed record XQuery(… , CursorPageRequest pageRequest) : IRequest<Result<CursorPageResult<TDto>>>` for lists.
2. `<UseCase>Handler.cs` — resolve identity, build a `FilterDefinition`, call
   `CachedCursorPaginator.PaginateAsync` passing `entity` and `filterSignature` (the paginator
   resolves the cache version itself), enrich if needed.
3. A validator only if the query takes constrained input — **38 of 72 handlers have no validator**,
   and they are overwhelmingly queries.

Feature-local supporting types live in the use-case folder: `ReportTarget.cs` sits inside
`query/GetReportByTarget/`. Types shared across an area sit one level up
(`app/feature/post/PostProjection.cs`).

Handlers are `internal sealed`; commands, queries and DTOs are `public sealed` because `api`
references them. `AddApp` registers validators with `includeInternalTypes: true` for this reason.

**Wiring checklist** for a new feature: add the controller action, add an AutoMapper profile entry if
a new DTO is introduced, add a `CacheKey` member if a new cache shape is introduced, and — for
queued work — add a `QueueName` entry, a `*Message` record, an interface in
`app/common/interface/queue/`, the service in `infra/queue/<area>/`, and one
`services.AddQueueConsumer<TMessage>(…)` line in `infra/DependencyInjection.cs`.

## Git conventions

Branches present: `main` (default), `development-v1`, `update-v1`.

Commit subjects show an **inconsistently applied scope-prefix convention**, `<scope> summary`, where
scope is a project name or `repo`:

```
<app, infra> fix Redis list-cache invalidation gaps in comment edit and account delete cascade
<infra> soft delete account owner's posts on account deletion and invalidate their cache
<repo> update documentation
```

## Repository hygiene

There are **two** ignore files. The repository root `.gitignore` is a hand-written 4-liner —
`.DS_Store`, `.dockerignore`, `.claude/`, `.env`. `src/.gitignore` is the full
`dotnet new gitignore` template, which is what actually covers `[Bb]in/` and `[Oo]bj/`. No
`.DS_Store` files are tracked, though several exist on disk.

The `.dockerignore` entry in the root `.gitignore` is a **no-op**: the file was committed before the
entry was added, so git keeps tracking it. That is the outcome we want — Railway builds from the
repository, so an untracked `.dockerignore` would let the host's `bin/` and `obj/` into the build
context. `.dockerignore` excludes `.git/`, `.claude/`, `doc/`, `*.md`, `.DS_Store` and the Dockerfile
itself.

`src/api/api.http` still contains the project template's `GET /weatherforecast/` request, which
matches no route in this codebase.
