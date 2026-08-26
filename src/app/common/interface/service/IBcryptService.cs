namespace app;

public interface IBcryptService
{
    string HashPassword(string password);

    bool VerifyPassword(string password, string hash);
}
