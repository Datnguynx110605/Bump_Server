namespace integration;

[Collection(IntegrationCollection.Name)]
public class OtpServiceTests
{
    private readonly IntegrationTestFixture _fixture;

    public OtpServiceTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Identifier() => $"otp-test-{Guid.NewGuid():N}@test.invalid";

    [Fact]
    public async Task GenerateAsync_ReturnsASixDigitCode()
    {
        using var scope = _fixture.Services.CreateScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();

        string? code = await otp.GenerateAsync(Identifier(), CancellationToken.None);

        Assert.NotNull(code);
        Assert.Equal(6, code!.Length);
        Assert.True(int.TryParse(code, out _));
    }

    [Fact]
    public async Task GenerateAsync_SixthGenerationInsideAnHour_ReturnsNull()
    {
        using var scope = _fixture.Services.CreateScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();
        string identifier = Identifier();

        for(int i = 0; i < 5; i++)
            Assert.NotNull(await otp.GenerateAsync(identifier, CancellationToken.None));

        var sixth = await otp.GenerateAsync(identifier, CancellationToken.None);

        Assert.Null(sixth);
    }

    [Fact]
    public async Task VerifyAsync_CorrectCode_StillVerifiesASecondTimeWithinItsTTL()
    {
        using var scope = _fixture.Services.CreateScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();
        string identifier = Identifier();
        string code = (await otp.GenerateAsync(identifier, CancellationToken.None))!;

        var first = await otp.VerifyAsync(identifier, code, CancellationToken.None);
        var second = await otp.VerifyAsync(identifier, code, CancellationToken.None);

        Assert.True(first);
        Assert.True(second);
    }

    [Fact]
    public async Task VerifyAsync_ThirdWrongGuess_LocksOutTheEntry_EvenForTheCorrectCodeAfterwards()
    {
        using var scope = _fixture.Services.CreateScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();
        string identifier = Identifier();
        string code = (await otp.GenerateAsync(identifier, CancellationToken.None))!;
        string wrongCode = code == "000000" ? "111111" : "000000";

        await otp.VerifyAsync(identifier, wrongCode, CancellationToken.None);
        await otp.VerifyAsync(identifier, wrongCode, CancellationToken.None);
        await otp.VerifyAsync(identifier, wrongCode, CancellationToken.None);
        var withCorrectCodeAfterLockout = await otp.VerifyAsync(identifier, code, CancellationToken.None);

        Assert.False(withCorrectCodeAfterLockout);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredEntry_Fails()
    {
        using var scope = _fixture.Services.CreateScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();

        var result = await otp.VerifyAsync(Identifier(), "000000", CancellationToken.None);

        Assert.False(result);
    }
}
