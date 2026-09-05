using FluentResults;
using FluentValidation;
using MediatR;

namespace app;

public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if(!_validators.Any())
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(x => x.ValidateAsync(context, cancellationToken)));

        var errors = validationResults
            .SelectMany(x => x.Errors)
            .Select(x => AppError.BadRequest(x.ErrorMessage))
            .ToList();

        if(errors.Count == 0)
            return await next(cancellationToken);

        var response = Activator.CreateInstance<TResponse>();
        ((ResultBase)(object)response!).Reasons.AddRange(errors);

        return response;
    }
}
