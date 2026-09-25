using System.Text;
using WeatherBot.Configuration;

namespace WeatherBot.Tests;

/// <summary>
/// Проверяет загрузку локальных секретов: файлы <c>secrets.json</c> / <c>secret.json</c> / <c>.env</c>,
/// файл <c>dotnet user-secrets</c> и приоритет уже заданных переменных окружения.
/// </summary>
/// <remarks>
/// Тесты меняют переменные окружения процесса, поэтому выполняются в одной коллекции
/// с <see cref="BotOptionsTests"/>: xUnit не запускает коллекции параллельно.
/// </remarks>
[Collection(EnvironmentVariablesCollection.Name)]
public sealed class SecretsLoaderTests
{
    private static EnvironmentScope CleanEnvironment() => new(
        SecretsLoader.TelegramBotTokenVariable,
        SecretsLoader.OpenWeatherApiKeyVariable,
        SecretsLoader.PasswordVariable,
        SecretsLoader.SecretsFileVariable,
        SecretsLoader.StateFileVariable,
        SecretsLoader.TimeZoneOffsetVariable,
        SecretsLoader.DailySendHourVariable,
        SecretsLoader.DryRunVariable,
        SecretsLoader.ForceSendVariable);

    /// <summary>
    /// Загружает секреты из временного каталога. Путь к user-secrets задаётся всегда, чтобы тесты
    /// не зависели от файла, созданного разработчиком через <c>dotnet user-secrets</c>.
    /// </summary>
    private static SecretsLoadResult Load(TempDirectory directory, params string[] args) =>
        SecretsLoader.Load(args, directory.FullPath, directory.File("no-user-secrets.json"));

    /// <summary>Содержимое файла секретов в том виде, в котором его создаёт <c>dotnet user-secrets</c>.</summary>
    private static string UserSecretsJson() => """
        {
          "MySecretSettings": {
            "TELEGRAM_BOT_TOKEN": "token",
            "OPENWEATHER_API_KEY": "key",
            "BOT_PASSWORD": "secret"
          }
        }
        """;

    private static string WriteFile(TempDirectory directory, string fileName, string content)
    {
        var path = directory.File(fileName);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string? Variable(string name) => Environment.GetEnvironmentVariable(name);

    [Fact]
    public void Load_ReadsFlatJsonFile()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        var path = WriteFile(directory, "secrets.json", """
            {
              "TELEGRAM_BOT_TOKEN": "token",
              "OPENWEATHER_API_KEY": "key",
              "BOT_PASSWORD": "secret"
            }
            """);

        var result = Load(directory);

        Assert.Equal(path, result.Source);
        Assert.Equal(
            new[]
            {
                SecretsLoader.TelegramBotTokenVariable,
                SecretsLoader.OpenWeatherApiKeyVariable,
                SecretsLoader.PasswordVariable,
            },
            result.Applied);
        Assert.Empty(result.Skipped);
        Assert.False(result.NothingMatched);
        Assert.Equal("token", Variable(SecretsLoader.TelegramBotTokenVariable));
        Assert.Equal("key", Variable(SecretsLoader.OpenWeatherApiKeyVariable));
        Assert.Equal("secret", Variable(SecretsLoader.PasswordVariable));
    }

    [Fact]
    public void Load_ReadsKeysFromNestedSection()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        WriteFile(directory, "secrets.json", UserSecretsJson());

        var result = Load(directory);

        Assert.Equal(3, result.Applied.Count);
        Assert.Equal("token", Variable(SecretsLoader.TelegramBotTokenVariable));
        Assert.Equal("key", Variable(SecretsLoader.OpenWeatherApiKeyVariable));
        Assert.Equal("secret", Variable(SecretsLoader.PasswordVariable));
    }

    [Fact]
    public void Load_ReadsCamelCaseNamesAndOtherSettings()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        WriteFile(directory, "secret.json", """
            {
              "TelegramBotToken": "token",
              "OpenWeatherApiKey": "key",
              "Password": "secret",
              "DailySendHour": 21,
              "DryRun": true
            }
            """);

        var result = Load(directory);

        Assert.Equal("token", Variable(SecretsLoader.TelegramBotTokenVariable));
        Assert.Equal("secret", Variable(SecretsLoader.PasswordVariable));
        Assert.Equal("21", Variable(SecretsLoader.DailySendHourVariable));
        Assert.Equal("true", Variable(SecretsLoader.DryRunVariable));
        Assert.Equal(5, result.Applied.Count);
    }

    [Fact]
    public void Load_ReadsDotEnvFile()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        WriteFile(directory, ".env", """
            # комментарий
            export TELEGRAM_BOT_TOKEN="token"

            OPENWEATHER_API_KEY='key'
            BOT_PASSWORD=secret с пробелом
            """);

        var result = Load(directory);

        Assert.Equal(SecretsLoader.TelegramBotTokenVariable, result.Applied[0]);
        Assert.Equal("token", Variable(SecretsLoader.TelegramBotTokenVariable));
        Assert.Equal("key", Variable(SecretsLoader.OpenWeatherApiKeyVariable));
        Assert.Equal("secret с пробелом", Variable(SecretsLoader.PasswordVariable));
    }

    [Fact]
    public void Load_UsesPathFromArguments()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        var path = WriteFile(directory, "мои-секреты.txt", """{"TELEGRAM_BOT_TOKEN": "token"}""");

        var withEquals = Load(directory, $"--secrets={path}");
        var withSpace = Load(directory, "--secrets", path);

        Assert.Equal(path, withEquals.Source);
        Assert.Equal(path, withSpace.Source);
        Assert.Equal("token", Variable(SecretsLoader.TelegramBotTokenVariable));
    }

    [Fact]
    public void Load_UsesPathFromEnvironmentVariable()
    {
        using var directory = new TempDirectory();
        var path = WriteFile(directory, "secrets.json", """{"BOT_PASSWORD": "secret"}""");
        using var environment = CleanEnvironment().Set(SecretsLoader.SecretsFileVariable, path);

        var result = Load(directory);

        Assert.Equal(path, result.Source);
        Assert.Equal("secret", Variable(SecretsLoader.PasswordVariable));
    }

    [Fact]
    public void Load_Throws_WhenExplicitFileIsMissing()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        var missing = directory.File("absent.json");

        var exception = Assert.Throws<InvalidOperationException>(() => Load(directory, "--secrets", missing));

        Assert.Contains(missing, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Throws_WhenSecretsPathIsEmpty()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();

        var exception = Assert.Throws<InvalidOperationException>(() => Load(directory, "--secrets"));

        Assert.Contains("--secrets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_DoesNotOverrideEnvironmentVariable()
    {
        using var directory = new TempDirectory();
        WriteFile(directory, "secrets.json", """
            {
              "MySecretSettings": {
                "TELEGRAM_BOT_TOKEN": "из-файла",
                "OPENWEATHER_API_KEY": "key"
              }
            }
            """);
        using var environment = CleanEnvironment()
            .Set(SecretsLoader.TelegramBotTokenVariable, "из-окружения");

        var result = Load(directory);

        Assert.Equal("из-окружения", Variable(SecretsLoader.TelegramBotTokenVariable));
        Assert.Equal(new[] { SecretsLoader.OpenWeatherApiKeyVariable }, result.Applied);
        Assert.Equal(new[] { SecretsLoader.TelegramBotTokenVariable }, result.Skipped);
        Assert.False(result.NothingMatched);
    }

    [Fact]
    public void Load_IgnoresUnknownKeysAndEmptyValues()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        WriteFile(directory, "secrets.json", """
            {
              "SomethingElse": "value",
              "MySecretSettings": { "TELEGRAM_BOT_TOKEN": "" }
            }
            """);

        var result = Load(directory);

        Assert.Equal(directory.File("secrets.json"), result.Source);
        Assert.Empty(result.Applied);
        Assert.Empty(result.Skipped);
        Assert.True(result.NothingMatched);
        Assert.Null(Variable(SecretsLoader.TelegramBotTokenVariable));
    }

    [Fact]
    public void Load_Throws_ForBrokenJson()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        var path = WriteFile(directory, "secrets.json", """{ "TELEGRAM_BOT_TOKEN": }""");

        var exception = Assert.Throws<InvalidOperationException>(() => Load(directory));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Throws_WhenRootIsNotObject()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        WriteFile(directory, "secrets.json", """["token", "key"]""");

        var exception = Assert.Throws<InvalidOperationException>(() => Load(directory));

        Assert.Contains("объект JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReturnsEmptyResult_WhenFileIsAbsent()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();

        var result = Load(directory);

        Assert.Null(result.Source);
        Assert.Empty(result.Applied);
        Assert.Empty(result.Skipped);
        Assert.False(result.NothingMatched);
    }

    [Fact]
    public void Load_ReadsUserSecretsFile_WhenNothingElseFound()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        var path = WriteFile(directory, "user-secrets.json", UserSecretsJson());

        var result = SecretsLoader.Load([], directory.FullPath, path);

        Assert.Equal(path, result.Source);
        Assert.Equal("token", Variable(SecretsLoader.TelegramBotTokenVariable));
        Assert.Equal("secret", Variable(SecretsLoader.PasswordVariable));
    }

    [Fact]
    public void Load_PrefersFileInWorkingDirectoryOverUserSecrets()
    {
        using var environment = CleanEnvironment();
        using var directory = new TempDirectory();
        var local = WriteFile(directory, "secrets.json", """{"BOT_PASSWORD": "рядом-с-проектом"}""");
        var userSecrets = WriteFile(directory, "user-secrets.json", """{"BOT_PASSWORD": "из-user-secrets"}""");

        var result = SecretsLoader.Load([], directory.FullPath, userSecrets);

        Assert.Equal(local, result.Source);
        Assert.Equal("рядом-с-проектом", Variable(SecretsLoader.PasswordVariable));
    }

    [Fact]
    public void UserSecretsFile_BuildsPathForProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "user-secrets-root");

        var path = SecretsLoader.UserSecretsFile("3a49db92-16ee-4c1e-adf9-e29638625455", root);

        Assert.Equal(
            Path.Combine(root, "3a49db92-16ee-4c1e-adf9-e29638625455", "secrets.json"),
            path);
    }

    [Fact]
    public void DefaultUserSecretsRoot_PointsToUserProfile()
    {
        var root = SecretsLoader.DefaultUserSecretsRoot();

        Assert.Contains(
            OperatingSystem.IsWindows() ? "UserSecrets" : "usersecrets",
            root,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReadUserSecretsId_ReturnsNull_ForAssemblyWithoutAttribute() =>
        Assert.Null(SecretsLoader.ReadUserSecretsId(typeof(SecretsLoaderTests).Assembly));

    [Fact]
    public void ReadUserSecretsId_ReadsIdOfBotAssembly()
    {
        // Атрибут появляется из свойства UserSecretsId в src/WeatherBot/WeatherBot.csproj —
        // именно он связывает проект с файлом, который заполняет dotnet user-secrets.
        var id = SecretsLoader.ReadUserSecretsId(typeof(BotOptions).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(id));
    }
}
