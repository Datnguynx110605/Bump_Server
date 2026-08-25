namespace domain;

public sealed class Name
{
    public string Value { get; }
    private Name() {}
    private Name(string value)
    {
        Value = value;
    }

    public static DomainResult<Name> Create(string value)
    {
        if(string.IsNullOrWhiteSpace(value))
            return DomainResult<Name>.Failure("Name is required");

        if(value.Length > 200)
            return DomainResult<Name>.Failure("Name is invalid");

        if(!value.All(c => char.IsLower(c) || char.IsDigit(c)))
            return DomainResult<Name>.Failure("Name is invalid");

        Name name = new Name(value);

        return DomainResult<Name>.Success(name);
    }
}
