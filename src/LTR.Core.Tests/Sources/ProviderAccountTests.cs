namespace LTR.Core.Sources;

public sealed class ProviderAccountTests
{
    [Fact]
    public void HasFreeConnection_WhenBelowTheLimit_IsTrue()
    {
        // Arrange
        var account = Account(maxConnections: 2, activeConnections: 1);

        // Act & Assert
        account.HasFreeConnection.ShouldBeTrue();
    }

    [Fact]
    public void HasFreeConnection_AtTheLimit_IsFalse()
    {
        // Arrange
        var account = Account(maxConnections: 1, activeConnections: 1);

        // Act & Assert
        account.HasFreeConnection.ShouldBeFalse();
    }

    [Fact]
    public void HasFreeConnection_AboveTheLimit_IsFalse()
    {
        // Arrange: providers do report counts above the limit after a leaked connection.
        var account = Account(maxConnections: 1, activeConnections: 3);

        // Act & Assert
        account.HasFreeConnection.ShouldBeFalse();
    }

    [Fact]
    public void HasFreeConnection_WhenNoLimitIsReported_IsTrue()
    {
        // Arrange: an unreported limit must not be read as "no connections permitted", which would
        // make the player refuse to open anything.
        var account = Account(maxConnections: 0, activeConnections: 0);

        // Act & Assert
        account.HasFreeConnection.ShouldBeTrue();
    }

    [Fact]
    public void IsUsable_IsTrueOnlyForAnActiveAccount()
    {
        // Arrange & Act & Assert
        Account(status: AccountStatus.Active).IsUsable.ShouldBeTrue();
        Account(status: AccountStatus.Expired).IsUsable.ShouldBeFalse();
        Account(status: AccountStatus.Banned).IsUsable.ShouldBeFalse();
        Account(status: AccountStatus.Unknown).IsUsable.ShouldBeFalse();
        Account(status: AccountStatus.AuthenticationFailed).IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void Unauthenticated_IsNotUsable()
    {
        // Arrange & Act & Assert
        ProviderAccount.Unauthenticated.IsUsable.ShouldBeFalse();
        ProviderAccount.Unauthenticated.AllowedFormats.ShouldBeEmpty();
    }

    private static ProviderAccount Account(
        AccountStatus status = AccountStatus.Active,
        int maxConnections = 1,
        int activeConnections = 0)
    {
        return new ProviderAccount(
            status,
            ExpiresAtUtc: null,
            IsTrial: false,
            maxConnections,
            activeConnections,
            AllowedFormats: []);
    }
}
