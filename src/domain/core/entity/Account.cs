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
    private List<Post> _savePosts = new();
    public IReadOnlyCollection<Post> SavePosts => _savePosts.AsReadOnly();
    private List<Shop> _saveShops = new();
    public IReadOnlyCollection<Shop> SaveShops => _saveShops.AsReadOnly();
    public Role Role { get; private set; } = Role.User;
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

        account.RaiseEvent(new AccountCreatedEvent(
            account.Id,
            account.Email,
            account.CreatedAt));

        return DomainResult<Account>.Success(account);
    }

    public DomainResult SavePost(Post post)
    {
        if(_savePosts.Any(x => x.Id == post.Id))
            return DomainResult.Failure("Post already saved");

        _savePosts.Add(post);

        return DomainResult.Success();
    }

    public DomainResult RemoveSavedPost(Guid postId)
    {
       var post = _savePosts.FirstOrDefault(x => x.Id == postId);
        if(post is null)
            return DomainResult.Failure("Post not found");

        _savePosts.Remove(post);

        return DomainResult.Success();
    }

    public DomainResult SaveShop(Shop shop)
    {
        if(_saveShops.Any(x => x.Id == shop.Id))
            return DomainResult.Failure("Shop already saved");

        _saveShops.Add(shop);

        return DomainResult.Success();
    }

    public DomainResult RemoveSavedShop(Guid shopId)
    {
        var shop = _saveShops.FirstOrDefault(x => x.Id == shopId);
        if(shop is null)
            return DomainResult.Failure("Shop not found");

        _saveShops.Remove(shop);

        return DomainResult.Success();
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

    public DomainResult ChangePassword(string password)
    {
        if(Password == password)
            return DomainResult.Failure("New password must be different");

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

    public DomainResult ChangeAvatar(string url)
    {
        if(string.IsNullOrWhiteSpace(url))
            return DomainResult.Failure("Url is required");

        if(_avatars.Any(x => x.Url == url))
            return DomainResult.Failure("New avatar must be different");

        _avatars.Add(new Avatar(url));

        return Update();
    }

    public DomainResult RemoveAvatar(string url)
    {
        var avatar = _avatars.FirstOrDefault(x => x.Url == url);
        if(avatar is null)
            return DomainResult.Failure("Avatar not found");

        _avatars.Remove(avatar);

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
