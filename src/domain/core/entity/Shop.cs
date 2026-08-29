namespace domain;

public sealed class Shop : BaseEntity
{
    public string Name { get; private set; }
    public string Address { get; private set; }
    public Amount Rate { get; private set; } = Amount.Create(0).Value;
    private Shop() {}
    private Shop(
        string name,
        string address)
    {
        Name = name;
        Address = address;
    }

    public static DomainResult<Shop> Create(string name, string address)
    {
        if(string.IsNullOrWhiteSpace(name))
            return DomainResult<Shop>.Failure("Shop name is required");

        if(string.IsNullOrWhiteSpace(address))
            return DomainResult<Shop>.Failure("Shop address is required");

        Shop shop = new Shop(name, address);

        return DomainResult<Shop>.Success(shop);
    }

    public DomainResult ChangeRate(Amount rate)
    {
        Rate = rate;

        return Update();
    }

    public DomainResult DeleteShop()
    {
        return Delete();
    }
}
