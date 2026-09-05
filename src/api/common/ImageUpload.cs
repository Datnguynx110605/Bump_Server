using FluentResults;

namespace api;

internal static class ImageUpload
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private const int MaxFiles = 10;
    private const int HeaderBytes = 12;

    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static Result Validate(IFormFile file)
    {
        if(file.Length == 0)
            return Result.Fail("File is empty");

        if(file.Length > MaxBytes)
            return Result.Fail($"File is too large, maximum size is {MaxBytes / (1024 * 1024)} MB");

        string extension = Path.GetExtension(file.FileName);

        if(!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return Result.Fail($"File type is not supported, allowed types are {string.Join(", ", AllowedExtensions)}");

        // The extension and the client-supplied ContentType are both attacker-controlled,
        // so the actual bytes decide. Without this, HTML named .jpg reaches the bucket.
        if(!HasImageSignature(file))
            return Result.Fail("File content does not match a supported image format");

        return Result.Ok();
    }

    public static Result Validate(IReadOnlyCollection<IFormFile> files)
    {
        if(files.Count > MaxFiles)
            return Result.Fail($"Too many files, maximum is {MaxFiles}");

        foreach(var file in files)
        {
            var result = Validate(file);
            if(result.IsFailed)
                return result;
        }

        return Result.Ok();
    }

    private static bool HasImageSignature(IFormFile file)
    {
        Span<byte> header = stackalloc byte[HeaderBytes];

        using var stream = file.OpenReadStream();
        int read = stream.ReadAtLeast(header, HeaderBytes, throwOnEndOfStream: false);

        if(read < HeaderBytes)
            return false;

        return IsJpeg(header) || IsPng(header) || IsWebp(header);
    }

    // FF D8 FF
    private static bool IsJpeg(ReadOnlySpan<byte> header) =>
        header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;

    // 89 P N G CR LF 1A LF
    private static bool IsPng(ReadOnlySpan<byte> header) =>
        header[..8].SequenceEqual(PngSignature);

    // "RIFF" .... "WEBP"
    private static bool IsWebp(ReadOnlySpan<byte> header) =>
        header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8);
}
