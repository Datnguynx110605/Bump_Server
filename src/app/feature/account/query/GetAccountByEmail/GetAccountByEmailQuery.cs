using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record GetAccountByEmailQuery(Email email)
    : IRequest<Result<AccountSummaryDto>>;
