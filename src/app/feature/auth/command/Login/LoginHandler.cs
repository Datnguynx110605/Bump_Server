using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class LoginHandler
    : IRequestHandler<LoginCommand, Result<AuthResult>>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IBcryptService _bcryptService;
    private readonly IJwtService _jwtService;
    public LoginHandler(
        IMongoDbContext dbContext,
        IBcryptService bcryptService,
        IJwtService jwtService)
    {
        _dbContext = dbContext;
        _bcryptService = bcryptService;
        _jwtService = jwtService;
    }

    public async Task<Result<AuthResult>> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Email)
            .Include(x => x.Password)
            .Include(x => x.Role)
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Email == request.email && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail("Invalid credentials");

        if(!_bcryptService.VerifyPassword(
            request.password, 
            account.Password))
            return Result.Fail("Invalid credentials");

        var accessToken = _jwtService.GenerateAccessToken(
            account.Id, 
            account.Role);

        var refreshTokenResult = RefreshToken.Create(account.Id);
        if(!refreshTokenResult.IsSuccess)
            return Result.Fail(refreshTokenResult.Error);

        RefreshToken refreshToken = refreshTokenResult.Value;

        var refreshTokenCollection = _dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);

        await refreshTokenCollection.InsertOneAsync(
            refreshToken,
            cancellationToken : cancellationToken
        );

        return Result.Ok(new AuthResult(
            accessToken, 
            refreshToken.Token));
    }
}
