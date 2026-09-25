using WeatherBot.Models;
using WeatherBot.Services;

namespace WeatherBot.Tests;

public sealed class JsonStateStoreTests
{
    private static BotState StateWithUser()
    {
        var state = new BotState { IsInitialized = true, TelegramUpdateOffset = 77 };
        state.Users.Add(new BotUser
        {
            ChatId = 12345,
            UserName = "tester",
            FirstName = "Иван",
            IsAuthorized = true,
            IsSubscribed = true,
            City = "Москва",
            CountryCode = "RU",
            Latitude = 55.7522,
            Longitude = 37.6156,
            TimeZoneOffsetSeconds = Samples.MoscowOffsetSeconds,
            LastSentDate = "2026-09-26",
        });

        return state;
    }

    [Fact]
    public async Task LoadAsync_ReturnsNewState_WhenFileDoesNotExist()
    {
        using var directory = new TempDirectory();
        var store = new JsonStateStore(directory.File("state.json"));

        var state = await store.LoadAsync(CancellationToken.None);

        Assert.False(state.IsInitialized);
        Assert.Equal(0, state.TelegramUpdateOffset);
        Assert.Empty(state.Users);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsEverything()
    {
        using var directory = new TempDirectory();
        var store = new JsonStateStore(directory.File("state.json"));

        await store.SaveAsync(StateWithUser(), CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.True(loaded.IsInitialized);
        Assert.Equal(77, loaded.TelegramUpdateOffset);
        var user = Assert.Single(loaded.Users);
        Assert.Equal(12345, user.ChatId);
        Assert.Equal("tester", user.UserName);
        Assert.Equal("Иван", user.FirstName);
        Assert.True(user.IsAuthorized);
        Assert.True(user.IsSubscribed);
        Assert.Equal("Москва", user.City);
        Assert.Equal("RU", user.CountryCode);
        Assert.Equal(55.7522, user.Latitude);
        Assert.Equal(37.6156, user.Longitude);
        Assert.Equal(Samples.MoscowOffsetSeconds, user.TimeZoneOffsetSeconds);
        Assert.Equal("2026-09-26", user.LastSentDate);
    }

    [Fact]
    public async Task SaveAsync_WritesReadableJsonWithCamelCaseNames()
    {
        using var directory = new TempDirectory();
        var path = directory.File("state.json");
        var store = new JsonStateStore(path);

        await store.SaveAsync(StateWithUser(), CancellationToken.None);
        var json = await File.ReadAllTextAsync(path);

        Assert.Contains("\"isInitialized\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"telegramUpdateOffset\": 77", json, StringComparison.Ordinal);
        Assert.Contains("\"lastSentDate\": \"2026-09-26\"", json, StringComparison.Ordinal);
        // Кириллица не экранируется — диффы в git остаются читаемыми.
        Assert.Contains("Москва", json, StringComparison.Ordinal);
        // Отсутствующие ссылочные поля в файл не пишутся.
        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_DoesNotLeaveTemporaryFile()
    {
        using var directory = new TempDirectory();
        var store = new JsonStateStore(directory.File("state.json"));

        await store.SaveAsync(StateWithUser(), CancellationToken.None);

        Assert.Empty(Directory.GetFiles(directory.FullPath, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_CreatesMissingDirectories()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.FullPath, "data", "state.json");
        var store = new JsonStateStore(path);

        await store.SaveAsync(new BotState(), CancellationToken.None);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task SaveAsync_Throws_ForNullState()
    {
        using var directory = new TempDirectory();
        var store = new JsonStateStore(directory.File("state.json"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_ReturnsNewState_ForEmptyFile()
    {
        using var directory = new TempDirectory();
        var path = directory.File("state.json");
        await File.WriteAllTextAsync(path, string.Empty);
        var store = new JsonStateStore(path);

        var state = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(state.Users);
    }

    [Fact]
    public async Task LoadAsync_Throws_ForBrokenJson()
    {
        using var directory = new TempDirectory();
        var path = directory.File("state.json");
        await File.WriteAllTextAsync(path, "{ \"users\": [ ");
        var store = new JsonStateStore(path);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.LoadAsync(CancellationToken.None));

        Assert.Contains("повреждён", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ReadsJsonWithUnknownFields()
    {
        using var directory = new TempDirectory();
        var path = directory.File("state.json");
        await File.WriteAllTextAsync(path, """{ "telegramUpdateOffset": 5, "users": [], "newField": 42 }""");
        var store = new JsonStateStore(path);

        var state = await store.LoadAsync(CancellationToken.None);

        Assert.False(state.IsInitialized);
        Assert.Equal(5, state.TelegramUpdateOffset);
        Assert.Empty(state.Users);
    }

    [Fact]
    public void Constructor_Throws_ForEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => new JsonStateStore(string.Empty));
        Assert.Throws<ArgumentException>(() => new JsonStateStore("   "));
    }

    [Fact]
    public void StatePath_IsPreserved()
    {
        var store = new JsonStateStore("data/state.json");

        Assert.Equal("data/state.json", store.StatePath);
    }
}
