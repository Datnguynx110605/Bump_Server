namespace domain;

public sealed class Amount
{
    public int Value { get; }
    private Amount() {}
    private Amount(int value)
    {
        Value = value;
    }

    public static DomainResult<Amount> Create(int value)
    {
        if(value <= 0)
            return DomainResult<Amount>.Failure("Amount must be positive interger");

        Amount amount = new Amount(value);

        return DomainResult<Amount>.Success(amount);
    }
}
