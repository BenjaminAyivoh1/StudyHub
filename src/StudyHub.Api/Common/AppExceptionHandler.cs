using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace StudyHub.Api.Common;

public class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not AppException appException)
        {
            // Not one of ours: log it and let the default handler return a 500.
            logger.LogError(exception, "Unhandled exception");
            return false;
        }

        httpContext.Response.StatusCode = appException.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = appException.StatusCode,
                Title = appException.Message
            },
            cancellationToken);

        return true;
    }
}
