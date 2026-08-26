namespace domain;

public sealed class Image
{
    public string Url { get; private set; }
    public DateTime UploadedAt { get; private set; } = DateTime.UtcNow;
    private Image() {}
    public Image(string url)
    {
        Url = url;
    }
}
