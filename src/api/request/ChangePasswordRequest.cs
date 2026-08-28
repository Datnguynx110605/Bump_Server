namespace api;

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
