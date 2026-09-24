using System.Net;

namespace integration;

[Collection(IntegrationCollection.Name)]
public class HealthCheckTests
{
    private readonly IntegrationTestFixture _fixture;

    public HealthCheckTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Health_AllThreeBackingServicesUp_ReturnsHealthy()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync("/health");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body);
    }
}
