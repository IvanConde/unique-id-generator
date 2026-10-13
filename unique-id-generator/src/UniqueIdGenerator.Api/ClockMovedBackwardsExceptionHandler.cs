using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using UniqueIdGenerator.Core;

namespace UniqueIdGenerator.Api;

// The node recovers on its own once the clock catches up, so this is a transient 503, not a 500.
internal sealed class ClockMovedBackwardsExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ClockMovedBackwardsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ClockMovedBackwardsException clockException)
        {
            return false;
        }

        logger.LogWarning("Refused to generate IDs: the clock moved backwards by {BackwardsMilliseconds} ms.",
            clockException.BackwardsMilliseconds);

        // Retry-After is expressed in whole seconds, rounded up so the client never retries too early.
        var retryAfterSeconds = (clockException.BackwardsMilliseconds + 999) / 1000;

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "ID generation temporarily unavailable",
                Detail = "The server clock moved backwards. Retry after the indicated delay.",
            },
        });
    }
}
