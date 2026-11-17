using System.Net;
using System.Security.Claims;
using StudyHub.Api.Common;

namespace StudyHub.Tests;

public class RateLimitTests
{
    private static ClaimsPrincipal LoggedIn(Guid id)
        => new(new ClaimsIdentity(new[] { new Claim("sub", id.ToString()) }, "test"));

    private static readonly ClaimsPrincipal Visitor = new(new ClaimsIdentity());

    [Fact]
    public void Logged_in_students_are_counted_by_account_not_by_address()
    {
        var id = Guid.NewGuid();

        // Two students on the same campus Wi-Fi get separate allowances.
        Assert.Equal($"user:{id}", RateLimits.ClientKey(LoggedIn(id), IPAddress.Parse("41.66.1.2")));
        Assert.NotEqual(
            RateLimits.ClientKey(LoggedIn(Guid.NewGuid()), IPAddress.Parse("41.66.1.2")),
            RateLimits.ClientKey(LoggedIn(Guid.NewGuid()), IPAddress.Parse("41.66.1.2")));
    }

    [Fact]
    public void Visitors_are_counted_by_address()
    {
        Assert.Equal("ip:41.66.1.2", RateLimits.ClientKey(Visitor, IPAddress.Parse("41.66.1.2")));
        Assert.Equal("ip:unknown", RateLimits.ClientKey(Visitor, null));
    }

    [Fact]
    public void The_same_address_written_as_IPv6_counts_as_one()
    {
        var v4 = IPAddress.Parse("41.66.1.2");

        Assert.Equal(RateLimits.AddressKey(v4), RateLimits.AddressKey(v4.MapToIPv6()));
    }

    [Fact]
    public void A_limit_allows_that_many_requests_a_minute_then_refuses()
    {
        var partition = RateLimits.PerMinute("ip:41.66.1.2", 3);
        using var limiter = partition.Factory(partition.PartitionKey);

        for (var i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire();
            Assert.True(lease.IsAcquired, $"request {i + 1} should be allowed");
        }

        using var refused = limiter.AttemptAcquire();
        Assert.False(refused.IsAcquired);
    }

    [Fact]
    public void The_default_limits_are_strict_for_logins_and_roomy_for_a_class()
    {
        var limits = new RateLimitOptions();

        Assert.True(limits.Enabled);
        Assert.True(limits.LoginPerIp < limits.GeneralPerIp);
        Assert.True(limits.WritesPerUser < limits.GeneralPerUser);
        // A class of 60 joining a live quiz from one Wi-Fi address must fit.
        Assert.True(limits.LiveConnectionsPerIp >= 120);
    }
}
