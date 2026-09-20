using MediatR;

namespace app;

// Detaches commands from the caller's cancellation token.
//
// A command is usually several writes that must all land: CreateComment inserts the row and
// then increments the post's CommentAmount, DeletePost flips IsDeleted and then publishes
// the cascade that removes the images. HttpContext.RequestAborted fires the moment a mobile
// client backgrounds or drops the connection, and it used to reach every one of those calls
// — so an abandoned request committed the first write and skipped the rest, leaving either a
// counter drifting toward negative or a soft-deleted post whose images are never removed and
// can never be found again once the retention sweep takes the row.
//
// Substituted here rather than reassigned inside each handler because the rule is about
// commands as a class, not about the nineteen that happen to have the shape today: a use
// case added next year inherits it without anyone having to remember. CreatePostHandler
// already argues the same point locally, passing CancellationToken.None to its upload
// cleanup so that it runs even when the request that triggered it is gone.
//
// Queries keep the token. They are read-only, nothing is half-applied by abandoning one, and
// cancelling a feed query whose reader has walked away is work saved.
//
// The cost accepted: an abandoned CreatePost finishes uploading its images and creates the
// post. That is bounded by the upload ceiling, and the alternative was leaving those same
// objects in R2 with no row referencing them.
public sealed class WriteCancellationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    // Commands and queries are indistinguishable by type — both are IRequest<Result<T>> — so
    // the name is what separates them. The convention is exhaustive today (42 *Command, 41
    // *Query, nothing else under feature/) and documented in project-rules.md; a use case
    // named outside it silently keeps the caller's token.
    private static readonly bool IsCommand =
        typeof(TRequest).Name.EndsWith("Command", StringComparison.Ordinal);

    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        return next(IsCommand ? CancellationToken.None : cancellationToken);
    }
}
