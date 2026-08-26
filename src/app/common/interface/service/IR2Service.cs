namespace app;

public interface IR2Service
{
    Task<string> UploadAsync(Stream content, string fileName, CancellationToken cancellationToken);

    Task DeleteAsync(string url, CancellationToken cancellationToken);
}
