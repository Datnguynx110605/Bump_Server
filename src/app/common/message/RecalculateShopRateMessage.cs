namespace app;

public sealed record RecalculateShopRateMessage(
    Guid MessageId,
    Guid ShopId);
