namespace app;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken);

    Task SendOtpRegistrationEmailAsync(string to, string code, CancellationToken cancellationToken);

    Task SendWelcomeEmailAsync(string to, string name, CancellationToken cancellationToken);
}
