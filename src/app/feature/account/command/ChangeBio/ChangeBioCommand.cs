using FluentResults;
using MediatR;

namespace app;

public sealed record ChangeBioCommand(string? bio)
    : IRequest<Result>;
