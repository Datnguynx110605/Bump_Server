namespace unit;

public class AppErrorTests
{
    [Fact]
    public void BadRequest_AttachesStatus()
    {
        var error = AppError.BadRequest("message");

        Assert.Equal(400, error.Metadata[AppError.StatusKey]);
    }

    [Fact]
    public void Unauthorized_AttachesStatus()
    {
        var error = AppError.Unauthorized("message");

        Assert.Equal(401, error.Metadata[AppError.StatusKey]);
    }

    [Fact]
    public void NotFound_AttachesStatus()
    {
        var error = AppError.NotFound("message");

        Assert.Equal(404, error.Metadata[AppError.StatusKey]);
    }

    [Fact]
    public void Conflict_AttachesStatus()
    {
        var error = AppError.Conflict("message");

        Assert.Equal(409, error.Metadata[AppError.StatusKey]);
    }

    [Fact]
    public void TooManyRequests_AttachesStatus()
    {
        var error = AppError.TooManyRequests("message");

        Assert.Equal(429, error.Metadata[AppError.StatusKey]);
    }

    [Fact]
    public void EachFactory_CarriesTheGivenMessage()
    {
        Assert.Equal("message", AppError.BadRequest("message").Message);
    }
}
