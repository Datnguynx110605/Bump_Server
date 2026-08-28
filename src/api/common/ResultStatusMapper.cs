namespace api;

internal static class ResultStatusMapper
{
    public static int Resolve(string message)
    {
        string value = message.ToLowerInvariant();

        if(value.Contains("not found"))
            return StatusCodes.Status404NotFound;

        if(value.Contains("already") || value.Contains("must be different"))
            return StatusCodes.Status409Conflict;

        if(value.Contains("unauthoriz") || value.Contains("unauthentic") || value.Contains("credentials") || value.Contains("revoked") || value.Contains("token"))
            return StatusCodes.Status401Unauthorized;

        return StatusCodes.Status400BadRequest;
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
