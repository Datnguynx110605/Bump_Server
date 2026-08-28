# Project Rules

## Folder Conventions

- All source code lives under `src/`, split into one .NET project per architectural layer: `domain`, `app`, `infra`, `api`.
- Dependency direction between projects (via `ProjectReference`) flows one way: `domain` ← `app` ← `infra` ← `api`. `domain` has no project references. `app` references only `domain`. `infra` references `domain` and `app`. `api` references `infra`.
- Within each project, folders are lower-case and grouped by concern (`core`, `common`, `feature`, `service`, `persistence`), mirrored by matching C# namespaces.
- Files not yet created for a folder (e.g. an empty `feature/<name>` folder) mark a planned area of the project reserved for future work, not dead structure to remove.
- Root-level `doc/` holds project documentation such as this file. `docs/superpowers/` holds Superpowers workflow artifacts (plans, specs) and is separate from project documentation.

## Folder Structure & Responsibilities

Generated with `tree -d -I 'bin|obj|.git'` (directories only), annotated with each folder's responsibility. Empty `command`/`query` use-case folders mark planned features not yet implemented.

```
.
├── doc                             # project documentation (this file)
├── docs
│   └── superpowers                 # Superpowers workflow artifacts, not project docs
│       ├── plans
│       └── specs
└── src
    ├── api                         # entry-point layer: ASP.NET Core Web API host (depends on infra)
    │   │                           # DependencyInjection.cs at the root is the composition root (AddApi)
    │   └── Properties              # launch settings
    ├── app                         # application layer: use cases + ports for infra (depends on domain only)
    │   │                           # DependencyInjection.cs at the root registers app's own services (AddApp)
    │   ├── common
    │   │   ├── behavior            # MediatR pipeline behaviors wrapping every handler
    │   │   │                       # (LoggingBehavior, PerformanceBehavior, ValidationBehavior)
    │   │   ├── cache               # cache key conventions
    │   │   ├── dto                 # data transfer objects returned by handlers
    │   │   ├── interface
    │   │   │   ├── persistence     # persistence abstractions (e.g. IMongoDbContext) implemented by infra
    │   │   │   ├── queue           # queue-processing abstractions (e.g. IFollowService, IReactionService) implemented by infra
    │   │   │   └── service         # external service abstractions (e.g. IJwtService, IEmailService) implemented by infra
    │   │   ├── mapping             # AutoMapper profiles, one *Profile.cs per entity, mapping entity -> DTO
    │   │   └── pagination          # cursor-based pagination request/result/paginator types
    │   └── feature                 # one folder per business feature, CQRS: command/<UseCase> or query/<UseCase>,
    │       │                       # each holding *Command.cs/*Query.cs + *Handler.cs (MediatR) + *Validator.cs (FluentValidation)
    │       ├── account
    │       │   ├── command
    │       │   │   ├── ChangeAvatar
    │       │   │   ├── ChangeBio
    │       │   │   ├── ChangeEmail
    │       │   │   ├── ChangeName
    │       │   │   ├── ChangePassword
    │       │   │   ├── Create
    │       │   │   ├── Delete
    │       │   │   ├── Recover
    │       │   │   └── RemoveAvatar
    │       │   └── query
    │       │       ├── GetAccountByEmail
    │       │       ├── GetAccountByKeyword
    │       │       ├── GetAccountByName
    │       │       ├── GetAllAccount
    │       │       ├── GetMyAccount
    │       │       └── GetSpecificAccount
    │       ├── auth
    │       │   ├── command
    │       │   │   ├── Login
    │       │   │   └── RequestNewAccessToken
    │       │   └── query
    │       │       ├── GetAllRefreshToken
    │       │       └── GetSpecificRefreshToken
    │       ├── comment
    │       │   ├── command
    │       │   │   ├── ChangeContent
    │       │   │   ├── Create
    │       │   │   └── Delete
    │       │   └── query
    │       │       ├── GetCommentByPost
    │       │       ├── GetMyCommentHistory
    │       │       └── GetSpecificComment
    │       ├── email                       # not command/query split: one-off notification use cases
    │       │   ├── SendEmailForRegister
    │       │   ├── SendEmailForVerify
    │       │   └── VerifyEmail
    │       ├── follow
    │       │   ├── command
    │       │   │   ├── Follow
    │       │   │   └── Unfollow
    │       │   └── query
    │       │       ├── GetFollowerByAccount
    │       │       ├── GetFollowingByAccount
    │       │       ├── GetMyFollower
    │       │       └── GetMyFollowing
    │       ├── post
    │       │   ├── command
    │       │   │   ├── ChangeCaption
    │       │   │   ├── ChangeRate
    │       │   │   ├── Create
    │       │   │   └── Delete
    │       │   └── query
    │       │       ├── GetLatestPost
    │       │       ├── GetMostReactedPost
    │       │       ├── GetMyPost
    │       │       ├── GetPostByAccount
    │       │       ├── GetPostByKeyword
    │       │       ├── GetPostByShop
    │       │       └── GetSpecificPost
    │       ├── reaction
    │       │   ├── command
    │       │   │   ├── ReactComment
    │       │   │   ├── ReactPost
    │       │   │   ├── UnreactComment
    │       │   │   └── UnreactPost
    │       │   └── query
    │       │       ├── GetReactionByComment
    │       │       └── GetReactionByPost
    │       ├── report
    │       │   ├── command
    │       │   │   └── Create
    │       │   └── query
    │       │       ├── GetAllReport
    │       │       ├── GetReportByTarget
    │       │       └── GetSpecificReport
    │       └── shop
    │           ├── command
    │           │   ├── Create
    │           │   └── Delete
    │           └── query
    │               ├── GetAllShop
    │               ├── GetMostRatedShop
    │               ├── GetShopByAddress
    │               ├── GetShopByKeyword
    │               ├── GetShopByName
    │               └── GetSpecificShop
    ├── domain                      # innermost layer: pure business/domain model, no dependencies
    │   ├── common                  # cross-cutting domain building blocks (e.g. DomainResult, Event base type)
    │   └── core
    │       ├── entity              # domain entities and aggregate roots (e.g. Account, Post, Comment)
    │       ├── enum                # domain enumerations (e.g. Role, EventStatus)
    │       ├── event                # domain events raised by entities (e.g. AccountCreatedEvent)
    │       └── valueobject          # value objects owned by entities
    └── infra                       # infrastructure layer: implements app's ports (depends on domain + app)
        │                           # DependencyInjection.cs at the root registers every infra service (AddInfra)
        ├── persistence
        │   ├── configuration        # Mongo class maps/serializers (BsonConfiguration, BackingFieldBsonSerializer)
        │   │                        # and index creation (IndexConfiguration, run by the IndexInitializer
        │   │                        # hosted service), both driven from AddInfra
        │   └── dbcontext            # MongoDB database context implementing IMongoDbContext
        ├── queue                    # implementations of app's queue-processing ports (FollowService, ReactionService)
        ├── serialization            # System.Text.Json converters for domain value objects (Name, Email, Amount)
        └── service                  # concrete implementations of app's service interfaces
                                      # (JwtService, EmailService, RedisService, R2Service, RabbitMQService,
                                      #  BCryptService, OtpService, CurrentIdentityService, RegistrationService)

123 directories
```

A query folder is named after one filter dimension, not one filter field: `report/query/GetReportByTarget` serves all four of the `Report` foreign keys (`PostId`, `CommentId`, `AccountId`, `ReporterId`) through a single `ReportTarget` discriminator on the query record, because the four handlers were otherwise byte-identical. The `ReportTarget` enum lives in `app` beside the query rather than in `domain/core/enum`, since `domain` never discriminates report targets — `Report.Create` simply accepts three nullable `Guid`s — and an enum there would be a domain type no domain code reads.

`shop/command/Create` is deliberately empty, not unfinished scaffolding: `Shop` has no standalone, user-invokable create use case. Every `Shop` comes into existence as a side effect of `post/command/Create`'s `CreatePostHandler`, which looks up an existing non-deleted `Shop` by exact `Name`+`Address` and reuses its id, or creates a new `Shop` (and write-through-caches it) when none matches, before creating the `Post` with that `ShopId`. `shop/command/Delete` is the only shop command implemented.

## Naming Conventions

- **Namespaces**: one namespace per project, matching the project name exactly (`namespace domain;`, `namespace app;`, `namespace infra;`, `namespace api;`), declared file-scoped. `Program.cs` is the one exception: top-level statements compile into the global namespace, so it carries no declaration and `using api;` to reach `AddApi`. Folders organize files on disk but do not create nested namespaces.
- **Entities / value objects**: PascalCase noun (`Account`, `Post`, `Email`, `Name`, `Amount`), `sealed class`.
- **Domain events**: PascalCase, past-tense verb + `Event` suffix (`AccountCreatedEvent`, `PostCreatedEvent`, `FollowedAccountEvent`), `abstract record` base with `sealed record` (or plain `record`) per event.
- **CQRS messages**: `<UseCase><Command|Query>` (e.g. `CreateAccountCommand`, `ChangeNameCommand`, `GetMyAccountQuery`), `public sealed record` implementing `IRequest<TResult>`. `public` because the message is the feature's crossing point — constructed and sent via MediatR (`ISender`) from outside the `app` assembly (e.g. from `api`).
- **Handlers**: `<UseCase>Handler`, `internal sealed class` implementing `IRequestHandler<TRequest, TResult>`. `internal` because a handler is an implementation detail: MediatR discovers and invokes it by reflection at runtime, and no other layer references the class directly.
- **Validators**: `<UseCase>Validator`, `internal sealed class` extending `AbstractValidator<TCommand>` (FluentValidation), one per command/query that needs input validation. `internal` for the same reason as handlers — FluentValidation's pipeline behavior discovers and runs it, nothing calls it directly.
- **Pipeline behaviors**: `<Concern>Behavior`, `public sealed class <Concern>Behavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>` with a `where TRequest : notnull` constraint, one per cross-cutting concern in `app/common/behavior`. `public` — unlike handlers and validators, an open-generic behavior is not discovered by MediatR's assembly scan, so `AddApp` names each one explicitly in `cfg.AddOpenBehavior(typeof(LoggingBehavior<,>))`, making a behavior part of `app`'s deliberately named surface rather than an internal detail reflection happens to find. The rule that decides visibility everywhere in the codebase is the assembly boundary: a type is `public` when a project other than its own names it, and `internal` when only its own project's code or reflection resolves it.
- **Persistence configuration**: `<Concern>Configuration`, `public static class` in `infra/persistence/configuration`, one per setup concern (`BsonConfiguration` for class maps and serializers, `IndexConfiguration` for index creation), each exposing a single entry point called once at startup. A custom BSON serializer is `<Purpose>BsonSerializer`, `internal sealed class` (e.g. `BackingFieldBsonSerializer<TEntity>`), mirroring `infra/serialization`'s `<Type>JsonConverter` naming.
- **DI registration classes**: `DependencyInjection`, `public static class` at the root of `app`, `infra` and `api`, each holding exactly one `public static IServiceCollection Add<Layer>(this IServiceCollection services, ...)` extension method named after its project — `AddApp`, `AddInfra`, `AddApi` — that returns `services` so calls can chain. A layer's registrations live in that layer because its implementations are its own types to name; `api`'s method is the only one that calls another layer's.
- **Startup initializers**: `<Concern>Initializer`, `internal sealed class` in the folder of the concern it initializes, implementing `IHostedService` to run one asynchronous startup step that DI registration cannot `await` (e.g. `IndexInitializer` in `infra/persistence/configuration` calling `IndexConfiguration.CreateAsync`). `internal` because only its own project's `DependencyInjection` names it, via `AddHostedService<T>()`.
- **Domain event handlers**: `<DomainEvent>Handler`, `internal sealed class` implementing MediatR's `INotificationHandler<DomainEventNotification<TEvent>>`, scaffolded alongside the command that raises the event (e.g. `PostCreatedEventHandler.cs` sits next to `CreatePostCommand`) and following the same encapsulation rule as request handlers — `internal` because MediatR discovers and invokes it by reflection, nothing references it directly.
- **DTOs**: `<Entity>Dto` / `<Entity>SummaryDto` (e.g. `AccountDto`, `AccountSummaryDto`), `sealed record` with positional parameters.
- **Mapping profiles**: `<Entity>Profile`, `sealed class` extending AutoMapper's `Profile`, one per entity.
- **Service ports (interfaces)**: `I<Capability>Service` in `app` (e.g. `IJwtService`, `IRedisService`), implemented by `<Capability>Service` in `infra` (e.g. `JwtService`, `RedisService`), `public sealed class` — `api` names `JwtService` to read its claim-type constants when configuring token validation, and all of them share one visibility rather than singling that class out.
- **Private fields**: `_camelCase`, `readonly`, assigned via constructor injection.
- **Async methods**: `Async` suffix, with `CancellationToken cancellationToken` as the last parameter (e.g. `UploadAsync`, `DispatchEventAsync`, `GetAsync`). Synchronous methods (e.g. `HashPassword`) omit the suffix.
- **Static constant/lookup holders**: declared as `sealed record` even though they hold only static members (e.g. `CollectionName`, `CacheKey`), matching how DTOs/commands are declared in the same layer.
- **Static utility classes**: `<Verb+er>` `static class` grouping pure helper methods with no state to hold (e.g. `CursorPaginator`) — unlike constant/lookup holders, these are declared as a plain `static class`, not a `record`.
- **Extension-method classes**: `<Type>Extensions`, `public static class` in the layer that owns the conversion, holding `this`-parameter extension methods over a type declared elsewhere (e.g. `ResultExtensions` in `api/common`, extending `FluentResults.Result`/`Result<T>`). Same shape as a static utility class, named after the type being extended rather than the verb performed.
- **Notification wrappers**: `<Concept>Notification<TGeneric>` generic `sealed class` implementing MediatR's `INotification` (e.g. `DomainEventNotification<TEvent> : INotification where TEvent : Event`), used to adapt a domain event into a MediatR notification for dispatch via `IPublisher`/`INotificationHandler`.
- **Request/result records**: paired `<Concept>Request` / `<Concept>Result` `sealed record` types for cross-cutting operations that aren't tied to one entity (e.g. `CursorPageRequest`, `CursorPageResult<T>`), following the same positional-record shape as DTOs. `AuthResult(string AccessToken, string RefreshToken)` (`feature/auth/AuthResult.cs`) is the same pattern with no paired `Request` type — `LoginCommand`/`RequestNewAccessTokenCommand` already serve as the request, so only the result half was needed; it lives at the feature root rather than nested in one use-case folder because both `Login` and `RequestNewAccessToken` return it.
- **`using` ordering**: `System.*` namespaces first (when present), then remaining namespaces alphabetically.

## OOP / DDD Conventions

- **Encapsulation**: entity and value-object properties use `{ get; private set; }` (or a read-only computed property); state changes only happen through named methods on the entity itself (`ChangeName`, `ChangeEmail`, `SavePost`), never through public setters from outside the class.
- **Construction**: entities and value objects have a `private` parameterless constructor (for the persistence/serialization layer) and a `private` parameterized constructor, with object creation and validation funneled through a `public static Create(...)` factory that returns `DomainResult<T>` instead of throwing on invalid input.
- **Aggregate collections**: child collections are stored as `private List<T> _items = new()` with a public `IReadOnlyCollection<T> Items => _items.AsReadOnly()` accessor, mutated only via aggregate-root methods (e.g. `Account.SavePost`, `Account.RemoveAvatar`).
- **Shared entity behavior**: cross-entity lifecycle operations (`Update`, `Delete`, `Recover`, soft-delete flags, timestamps) live once on `BaseEntity` and are reused/wrapped by entity-specific methods (e.g. `Account.DeleteAccount()` calls `Delete()`).
- **Domain events**: entities raise events via the protected `RaiseEvent(Event @event)` on `BaseEntity`; events are immutable `record` types capturing the data needed by downstream handlers, collected on the entity until dispatched.
- **Sealed by default**: concrete entities, value objects, handlers, validators, DTOs, and service implementations are `sealed`. Only intentional base types (`BaseEntity`, `Event`) are `abstract` and unsealed.
- **Result pattern over exceptions**: domain methods that can fail return `DomainResult` / `DomainResult<T>` rather than throwing, for both construction (`Account.Create`) and mutation (`Account.ChangeName`).
- **Two Result types, one per layer boundary, intentionally**: `domain` defines and uses only its own `DomainResult`/`DomainResult<T>` and takes no NuGet dependencies, keeping the domain layer framework-free. `app` uses the third-party `FluentResults.Result`/`Result<T>` as the MediatR handler return type, translating a failed `DomainResult` into a failed `FluentResults.Result` at the point the domain call returns (e.g. `if(!result.IsSuccess) return Result.Fail(result.Error);`). Do not introduce `FluentResults` (or any other app-layer package) into `domain`, and do not use `DomainResult` as a MediatR handler return type.

## Formatting Conventions

- **Indentation**: 4 spaces, no tabs.
- **Braces**: Allman style — the opening `{` for a type, method, or any multi-statement block sits on its own line. A single-statement `if` body omits braces entirely and is written indented on the next line (e.g. `if(IsDeleted)\n    return DomainResult.Failure(...);`); braces are added only once the block holds more than one statement.
- **Empty bodies**: `{}` with no space inside, on the same line as the declaration (e.g. `private Account() {}`), never `{ }`.
- **No space before a control-flow parenthesis**: `if(...)`, not `if (...)`.
- **No code comments anywhere in the codebase** — every `.cs` file relies on naming alone to convey intent; there are zero `//` comments in the current source tree. New code should follow suit rather than adding explanatory comments.
- **Field/property layout**: no blank line between adjacent field/property declarations at the top of a class; a private backing field is declared immediately above the public accessor that exposes it, with no blank line between the pair (e.g. `private List<Avatar> _avatars = new();` directly followed by `public IReadOnlyCollection<Avatar> Avatars => _avatars.AsReadOnly();`).
- **Method body spacing**: a blank line separates a guard-clause block from the logic that follows it, and a blank line precedes the final `return` statement.
- **Multi-line argument/parameter lists**: when a call, constructor, or record's positional parameter list doesn't fit one line, each argument goes on its own line, indented one level, with the closing `)` attached to the last argument (never on its own line) and no trailing comma. A record's base-list `: IRequest<...>` clause is placed on its own indented line following a multi-line parameter list (e.g. `CreateAccountCommand`).
- **Implicit usings**: `<ImplicitUsings>enable</ImplicitUsings>` is set on every project, so `System.*` namespaces are not explicitly `using`-imported unless the compiler requires a more specific one.

## Persistence & Cross-Cutting Conventions

- **No repository abstraction**: handlers call `IMongoDbContext.GetCollection<T>(name)` and use the MongoDB driver's `Builders<T>.Filter` / `.Projection` / `.Update` fluent API directly; there is no repository interface per aggregate.
- **Collection/cache key naming centralized**: Mongo collection names come from the static `CollectionName` holder; Redis cache keys and TTLs come from the static `CacheKey` holder (e.g. `CacheKey.Account(id)` → `"account:v1:id:{id}"`). Handlers must not inline literal collection or cache key strings.
- **Read: cache-aside**: query handlers check Redis first and populate it on a cache miss (e.g. `GetMyAccountHandler`). `auth`'s `GetAllRefreshTokenHandler`/`GetSpecificRefreshTokenHandler` are the deliberate exception: they never touch Redis at all, since `RefreshToken` is itself a security-sensitive credential — see `doc/app-operation.md`.
- **Create: write-through**: create handlers write the new row to MongoDB, map it to its DTO, then call `_redisService.SetAsync` to populate the cache directly with that DTO (e.g. `CreateAccountHandler`) — the cache is written alongside the database, not just invalidated. `LoginHandler`/`RequestNewAccessTokenHandler` also insert a new `RefreshToken` row but skip this step, for the same reason as above.
- **Update: invalidate-on-write**: update handlers write to MongoDB then call `_redisService.RemoveAsync` to drop the corresponding key (e.g. `ChangeNameHandler`), letting the next read repopulate it via cache-aside, rather than writing the updated value through.
- **List/cursor-paginated queries: cache-aside, short TTL**: handlers that return a page via `CursorPaginator.PaginateAsync<T>` follow the same cache-aside shape as single-entity reads — check Redis first, populate on a miss — but keyed via `CacheKey.List(entity, filterSignature, request)` (`"list:v1:{entity}:{filterSignature}:{cursor}:{pageSize}"`, encoding whatever filter the handler applies plus the cursor and page size) rather than `CacheKey.Account`-style per-id keys, and with `CacheKey.DefaultListTTL` (2 minutes) instead of `DefaultAccountTTL` (7 days) — list contents shift more often than one entity's data, so a short TTL bounds staleness without needing per-write invalidation logic that would have to reason about every cached page a given write could affect.
- **Cache-aside list queries go through `CachedCursorPaginator.PaginateAsync<TEntity, TDto>`**, not hand-written per handler: it wraps `CursorPaginator` with the Redis get/miss/populate cycle and the entity→DTO mapping, and always writes with `CacheKey.DefaultListTTL`. A handler supplies only what actually differs — the collection, the filter, the projection, and the cache key — so the cache-aside block exists in exactly one place. `GetMostReactedPostHandler` is one deliberate exception: it sorts by `ReactionAmount` instead of the cursor's `CreatedAt` ordering, so it cannot use `CursorPaginator` underneath and keeps its own inline cache-aside block. `GetAllRefreshTokenHandler` is another, for a different reason: it calls `CursorPaginator.PaginateAsync` directly with no cache wrapper at all, since `auth` never touches Redis (see above).
- **Paginating an embedded child array goes through `CachedEmbeddedArrayPaginator.PaginateAsync<TParent, TItem, TDto>`**, the counterpart to `CachedCursorPaginator` for `Follower`/`Following`/`Reaction` — child objects that live inside `Account.Followers`/`Account.Followings`/`Post.Reactions`/`Comment.Reactions` rather than their own top-level Mongo collection, so they have no `Id` and no `CreatedAt` for `CursorPaginator`'s cursor to key off. Underneath, `EmbeddedArrayPaginator` runs a `$match` (parent) → `$project` (down to just the target array field) → `$unwind` → `$match` (keyset cursor filter on dotted `{array}.{field}` paths) → `$sort` → `$limit` → `$replaceRoot` (promotes the unwound subdocument to the root so it deserializes cleanly as `TItem`) aggregation pipeline, using the child object's own timestamp (`FollowedAt`/`ReactedAt`) as the order key and its `AccountId` as the tiebreaker in place of `Id` — safe because every mutation path that adds to these arrays (`FollowService`, `ReactionService`) guards against a duplicate `AccountId` first. Cache keys follow the same `CacheKey.List` shape as `CachedCursorPaginator`, with the entity string suffixed to name the array (e.g. `"account:follower"`, `"post:reaction"`) so they don't collide with the parent entity's own cache keys.
- **The projection for an entity's `*SummaryDto` is declared once per feature** as `<Entity>Projection.Summary` (e.g. `feature/post/PostProjection.cs`), a static holder following the same `sealed record` shape as `CollectionName`/`CacheKey`. Query handlers reference that constant rather than re-listing `.Include(...)` calls, so adding a field to a `*SummaryDto` is a one-line change instead of an edit to every handler that projects it. Command handlers keep their own inline projections, which are narrower and differ per use case (e.g. `ChangeName` projects only `Name` and `IsDeleted`).
- **List queries always pass an explicit projection to `CursorPaginator.PaginateAsync`**: its `ProjectionDefinition<T>?` parameter has no default, so a handler must consciously pass `null` to fetch full documents rather than getting that by omission — projecting down to only the fields the target `*SummaryDto` needs is the expected default. The paginator itself always merges `CreatedAt` into whatever projection is passed, since the cursor needs it to encode the next page regardless of what the caller asked for.
- **Domain-to-BSON mapping lives entirely in `infra`**: `domain` carries no `[BsonId]`/`[BsonElement]` attributes and never will — that would be a package dependency the architecture forbids. Every mapping decision (id member, Guid representation, value-object shape, the private backing field behind each read-only collection property) is declared in `BsonConfiguration`, and a new entity or a renamed collection field is only persisted correctly once it is registered there. See `doc/infra-operation.md` for the mapping rules and their consequences.
- **Index keys are written as typed expressions, never strings**: `IndexConfiguration` builds every key with `Builders<T>.IndexKeys.Ascending(x => x.AccountId)` so a renamed property is a compile error rather than a silently unused index, and so the key path always matches whatever `BsonConfiguration` maps that member to (e.g. `x => x.Rate.Value` → `"Rate.Value"`). Indexes are never given explicit names: the driver's auto-generated name keeps `CreateManyAsync` idempotent across restarts, whereas a custom name over an existing equivalent index raises `IndexOptionsConflict`.
- **Domain event dispatch**: after a successful write, handlers call `_dbContext.DispatchEventAsync(entity.Events, cancellationToken)` to publish the entity's raised domain events through MediatR (`IPublisher`), wrapped in `DomainEventNotification<TEvent>`.
- **DI registration is layered, one `DependencyInjection` class per project**: `app`'s `AddApp` registers MediatR, AutoMapper and FluentValidation by assembly scan over `typeof(DependencyInjection).Assembly` — its own assembly — so new handlers, mapping profiles and validators need no manual registration. Validators are the one scan that needs an extra argument, `AddValidatorsFromAssembly(..., includeInternalTypes: true)`, because a `<UseCase>Validator` is `internal`. Pipeline behaviors are the exception to assembly scanning entirely: an open generic cannot be discovered by a scan, so each is named explicitly via `cfg.AddOpenBehavior(...)`, and **registration order is execution order** (see `doc/app-operation.md`). `infra`'s `AddInfra` registers the shared clients as singletons and wires every `app` port to its implementation one line per service via `services.AddScoped<IX, X>()`, listed alphabetically by interface name. `api`'s `AddApi` calls `AddApp()` and `AddInfra(configuration)` and adds the host's own concerns — OpenAPI, authentication, authorization. `Program.cs` calls `AddApi` and registers nothing itself; it holds only the middleware pipeline.
- **Nullable reference types** are enabled (`<Nullable>enable</Nullable>`) across all four projects; optional/absent values are modeled with `?` rather than sentinel values.

## API Layer Conventions

- **`app`'s `Result`/`Result<T>` carries only a plain-text error message**, never a typed error code — see "Two Result types, one per layer boundary, intentionally" above. `api/common/ResultExtensions.cs` is the single place that turns that message into an HTTP status code, via `ResultExtensions.ToActionResult()` / `ToActionResult<T>()` (success: `Result<T>` → `200 OK` with the value, non-generic `Result` → `204 No Content`; an endpoint that needs `201 Created` builds that itself with the `Result<T>`'s value rather than calling `ToActionResult`). A failure becomes a `ProblemDetails` (RFC 7807) response, or a `ValidationProblemDetails` when `Result.Errors` holds more than one entry — the signal that `ValidationBehavior` (not a handler) produced them, since a handler returns on its first failure and never accumulates more than one `Error`.
- **Failure status codes come from `ResultStatusMapper.Resolve(message)`**, a keyword classifier over the lowercased error message — not a fixed table of the exact strings in use today, because that would silently stop covering new handlers' failure messages as they're added. Keyword → status, checked in this order: message contains `"not found"` → `404`; contains `"already"` or `"must be different"` → `409`; contains `"unauthoriz"`, `"unauthentic"`, `"credentials"`, `"revoked"`, or `"token"` → `401`; anything else → `400` (an unclassified failure defaults to `400`, never `500` — `500` is reserved for unhandled exceptions, handled by middleware outside this mapper).
- **A handler's failure message is part of this contract**: wording drives the HTTP status a caller sees, so a new `Result.Fail(...)` / `DomainResult.Failure(...)` message should fit the existing vocabulary above (e.g. include "not found" for a missing entity, "already" for a duplicate/idempotency conflict) rather than introducing new phrasing that `ResultStatusMapper` cannot classify — an unmatched message still degrades safely to `400`, but not to the status the failure actually means.
