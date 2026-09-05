using FluentResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace api;

public static class ResultExtensions
{
    public static ActionResult<T> ToActionResult<T>(this Result<T> result)
    {
        if(result.IsSuccess)
            return new OkObjectResult(result.Value);

        return ToProblemResult(result.Errors);
    }

    public static ActionResult ToActionResult(this Result result)
    {
        if(result.IsSuccess)
            return new NoContentResult();

        return ToProblemResult(result.Errors);
    }

    public static ActionResult ToAcceptedResult(this Result result)
    {
        if(result.IsSuccess)
            return new AcceptedResult();

        return ToProblemResult(result.Errors);
    }

    private static ObjectResult ToProblemResult(IReadOnlyList<IError> errors)
    {
        if(errors.Count > 1)
            return ToValidationProblemResult(errors);

        string message = errors[0].Message;
        int statusCode = ResultStatusMapper.Resolve(errors[0]);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ResultStatusMapper.Title(statusCode),
            Detail = message
        };

        return new ObjectResult(problem) { StatusCode = statusCode };
    }

    private static ObjectResult ToValidationProblemResult(IReadOnlyList<IError> errors)
    {
        var modelState = new ModelStateDictionary();
        foreach(var error in errors)
            modelState.AddModelError(string.Empty, error.Message);

        var problem = new ValidationProblemDetails(modelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = ResultStatusMapper.Title(StatusCodes.Status400BadRequest)
        };

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
    }
}
