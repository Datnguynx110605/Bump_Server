using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record VerifyEmailCommand(
    Email email,
    string otp)
    : IRequest<Result<string>>;
