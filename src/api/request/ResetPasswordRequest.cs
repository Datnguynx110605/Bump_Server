namespace api;

public sealed record ResetPasswordRequest(
    string RegistrationToken,
    string NewPassword);
