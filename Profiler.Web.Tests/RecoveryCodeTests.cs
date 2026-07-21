using Profiler.Web.Security;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// The recovery code is the only way back into an account, and it is typed back in by hand from
/// wherever the user wrote it down — so it has to survive transcription without being any easier to
/// guess.
/// </summary>
public class RecoveryCodeTests
{
    [Fact]
    public void GeneratedCodes_AreDistinct()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => RecoveryCode.Generate()).ToHashSet();
        Assert.Equal(200, codes.Count);
    }

    [Fact]
    public void GeneratedCodes_AvoidCharactersThatReadAsOtherCharacters()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = RecoveryCode.Generate();
            Assert.DoesNotContain('I', code);
            Assert.DoesNotContain('L', code);
            Assert.DoesNotContain('O', code);
            Assert.DoesNotContain('U', code);
        }
    }

    [Theory]
    [InlineData("4km7q")]                    // lower case
    [InlineData("4KM7Q")]                    // as displayed
    [InlineData(" 4 K M 7 Q ")]              // spaced out
    [InlineData("4km-7q")]                   // regrouped
    public void Verification_IgnoresCaseSpacingAndGrouping(string typed)
    {
        var hash = RecoveryCode.Hash("4KM7Q");
        Assert.True(RecoveryCode.Verify(typed, hash));
    }

    [Theory]
    [InlineData("O", "0")]   // letter O typed for zero
    [InlineData("I", "1")]
    [InlineData("l", "1")]
    public void Verification_FoldsLookalikesOntoTheCharacterActuallyUsed(string typed, string actual)
    {
        var hash = RecoveryCode.Hash(actual);
        Assert.True(RecoveryCode.Verify(typed, hash));
    }

    [Fact]
    public void Verification_RejectsAWrongCode()
    {
        var hash = RecoveryCode.Hash(RecoveryCode.Generate());
        Assert.False(RecoveryCode.Verify(RecoveryCode.Generate(), hash));
    }

    [Fact]
    public void Verification_RejectsEmptyInput_AndAccountsWithNoCodeOnFile()
    {
        var hash = RecoveryCode.Hash("4KM7Q");

        Assert.False(RecoveryCode.Verify("", hash));
        Assert.False(RecoveryCode.Verify("   ", hash));
        Assert.False(RecoveryCode.Verify(null, hash));
        // An account with no code must not be openable by supplying nothing.
        Assert.False(RecoveryCode.Verify("anything", null));
        Assert.False(RecoveryCode.Verify(null, null));
    }

    [Fact]
    public void TheStoredHash_IsNotTheCode()
    {
        var code = RecoveryCode.Generate();
        var hash = RecoveryCode.Hash(code);

        Assert.DoesNotContain(RecoveryCode.Normalize(code), hash);
    }

    [Fact]
    public void Verify_TreatsAGarbageHashAsNoMatch_RatherThanThrowing()
    {
        Assert.False(RecoveryCode.Verify("4KM7Q", "not-a-bcrypt-hash"));
    }
}
