namespace unit;

// Every rule here duplicates a check Name.Create already enforces on the only Name the
// command can ever carry, so none of them are reachable through the public API — see
// CreateAccountValidatorTests for the same note. Only the valid case is meaningful.
public class ChangeNameValidatorTests
{
    private static readonly ChangeNameValidator Validator = new();

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new ChangeNameCommand(Name.Create("janedoe").Value!));

        Assert.True(result.IsValid);
    }
}
