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
