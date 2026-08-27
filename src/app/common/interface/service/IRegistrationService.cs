namespace app;

public interface IRegistrationService
{
    Task<string> GenerateTokenAsync(string email, CancellationToken cancellationToken);

    Task<string?> ExtractEmailAsync(string registrationToken, CancellationToken cancellationToken);

    Task InvalidateTokenAsync(string registrationToken, CancellationToken cancellationToken);
}
