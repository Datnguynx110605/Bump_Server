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
    private List<Comment> _comments = new();
    public IReadOnlyCollection<Comment> Comments => _comments.AsReadOnly();
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

        if(string.IsNullOrWhiteSpace(name))
            return DomainResult<Post>.Failure("Shop name is required");

        if(string.IsNullOrWhiteSpace(address))
            return DomainResult<Post>.Failure("Shop address is required");

        var shop = new Shop(
            name, 
            address);

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

    public DomainResult ReactPost(
        Guid accountId,
        Name name,
        Avatar avatar)
    {
        if(AccountId == accountId)
            return DomainResult.Failure("Can not react your own post");

        if(_reactions.Any(x => x.AccountId == accountId))
            return DomainResult.Failure("Already reacted");

        _reactions.Add(new Reaction(
            accountId,
            name,
            avatar));

        RaiseEvent(new ReactedPostEvent(
            accountId,
            Id));

        return DomainResult.Success();
    }

    public DomainResult UnreactPost(Guid accountId)
    {
        if(AccountId == accountId)
            return DomainResult.Failure("Can not unreact your own post");

        var reaction = _reactions.FirstOrDefault(x => x.AccountId == accountId);
        if(reaction is null)
            return DomainResult.Failure("Not reacted yet");

        _reactions.Remove(reaction);

        return DomainResult.Success();
    }

    public DomainResult Comment(Comment comment)
    {
        if(_comments.Any(x => x.Id == comment.Id))
            return DomainResult.Failure("Already commented");

        _comments.Add(comment);

        RaiseEvent(new CommentedPostEvent(
            comment.AccountId,
            comment.Id,
            Id,
            comment.CreatedAt));

        return DomainResult.Success();
    }

    public DomainResult RemoveComment(Guid commentId)
    {
        var comment = _comments.FirstOrDefault(x => x.Id == commentId);
        if(comment is null)
            return DomainResult.Failure("Not commented yet");

        _comments.Remove(comment);

        return DomainResult.Success();
    }

    public DomainResult ChangeCaption(string caption)
    {
        Caption = caption;
        Update();
        
        return DomainResult.Success();
    }

    public DomainResult ChangeRate(Amount rate)
    {
        Rate = rate;
        Update();

        return DomainResult.Success();
    }

    public DomainResult DeletePost()
    {
        Delete();

        return DomainResult.Success();
    }
}
