namespace unit;

// GetDashboardActivityQuery has no validator (project-rules.md: dashboard's two queries take
// no FluentValidation rules) — the windowHours bound is a plain guard clause, the first
// statement in Handle. It returns before _dbContext is ever touched, so the invalid path is
// provably container-free and can run here with a null IMongoDbContext, the same way an
// InlineValidator stands in for a real one in the ValidationBehavior test.
public class GetDashboardActivityHandlerTests
{
    private static readonly GetDashboardActivityHandler Handler = new(dbContext: null!);

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public async Task WindowHoursOutOfBounds_Fails(int windowHours)
    {
        var result = await Handler.Handle(new GetDashboardActivityQuery(windowHours), CancellationToken.None);

        Assert.True(result.IsFailed);
    }
}
