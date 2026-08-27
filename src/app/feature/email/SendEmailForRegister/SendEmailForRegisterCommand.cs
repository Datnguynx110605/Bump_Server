using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record SendEmailForRegisterCommand(Email email) 
    : IRequest<Result>;
