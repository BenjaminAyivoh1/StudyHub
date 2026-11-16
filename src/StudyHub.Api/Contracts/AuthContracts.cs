using System.ComponentModel.DataAnnotations;

namespace StudyHub.Api.Contracts;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 2)] string DisplayName,
    [Required, StringLength(100, MinimumLength = 8)] string Password);

public record LoginRequest(
    [Required] string Email,
    [Required] string Password);

public record AuthResponse(string Token, Guid UserId, string DisplayName);
