namespace app;

public interface IOtpService
{
    Task<string> GenerateAsync(string identifier, CancellationToken cancellationToken);

    Task<bool> VerifyAsync(string identifier, string code, CancellationToken cancellationToken);
}
