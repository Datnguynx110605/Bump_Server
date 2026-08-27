using AutoMapper;
using domain;
using FluentResults;
using MediatR;

namespace app;

internal sealed class CreateAccountHandler
    : IRequestHandler<CreateAccountCommand, Result<AccountDto>>
{
    private readonly IRegistrationService _registrationService;
    private readonly IBcryptService _bcryptService;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public CreateAccountHandler(
        IRegistrationService registrationService,
        IBcryptService bcryptService,
        IRedisService redisService,
        IMongoDbContext dbContext,
        IR2Service r2Service,
        IMapper mapper)
    {
        _registrationService = registrationService;
        _bcryptService = bcryptService;
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
        _r2Service = r2Service;
    }

    public async Task<Result<AccountDto>> Handle(
        CreateAccountCommand request, 
        CancellationToken cancellationToken)
    {
        var rawEmail = await _registrationService.ExtractEmailAsync(
            request.registrationToken,
            cancellationToken
        );
        if(rawEmail is null)
            return Result.Fail("Unauthenticated");

        var emailResult = Email.Create(rawEmail);
        if(!emailResult.IsSuccess)
            return Result.Fail(emailResult.Error);

        Email email = emailResult.Value;

        string hashPassword = _bcryptService.HashPassword(request.password);

        string url = await _r2Service.UploadAsync(
            request.image,
            request.imageFileName,
            cancellationToken);

        var accountResult = Account.Create(
            email,
            request.name,
            hashPassword,
            request.bio,
            url);
        if(!accountResult.IsSuccess)
        {
            await _r2Service.DeleteAsync(url, cancellationToken);
            return Result.Fail(accountResult.Error);
        }

        Account account = accountResult.Value;

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        await accountCollection.InsertOneAsync(
            account,
            cancellationToken : cancellationToken
        );

        var dto = _mapper.Map<AccountDto>(account);

        string cacheKey = CacheKey.Account(account.Id);

        await _redisService.SetAsync(
            cacheKey, 
            dto, 
            CacheKey.DefaultAccountTTL, 
            cancellationToken);

        await _dbContext.DispatchEventAsync(
            account.Events,
            cancellationToken);

        return Result.Ok(dto);
    }
}
