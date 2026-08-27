using FluentResults;
using MediatR;

namespace app;

public sealed record GetMyAccountQuery 
    : IRequest<Result<AccountSummaryDto>>;
