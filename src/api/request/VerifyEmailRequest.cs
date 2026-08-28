namespace api;

public sealed record VerifyEmailRequest(
    string Email,
    string Otp);
