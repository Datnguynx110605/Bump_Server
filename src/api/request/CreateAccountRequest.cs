namespace api;

public sealed record CreateAccountRequest(
    string RegistrationToken,
    string Name,
    string Password,
    string? Bio);
