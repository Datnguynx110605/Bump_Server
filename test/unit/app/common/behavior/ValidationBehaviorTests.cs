using FluentResults;
using FluentValidation;
using MediatR;

namespace unit;

public class ValidationBehaviorTests
{
    private sealed record TestCommand(string Value) : IRequest<Result>;

    [Fact]
    public async Task NoValidatorsRegistered_InvokesNext()
    {
        var behavior = new ValidationBehavior<TestCommand, Result>(Array.Empty<IValidator<TestCommand>>());
        var nextCalled = false;
        Task<Result> Next(CancellationToken ct)
        {
            nextCalled = true;
            return Task.FromResult(Result.Ok());
        }

        var result = await behavior.Handle(new TestCommand(""), Next, CancellationToken.None);

        Assert.True(nextCalled);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ValidationFails_ReturnsBadRequestAndDoesNotInvokeNext()
    {
        var validator = new InlineValidator<TestCommand>();
        validator.RuleFor(x => x.Value).NotEmpty().WithMessage("Value is required");
        var behavior = new ValidationBehavior<TestCommand, Result>(new[] { validator });
        var nextCalled = false;
        Task<Result> Next(CancellationToken ct)
        {
            nextCalled = true;
            return Task.FromResult(Result.Ok());
        }

        var result = await behavior.Handle(new TestCommand(""), Next, CancellationToken.None);

        Assert.False(nextCalled);
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Value is required", error.Message);
        Assert.Equal(400, error.Metadata[AppError.StatusKey]);
    }
}
