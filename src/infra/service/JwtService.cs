using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using app;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace infra;

public class JwtService : IJwtService
{
    public const string AccountIdClaimType = "accountId";

    private readonly IConfiguration _configuration;

    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateAccessToken(Guid accountId)
    {
        var claims = new[]
        {
            new Claim(AccountIdClaimType, accountId.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:SecretKey"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryMinutes = _configuration.GetValue<int>("Jwt:ExpiryMinutes");

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
