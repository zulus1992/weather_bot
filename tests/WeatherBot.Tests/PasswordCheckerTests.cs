using WeatherBot.Security;

namespace WeatherBot.Tests;

public sealed class PasswordCheckerTests
{
    [Theory]
    [InlineData("secret", "secret")]
    [InlineData("  secret  ", "secret")]
    [InlineData("пароль-с-кириллицей", "пароль-с-кириллицей")]
    [InlineData("P@ssw0rd!", "P@ssw0rd!")]
    public void IsMatch_ReturnsTrue_ForCorrectPassword(string candidate, string expected) =>
        Assert.True(PasswordChecker.IsMatch(candidate, expected));

    [Theory]
    [InlineData("Secret", "secret")]
    [InlineData("secre", "secret")]
    [InlineData("secret ", "secrets")]
    [InlineData("", "secret")]
    [InlineData("   ", "secret")]
    [InlineData(null, "secret")]
    public void IsMatch_ReturnsFalse_ForWrongPassword(string? candidate, string expected) =>
        Assert.False(PasswordChecker.IsMatch(candidate, expected));

    [Fact]
    public void IsMatch_ReturnsFalse_WhenExpectedPasswordIsEmpty() =>
        Assert.False(PasswordChecker.IsMatch("secret", string.Empty));

    [Fact]
    public void IsMatch_IsCaseSensitive() =>
        Assert.False(PasswordChecker.IsMatch("SECRET", "secret"));
}
