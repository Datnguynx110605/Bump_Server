namespace unit;

public class CommentTests
{
    [Fact]
    public void Create_EmptyAccountId_IsRejected()
    {
        DomainResult<Comment> result = Comment.Create(Guid.Empty, Guid.NewGuid(), "hi");

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountId is required", result.Error);
    }

    [Fact]
    public void Create_EmptyPostId_IsRejected()
    {
        DomainResult<Comment> result = Comment.Create(Guid.NewGuid(), Guid.Empty, "hi");

        Assert.False(result.IsSuccess);
        Assert.Equal("PostId is required", result.Error);
    }

    [Fact]
    public void Create_ContentOverTwoThousandChars_IsRejected()
    {
        DomainResult<Comment> result = Comment.Create(Guid.NewGuid(), Guid.NewGuid(), new string('a', 2001));

        Assert.False(result.IsSuccess);
        Assert.Equal("Content maximum length is 2000 characters", result.Error);
    }

    [Fact]
    public void Create_NullContent_Succeeds()
    {
        DomainResult<Comment> result = Comment.Create(Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Content);
    }

    [Fact]
    public void Create_NoUrls_HasNoImages()
    {
        DomainResult<Comment> result = Comment.Create(Guid.NewGuid(), Guid.NewGuid(), "hi");

        Assert.Empty(result.Value!.Images);
    }

    [Fact]
    public void Create_WithUrls_PopulatesImages()
    {
        var urls = new List<string> { "https://a" };

        DomainResult<Comment> result = Comment.Create(Guid.NewGuid(), Guid.NewGuid(), "hi", urls);

        Assert.Single(result.Value!.Images);
    }

    [Fact]
    public void ChangeContent_TooLong_Refuses()
    {
        Comment comment = Comment.Create(Guid.NewGuid(), Guid.NewGuid(), "hi").Value!;

        DomainResult result = comment.ChangeContent(new string('a', 2001));

        Assert.False(result.IsSuccess);
        Assert.Equal("Content maximum length is 2000 characters", result.Error);
    }

    [Fact]
    public void ChangeContent_Valid_Succeeds()
    {
        Comment comment = Comment.Create(Guid.NewGuid(), Guid.NewGuid(), "hi").Value!;

        DomainResult result = comment.ChangeContent("updated");

        Assert.True(result.IsSuccess);
        Assert.Equal("updated", comment.Content);
    }

    [Fact]
    public void DeleteComment_MarksDeleted()
    {
        Comment comment = Comment.Create(Guid.NewGuid(), Guid.NewGuid(), "hi").Value!;

        DomainResult result = comment.DeleteComment();

        Assert.True(result.IsSuccess);
        Assert.True(comment.IsDeleted);
    }
}
