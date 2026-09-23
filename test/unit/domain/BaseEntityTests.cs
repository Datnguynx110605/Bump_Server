namespace unit;

internal sealed class ProbeEntity : BaseEntity
{
}

public class BaseEntityTests
{
    [Fact]
    public void Update_OnLiveEntity_SucceedsAndStampsUpdatedAt()
    {
        var entity = new ProbeEntity();

        DomainResult result = entity.Update();

        Assert.True(result.IsSuccess);
        Assert.NotNull(entity.UpdatedAt);
    }

    [Fact]
    public void Update_OnDeletedEntity_Refuses()
    {
        var entity = new ProbeEntity();
        entity.Delete();

        DomainResult result = entity.Update();

        Assert.False(result.IsSuccess);
        Assert.Equal("Operation is invalid", result.Error);
    }

    [Fact]
    public void Delete_OnLiveEntity_SucceedsAndStampsDeletedAt()
    {
        var entity = new ProbeEntity();

        DomainResult result = entity.Delete();

        Assert.True(result.IsSuccess);
        Assert.True(entity.IsDeleted);
        Assert.NotNull(entity.DeletedAt);
    }

    [Fact]
    public void Delete_OnAlreadyDeletedEntity_Refuses()
    {
        var entity = new ProbeEntity();
        entity.Delete();

        DomainResult result = entity.Delete();

        Assert.False(result.IsSuccess);
        Assert.Equal("Operation is invalid", result.Error);
    }

    [Fact]
    public void Recover_OnDeletedEntity_SucceedsAndClearsDeletedAt()
    {
        var entity = new ProbeEntity();
        entity.Delete();

        DomainResult result = entity.Recover();

        Assert.True(result.IsSuccess);
        Assert.False(entity.IsDeleted);
        Assert.Null(entity.DeletedAt);
        Assert.NotNull(entity.RecoveredAt);
    }

    [Fact]
    public void Recover_OnLiveEntity_Refuses()
    {
        var entity = new ProbeEntity();

        DomainResult result = entity.Recover();

        Assert.False(result.IsSuccess);
        Assert.Equal("Operation is invalid", result.Error);
    }
}
