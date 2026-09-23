namespace unit;

public class PostTests
{
    private static readonly Amount ValidRate = Amount.Create(3).Value!;

    [Fact]
    public void Create_EmptyAccountId_IsRejected()
    {
        DomainResult<Post> result = Post.Create(Guid.Empty, Guid.NewGuid(), null, ValidRate);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountId is required", result.Error);
    }

    [Fact]
    public void Create_EmptyShopId_IsRejected()
    {
        DomainResult<Post> result = Post.Create(Guid.NewGuid(), Guid.Empty, null, ValidRate);

        Assert.False(result.IsSuccess);
        Assert.Equal("ShopId is required", result.Error);
    }

    [Fact]
    public void Create_NullCaption_LeavesCaptionNormalizedNull()
    {
        DomainResult<Post> result = Post.Create(Guid.NewGuid(), Guid.NewGuid(), null, ValidRate);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Caption);
        Assert.Null(result.Value.CaptionNormalized);
    }

    [Fact]
    public void Create_WithCaption_DerivesCaptionNormalized()
    {
        DomainResult<Post> result = Post.Create(Guid.NewGuid(), Guid.NewGuid(), "Phở ngon", ValidRate);

        Assert.True(result.IsSuccess);
        Assert.Equal("Phở ngon", result.Value!.Caption);
        Assert.Equal("pho ngon", result.Value.CaptionNormalized);
    }

    [Fact]
    public void Create_NoUrls_HasNoImages()
    {
        DomainResult<Post> result = Post.Create(Guid.NewGuid(), Guid.NewGuid(), null, ValidRate);

        Assert.Empty(result.Value!.Images);
    }

    [Fact]
    public void Create_EmptyUrlList_HasNoImages()
    {
        DomainResult<Post> result = Post.Create(Guid.NewGuid(), Guid.NewGuid(), null, ValidRate, new List<string>());

        Assert.Empty(result.Value!.Images);
    }

    [Fact]
    public void Create_WithUrls_PopulatesImages()
    {
        var urls = new List<string> { "https://a", "https://b" };

        DomainResult<Post> result = Post.Create(Guid.NewGuid(), Guid.NewGuid(), null, ValidRate, urls);

        Assert.Equal(2, result.Value!.Images.Count);
    }

    [Fact]
    public void ChangeCaption_RederivesCaptionNormalized()
    {
        Post post = Post.Create(Guid.NewGuid(), Guid.NewGuid(), null, ValidRate).Value!;

        DomainResult result = post.ChangeCaption("Đà Nẵng");

        Assert.True(result.IsSuccess);
        Assert.Equal("Đà Nẵng", post.Caption);
        Assert.Equal("da nang", post.CaptionNormalized);
    }

    [Fact]
    public void ChangeRate_SetsRate()
    {
        Post post = Post.Create(Guid.NewGuid(), Guid.NewGuid(), null, ValidRate).Value!;
        Amount newRate = Amount.Create(5).Value!;

        DomainResult result = post.ChangeRate(newRate);

        Assert.True(result.IsSuccess);
        Assert.Same(newRate, post.Rate);
    }

    [Fact]
    public void DeletePost_MarksDeleted()
    {
        Post post = Post.Create(Guid.NewGuid(), Guid.NewGuid(), null, ValidRate).Value!;

        DomainResult result = post.DeletePost();

        Assert.True(result.IsSuccess);
        Assert.True(post.IsDeleted);
    }
}
