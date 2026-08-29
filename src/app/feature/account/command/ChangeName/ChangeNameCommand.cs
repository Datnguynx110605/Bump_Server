using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record ChangeNameCommand(Name name) 
    : IRequest<Result>;
