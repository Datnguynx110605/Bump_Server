namespace domain;

public sealed class Account : BaseEntity
{
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
    private List<Post> _saveShops = new();
    public IReadOnlyCollection<Post> SaveShops => _saveShops.AsReadOnly();
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
        string? bio,
        string? url = null)
    {
        if(password.Length != 60)
            return DomainResult<Account>.Failure("Hashing incorrect");

        if(bio?.Length > 2000)
            return DomainResult<Account>.Failure("Bio maximum length is 2000 characters");

        Account account = new Account(
            email,
            name,
            password,
            bio);

        if(url is not null)
            account._avatars.Add(new Avatar(url));

        account.RaiseEvent(new AccountCreatedEvent(
            account.Id,
            account.Email,
            account.CreatedAt));

        return DomainResult<Account>.Success(account);
    }

    public DomainResult Follow(
        Guid accountId, 
        Name name, 
        Avatar avatar)
    {
        if(accountId == Guid.Empty)
            return DomainResult.Failure("AccountId is required");

        if(Id == accountId)
            return DomainResult.Failure("Account can't follow itself");

        if(_followings.Any(x => x.AccountId == accountId))
            return DomainResult.Failure("Already followed");

        _followings.Add(new Following(
            accountId, 
            name, 
            avatar));

        RaiseEvent(new FollowedAccountEvent(
            Id,
            accountId));

        return DomainResult.Success();
    }

    public DomainResult Unfollow(Guid accountId)
    {
        if(accountId == Guid.Empty)
            return DomainResult.Failure("AccountId is required");

        if(Id == accountId)
            return DomainResult.Failure("Account can't unfollow itself");

        var following = _followings.FirstOrDefault(x => x.AccountId == accountId);
        if(following is null)
            return DomainResult.Failure("Following account not found");

        _followings.Remove(following);

        return DomainResult.Success();
    }

    public DomainResult GetFollowed(
        Guid accountId, 
        Name name, 
        Avatar avatar)
    {
        if(accountId == Guid.Empty)
            return DomainResult.Failure("AccountId is required");

        if(Id == accountId)
            return DomainResult.Failure("Account can't follow itself");

        if(_followers.Any(x => x.AccountId == accountId))
            return DomainResult.Failure("Already followed");

        _followers.Add(new Follower(
            accountId,
            name,
            avatar));

        RaiseEvent(new GetFollowedEvent(
            accountId,
            Id));

        return DomainResult.Success();
    }

    public DomainResult GetUnfollowed(Guid accountId)
    {
        if(accountId == Guid.Empty)
            return DomainResult.Failure("AccountId is required");

        if(Id == accountId)
            return DomainResult.Failure("Account can't unfollow itself");

        var follower = _followers.FirstOrDefault(x => x.AccountId == accountId);
        if(follower is null)
            return DomainResult.Failure("Already unfollowed");

        _followers.Remove(follower);

        return DomainResult.Success();
    }

    public DomainResult SavePost(Guid postId)
    {
        var post = _savePosts.FirstOrDefault(x => x.Id == postId);
        if(post is not null)
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

    public DomainResult SaveShop(Guid shopId)
    {
        var shop = _saveShops.FirstOrDefault(x => x.Id == shopId);
        if(shop is not null)
            return DomainResult.Failure("Shop already saved");

        _saveShops.Add(shop);

        return DomainResult.Success();
    }

    public DomainResult RemoveSavedShop(Guid shopId)
    {
        var shop = _saveShops.FirstOrDefault(x => x.Id == shopId);
        if(shop is null)
            return DomainResult.Failure("Shop already saved");

        _saveShops.Remove(shop);

        return DomainResult.Success();
    }

    public DomainResult ChangeEmail(Email email)
    {
        if(Email == email)
            return DomainResult.Failure("New email must be different");

        Email = email;
        Update();

        return DomainResult.Success();
    }

    public DomainResult ChangeName(Name name)
    {
        if(Name == name)
            return DomainResult.Failure("New name must be different");

        Name = name;
        Update();

        return DomainResult.Success();
    }

    public DomainResult ChangePassword(string password)
    {
        if(Password == password)
            return DomainResult.Failure("New password must be different");

        if(password.Length != 60)
            return DomainResult<Account>.Failure("Hashing incorrect");

        Password = password;
        Update();

        return DomainResult.Success();
    }

    public DomainResult ChangeBio(string? bio)
    {
        if(bio?.Length > 2000)
            return DomainResult<Account>.Failure("Bio maximum length is 2000 characters");

        Bio = bio;
        Update();

        return DomainResult.Success();
    }

    public DomainResult ChangeAvatar(string url)
    {
        if(string.IsNullOrWhiteSpace(url))
            return DomainResult.Failure("Url is required");

        if(_avatars.Any(x => x.Url == url))
            return DomainResult.Failure("New avatar must be different");

        _avatars.Add(new Avatar(url));
        Update();

        return DomainResult.Success();
    }

    public DomainResult RemoveAvatar(string url)
    {
        var avatar = _avatars.FirstOrDefault(x => x.Url == url);
        if(avatar is null)
            return DomainResult.Failure("Avatar not found");

        _avatars.Remove(avatar);
        Update();

        return DomainResult.Success();
    }

    public DomainResult DeleteAccount()
    {
        Delete();

        return DomainResult.Success();
    }

    public DomainResult RecoverAccount()
    {
        Recover();

        return DomainResult.Success();
    }
}
