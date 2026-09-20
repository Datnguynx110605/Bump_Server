namespace app;

public interface IOtpService
{
    /// Returns null when the address has exhausted its generation budget or is locked out
    /// after too many failed guesses. Callers must not distinguish that from success in what
    /// they tell the caller — see SendEmailForVerifyHandler.
    Task<string?> GenerateAsync(string identifier, CancellationToken cancellationToken);

    Task<bool> VerifyAsync(string identifier, string code, CancellationToken cancellationToken);
}
