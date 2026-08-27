# Domain Operation

This document explains how the `domain` layer actually operates end to end: how an object comes into existence, how it enforces its own rules, how it communicates success/failure, and how it raises events — entirely within `domain`'s own boundary. It does not cover how `app`/`infra` consume `domain` (composition, dispatch, persistence) — see `doc/architecture.md` for that. For naming and formatting conventions, see `doc/project-rules.md`.

`domain` is 100% pure: it has no project or package references. Every mechanism described below is plain C#, with no framework involved.

## The Three Core Patterns

### 1. Static Factory Method for Creation

No `domain` type with invariants to enforce is ever constructed with `new` from outside itself. Each has:

- A `private` parameterless constructor — exists only so the persistence layer can materialize an instance from a stored document without running validation again; it is never called from application code.
- A `private` parameterized constructor — assigns fields, but performs no validation itself.
- A `public static Create(...)` factory — the *only* way outside code can produce a valid instance. It validates every input, and only calls the private constructor once validation passes.

```csharp
public sealed class Email
{
    public string Value { get; }
    private Email() {}
    private Email(string value) { Value = value; }

    public static DomainResult<Email> Create(string value)
    {
        if(string.IsNullOrWhiteSpace(value))
            return DomainResult<Email>.Failure("Email is required");
        if(!value.Contains("@"))
            return DomainResult<Email>.Failure("Email is invalid");
        if(value.Length > 200)
            return DomainResult<Email>.Failure("Email is invalid");

        return DomainResult<Email>.Success(new Email(value));
    }
}
```

Because both constructors are `private`, it is a compile error to write `new Email("x")` or `new Account(...)` from `app` or anywhere else in the codebase. The only path to an instance is through `Create`, so an invalid `Account` or `Email` cannot exist in memory — the type system itself enforces the invariant, not caller discipline.

This pattern applies to every aggregate root and every value object. It does **not** apply to the five plain child objects (`Avatar`, `Image`, `Follower`, `Following`, `Reaction`) — see "Child Objects" below for why they're the exception.

### 2. Rich Domain Model

Aggregate roots are not data bags read and mutated by external handler code — they hold their own business logic:

- Every mutable property is `{ get; private set; }`. There are no public setters anywhere in `domain`.
- Every child collection is a `private List<T> _items = new()` field, exposed publicly only as `IReadOnlyCollection<T> Items => _items.AsReadOnly()`. Outside code can read the collection but cannot add, remove, or replace items in it directly.
- The *only* way to change an aggregate's state is to call one of its own methods (`Account.ChangeName(...)`, `Post.ChangeRate(...)`, `Shop.AddPost(...)`). Each method contains the business rule for that specific change — the check that makes the change valid or invalid lives next to the state it protects, not in a handler in `app`.
- Methods return `DomainResult`/`DomainResult<T>` rather than throwing, so a handler in `app` can react to an invalid operation (e.g. "new name must be different") as a normal control-flow value, not an exception.

```csharp
public DomainResult ChangeName(Name name)
{
    if(Name.Value == name.Value)
        return DomainResult.Failure("New name must be different");

    Name = name;

    return Update();
}
```

`app` handlers fetch an aggregate, call one of these methods, and persist whatever the aggregate decided its new state should be — they never assign to a property or manipulate a collection themselves.

### 3. Result Pattern

`domain` defines its own result type and does not use exceptions for expected failures (invalid input, a business rule being violated). This is the one abstraction `domain` introduces on top of plain C#, and it is deliberately self-contained — `domain.csproj` has zero package references, so this type could not be a third-party library type.

```csharp
public class DomainResult
{
    public bool IsSuccess { get; private set; }
    public string? Error { get; private set; }

    public static DomainResult Success() => new(true, null);
    public static DomainResult Failure(string error) => new(false, error);
    public static DomainResult<T> Success<T>(T value) => DomainResult<T>.Success(value);
    public static DomainResult<T> Failure<T>(string error) => DomainResult<T>.Failure(error);
}

public class DomainResult<T> : DomainResult
{
    public T? Value { get; private set; }

    public static DomainResult<T> Success(T value) => new(true, value, null);
    public static new DomainResult<T> Failure(string error) => new(false, default, error);
}
```

- `DomainResult` (non-generic) is returned by mutation methods that don't produce a new value (`ChangeName`, `Delete`, `AddPost`) — the caller only needs to know whether it succeeded.
- `DomainResult<T>` (generic, inherits `DomainResult`) is returned by every `Create` factory — the caller needs both the pass/fail outcome and, on success, the created instance.
- `Value` is only meaningful when `IsSuccess` is `true`; on failure it is `default(T)` (`null` for reference types). Every caller is expected to check `IsSuccess` before reading `Value` — nothing enforces this at compile time, it's a convention every current call site in `app` follows.
- Failure carries a plain `string Error` — there is no error code, error type hierarchy, or list of multiple errors. One operation fails for exactly one reason at a time.
- Methods commonly delegate to `BaseEntity.Update()` as their last step, so a mutation's `DomainResult` is often literally the `DomainResult` that `Update()` produced (see below) — failures compose without extra glue code.

The result pattern is only as good as how consistently it's applied, and the codebase applies it the same way everywhere: every `Create` and every mutation method opens with one or more **guard clauses** — `if(invalid) return DomainResult.Failure(...);` — before touching any state, and only reaches the "happy path" assignment/mutation at the bottom once every guard has passed. This fail-fast style holds with no exception across all 13 entities and 3 value objects: there is no method in `domain` that validates partway through a mutation, or validates after already changing state.

## Entity Lifecycle (`BaseEntity`)

Every aggregate root (`Account`, `Post`, `Comment`, `Shop`, `Report`, `RefreshToken`) inherits identity and soft-delete lifecycle from `BaseEntity`, which is the one non-sealed, `abstract` type in `domain/core/entity`:

| Member | Behavior |
|---|---|
| `Id` | `Guid`, assigned once via `Guid.NewGuid()` at construction, never changes. |
| `CreatedAt` | `DateTime`, set to `DateTime.UtcNow` at construction. |
| `UpdatedAt` | `DateTime?`, `null` until the first successful `Update()`. |
| `DeletedAt` / `RecoveredAt` | `DateTime?`, set by `Delete()` / `Recover()` respectively. |
| `IsDeleted` | `bool`, the soft-delete flag; `false` at construction. |

Three protected-by-convention (but `public`) methods drive the lifecycle, each returning `DomainResult`:

- **`Update()`** — fails with `"Operation is invalid"` if `IsDeleted` is already `true`; otherwise sets `UpdatedAt = DateTime.UtcNow` and succeeds. Every property-changing method on every aggregate root ends by calling this (directly or by returning its result), so `UpdatedAt` is always kept in sync with the last successful change.
- **`Delete()`** — fails if `IsDeleted` is already `true` (no double-delete); otherwise sets `IsDeleted = true` and `DeletedAt = DateTime.UtcNow`.
- **`Recover()`** — fails if `IsDeleted` is `false` (nothing to recover); otherwise sets `IsDeleted = false`, `DeletedAt = null`, and `RecoveredAt = DateTime.UtcNow`.

This gives every aggregate root the same state machine: **Active → (Delete) → Deleted → (Recover) → Active**, looping indefinitely. `Update()` only succeeds while Active. There is no hard-delete method anywhere in `domain` — deletion is always the soft-delete flag flip above.

This is a **Template Method**: `BaseEntity` implements the lifecycle transitions once, and every subclass reuses that exact implementation rather than re-deriving "is this entity deleted" logic per aggregate — a subclass's job is only to decide *when* to call `Update()`/`Delete()`/`Recover()` (i.e. which of its own methods should trigger a state transition), not *how* the transition itself works.

`Account`, `Post`, and `Comment` wrap these base methods with a domain-specific name: `Account.DeleteAccount()` and `Account.RecoverAccount()` call `Delete()`/`Recover()`; `Post.DeletePost()` and `Comment.DeleteComment()` call `Delete()`. `Shop`, `Report`, and `RefreshToken` have no such wrapper — their base `Update`/`Delete`/`Recover` are `public` and callable directly, without an entity-specific method name. The underlying state machine is identical for all six aggregate roots either way.

Value objects (`Email`, `Name`, `Amount`) and child objects (`Avatar`, `Image`, `Follower`, `Following`, `Reaction`) do **not** inherit `BaseEntity` — they have no `Id`, no timestamps, and no soft-delete lifecycle of their own; they live and die with the aggregate that owns them.

## Value Objects

| Value object | Wraps | `Create` validation rules |
|---|---|---|
| `Email` | `string Value` | required (not empty/whitespace); must contain `"@"`; max 200 characters |
| `Name` | `string Value` | required; max 200 characters; every character must be lowercase letter or digit (`char.IsLower` or `char.IsDigit`) |
| `Amount` | `int Value` | must be non-negative (`>= 0`) |

All three follow the same shape as aggregate roots (private constructors + static `Create` returning `DomainResult<T>`), but differ in two ways: they have no identity (no `Id`, no `BaseEntity`), and they are fully immutable after construction — none of them expose a mutation method. Changing a `Name` or `Email` means creating a brand-new value object via `Create` and replacing the old one on the owning aggregate (see `Account.ChangeName`/`Account.ChangeEmail` above), never mutating the value object in place.

This no-identity, no-mutation shape is the DDD **Value Object** pattern in its own right, not just a variant of the static-factory pattern above: an `Email` is defined entirely by its `Value`, so two `Email` instances holding the same string are interchangeable, whereas two `Account` instances are never interchangeable even if every property happened to match — identity (`Id`) is what makes an aggregate root an aggregate root, and its deliberate absence is what makes a value object a value object.

## Aggregate Roots

`domain` follows the DDD rule that aggregates reference other aggregates only by `Guid` id, never by holding a loaded object — `Post.AccountId`, `Comment.AccountId`/`PostId`, and `Report.PostId`/`CommentId`/`AccountId` are all plain `Guid`s, not navigation properties. This keeps each aggregate's consistency boundary self-contained: mutating an `Account` never requires loading or touching a `Post`. `Post` is the one deliberate exception — `Post.Create` embeds a freshly-created `Shop` object rather than referencing an existing one by id (see the `Post` profile below), so it does not fully follow this rule today.

### `Account`

- **Identity/state**: `Email`, `Name`, `Password` (BCrypt hash), `Bio` (nullable, max 2000 chars), `Role` (defaults to `Role.User`), `FollowerAmount`/`FollowingAmount` (`Amount`, default 0).
- **Owned collections**: `Avatars` (`List<Avatar>`), `Followers` (`List<Follower>`), `Followings` (`List<Following>`), `SavePosts` (`List<Post>`), `SaveShops` (`List<Shop>`).
- **`Create(email, name, password, bio, url?)`**: fails if `password.Length != 60` (i.e. the caller must pass an already-BCrypt-hashed password, not plaintext); fails if `bio` exceeds 2000 characters. On success, optionally seeds one `Avatar` if `url` is non-empty, and raises `AccountCreatedEvent(accountId, email, createdAt)`.
- **Business methods**: `SavePost`/`RemoveSavedPost`, `SaveShop`/`RemoveSavedShop` (all fail if the target is already present / not found, respectively); `ChangeEmail`/`ChangeName` (fail if the new value equals the current one); `ChangePassword` (fails if identical to current, or if the new hash isn't exactly 60 characters); `ChangeBio` (fails only on the 2000-character limit — unlike the others, a `null`/empty bio is allowed and re-settable); `ChangeAvatar` (fails on empty URL or a duplicate URL already present); `RemoveAvatar` (fails if the URL isn't found).
- **Lifecycle wrappers**: `DeleteAccount()`, `RecoverAccount()`.

### `Post`

- **Identity/state**: `AccountId` (owner reference by `Guid`, not a loaded `Account`), `Shop` (embedded, not referenced by id — see note below), `Caption` (nullable), `Rate`/`ReactionAmount`/`CommentAmount` (`Amount`, latter two default 0).
- **Owned collections**: `Images` (`List<Image>`), `Reactions` (`List<Reaction>`).
- **`Create(accountId, name, address, caption, rate, urls?)`**: fails if `accountId == Guid.Empty`; internally calls `Shop.Create(name, address)` and fails (propagating the `Shop` error) if that fails. On success, seeds `Images` from `urls` if provided, and raises `PostCreatedEvent(accountId, postId, createdAt)`. Note: this constructs a **new** `Shop` value every time a post is created — `Post` does not look up an existing `Shop` by id, so `Shop.Create` runs (and can fail) as part of every `Post.Create` call.
- **Business methods**: `ChangeCaption` (takes a non-nullable `string`; no validation beyond `Update()`'s own soft-delete check — any text, including an empty string, is accepted and assigned directly), `ChangeRate` (same — accepts any `Amount`, no bounds check beyond `Amount.Create`'s own non-negative rule).
- **Lifecycle wrapper**: `DeletePost()`.

### `Comment`

- **Identity/state**: `AccountId`, `PostId` (both `Guid` references, not loaded aggregates), `Content` (nullable, max 2000 chars), `ReactionAmount` (`Amount`, default 0).
- **Owned collections**: `Images` (`List<Image>`), `Reactions` (`List<Reaction>`).
- **`Create(accountId, postId, content, urls?)`**: fails if `accountId == Guid.Empty`; fails if `content` exceeds 2000 characters. Seeds `Images` from `urls` if provided, and raises `CommentedPostEvent(accountId, commentId, postId, createdAt)`.
- **Business methods**: `ChangeContent(content)` — fails only on the 2000-character limit; unlike `Create`, this method takes a non-nullable `string`, so an edit can set `Content` to an empty string but never back to `null` — `Content == null` is a state only `Create(content: null)` can produce.
- **Lifecycle wrapper**: `DeleteComment()`.

### `Shop`

- **Identity/state**: `Name`, `Address` (both required strings), `Rate` (`Amount`, default 0).
- **Owned collection**: `Posts` (`List<Post>`).
- **`Create(name, address)`**: fails if `name` or `address` is empty/whitespace. Raises no domain event.
- **Business methods**: `AddPost(post)` (fails if a post with the same `Id` is already present), `RemovePost(postId)` (fails if not found).
- **Lifecycle wrapper**: none — `Update()`/`Delete()`/`Recover()` are called directly if needed, with no `Shop`-specific method name.

### `Report`

- **Identity/state**: `ReporterId` (required `Guid`), and up to one target — `PostId`, `CommentId`, `AccountId` (all nullable `Guid`), plus `Reason` (required string).
- **Owned collections**: none — a flat entity.
- **`Create(reporterId, postId, commentId, accountId, reason)`**: fails if `reporterId == Guid.Empty`; fails if `reason` is empty/whitespace. Notably, `Create` does not enforce that exactly one of `postId`/`commentId`/`accountId` is provided — all three, or none, are currently accepted without a domain-level error.
- **Business methods**: none beyond `Create` — a `Report` is created once and never mutated by any method in `domain` today.
- **Lifecycle wrapper**: none.

### `RefreshToken`

- **Identity/state**: `AccountId` (required `Guid`), `Token` (a 64-byte random value, Base64-encoded, generated internally — never supplied by the caller), `IsRevoked` (`bool`, default `false`), `ExpiryDate` (defaults to `DateTime.UtcNow.AddDays(7)` at construction, not adjustable via `Create`).
- **Owned collections**: none.
- **`Create(accountId)`**: fails if `accountId == Guid.Empty`. Generates the token itself via a private `GenerateToken()` (`RandomNumberGenerator.GetBytes(64)`); the caller cannot choose or influence the token value.
- **Business methods**: `Validate()` — returns `DomainResult` (not `DomainResult<T>`, since it produces no value): fails with `"Refresh token has been revoked"` if `IsRevoked`, fails with `"Expired"` if past `ExpiryDate`, otherwise succeeds. `IsRevoked` is `private set`, changeable only from within `RefreshToken` itself; no method on `RefreshToken` currently sets it.
- **Lifecycle wrapper**: none.

## Child Objects (not aggregate roots)

`Avatar`, `Image`, `Follower`, `Following`, and `Reaction` are the five entities in `domain/core/entity` that do **not** derive from `BaseEntity` and do **not** follow the static-factory pattern — each has a `public` constructor callable directly, with no `Create` method and no validation:

| Type | Fields | Constructed by |
|---|---|---|
| `Avatar` | `Url`, `CreatedAt` (auto) | `Account.Create` / `Account.ChangeAvatar` |
| `Image` | `Url`, `UploadedAt` (auto) | `Post.Create` / `Comment.Create` (from a `urls` list) |
| `Follower` | `AccountId`, `Name`, `Avatar?`, `FollowedAt` (auto) | no domain method constructs one |
| `Following` | `AccountId`, `Name`, `Avatar?`, `FollowedAt` (auto) | no domain method constructs one |
| `Reaction` | `AccountId`, `Name`, `Avatar?`, `ReactedAt` (auto) | no domain method constructs one |

These are intentionally lighter-weight than aggregate roots: they carry denormalized display data (`Name`, `Avatar`) rather than being looked up by reference, they have no independent identity or lifecycle, and they are only ever meant to be added to / removed from a parent aggregate's private collection — never persisted or queried on their own. Their public constructor is a smaller version of the same ownership rule the aggregate roots enforce with `private` constructors: nothing outside `domain` is expected to hold a bare `Avatar` or `Reaction` that isn't already attached to an aggregate, even though the compiler does not currently prevent it the way it prevents `new Account(...)`.

## Domain Events

Alongside static factories, the rich domain model, value objects, and the result pattern, **Domain Events** are the fifth pattern `domain` applies: instead of a mutation only changing state and returning a result, a significant occurrence (an account being created, a post being commented on) is also recorded as an immutable fact for interested parties elsewhere in the system to react to later — decoupling "what happened inside this aggregate" from "who needs to know and what they do about it."

Every domain event is a `sealed record` deriving from the `abstract record Event`:

```csharp
public abstract record Event
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EntityId { get; private set; }
    public string PayloadHash { get; private set; }
    public EventStatus Status { get; private set; } = EventStatus.Pending;
    public DateTime RaisedAt { get; private set; } = DateTime.UtcNow;
}
```

Only `Id` and `RaisedAt` are populated automatically at construction. `EntityId`, `PayloadHash`, and `Status` are declared on the base type but no derived event's constructor sets them — every event raised today has `EntityId == Guid.Empty`, `PayloadHash == null`, and `Status == EventStatus.Pending` regardless of what actually happened. Each derived event instead carries its own explicit identifiers (e.g. `AccountCreatedEvent.AccountId`) rather than using the base `EntityId`.

An aggregate raises an event by calling the `protected void RaiseEvent(Event @event)` method it inherits from `BaseEntity`, which appends to a private `List<Event> _events`; the event sits in `entity.Events` (`IReadOnlyCollection<Event>`) until something outside `domain` reads and dispatches it — that dispatch mechanism is `app`/`infra`'s responsibility, described in `doc/architecture.md`, not `domain`'s.

Seven event types exist in `domain/core/event`; three are constructed, all from inside a `Create` factory:

| Event | Raised by |
|---|---|
| `AccountCreatedEvent` | `Account.Create` |
| `PostCreatedEvent` | `Post.Create` |
| `CommentedPostEvent` | `Comment.Create` |
| `FollowedAccountEvent` | no domain method constructs one |
| `GetFollowedEvent` | no domain method constructs one |
| `ReactedPostEvent` | no domain method constructs one |
| `ReactedCommentEvent` | no domain method constructs one |
