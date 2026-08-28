namespace api;

public sealed record LoginRequest(
    string Email,
    string Password);
