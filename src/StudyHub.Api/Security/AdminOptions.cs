namespace StudyHub.Api.Security;

/// <summary>
/// Who may moderate the site. Set Admin:Emails (Admin__Emails as an environment variable)
/// to one or more email addresses separated by commas.
/// </summary>
public class AdminOptions
{
    public const string Section = "Admin";
    public const string Policy = "Admin";

    public string Emails { get; set; } = string.Empty;

    public bool IsAdmin(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return Emails
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(adminEmail => string.Equals(adminEmail, email.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
