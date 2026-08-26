namespace app;

public sealed record RefreshTokenDto(
    Guid Id,
    Guid AccountId,
    bool IsRevoked,
    DateTime ExpiryDate,
    DateTime CreatedAt);
