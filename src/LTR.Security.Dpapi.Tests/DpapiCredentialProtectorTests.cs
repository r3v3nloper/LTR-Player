using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Security.Dpapi;

/// <summary>
/// Exercises the real Data Protection API rather than a stand-in, because the behaviour that matters —
/// what a round trip produces, and what happens to values written before protection existed — is the
/// platform's, not ours.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialProtectorTests
{
    [Fact]
    public void ProtectThenUnprotect_ReturnsTheOriginalSecret()
    {
        // Arrange
        var protector = CreateProtector();
        const string password = "s3cret-with-Ümläute-and-symbols-!@#/\\";

        // Act
        var stored = protector.Protect(password);
        var recovered = protector.Unprotect(stored);

        // Assert
        recovered.ShouldBe(password);
    }

    [Fact]
    public void Protect_DoesNotLeaveThePlaintextInTheStoredValue()
    {
        // Arrange: the whole point, and cheap to assert.
        var protector = CreateProtector();
        const string password = "s3cret";

        // Act
        var stored = protector.Protect(password);

        // Assert
        stored.ShouldNotContain(password);
        stored.ShouldStartWith("dpapi.v1:");
    }

    [Fact]
    public void Protect_ProducesADifferentValueEachTime()
    {
        // Arrange: DPAPI salts its output, so identical passwords must not produce identical rows.
        var protector = CreateProtector();

        // Act
        var first = protector.Protect("same");
        var second = protector.Protect("same");

        // Assert
        first.ShouldNotBe(second);
        protector.Unprotect(first).ShouldBe("same");
        protector.Unprotect(second).ShouldBe("same");
    }

    [Fact]
    public void Unprotect_ReturnsAPlaintextValueWrittenBeforeProtectionExisted()
    {
        // Arrange: this is what keeps an already-configured installation working. Failing here would
        // make every existing source unusable the moment protection is introduced.
        var protector = CreateProtector();

        // Act
        var recovered = protector.Unprotect("legacy-plaintext-password");

        // Assert
        recovered.ShouldBe("legacy-plaintext-password");
    }

    [Fact]
    public void IsProtected_DistinguishesStoredFormFromPlaintext()
    {
        // Arrange: the upgrade pass depends on this telling the two apart.
        var protector = CreateProtector();

        // Act & Assert
        protector.IsProtected(protector.Protect("s3cret")).ShouldBeTrue();
        protector.IsProtected("legacy-plaintext-password").ShouldBeFalse();
        protector.IsProtected(string.Empty).ShouldBeFalse();
    }

    [Fact]
    public void Unprotect_WhenTheStoredValueIsCorrupt_YieldsEmptyRatherThanThrowing()
    {
        // Arrange: what a database copied from another machine looks like. One unreadable credential
        // must not stop the application from starting.
        var protector = CreateProtector();

        // Act
        var recovered = protector.Unprotect("dpapi.v1:this-is-not-valid-base64-ciphertext!!");

        // Assert
        recovered.ShouldBeEmpty();
    }

    [Fact]
    public void Protect_LeavesAnEmptySecretAlone()
    {
        // Arrange: an M3U source has no password, and encrypting nothing would only obscure that.
        var protector = CreateProtector();

        // Act & Assert
        protector.Protect(string.Empty).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    [InlineData(128)]
    public void Protect_StaysWithinTheStorageBudgetForTheColumn(int passwordLength)
    {
        // Arrange: the Password column is mapped with a 1000 character limit. SQLite does not enforce
        // that, so exceeding it would fail silently here and only surface on a database that does.
        var protector = CreateProtector();
        var password = new string('x', passwordLength);

        // Act
        var stored = protector.Protect(password);

        // Assert
        stored.Length.ShouldBeLessThan(1000);
        protector.Unprotect(stored).ShouldBe(password);
    }

    private static DpapiCredentialProtector CreateProtector()
    {
        return new DpapiCredentialProtector(NullLogger<DpapiCredentialProtector>.Instance);
    }
}
