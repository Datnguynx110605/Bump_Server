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

    /// Validates a form-file collection and, only if it passes, opens the streams the
    /// handler will upload.
    ///
    /// Handing back the streams *from the check* is the point. While validating and
    /// opening were two separate steps, an action could take the second without the
    /// first — and `CreateComment` did exactly that, so every comment image reached R2
    /// with no size, extension or magic-byte check while `CreatePost` next door was
    /// guarded. There is now no way to reach the bytes that does not pass through here.
    public static Result<List<(Stream stream, string fileName)>?> ToStreams(IReadOnlyCollection<IFormFile>? files)
    {
        if(files is null || files.Count == 0)
            return Result.Ok<List<(Stream stream, string fileName)>?>(null);

        var validation = Validate(files);
        if(validation.IsFailed)
            return Result.Fail<List<(Stream stream, string fileName)>?>(validation.Errors);

        // `OpenReadStream` hands back a fresh reader over the buffered body on every call,
        // so the header bytes the signature check consumed above are not missing here.
        return Result.Ok<List<(Stream stream, string fileName)>?>(
            files.Select(x => (x.OpenReadStream(), x.FileName)).ToList());
    }

    /// Private so the collection path cannot be validated without also being opened, and
    /// cannot be opened without being validated. The single-file overload above stays
    /// public for `ChangeAvatar`, which takes one `IFormFile` rather than a list.
    private static Result Validate(IReadOnlyCollection<IFormFile> files)
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
