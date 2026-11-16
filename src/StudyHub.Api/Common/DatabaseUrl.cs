using Npgsql;

namespace StudyHub.Api.Common;

public static class DatabaseUrl
{
    /// <summary>
    /// Hosting providers usually hand out a database address as one URL, like
    /// postgres://user:password@host:5432/dbname. Npgsql wants the "Host=...;Username=..."
    /// form instead, so this converts it. Anything that isn't a URL is returned unchanged.
    /// </summary>
    public static string ToConnectionString(string value)
    {
        var isUrl = value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
        if (!isUrl)
        {
            return value;
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = SslMode.Require // hosted databases only accept encrypted connections
        };

        return builder.ConnectionString;
    }
}
