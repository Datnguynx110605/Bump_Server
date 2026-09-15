using FluentResults;
using MediatR;

namespace app;

// Returns a fresh pair rather than nothing: changing the password revokes every refresh
// token on the account, including the caller's own, so without a replacement the user is
// silently logged out the moment their current access token expires.
public sealed record ChangePasswordCommand(
    string currentPassword,
    string newPassword)
    : IRequest<Result<AuthResult>>;
