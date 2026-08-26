namespace app;

public interface IJwtService
{
    string GenerateAccessToken(Guid accountId);
}
