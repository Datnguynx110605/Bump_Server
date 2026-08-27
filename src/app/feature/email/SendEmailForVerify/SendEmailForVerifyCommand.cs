using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record SendEmailForVerifyCommand(Email email)
    : IRequest<Result>;
