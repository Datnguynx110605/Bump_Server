namespace domain;

public sealed class Comment : BaseEntity
{
    public Guid AccountId { get; private set; }
    public string? Content { get; private set; }
    private List<Image> _images = new();
    public IReadOnlyCollection<Image> Images => _images.AsReadOnly();
    private List<Reaction> _reactions = new();
    public IReadOnlyCollection<Reaction> Reactions => _reactions.AsReadOnly();
    public Amount ReactionAmount { get; private set; } = Amount.Create(0).Value;
    private Comment() {}
    private Comment(
        Guid accountId,
        string? content)
    {
        AccountId = accountId;
        Content = content;
    }

    public static DomainResult<Comment> Create(
        Guid accountId,
        string? content,
        List<string>? urls = null)
    {
        if(accountId == Guid.Empty)
            return DomainResult<Comment>.Failure("AccountId is required");

        Comment comment = new Comment(accountId, content);

        if(urls is not null)
            comment._images.AddRange(urls.Select(url => new Image(url)));

        return DomainResult<Comment>.Success(comment);
    }

    public DomainResult ReactComment(
        Guid accountId,
        Name name,
        Avatar avatar)
    {
        if(AccountId == accountId)
            return DomainResult.Failure("Can not react your own comment");

        if(_reactions.Any(x => x.AccountId == accountId))
            return DomainResult.Failure("Already reacted");

        _reactions.Add(new Reaction(
            accountId,
            name,
            avatar));

        RaiseEvent(new ReactedCommentEvent(
            accountId,
            Id));

        return DomainResult.Success();
    }

    public DomainResult UnreactComment(Guid accountId)
    {
        if(AccountId == accountId)
            return DomainResult.Failure("Can not unreact your own comment");

        var reaction = _reactions.FirstOrDefault(x => x.AccountId == accountId);
        if(reaction is null)
            return DomainResult.Failure("Not reacted yet");

        _reactions.Remove(reaction);

        return DomainResult.Success();
    }

    public DomainResult ChangeContent(string content)
    {
        Content = content;
        Update();

        return DomainResult.Success();
    }

    public DomainResult DeleteComment()
    {
        Delete();

        return DomainResult.Success();
    }
}
