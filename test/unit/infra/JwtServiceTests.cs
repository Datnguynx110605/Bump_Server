using System.IdentityModel.Tokens.Jwt;
using infra;
using Microsoft.Extensions.Configuration;

namespace unit;

public class JwtServiceTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "unit-test-signing-key-not-for-production-use",
            ["Jwt:Issuer"] = "bump-unit-tests",
            ["Jwt:Audience"] = "bump-unit-tests",
            ["Jwt:ExpiryMinutes"] = "15"
        })
        .Build();

    [Fact]
    public void GenerateAccessToken_CarriesAccountIdAndRoleClaims_AndConfiguredIssuerAudienceAndExpiry()
    {
        var service = new JwtService(Configuration);
        var accountId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var token = service.GenerateAccessToken(accountId, Role.User);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(accountId.ToString(), jwt.Claims.Single(c => c.Type == JwtService.AccountIdClaimType).Value);
        Assert.Equal(Role.User.ToString(), jwt.Claims.Single(c => c.Type == JwtService.RoleClaimType).Value);
        Assert.Equal("bump-unit-tests", jwt.Issuer);
        Assert.Equal("bump-unit-tests", jwt.Audiences.Single());
        Assert.True(jwt.ValidTo > before.AddMinutes(15).AddSeconds(-5));
        Assert.True(jwt.ValidTo <= before.AddMinutes(15).AddSeconds(5));
    }
}
