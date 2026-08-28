namespace domain;

public sealed class Comment : BaseEntity
{
    private const int ContentMaxLength = 2000;
    public Guid AccountId { get; private set; }
    public Guid PostId { get; private set; }
    public string? Content { get; private set; }
    private List<Image> _images = new();
    public IReadOnlyCollection<Image> Images => _images.AsReadOnly();
    private List<Reaction> _reactions = new();
    public IReadOnlyCollection<Reaction> Reactions => _reactions.AsReadOnly();
    public Amount ReactionAmount { get; private set; } = Amount.Create(0).Value;
    private Comment() {}
    private Comment(
        Guid accountId,
        Guid postId,
        string? content)
    {
        AccountId = accountId;
        Content = content;
        PostId = postId;
    }

    public static DomainResult<Comment> Create(
        Guid accountId,
        Guid postId,
        string? content,
        List<string>? urls = null)
    {
        if(accountId == Guid.Empty)
            return DomainResult<Comment>.Failure("AccountId is required");

        if(postId == Guid.Empty)
            return DomainResult<Comment>.Failure("PostId is required");

        if(content?.Length > ContentMaxLength)
            return DomainResult<Comment>.Failure("Content maximum length is 2000 characters");

        Comment comment = new Comment(
            accountId, 
            postId, 
            content);

        if(urls is not null)
            comment._images.AddRange(urls.Select(url => new Image(url)));

        comment.RaiseEvent(new CommentedPostEvent(
            comment.AccountId,
            comment.Id,
            comment.PostId,
            comment.CreatedAt));

        return DomainResult<Comment>.Success(comment);
    }

    public DomainResult ChangeContent(string content)
    {
        if(content.Length > ContentMaxLength)
            return DomainResult.Failure("Content maximum length is 2000 characters");

        Content = content;

        return Update();
    }

    public DomainResult DeleteComment()
    {
        return Delete();
    }
}
