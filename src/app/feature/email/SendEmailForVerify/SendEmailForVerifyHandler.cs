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

        // Deliberately not filtered on IsDeleted, unlike every other read in the codebase.
        // This is the OTP that RecoverAccount exchanges for a registration token, and a
        // soft-deleted account is precisely the one that needs to recover — adding the
        // filter here would close the recovery path entirely.
        bool checkEmail = await accountCollection
            .Find(x => x.Email == request.email)
            .AnyAsync(cancellationToken);

        // Every exit below succeeds, whatever happened. This endpoint used to answer 404 for
        // an unknown address, which told an unauthenticated caller whether an account exists
        // — the first step of both credential stuffing and a targeted OTP attack, and the
        // more damaging of the two disclosures because this is the password-reset path.
        // send-for-register still answers 409, because there the registration screen needs to
        // say "that address is taken" and the caller is about to learn the same fact from
        // POST /account regardless.
        //
        // A throttled address takes the same exit: answering differently when the OTP budget
        // is spent would put the oracle straight back, since only a real account ever
        // reaches the generator.
        //
        // Not closed here: the existing-account path pays a Redis write and a Resend round
        // trip, so the two exits are still distinguishable by latency. Closing that means
        // dispatching the mail off the request thread, which is a larger change than this
        // one and has not been made.
        if(!checkEmail)
            return Result.Ok();

        var otp = await _otpService.GenerateAsync(
            request.email.Value, 
            cancellationToken);

        if(otp is null)
            return Result.Ok();

        await _emailService.SendOtpRegistrationEmailAsync(
            request.email.Value, 
            otp, 
            cancellationToken);

        return Result.Ok();
    }
}
