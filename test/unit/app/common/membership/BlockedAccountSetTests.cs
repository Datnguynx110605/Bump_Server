namespace unit;

public class BlockedAccountSetTests
{
    [Fact]
    public void Signature_EmptyHidden_ReturnsCallerSignatureUnchanged()
    {
        var result = BlockedAccountSet.Signature("post:all", Array.Empty<Guid>());

        Assert.Equal("post:all", result);
    }

    [Fact]
    public void Signature_EmptyHidden_NullFilterSignature_StaysNull()
    {
        var result = BlockedAccountSet.Signature(null, Array.Empty<Guid>());

        Assert.Null(result);
    }

    [Fact]
    public void Signature_SameIdsDifferentOrder_ProduceSameSignature()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var signature1 = BlockedAccountSet.Signature("post:all", new[] { a, b, c });
        var signature2 = BlockedAccountSet.Signature("post:all", new[] { c, a, b });

        Assert.Equal(signature1, signature2);
    }
}
