using WeatherBot.Configuration;

namespace WeatherBot.Tests;

public sealed class BotOptionsTests
{
    private const string TokenVariable = "TELEGRAM_BOT_TOKEN";
    private const string ApiKeyVariable = "OPENWEATHER_API_KEY";
    private const string PasswordVariable = "BOT_PASSWORD";

    /// <summary>Очищает все переменные окружения, которые читает <see cref="BotOptions"/>.</summary>
    private static EnvironmentScope CleanEnvironment() => new(
        TokenVariable, ApiKeyVariable, PasswordVariable,
        "BOT_STATE_FILE", "BOT_TIMEZONE_OFFSET_HOURS", "BOT_DAILY_SEND_HOUR", "BOT_DRY_RUN", "BOT_FORCE_SEND");

    private static string[] MinimalArguments() =>
        ["--telegram-token", "token", "--openweather-key", "key", "--password", "secret"];

    [Fact]
    public void Parse_UsesDefaults()
    {
        using var environment = CleanEnvironment();

        var options = BotOptions.Parse(MinimalArguments());

        Assert.Equal("token", options.TelegramBotToken);
        Assert.Equal("key", options.OpenWeatherApiKey);
        Assert.Equal("secret", options.Password);
        Assert.Equal("state.json", options.StateFilePath);
        Assert.Equal(18, options.DailySendHour);
        Assert.Equal(3d, options.DefaultTimeZoneOffsetHours);
        Assert.False(options.DryRun);
        Assert.False(options.ForceSend);
        Assert.Null(options.PrintForecastCity);
    }

    [Fact]
    public void Parse_Throws_WhenApiKeyIsMissing()
    {
        using var environment = CleanEnvironment();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BotOptions.Parse(["--telegram-token", "token", "--password", "secret"]));

        Assert.Contains("OPENWEATHER_API_KEY", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Throws_ForAllMissingTelegramParameters()
    {
        using var environment = CleanEnvironment();

        var exception = Assert.Throws<InvalidOperationException>(() => BotOptions.Parse([]));

        Assert.Contains("OPENWEATHER_API_KEY", exception.Message, StringComparison.Ordinal);
        Assert.Contains(TokenVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains(PasswordVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Throws_WhenOnlyPasswordIsMissing()
    {
        using var environment = CleanEnvironment();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BotOptions.Parse(["--telegram-token", "token", "--openweather-key", "key"]));

        Assert.Contains(PasswordVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PrintForecastMode_DoesNotRequireTelegram()
    {
        using var environment = CleanEnvironment();

        var options = BotOptions.Parse(["--print-forecast", "Москва", "--openweather-key", "key"]);

        Assert.Equal("Москва", options.PrintForecastCity);
        Assert.Equal(string.Empty, options.TelegramBotToken);
        Assert.Equal(string.Empty, options.Password);
    }

    [Fact]
    public void Parse_ReadsEnvironmentVariables()
    {
        using var environment = CleanEnvironment()
            .Set(TokenVariable, "env-token")
            .Set(ApiKeyVariable, "env-key")
            .Set(PasswordVariable, "env-secret")
            .Set("BOT_STATE_FILE", "data/state.json")
            .Set("BOT_DAILY_SEND_HOUR", "9")
            .Set("BOT_TIMEZONE_OFFSET_HOURS", "-5.5")
            .Set("BOT_DRY_RUN", "true")
            .Set("BOT_FORCE_SEND", "TRUE");

        var options = BotOptions.Parse([]);

        Assert.Equal("env-token", options.TelegramBotToken);
        Assert.Equal("env-key", options.OpenWeatherApiKey);
        Assert.Equal("env-secret", options.Password);
        Assert.Equal("data/state.json", options.StateFilePath);
        Assert.Equal(9, options.DailySendHour);
        Assert.Equal(-5.5d, options.DefaultTimeZoneOffsetHours);
        Assert.Equal(-19800, options.DefaultTimeZoneOffsetSeconds);
        Assert.True(options.DryRun);
        Assert.True(options.ForceSend);
    }

    [Fact]
    public void Parse_TreatsNonBooleanEnvironmentFlagsAsDisabled()
    {
        using var environment = CleanEnvironment()
            .Set(TokenVariable, "t")
            .Set(ApiKeyVariable, "k")
            .Set(PasswordVariable, "p")
            .Set("BOT_DRY_RUN", "1");

        var options = BotOptions.Parse([]);

        Assert.False(options.DryRun);
    }

    [Fact]
    public void Parse_CliArgumentsOverrideEnvironment()
    {
        using var environment = CleanEnvironment()
            .Set(TokenVariable, "env-token")
            .Set(ApiKeyVariable, "env-key")
            .Set(PasswordVariable, "env-secret")
            .Set("BOT_DAILY_SEND_HOUR", "9");

        var options = BotOptions.Parse(
            ["--telegram-token", "cli-token", "--password", "cli-secret", "--send-hour", "21"]);

        Assert.Equal("cli-token", options.TelegramBotToken);
        Assert.Equal("env-key", options.OpenWeatherApiKey);
        Assert.Equal("cli-secret", options.Password);
        Assert.Equal(21, options.DailySendHour);
    }

    [Fact]
    public void Parse_SupportsEqualsSyntax()
    {
        using var environment = CleanEnvironment();

        var options = BotOptions.Parse(
            ["--telegram-token=t", "--openweather-key=k", "--password=p", "--state=state.json"]);

        Assert.Equal("t", options.TelegramBotToken);
        Assert.Equal("k", options.OpenWeatherApiKey);
        Assert.Equal("p", options.Password);
        Assert.Equal("state.json", options.StateFilePath);
    }

    [Fact]
    public void Parse_ReadsFlags()
    {
        using var environment = CleanEnvironment();

        var options = BotOptions.Parse([.. MinimalArguments(), "--dry-run", "--force-send"]);

        Assert.True(options.DryRun);
        Assert.True(options.ForceSend);
    }

    [Fact]
    public void Parse_IgnoresUnknownArguments()
    {
        using var environment = CleanEnvironment();

        var options = BotOptions.Parse([.. MinimalArguments(), "лишний", "--unknown", "значение"]);

        Assert.Equal("key", options.OpenWeatherApiKey);
    }

    [Theory]
    [InlineData("25")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Parse_Throws_ForInvalidSendHour(string value)
    {
        using var environment = CleanEnvironment();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BotOptions.Parse([.. MinimalArguments(), "--send-hour", value]));

        Assert.Contains("BOT_DAILY_SEND_HOUR", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("15")]
    [InlineData("-13")]
    [InlineData("abc")]
    public void Parse_Throws_ForInvalidTimeZoneOffset(string value)
    {
        using var environment = CleanEnvironment();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BotOptions.Parse([.. MinimalArguments(), "--timezone-offset", value]));

        Assert.Contains("BOT_TIMEZONE_OFFSET_HOURS", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3, 10800)]
    [InlineData(-3.5, -12600)]
    [InlineData(5.75, 20700)]
    public void DefaultTimeZoneOffsetSeconds_ConvertsHoursToSeconds(double hours, int expected)
    {
        var options = new BotOptions
        {
            TelegramBotToken = "t",
            OpenWeatherApiKey = "k",
            Password = "p",
            DefaultTimeZoneOffsetHours = hours,
        };

        Assert.Equal(expected, options.DefaultTimeZoneOffsetSeconds);
    }
}
