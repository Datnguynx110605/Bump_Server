namespace domain;

public sealed class Email
{
    public string Value { get; }
    private Email() {}
    private Email(string value)
    {
        Value = value;
    }

    public static DomainResult<Email> Create(string value)
    {
        if(string.IsNullOrWhiteSpace(value))
            return DomainResult<Email>.Failure("Email is required");

        if(!value.Contains("@"))
            return DomainResult<Email>.Failure("Email is invalid");

        if(value.Length > 200)
            return DomainResult<Email>.Failure("Email is invalid");

        Email email = new Email(value);

        return DomainResult<Email>.Success(email);
    }
}
