using Profiler.Web.Security;
using Xunit;

namespace Profiler.Web.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("password123")]
    [InlineData("PASSWORD123")] // blocklist is case-insensitive
    [InlineData("qwerty123")]
    [InlineData("letmein")]
    public void Rejects_CommonPasswords(string password)
    {
        Assert.NotNull(PasswordPolicy.Validate(password, "someone"));
    }

    [Theory]
    [InlineData("alice-is-great", "alice")]
    [InlineData("xxALICExx99", "alice")] // case-insensitive containment
    public void Rejects_PasswordContainingUsername(string password, string username)
    {
        var error = PasswordPolicy.Validate(password, username);
        Assert.Contains("username", error);
    }

    [Theory]
    [InlineData("aaaaaaaa")]
    [InlineData("ababababab")]
    public void Rejects_TooFewDistinctCharacters(string password)
    {
        Assert.NotNull(PasswordPolicy.Validate(password, "someone"));
    }

    [Theory]
    [InlineData("Tr0ubad0ur-x9")]
    [InlineData("correct horse battery")]
    public void Accepts_ReasonablePasswords(string password)
    {
        Assert.Null(PasswordPolicy.Validate(password, "someone"));
    }

    [Fact]
    public void Ignores_EmptyPassword_LeavingItToModelValidation()
    {
        Assert.Null(PasswordPolicy.Validate("", "someone"));
    }
}
