namespace StudyHub.Api.Common;

/// <summary>
/// Errors we expect and want to turn into a clean HTTP response.
/// Services throw these; <see cref="AppExceptionHandler"/> maps them to status codes.
/// </summary>
public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class BadRequestException(string message) : AppException(message, StatusCodes.Status400BadRequest);

public class UnauthorizedException(string message) : AppException(message, StatusCodes.Status401Unauthorized);

public class ForbiddenException(string message) : AppException(message, StatusCodes.Status403Forbidden);

public class NotFoundException(string message) : AppException(message, StatusCodes.Status404NotFound);

public class ConflictException(string message) : AppException(message, StatusCodes.Status409Conflict);
