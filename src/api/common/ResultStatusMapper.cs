using app;
using FluentResults;

namespace api;

internal static class ResultStatusMapper
{
    public static int Resolve(IError error)
    {
        return error.Metadata.TryGetValue(AppError.StatusKey, out var status) && status is int statusCode
            ? statusCode
            : StatusCodes.Status400BadRequest;
    }

    public static string Title(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status404NotFound => "Not Found",
            StatusCodes.Status409Conflict => "Conflict",
            _ => "Bad Request"
        };
    }
}
