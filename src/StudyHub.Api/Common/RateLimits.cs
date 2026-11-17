using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace StudyHub.Api.Common;

/// <summary>
/// How many requests one person may send per minute. The numbers live in the "RateLimits" settings,
/// so they can be changed on the hosting provider without touching code (e.g. RateLimits__LoginPerIp=50).
/// </summary>
public class RateLimitOptions
{
    public const string Section = "RateLimits";

    /// <summary>Set to false to switch every limit off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Any API request by a logged-in student, per minute.</summary>
    public int GeneralPerUser { get; set; } = 300;

    /// <summary>
    /// Any API request by visitors who aren't logged in, per minute, per internet address.
    /// Generous because a whole class on campus Wi-Fi can share one address.
    /// </summary>
    public int GeneralPerIp { get; set; } = 1000;

    /// <summary>Log in and sign up attempts per minute, per internet address. Slows down password guessing.</summary>
    public int LoginPerIp { get; set; } = 60;

    /// <summary>New or edited notes, comments, reports, votes and quizzes per minute, per student.</summary>
    public int WritesPerUser { get; set; } = 30;

    /// <summary>New practice runs started per minute, per student (or per address for guests).</summary>
    public int PracticeStartsPerUser { get; set; } = 30;

    /// <summary>Live quiz connections opened per minute, per internet address. A class joining at once needs room.</summary>
    public int LiveConnectionsPerIp { get; set; } = 300;
}

public static class RateLimits
{
    // Names used with [EnableRateLimiting(...)] on the controllers.
    public const string Login = "login";
    public const string Writes = "writes";
    public const string PracticeStart = "practice-start";
    public const string Live = "live";

    public const string TooManyMessage = "Too many requests. Wait a minute and try again.";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Logged-in students are counted by account; visitors by internet address.</summary>
    public static string ClientKey(ClaimsPrincipal user, IPAddress? address)
        => user.GetUserIdOrNull() is Guid id ? $"user:{id}" : AddressKey(address);

    /// <summary>The same address can arrive written as IPv4 or as IPv6; count both as one.</summary>
    public static string AddressKey(IPAddress? address)
    {
        if (address is null)
        {
            return "ip:unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return $"ip:{address}";
    }

    /// <summary>Allows <paramref name="limit"/> requests per minute for each key, and refuses the rest until the minute is up.</summary>
    public static RateLimitPartition<string> PerMinute(string key, int limit)
        => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, limit),
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true
        });

    public static IServiceCollection AddStudyHubRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                var seconds = 60;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                }

                response.Headers.RetryAfter = seconds.ToString();
                await response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = TooManyMessage,
                    Detail = $"Try again in {seconds} seconds."
                }, ct);
            };

            if (!limits.Enabled)
            {
                // The policies must still exist, because the controllers name them.
                foreach (var name in new[] { Login, Writes, PracticeStart, Live })
                {
                    options.AddPolicy(name, _ => RateLimitPartition.GetNoLimiter("off"));
                }

                return;
            }

            // Every API request counts towards a general limit. The website's own files don't.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (!http.Request.Path.StartsWithSegments("/api"))
                {
                    return RateLimitPartition.GetNoLimiter("site-files");
                }

                var key = ClientKey(http.User, http.Connection.RemoteIpAddress);
                return PerMinute(key, key.StartsWith("user:") ? limits.GeneralPerUser : limits.GeneralPerIp);
            });

            options.AddPolicy(Login, http =>
                PerMinute(AddressKey(http.Connection.RemoteIpAddress), limits.LoginPerIp));

            options.AddPolicy(Writes, http =>
                PerMinute(ClientKey(http.User, http.Connection.RemoteIpAddress), limits.WritesPerUser));

            options.AddPolicy(PracticeStart, http =>
                PerMinute(ClientKey(http.User, http.Connection.RemoteIpAddress), limits.PracticeStartsPerUser));

            options.AddPolicy(Live, http =>
                PerMinute(AddressKey(http.Connection.RemoteIpAddress), limits.LiveConnectionsPerIp));
        });
    }
}
