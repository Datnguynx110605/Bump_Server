using infra;

namespace unit;

public class BCryptServiceTests
{
    private static readonly BCryptService Service = new();

    [Fact]
    public void HashPassword_ProducesA60CharacterHash()
    {
        var hash = Service.HashPassword("correct-password");

        Assert.Equal(60, hash.Length);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_Succeeds()
    {
        var hash = Service.HashPassword("correct-password");

        Assert.True(Service.VerifyPassword("correct-password", hash));
    }

    [Fact]
    public void VerifyPassword_WrongPassword_Fails()
    {
        var hash = Service.HashPassword("correct-password");

        Assert.False(Service.VerifyPassword("wrong-password", hash));
    }
}
