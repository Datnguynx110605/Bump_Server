using domain;

namespace app;

public interface IJwtService
{
    string GenerateAccessToken(Guid accountId, Role role);
}
