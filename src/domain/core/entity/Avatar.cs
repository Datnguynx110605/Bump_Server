namespace domain;

public sealed class Avatar
{
    public string Url { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    private Avatar() {}
    public Avatar(string url)
    {
        Url = url;
    }
}
