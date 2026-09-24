using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace integration;

[Collection(IntegrationCollection.Name)]
public class EmailServiceTests
{
    private readonly IntegrationTestFixture _fixture;

    public EmailServiceTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SendAsync_SuccessResponse_SendsToResendWithTheExpectedFields()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        _fixture.HttpHandler.Respond = request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        using var scope = _fixture.Services.CreateScope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

        await email.SendAsync("to@test.invalid", "Subject", "<p>Hello</p>", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("https://api.resend.com/emails", captured!.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);

        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.Equal("to@test.invalid", payload.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal("Subject", payload.RootElement.GetProperty("subject").GetString());
        Assert.Equal("<p>Hello</p>", payload.RootElement.GetProperty("html").GetString());
        Assert.Equal("Hello", payload.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendAsync_5xxResponse_ThrowsRatherThanSilentlyDroppingTheEmail()
    {
        _fixture.HttpHandler.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = JsonContent.Create(new { message = "internal error" })
        };

        using var scope = _fixture.Services.CreateScope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => email.SendAsync("to@test.invalid", "Subject", "<p>Hello</p>", CancellationToken.None));

        Assert.Contains("500", exception.Message);
    }
}
