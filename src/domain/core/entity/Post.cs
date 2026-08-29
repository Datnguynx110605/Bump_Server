namespace domain;

public sealed class Post : BaseEntity
{
    public Guid AccountId { get; private set; }
    public Guid ShopId { get; private set; }
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
        Guid shopId,
        string? caption,
        Amount rate)
    {
        AccountId = accountId;
        ShopId = shopId;
        Caption = caption;
        Rate = rate;
    }

    public static DomainResult<Post> Create(
        Guid accountId,
        Guid shopId,
        string? caption,
        Amount rate,
        List<string>? urls = null)
    {
        if(accountId == Guid.Empty)
            return DomainResult<Post>.Failure("AccountId is required");

        if(shopId == Guid.Empty)
            return DomainResult<Post>.Failure("ShopId is required");

        Post post = new Post(
            accountId,
            shopId,
            caption,
            rate);

        if(urls?.Any() == true)
            post._images.AddRange(urls.Select(url => new Image(url)));

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
