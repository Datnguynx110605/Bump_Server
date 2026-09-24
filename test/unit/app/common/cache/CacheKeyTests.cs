namespace unit;

public class CacheKeyTests
{
    [Fact]
    public void List_ComposesEntitySignatureCursorPageSizeVersion_InOrder()
    {
        var request = new CursorPageRequest("some-cursor", 25);

        var key = CacheKey.List("post", "sig", request, 3);

        Assert.Equal("list:v1:post:sig:some-cursor:25:v3", key);
    }

    [Fact]
    public void List_NullFilterSignatureAndCursor_FallBackToAllAndFirst()
    {
        var request = new CursorPageRequest(null, 20);

        var key = CacheKey.List("post", null, request, 1);

        Assert.Equal("list:v1:post:all:first:20:v1", key);
    }

    [Fact]
    public void KeywordSignature_HashesInput_CannotCollideWithBlockedAccountSetShape()
    {
        var evilKeyword = "foo:hidden:0123456789abcdef";

        var signature = CacheKey.KeywordSignature(evilKeyword);

        Assert.Matches("^keyword:[0-9a-f]{16}$", signature);
        Assert.DoesNotContain(":hidden:", signature);
    }

    [Fact]
    public void ListVersionTTL_ExceedsDefaultListTTL()
    {
        Assert.True(CacheKey.ListVersionTTL > CacheKey.DefaultListTTL);
    }
}
