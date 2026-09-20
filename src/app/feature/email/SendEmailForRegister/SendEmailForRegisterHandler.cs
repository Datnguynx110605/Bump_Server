using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class SendEmailForRegisterHandler
    : IRequestHandler<SendEmailForRegisterCommand, Result>
{
    private readonly IEmailService _emailService;
    private readonly IMongoDbContext _dbContext;
    private readonly IOtpService _otpService;
    public SendEmailForRegisterHandler(
        IEmailService emailService,
        IMongoDbContext dbContext,
        IOtpService otpService)
    {
        _emailService = emailService;
        _dbContext = dbContext;
        _otpService = otpService;
    }
    
    public async Task<Result> Handle(
        SendEmailForRegisterCommand request, 
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        bool checkEmail = await accountCollection
            .Find(x => x.Email == request.email && !x.IsDeleted)
            .AnyAsync(cancellationToken);

        // Kept as a distinguishing answer, unlike send-for-verify. The registration screen
        // needs to tell the user the address is taken, and the caller learns the same fact
        // from POST /account anyway — so hiding it here costs real usability and closes
        // nothing. The disclosure that mattered was on the password-reset path.
        if(checkEmail)
            return Result.Fail(AppError.Conflict("Email already exists"));

        var otp = await _otpService.GenerateAsync(
            request.email.Value, 
            cancellationToken);

        // The address has spent its hourly budget, or is locked out after repeated wrong
        // guesses. Reported plainly here because this endpoint already discloses whether the
        // address is in use, so a distinct answer gives an attacker nothing further.
        if(otp is null)
            return Result.Fail(AppError.TooManyRequests(
                "Too many verification requests for this address. Please retry later."));

        await _emailService.SendOtpRegistrationEmailAsync(
            request.email.Value, 
            otp, 
            cancellationToken);

        return Result.Ok();
    }
}
