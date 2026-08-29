namespace domain;

public sealed class SavedPost
{
    public Guid PostId { get; private set; }
    public DateTime SavedAt { get; private set; } = DateTime.UtcNow;
    private SavedPost() {}
    public SavedPost(Guid postId)
    {
        PostId = postId;
    }
}
