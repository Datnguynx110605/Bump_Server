using System.Text;
using Amazon.S3;
using Amazon.S3.Model;

namespace integration;

[Collection(IntegrationCollection.Name)]
public class R2ServiceTests
{
    private const string BucketName = "bump-test";

    private readonly IntegrationTestFixture _fixture;

    public R2ServiceTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static MemoryStream Content() => new(Encoding.UTF8.GetBytes("not a real image, R2 does not care"));

    [Fact]
    public async Task UploadAsync_ReturnsAUrlUnderThePublicUrlAndFolder_AndTheObjectExistsWithTheMappedContentType()
    {
        using var scope = _fixture.Services.CreateScope();
        var r2 = scope.ServiceProvider.GetRequiredService<IR2Service>();
        var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();

        string url = await r2.UploadAsync(Content(), "photo.png", CancellationToken.None);

        Assert.StartsWith("http://bump-test-public.invalid/posts/", url);
        Assert.EndsWith(".png", url);

        string key = new Uri(url).AbsolutePath.TrimStart('/');
        var metadata = await s3.GetObjectMetadataAsync(BucketName, key);

        Assert.Equal("image/png", metadata.Headers.ContentType);
    }

    [Fact]
    public async Task UploadAsync_ExtensionOutsideTheAllowlist_IsStoredAsOctetStream()
    {
        using var scope = _fixture.Services.CreateScope();
        var r2 = scope.ServiceProvider.GetRequiredService<IR2Service>();
        var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();

        string url = await r2.UploadAsync(Content(), "payload.exe", CancellationToken.None);

        string key = new Uri(url).AbsolutePath.TrimStart('/');
        var metadata = await s3.GetObjectMetadataAsync(BucketName, key);

        Assert.Equal("application/octet-stream", metadata.Headers.ContentType);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheObject()
    {
        using var scope = _fixture.Services.CreateScope();
        var r2 = scope.ServiceProvider.GetRequiredService<IR2Service>();
        var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
        string url = await r2.UploadAsync(Content(), "photo.jpg", CancellationToken.None);
        string key = new Uri(url).AbsolutePath.TrimStart('/');

        await r2.DeleteAsync(url, CancellationToken.None);

        await Assert.ThrowsAsync<AmazonS3Exception>(() => s3.GetObjectMetadataAsync(BucketName, key));
    }
}
