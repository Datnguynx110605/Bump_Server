namespace domain;

public sealed class Post : BaseEntity
{
    public Guid AccountId { get; private set; }
    public Shop Shop { get; private set; }
    private List<Image> _images = new();
    public IReadOnlyCollection<Image> Images => _images.AsReadOnly();
    public string? Caption { get; private set; }
    public Amount Rate { get; private set; }
    private List<Reaction> _reactions = new();
    public IReadOnlyCollection<Reaction> Reactions => _reactions.AsReadOnly();
    public Amount ReactionAmount { get; private set; } = Amount.Create(0).Value;
    public Amount CommentAmount { get; private set; } = Amount.Create(0).Value;
    private Post() {}
    private Post(
        Guid accountId,
        Shop shop,
        string? caption,
        Amount rate)
    {
        AccountId = accountId;
        Shop = shop;
        Caption = caption;
        Rate = rate;
    }

    public static DomainResult<Post> Create(
        Guid accountId,
        string name,
        string address,
        string? caption,
        Amount rate,
        List<string>? urls = null)
    {
        if(accountId == Guid.Empty)
            return DomainResult<Post>.Failure("AccountId is required");

        var shopResult = Shop.Create(name, address);
        if(!shopResult.IsSuccess)
            return DomainResult<Post>.Failure(shopResult.Error!);

        Shop shop = shopResult.Value;

        Post post = new Post(
            accountId,
            shop,
            caption,
            rate);

        if(urls?.Any() == true)
            post._images.AddRange(urls.Select(url => new Image(url)));

        post.RaiseEvent(new PostCreatedEvent(
            post.AccountId,
            post.Id,
            post.CreatedAt));

        return DomainResult<Post>.Success(post);
    }

    public DomainResult ChangeCaption(string caption)
    {
        Caption = caption;

        return Update();
    }

    public DomainResult ChangeRate(Amount rate)
    {
        Rate = rate;

        return Update();
    }

    public DomainResult DeletePost()
    {
        return Delete();
    }
}
