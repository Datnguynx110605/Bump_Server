using Amazon.S3;
using Amazon.S3.Model;
using app;
using Microsoft.Extensions.Configuration;

namespace infra;

public sealed class R2Service : IR2Service
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _folderName;
    private readonly string _publicUrl;

    public R2Service(
        IAmazonS3 s3Client, 
        IConfiguration configuration)
    {
        _s3Client = s3Client;
        _bucketName = configuration["R2:BucketName"]!;
        _folderName = configuration["R2:FolderName"]!;
        _publicUrl = configuration["R2:PublicUrl"]!;
    }

    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName);
        var key = $"{_folderName}/{Guid.NewGuid()}{extension}";

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = content,
            ContentType = ResolveContentType(extension),
            UseChunkEncoding = false
        }, cancellationToken);

        return $"{_publicUrl}/{key}";
    }

    // Anything outside the allowlist is stored as a download rather than as a renderable
    // type, so an object that slips past the API-layer check still cannot execute in a
    // browser served from the public bucket domain.
    private static string ResolveContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    public async Task DeleteAsync(
        string url, 
        CancellationToken cancellationToken)
    {
        var key = new Uri(url).AbsolutePath.TrimStart('/');

        await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        }, cancellationToken);
    }
}
