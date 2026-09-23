namespace integration;

[Collection(IntegrationCollection.Name)]
public class FixtureBootTest
{
    private readonly IntegrationTestFixture _fixture;

    public FixtureBootTest(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Host_Boots_And_ResolvesMediator()
    {
        using var scope = _fixture.Services.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<MediatR.IMediator>();

        Assert.NotNull(mediator);
    }
}
