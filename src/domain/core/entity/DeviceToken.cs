namespace domain;

public sealed class DeviceToken : BaseEntity
{
    public Guid AccountId { get; private set; }
    public string Token { get; private set; }
    public ApnsEnvironment Environment { get; private set; }
    private DeviceToken() {}
    private DeviceToken(
        Guid accountId,
        string token,
        ApnsEnvironment environment)
    {
        AccountId = accountId;
        Token = token;
        Environment = environment;
    }

    public static DomainResult<DeviceToken> Create(
        Guid accountId,
        string token,
        ApnsEnvironment environment)
    {
        if(accountId == Guid.Empty)
            return DomainResult<DeviceToken>.Failure("AccountId is required");

        if(string.IsNullOrWhiteSpace(token))
            return DomainResult<DeviceToken>.Failure("Token is required");

        DeviceToken deviceToken = new DeviceToken(
            accountId,
            token,
            environment);

        return DomainResult<DeviceToken>.Success(deviceToken);
    }
}
