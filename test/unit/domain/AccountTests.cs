namespace unit;

public class AccountTests
{
    private static readonly string ValidHash = new('a', 60);
    private static readonly Email ValidEmail = Email.Create("user@example.com").Value!;
    private static readonly Name ValidName = Name.Create("johndoe").Value!;

    private static Account CreateValidAccount(string? bio = null)
    {
        return Account.Create(ValidEmail, ValidName, ValidHash, bio).Value!;
    }

    [Theory]
    [InlineData(59)]
    [InlineData(61)]
    public void Create_PasswordNotExactlySixtyChars_IsRejected(int length)
    {
        DomainResult<Account> result = Account.Create(ValidEmail, ValidName, new string('a', length), null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Hashing incorrect", result.Error);
    }

    [Fact]
    public void Create_BioOverTwoThousandChars_IsRejected()
    {
        DomainResult<Account> result = Account.Create(ValidEmail, ValidName, ValidHash, new string('a', 2001));

        Assert.False(result.IsSuccess);
        Assert.Equal("Bio maximum length is 2000 characters", result.Error);
    }

    [Fact]
    public void Create_Valid_Succeeds()
    {
        DomainResult<Account> result = Account.Create(ValidEmail, ValidName, ValidHash, "hello");

        Assert.True(result.IsSuccess);
        Assert.Same(ValidEmail, result.Value!.Email);
        Assert.Same(ValidName, result.Value.Name);
        Assert.Equal("hello", result.Value.Bio);
    }

    [Fact]
    public void ChangeEmail_IdenticalValue_Refuses()
    {
        Account account = CreateValidAccount();

        DomainResult result = account.ChangeEmail(ValidEmail);

        Assert.False(result.IsSuccess);
        Assert.Equal("New email must be different", result.Error);
    }

    [Fact]
    public void ChangeEmail_DifferentValue_Succeeds()
    {
        Account account = CreateValidAccount();
        Email newEmail = Email.Create("other@example.com").Value!;

        DomainResult result = account.ChangeEmail(newEmail);

        Assert.True(result.IsSuccess);
        Assert.Same(newEmail, account.Email);
    }

    [Fact]
    public void ChangeName_IdenticalValue_Refuses()
    {
        Account account = CreateValidAccount();

        DomainResult result = account.ChangeName(ValidName);

        Assert.False(result.IsSuccess);
        Assert.Equal("New name must be different", result.Error);
    }

    [Fact]
    public void ChangeName_DifferentValue_Succeeds()
    {
        Account account = CreateValidAccount();
        Name newName = Name.Create("janedoe").Value!;

        DomainResult result = account.ChangeName(newName);

        Assert.True(result.IsSuccess);
        Assert.Same(newName, account.Name);
    }

    [Fact]
    public void ChangePassword_WrongLength_Refuses()
    {
        Account account = CreateValidAccount();

        DomainResult result = account.ChangePassword(new string('b', 59));

        Assert.False(result.IsSuccess);
        Assert.Equal("Hashing incorrect", result.Error);
    }

    [Fact]
    public void ChangePassword_ExactLength_Succeeds()
    {
        Account account = CreateValidAccount();
        string newHash = new('b', 60);

        DomainResult result = account.ChangePassword(newHash);

        Assert.True(result.IsSuccess);
        Assert.Equal(newHash, account.Password);
    }

    [Fact]
    public void ChangeBio_TooLong_Refuses()
    {
        Account account = CreateValidAccount();

        DomainResult result = account.ChangeBio(new string('c', 2001));

        Assert.False(result.IsSuccess);
        Assert.Equal("Bio maximum length is 2000 characters", result.Error);
    }

    [Fact]
    public void ChangeBio_Null_Succeeds()
    {
        Account account = CreateValidAccount("something");

        DomainResult result = account.ChangeBio(null);

        Assert.True(result.IsSuccess);
        Assert.Null(account.Bio);
    }

    [Fact]
    public void PromoteToAdmin_AlreadyAdmin_Refuses()
    {
        Account account = CreateValidAccount();
        account.PromoteToAdmin();

        DomainResult result = account.PromoteToAdmin();

        Assert.False(result.IsSuccess);
        Assert.Equal("Account is already an admin", result.Error);
    }

    [Fact]
    public void PromoteToAdmin_FromUser_Succeeds()
    {
        Account account = CreateValidAccount();

        DomainResult result = account.PromoteToAdmin();

        Assert.True(result.IsSuccess);
        Assert.Equal(Role.Admin, account.Role);
    }

    [Fact]
    public void DeleteAccount_MarksDeleted()
    {
        Account account = CreateValidAccount();

        DomainResult result = account.DeleteAccount();

        Assert.True(result.IsSuccess);
        Assert.True(account.IsDeleted);
    }

    [Fact]
    public void RecoverAccount_OnDeletedAccount_ClearsDeleted()
    {
        Account account = CreateValidAccount();
        account.DeleteAccount();

        DomainResult result = account.RecoverAccount();

        Assert.True(result.IsSuccess);
        Assert.False(account.IsDeleted);
    }
}
