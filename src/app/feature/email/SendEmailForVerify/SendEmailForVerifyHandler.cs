using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class SendEmailForVerifyHandler
    : IRequestHandler<SendEmailForVerifyCommand, Result>
{
    private readonly IEmailService _emailService;
    private readonly IMongoDbContext _dbContext;
    private readonly IOtpService _otpService;
    public SendEmailForVerifyHandler(
        IEmailService emailService,
        IMongoDbContext dbContext,
        IOtpService otpService)
    {
        _emailService = emailService;
        _dbContext = dbContext;
        _otpService = otpService;
    }
    
    public async Task<Result> Handle(
        SendEmailForVerifyCommand request, 
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        bool checkEmail = await accountCollection
            .Find(x => x.Email == request.email)
            .AnyAsync(cancellationToken);
        
        if(!checkEmail)
            return Result.Fail(AppError.NotFound("Email not found"));

        var otp = await _otpService.GenerateAsync(
            request.email.Value, 
            cancellationToken);
        await _emailService.SendOtpRegistrationEmailAsync(
            request.email.Value, 
            otp, 
            cancellationToken);

        return Result.Ok();
    }
}
