namespace domain;

public sealed class Account : BaseEntity
{
    private const int PasswordHashLength = 60;
    private const int BioMaxLength = 2000;
    public Email Email { get; private set; }
    public Name Name { get; private set; }
    public string Password { get; private set; }
    private List<Avatar> _avatars = new();
    public IReadOnlyCollection<Avatar> Avatars => _avatars.AsReadOnly();
    public string? Bio { get; private set; }
    private List<Follower> _followers = new();
    public IReadOnlyCollection<Follower> Followers => _followers.AsReadOnly();
    public Amount FollowerAmount { get; private set; } = Amount.Create(0).Value;
    private List<Following> _followings = new();
    public IReadOnlyCollection<Following> Followings => _followings.AsReadOnly();
    public Amount FollowingAmount { get; private set; } =  Amount.Create(0).Value;
    private List<Block> _blocks = new();
    public IReadOnlyCollection<Block> Blocks => _blocks.AsReadOnly();
    // Ids only, never displayed: this side exists so a blocked account can be filtered out
    // of the blocked-by account's reads without a collection-wide scan for "who blocked me".
    private List<Guid> _blockedBy = new();
    public IReadOnlyCollection<Guid> BlockedBy => _blockedBy.AsReadOnly();
    private List<SavedPost> _savePosts = new();
    public IReadOnlyCollection<SavedPost> SavePosts => _savePosts.AsReadOnly();
    private List<SavedShop> _saveShops = new();
    public IReadOnlyCollection<SavedShop> SaveShops => _saveShops.AsReadOnly();
    public Role Role { get; private set; } = Role.User;
    // Set directly via Mongo update in FollowService once follower/age/report thresholds
    // are met, same as FollowerAmount — not routed through a domain method since nothing
    // else in this aggregate needs to grant it yet.
    public bool IsTrusted { get; private set; } = false;
    private Account() {}
    private Account(
        Email email, 
        Name name, 
        string password, 
        string? bio)
    {
        Email = email;
        Name = name;
        Password = password;
        Bio = bio;
    }

    public static DomainResult<Account> Create(
        Email email,
        Name name,
        string password,
        string? bio)
    {
        if(password.Length != PasswordHashLength)
            return DomainResult<Account>.Failure("Hashing incorrect");

        if(bio?.Length > BioMaxLength)
            return DomainResult<Account>.Failure("Bio maximum length is 2000 characters");

        Account account = new Account(
            email,
            name,
            password,
            bio);

        return DomainResult<Account>.Success(account);
    }

    public DomainResult ChangeEmail(Email email)
    {
        if(Email.Value == email.Value)
            return DomainResult.Failure("New email must be different");

        Email = email;

        return Update();
    }

    public DomainResult ChangeName(Name name)
    {
        if(Name.Value == name.Value)
            return DomainResult.Failure("New name must be different");

        Name = name;

        return Update();
    }

    // Takes a hash, so it cannot check the new password differs from the old: BCrypt salts
    // randomly and two hashes of one plaintext never compare equal. That comparison used to
    // live here and was dead code. ChangePasswordHandler enforces the rule through
    // VerifyPassword, which is the only place the plaintext exists.
    public DomainResult ChangePassword(string password)
    {
        if(password.Length != PasswordHashLength)
            return DomainResult.Failure("Hashing incorrect");

        Password = password;

        return Update();
    }

    public DomainResult ChangeBio(string? bio)
    {
        if(bio?.Length > BioMaxLength)
            return DomainResult.Failure("Bio maximum length is 2000 characters");

        Bio = bio;

        return Update();
    }

    public DomainResult PromoteToAdmin()
    {
        if(Role == Role.Admin)
            return DomainResult.Failure("Account is already an admin");

        Role = Role.Admin;

        return Update();
    }

    public DomainResult DeleteAccount()
    {
        return Delete();
    }

    public DomainResult RecoverAccount()
    {
        return Recover();
    }
}
