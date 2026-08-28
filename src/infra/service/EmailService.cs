using System.Net;
using System.Text.RegularExpressions;
using app;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace infra;

public sealed class EmailService : IEmailService
{
    private const string Host = "smtp.gmail.com";
    private const int Port = 587;

    private readonly IConfiguration _configuration;

    public EmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var address = _configuration["Email:Address"]!;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(address));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        var plainBody = Regex.Replace(htmlBody, "<.*?>", string.Empty);
        message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = plainBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(Host, Port, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(address, _configuration["Email:AppPassword"], cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    public Task SendOtpRegistrationEmailAsync(string to, string code, CancellationToken cancellationToken) =>
        SendAsync(to, "Verify your account", BuildOtpRegistrationBody(code), cancellationToken);

    public Task SendWelcomeEmailAsync(string to, string name, CancellationToken cancellationToken) =>
        SendAsync(to, "Welcome to Bump", BuildWelcomeBody(name), cancellationToken);

    private static string BuildOtpRegistrationBody(string code) =>
        $"""
        <div style="font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto;">
            <h2>Verify your account</h2>
            <p>Use the code below to complete your registration. It expires in 10 minutes.</p>
            <p style="font-size: 32px; font-weight: bold; letter-spacing: 8px;">{code}</p>
            <p>If you didn't request this, you can safely ignore this email.</p>
        </div>
        """;

    private static string BuildWelcomeBody(string name) =>
        $"""
        <div style="font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto;">
            <h2>Welcome, {WebUtility.HtmlEncode(name)}!</h2>
            <p>Your account is all set up. We're glad to have you on Bump.</p>
        </div>
        """;
}
