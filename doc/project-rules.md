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
    │   └── Properties              # launch settings
    ├── app                         # application layer: use cases + ports for infra (depends on domain only)
    │   ├── common
    │   │   ├── behavior            # MediatR pipeline behaviors (cross-cutting concerns around handlers)
    │   │   ├── cache               # cache key conventions
    │   │   ├── dto                 # data transfer objects returned by handlers
    │   │   ├── interface
    │   │   │   ├── persistence     # persistence abstractions (e.g. IMongoDbContext) implemented by infra
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
    │       │       ├── GetReportByAccount
    │       │       ├── GetReportByComment
    │       │       ├── GetReportByPost
    │       │       ├── GetReportByReporter
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
        ├── persistence
        │   ├── configuration        # persistence-specific setup (e.g. Mongo class maps/serialization)
        │   └── dbcontext            # MongoDB database context implementing IMongoDbContext
        └── service                  # concrete implementations of app's service interfaces
                                      # (JwtService, EmailService, RedisService, R2Service, RabbitMQService,
                                      #  BCryptService, OtpService, CurrentIdentityService, RegistrationService)

123 directories
```

## Naming Conventions

- **Namespaces**: one namespace per project, matching the project name exactly (`namespace domain;`, `namespace app;`, `namespace infra;`), declared file-scoped. Folders organize files on disk but do not create nested namespaces.
- **Entities / value objects**: PascalCase noun (`Account`, `Post`, `Email`, `Name`, `Amount`), `sealed class`.
- **Domain events**: PascalCase, past-tense verb + `Event` suffix (`AccountCreatedEvent`, `PostCreatedEvent`, `FollowedAccountEvent`), `abstract record` base with `sealed record` (or plain `record`) per event.
- **CQRS messages**: `<UseCase><Command|Query>` (e.g. `CreateAccountCommand`, `ChangeNameCommand`, `GetMyAccountQuery`), `public sealed record` implementing `IRequest<TResult>`. `public` because the message is the feature's crossing point — constructed and sent via MediatR (`ISender`) from outside the `app` assembly (e.g. from `api`).
- **Handlers**: `<UseCase>Handler`, `internal sealed class` implementing `IRequestHandler<TRequest, TResult>`. `internal` because a handler is an implementation detail: MediatR discovers and invokes it by reflection at runtime, and no other layer references the class directly.
- **Validators**: `<UseCase>Validator`, `internal sealed class` extending `AbstractValidator<TCommand>` (FluentValidation), one per command/query that needs input validation. `internal` for the same reason as handlers — FluentValidation's pipeline behavior discovers and runs it, nothing calls it directly.
- **Domain event handlers**: `<DomainEvent>Handler`, `internal sealed class` implementing MediatR's `INotificationHandler<DomainEventNotification<TEvent>>`, scaffolded alongside the command that raises the event (e.g. `AccountCreatedEventHandler.cs` sits next to `CreateAccountCommand`) and following the same encapsulation rule as request handlers — `internal` because MediatR discovers and invokes it by reflection, nothing references it directly. A stub file not yet marked `internal sealed` or not yet implementing `INotificationHandler` is unfinished scaffolding to complete, not an exception to this rule.
- **DTOs**: `<Entity>Dto` / `<Entity>SummaryDto` (e.g. `AccountDto`, `AccountSummaryDto`), `sealed record` with positional parameters.
- **Mapping profiles**: `<Entity>Profile`, `sealed class` extending AutoMapper's `Profile`, one per entity.
- **Service ports (interfaces)**: `I<Capability>Service` in `app` (e.g. `IJwtService`, `IRedisService`), implemented by `<Capability>Service` in `infra` (e.g. `JwtService`, `RedisService`), `internal sealed class`.
- **Private fields**: `_camelCase`, `readonly`, assigned via constructor injection.
- **Async methods**: `Async` suffix, with `CancellationToken cancellationToken` as the last parameter (e.g. `UploadAsync`, `DispatchEventAsync`, `GetAsync`). Synchronous methods (e.g. `HashPassword`) omit the suffix.
- **Static constant/lookup holders**: declared as `sealed record` even though they hold only static members (e.g. `CollectionName`, `CacheKey`), matching how DTOs/commands are declared in the same layer.
- **Static utility classes**: `<Verb+er>` `static class` grouping pure helper methods with no state to hold (e.g. `CursorPaginator`) — unlike constant/lookup holders, these are declared as a plain `static class`, not a `record`.
- **Notification wrappers**: `<Concept>Notification<TGeneric>` generic `sealed class` implementing MediatR's `INotification` (e.g. `DomainEventNotification<TEvent> : INotification where TEvent : Event`), used to adapt a domain event into a MediatR notification for dispatch via `IPublisher`/`INotificationHandler`.
- **Request/result records**: paired `<Concept>Request` / `<Concept>Result` `sealed record` types for cross-cutting operations that aren't tied to one entity (e.g. `CursorPageRequest`, `CursorPageResult<T>`), following the same positional-record shape as DTOs.
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
- **Read: cache-aside**: query handlers check Redis first and populate it on a cache miss (e.g. `GetMyAccountHandler`).
- **Create: write-through**: create handlers write the new row to MongoDB, map it to its DTO, then call `_redisService.SetAsync` to populate the cache directly with that DTO (e.g. `CreateAccountHandler`) — the cache is written alongside the database, not just invalidated.
- **Update: invalidate-on-write**: update handlers write to MongoDB then call `_redisService.RemoveAsync` to drop the corresponding key (e.g. `ChangeNameHandler`), letting the next read repopulate it via cache-aside, rather than writing the updated value through.
- **List/cursor-paginated queries: cache-aside, short TTL**: handlers that return a page via `CursorPaginator.PaginateAsync<T>` follow the same cache-aside shape as single-entity reads — check Redis first, populate on a miss — but keyed via `CacheKey.List(entity, filterSignature, request)` (`"list:v1:{entity}:{filterSignature}:{cursor}:{pageSize}"`, encoding whatever filter the handler applies plus the cursor and page size) rather than `CacheKey.Account`-style per-id keys, and with `CacheKey.DefaultListTTL` (2 minutes) instead of `DefaultAccountTTL` (7 days) — list contents shift more often than one entity's data, so a short TTL bounds staleness without needing per-write invalidation logic that would have to reason about every cached page a given write could affect.
- **List queries always pass an explicit projection to `CursorPaginator.PaginateAsync`**: its `ProjectionDefinition<T>?` parameter has no default, so a handler must consciously pass `null` to fetch full documents rather than getting that by omission — projecting down to only the fields the target `*SummaryDto` needs is the expected default. The paginator itself always merges `CreatedAt` into whatever projection is passed, since the cursor needs it to encode the next page regardless of what the caller asked for.
- **Domain event dispatch**: after a successful write, handlers call `_dbContext.DispatchEventAsync(entity.Events, cancellationToken)` to publish the entity's raised domain events through MediatR (`IPublisher`), wrapped in `DomainEventNotification<TEvent>`.
- **DI registration**: `infra` implementations are wired to their `app` interface one line per service in `Program.cs` via `builder.Services.AddScoped<IX, X>()`, listed alphabetically by interface name; MediatR and AutoMapper are registered by assembly scan using `typeof(IMongoDbContext).Assembly` as the marker type, so new handlers/profiles need no manual registration.
- **Nullable reference types** are enabled (`<Nullable>enable</Nullable>`) across all four projects; optional/absent values are modeled with `?` rather than sentinel values.
