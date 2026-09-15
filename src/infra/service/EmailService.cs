using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using app;
using Microsoft.Extensions.Configuration;

namespace infra;

public sealed class EmailService : IEmailService
{
    private const string SendEndpoint = "https://api.resend.com/emails";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public EmailService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var plainBody = Regex.Replace(htmlBody, "<.*?>", string.Empty);

        using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
        {
            Content = JsonContent.Create(new
            {
                from = _configuration["Email:Address"]!,
                to = new[] { to },
                subject,
                html = htmlBody,
                text = plainBody
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _configuration["Email:ApiKey"]);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if(!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Resend returned {(int)response.StatusCode}: {body}");
        }
    }

    public Task SendOtpRegistrationEmailAsync(
        string to,
        string code,
        CancellationToken cancellationToken) =>
        SendAsync(to, "Verify your account", BuildOtpRegistrationBody(code), cancellationToken);

    public Task SendWelcomeEmailAsync(
        string to,
        string name,
        CancellationToken cancellationToken) =>
        SendAsync(to, "Welcome to Bump", BuildWelcomeBody(name), cancellationToken);

    private static string BuildOtpRegistrationBody(string code) =>
        $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Verify your account</title>
        </head>
        <body style="margin:0; padding:0; background-color:#F5EFE6; font-family:'Segoe UI', Roboto, Helvetica, Arial, sans-serif;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#F5EFE6; padding:32px 16px;">
                <tr>
                    <td align="center">
                        <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="max-width:480px; width:100%; background-color:#FBF8F3; border:1px solid #D8C9B5;">
                            <tr>
                                <td style="padding:24px 32px; border-bottom:1px solid #D8C9B5;">
                                    <span style="font-size:20px; font-weight:700; color:#3E2723; letter-spacing:1px;">BUMP</span>
                                </td>
                            </tr>
                            <tr>
                                <td style="padding:32px;">
                                    <h1 style="margin:0 0 12px; font-size:22px; color:#3E2723;">Verify your account</h1>
                                    <p style="margin:0 0 24px; font-size:15px; line-height:1.5; color:#6B584B;">
                                        Thanks for signing up! Enter the verification code below to complete your registration.
                                    </p>
                                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                                        <tr>
                                            <td align="center" style="background-color:#F5EFE6; border:1px solid #D8C9B5; padding:20px;">
                                                <span style="font-size:36px; font-weight:700; letter-spacing:10px; color:#3E2723;">{code}</span>
                                            </td>
                                        </tr>
                                    </table>
                                    <p style="margin:24px 0 0; font-size:14px; line-height:1.5; color:#8A7A6D;">
                                        This code expires in <strong style="color:#3E2723;">10 minutes</strong>. Do not share it with anyone, including Bump staff.
                                    </p>
                                    <p style="margin:16px 0 0; font-size:14px; line-height:1.5; color:#8A7A6D;">
                                        If you didn't request this code, you can safely ignore this email.
                                    </p>
                                </td>
                            </tr>
                            <tr>
                                <td style="padding:20px 32px; background-color:#F5EFE6; border-top:1px solid #D8C9B5;">
                                    <p style="margin:0; font-size:12px; color:#8A7A6D;">
                                        This is an automated message from Bump. Please do not reply to this email.
                                    </p>
                                </td>
                            </tr>
                        </table>
                    </td>
                </tr>
            </table>
        </body>
        </html>
        """;

    private static string BuildWelcomeBody(string name) =>
        $"""
        <div style="font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto;">
            <h2>Welcome, {WebUtility.HtmlEncode(name)}!</h2>
            <p>Your account is all set up. We're glad to have you on Bump.</p>
        </div>
        """;
}
