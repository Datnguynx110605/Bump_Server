namespace domain;

public sealed class SavedShop
{
    public Guid ShopId { get; private set; }
    public DateTime SavedAt { get; private set; } = DateTime.UtcNow;
    private SavedShop() {}
    public SavedShop(Guid shopId)
    {
        ShopId = shopId;
    }
}
