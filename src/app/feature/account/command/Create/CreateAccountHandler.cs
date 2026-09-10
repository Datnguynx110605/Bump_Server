using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class CreateAccountHandler
    : IRequestHandler<CreateAccountCommand, Result<AccountDto>>
{
    private readonly IRegistrationService _registrationService;
    private readonly IBcryptService _bcryptService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public CreateAccountHandler(
        IRegistrationService registrationService,
        IBcryptService bcryptService,
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _registrationService = registrationService;
        _bcryptService = bcryptService;
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<AccountDto>> Handle(
        CreateAccountCommand request,
        CancellationToken cancellationToken)
    {
        var rawEmail = await _registrationService.ExtractEmailAsync(
            request.registrationToken,
            cancellationToken);
        if(rawEmail is null)
            return Result.Fail(AppError.Unauthorized("Unauthenticated"));

        var emailResult = Email.Create(rawEmail);
        if(!emailResult.IsSuccess)
            return Result.Fail(emailResult.Error);

        Email email = emailResult.Value;

        string hashPassword = _bcryptService.HashPassword(request.password);

        var accountResult = Account.Create(
            email,
            request.name,
            hashPassword,
            request.bio);
        if(!accountResult.IsSuccess)
            return Result.Fail(accountResult.Error);

        Account account = accountResult.Value;

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        try
        {
            await accountCollection.InsertOneAsync(
                account,
                cancellationToken : cancellationToken);
        }
        catch(MongoWriteException ex)
            when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // The unique partial index on Email is the only thing standing between a
            // retried registration and a second account. Without this the driver's
            // exception escapes to UseExceptionHandler and the caller reads a 500
            // instead of the same 409 send-for-register would have given them.
            return Result.Fail(AppError.Conflict("Email already exists"));
        }

        await _registrationService.ConsumeTokenAsync(
            request.registrationToken,
            cancellationToken);

        var dto = _mapper.Map<AccountDto>(account);
        var summaryDto = _mapper.Map<AccountSummaryDto>(account);

        string cacheKey = CacheKey.Account(account.Id);

        await _redisService.SetAsync(
            cacheKey,
            summaryDto,
            CacheKey.DefaultAccountTTL,
            cancellationToken);

        return Result.Ok(dto);
    }
}
