using FluentResults;
using MediatR;

namespace app;

public sealed record RegisterDeviceCommand(string token, bool isSandbox)
    : IRequest<Result>;
