using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Providers.Xtream.Dtos;

namespace LTR.Providers.Xtream;

/// <summary>
/// Covers the interpretation of a panel's <c>user_info</c> block, which decides whether the user is
/// told their subscription works.
/// </summary>
public sealed class XtreamContentProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MapAccount_WithoutUserInfo_ReportsAuthenticationFailure()
    {
        // Arrange & Act
        var account = XtreamContentProvider.MapAccount(userInfo: null, Now);

        // Assert
        account.Status.ShouldBe(AccountStatus.AuthenticationFailed);
        account.IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void MapAccount_WhenAuthIsZero_ReportsAuthenticationFailure()
    {
        // Arrange: panels signal rejected credentials with auth = 0 while still returning a body.
        var userInfo = new XtreamUserInfoDto { Auth = 0, Status = "Active" };

        // Act
        var account = XtreamContentProvider.MapAccount(userInfo, Now);

        // Assert
        account.Status.ShouldBe(AccountStatus.AuthenticationFailed);
    }

    [Fact]
    public void MapAccount_WithAnActiveStatusAndFutureExpiry_IsUsable()
    {
        // Arrange
        var userInfo = new XtreamUserInfoDto
        {
            Auth = 1,
            Status = "Active",
            ExpiryUnixSeconds = Now.AddDays(30).ToUnixTimeSeconds(),
            MaxConnections = 2,
            ActiveConnections = 0,
        };

        // Act
        var account = XtreamContentProvider.MapAccount(userInfo, Now);

        // Assert
        account.Status.ShouldBe(AccountStatus.Active);
        account.IsUsable.ShouldBeTrue();
        account.ExpiresAtUtc.ShouldBe(Now.AddDays(30));
    }

    [Fact]
    public void MapAccount_WhenTheStatusSaysActiveButTheExpiryHasPassed_ReportsExpired()
    {
        // Arrange: panels keep reporting Active past the expiry date. Presenting a working account
        // that then fails on every stream is worse than saying the subscription has run out.
        var userInfo = new XtreamUserInfoDto
        {
            Auth = 1,
            Status = "Active",
            ExpiryUnixSeconds = Now.AddDays(-1).ToUnixTimeSeconds(),
        };

        // Act
        var account = XtreamContentProvider.MapAccount(userInfo, Now);

        // Assert
        account.Status.ShouldBe(AccountStatus.Expired);
        account.IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void MapAccount_WithoutAnExpiry_TreatsTheAccountAsUnlimited()
    {
        // Arrange
        var userInfo = new XtreamUserInfoDto { Auth = 1, Status = "Active", ExpiryUnixSeconds = null };

        // Act
        var account = XtreamContentProvider.MapAccount(userInfo, Now);

        // Assert
        account.ExpiresAtUtc.ShouldBeNull();
        account.Status.ShouldBe(AccountStatus.Active);
    }

    [Theory]
    [InlineData("Active", AccountStatus.Active)]
    [InlineData("active", AccountStatus.Active)]
    [InlineData("  ACTIVE  ", AccountStatus.Active)]
    [InlineData("Expired", AccountStatus.Expired)]
    [InlineData("Banned", AccountStatus.Banned)]
    [InlineData("Disabled", AccountStatus.Banned)]
    [InlineData("something else", AccountStatus.Unknown)]
    [InlineData("", AccountStatus.Unknown)]
    [InlineData(null, AccountStatus.Unknown)]
    public void MapAccount_MapsTheReportedStatusCaseInsensitively(string? status, AccountStatus expected)
    {
        // Arrange
        var userInfo = new XtreamUserInfoDto { Auth = 1, Status = status };

        // Act
        var account = XtreamContentProvider.MapAccount(userInfo, Now);

        // Assert
        account.Status.ShouldBe(expected);
    }

    [Fact]
    public void MapAccount_KeepsOnlyFormatsThePlayerUnderstands()
    {
        // Arrange: rtmp appears routinely and is not playable here; duplicates also occur.
        var userInfo = new XtreamUserInfoDto
        {
            Auth = 1,
            Status = "Active",
            AllowedOutputFormats = ["m3u8", "ts", "rtmp", "ts"],
        };

        // Act
        var account = XtreamContentProvider.MapAccount(userInfo, Now);

        // Assert
        account.AllowedFormats.ShouldBe([StreamFormat.HlsPlaylist, StreamFormat.MpegTs], ignoreOrder: true);
    }

    [Fact]
    public void MapAccount_WhenTheConnectionLimitIsReached_ReportsNoFreeConnection()
    {
        // Arrange: this is the number that decides whether opening a stream locks the account out.
        var userInfo = new XtreamUserInfoDto
        {
            Auth = 1,
            Status = "Active",
            MaxConnections = 1,
            ActiveConnections = 1,
        };

        // Act
        var account = XtreamContentProvider.MapAccount(userInfo, Now);

        // Assert
        account.HasFreeConnection.ShouldBeFalse();
    }

    [Fact]
    public void MapAccount_WhenThePanelReportsNoLimit_AllowsAConnection()
    {
        // Arrange: an unreported limit must not be read as "zero connections permitted".
        var userInfo = new XtreamUserInfoDto
        {
            Auth = 1,
            Status = "Active",
            MaxConnections = 0,
            ActiveConnections = 0,
        };

        // Act
        var account = XtreamContentProvider.MapAccount(userInfo, Now);

        // Assert
        account.HasFreeConnection.ShouldBeTrue();
    }
}
