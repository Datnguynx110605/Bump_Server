using FluentResults;
using MediatR;

namespace app;

public sealed record GetSpecificAccountQuery(Guid accountId)
    : IRequest<Result<AccountSummaryDto>>;
