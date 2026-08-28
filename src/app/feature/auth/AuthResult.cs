namespace app;

public sealed record AuthResult(
    string AccessToken,
    string RefreshToken);
