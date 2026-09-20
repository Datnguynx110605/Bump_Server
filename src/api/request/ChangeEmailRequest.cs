namespace api;

public sealed record ChangeEmailRequest(
    string RegistrationToken,
    string CurrentPassword);
