using FluentResults;

namespace app;

// Carries the intended HTTP status with the failure instead of leaving the API layer to
// infer it from the message text. Substring matching used to send anything containing
// "token" to 401, so "RegistrationToken is required" answered 401 instead of 400, and any
// reword silently changed the contract.
public static class AppError
{
    public const string StatusKey = "status";

    public static Error BadRequest(string message) => Create(message, 400);

    public static Error Unauthorized(string message) => Create(message, 401);

    public static Error NotFound(string message) => Create(message, 404);

    public static Error Conflict(string message) => Create(message, 409);

    // Distinct from the edge rate limiter, which rejects before a handler runs and answers
    // from RateLimiterPolicy. This is a handler-level budget the edge cannot express,
    // because it is scoped to an email address rather than to a connection or a principal.
    public static Error TooManyRequests(string message) => Create(message, 429);

    private static Error Create(string message, int status) =>
        new Error(message).WithMetadata(StatusKey, status);
}
