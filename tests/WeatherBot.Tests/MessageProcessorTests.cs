using Telegram.Bot.Types.Enums;
using WeatherBot.Configuration;
using WeatherBot.Models;
using WeatherBot.Services;

namespace WeatherBot.Tests;

public sealed class MessageProcessorTests
{
    private const long ChatId = 555;

    private static MessageProcessor CreateProcessor(
        FakeWeatherService weather,
        RecordingSender sender,
        BotState state,
        string password = "secret") =>
        new(
            weather,
            sender,
            new BotOptions { TelegramBotToken = "token", OpenWeatherApiKey = "key", Password = password },
            state);

    /// <summary>Готовый пользователь: авторизован, город Москва выбран.</summary>
    private static BotUser AuthorizedUser(BotState state, long chatId = ChatId, bool withCity = true)
    {
        var user = state.GetOrCreate(chatId, "tester", "Тестер");
        user.IsAuthorized = true;

        if (withCity)
        {
            user.City = "Москва";
            user.CountryCode = "RU";
            user.Latitude = 55.7522;
            user.Longitude = 37.6156;
            user.TimeZoneOffsetSeconds = Samples.MoscowOffsetSeconds;
        }

        return user;
    }

    [Fact]
    public async Task Start_AsksForPassword_WhenUserIsNotAuthorized()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/start"), CancellationToken.None);

        Assert.Equal(BotMessages.AskPassword, sender.Last);
        Assert.False(state.Find(ChatId)!.IsAuthorized);
    }

    [Fact]
    public async Task Help_AsksForPassword_UntilAuthorized()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/help"), CancellationToken.None);
        Assert.Equal(BotMessages.AskPassword, sender.Last);

        AuthorizedUser(state, withCity: false);
        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/help"), CancellationToken.None);
        Assert.Equal(BotMessages.Help, sender.Last);
    }

    [Fact]
    public async Task PlainText_IsTreatedAsPassword_BeforeAuthorization()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "secret"), CancellationToken.None);

        var user = state.Find(ChatId)!;
        Assert.True(user.IsAuthorized);
        Assert.Equal(BotMessages.AccessGranted, sender.Last);
        // Пароль не считается названием города: к геокодеру обращений нет.
        Assert.Empty(weather.Queries);
        Assert.Null(user.City);
    }

    [Fact]
    public async Task WrongPassword_IsRejected()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "не тот пароль"), CancellationToken.None);

        Assert.False(state.Find(ChatId)!.IsAuthorized);
        Assert.Equal(BotMessages.WrongPassword, sender.Last);
    }

    [Fact]
    public async Task Login_Command_AcceptsPasswordInArgument()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/login secret"), CancellationToken.None);

        Assert.True(state.Find(ChatId)!.IsAuthorized);
        Assert.Equal(BotMessages.AccessGranted, sender.Last);
    }

    [Fact]
    public async Task Login_Command_WithoutArgument_AsksForPassword()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/login"), CancellationToken.None);

        Assert.False(state.Find(ChatId)!.IsAuthorized);
        Assert.Equal(BotMessages.AskPassword, sender.Last);
    }

    [Fact]
    public async Task Password_Command_WorksAfterLogout()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/logout"), CancellationToken.None);
        Assert.False(user.IsAuthorized);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/password secret"), CancellationToken.None);

        Assert.True(user.IsAuthorized);
        // Город уже выбран, поэтому при повторном входе бот не просит его снова.
        Assert.Equal(BotMessages.Subscribed, sender.Last);
    }

    [Fact]
    public async Task PlainText_FromAuthorizedUser_IsSavedAsCity()
    {
        var weather = new FakeWeatherService { City = Samples.Moscow() };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state, withCity: false);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "  Москва  "), CancellationToken.None);

        Assert.Equal(["Москва"], weather.Queries);
        Assert.Equal("Москва", user.City);
        Assert.Equal("RU", user.CountryCode);
        Assert.Equal(55.7522, user.Latitude);
        Assert.Equal(37.6156, user.Longitude);
        Assert.Equal(Samples.MoscowOffsetSeconds, user.TimeZoneOffsetSeconds);
        Assert.Contains("Город сохранён", sender.Last, StringComparison.Ordinal);
        Assert.Contains("Москва, RU", sender.Last, StringComparison.Ordinal);
        Assert.True(user.IsSubscribed);
    }

    [Fact]
    public async Task City_Command_SavesCityFromArgument()
    {
        var weather = new FakeWeatherService { City = Samples.Moscow() };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state, withCity: false);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/city Париж, FR"), CancellationToken.None);

        Assert.Equal(["Париж, FR"], weather.Queries);
        Assert.Equal("Москва", user.City);
    }

    [Fact]
    public async Task City_Command_WithoutArgument_AsksForCity()
    {
        var weather = new FakeWeatherService { City = Samples.Moscow() };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        AuthorizedUser(state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/city"), CancellationToken.None);

        Assert.Equal(BotMessages.AskCity, sender.Last);
        Assert.Empty(weather.Queries);
    }

    [Fact]
    public async Task City_Command_RequiresAuthorization()
    {
        var weather = new FakeWeatherService { City = Samples.Moscow() };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/city Москва"), CancellationToken.None);

        Assert.Equal(BotMessages.AskPassword, sender.Last);
        Assert.Empty(weather.Queries);
    }

    [Fact]
    public async Task CityChange_ResetsLastSentDate()
    {
        var weather = new FakeWeatherService { City = Samples.Moscow() };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state);
        user.LastSentDate = "2026-09-26";

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "Казань"), CancellationToken.None);

        // Для нового города прогноз на ту же дату должен уйти заново.
        Assert.Null(user.LastSentDate);
    }

    [Fact]
    public async Task UnknownCity_ReportsNotFound()
    {
        var weather = new FakeWeatherService { City = null };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state, withCity: false);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "Атлантида"), CancellationToken.None);

        Assert.Contains("Не удалось найти город", sender.Last, StringComparison.Ordinal);
        Assert.Contains("Атлантида", sender.Last, StringComparison.Ordinal);
        Assert.Null(user.City);
    }

    [Fact]
    public async Task GeocodingFailure_IsReportedToUser()
    {
        var weather = new FakeWeatherService
        {
            FindCityException = new WeatherServiceException("Сервис недоступен"),
        };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        AuthorizedUser(state, withCity: false);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "Москва"), CancellationToken.None);

        Assert.Contains("Не удалось получить прогноз", sender.Last, StringComparison.Ordinal);
        Assert.Contains("Сервис недоступен", sender.Last, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CityIsSaved_EvenWhenForecastIsUnavailable()
    {
        var weather = new FakeWeatherService
        {
            City = Samples.Moscow(),
            ForecastException = new WeatherServiceException("OpenWeatherMap отклонил запрос"),
        };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state, withCity: false);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "Москва"), CancellationToken.None);

        Assert.Equal("Москва", user.City);
        Assert.Contains("Город сохранён", sender.Last, StringComparison.Ordinal);
        Assert.Contains("OpenWeatherMap отклонил запрос", sender.Last, StringComparison.Ordinal);
        Assert.Null(user.TimeZoneOffsetSeconds);
    }

    [Fact]
    public async Task MessagesFromGroupChats_AreIgnored()
    {
        var weather = new FakeWeatherService { City = Samples.Moscow() };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(
            MessageFactory.Create(ChatId, "/start", ChatType.Group),
            CancellationToken.None);

        Assert.True(sender.NothingSent);
        Assert.Empty(state.Users);
    }

    [Fact]
    public async Task NonTextMessages_AreHandledOnlyForAuthorizedUsers()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, null), CancellationToken.None);
        Assert.True(sender.NothingSent);

        AuthorizedUser(state);
        await processor.ProcessAsync(MessageFactory.Create(ChatId, null), CancellationToken.None);
        Assert.Equal(BotMessages.OnlyTextSupported, sender.Last);
    }

    [Fact]
    public async Task Tomorrow_RequiresAuthorization()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/tomorrow"), CancellationToken.None);

        Assert.Equal(BotMessages.AskPassword, sender.Last);
        Assert.Empty(weather.ForecastRequests);
    }

    [Fact]
    public async Task Tomorrow_WithoutCity_AsksForCity()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        AuthorizedUser(state, withCity: false);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/tomorrow"), CancellationToken.None);

        Assert.Equal(BotMessages.AskCity, sender.Last);
        Assert.Empty(weather.ForecastRequests);
    }

    [Theory]
    [InlineData("/tomorrow")]
    [InlineData("/weather")]
    [InlineData("/forecast")]
    [InlineData("/TOMORROW@MyWeatherBot")]
    public async Task Tomorrow_UsesUserCoordinatesAndCityName(string command)
    {
        var weather = new FakeWeatherService
        {
            // OpenWeatherMap возвращает название на английском — должно остаться имя пользователя.
            ForecastFactory = () => Samples.TomorrowForecast() with { City = "Moscow" },
        };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, command), CancellationToken.None);

        Assert.Equal([(55.7522, 37.6156)], weather.ForecastRequests);
        Assert.Contains("Москва, RU", sender.Last, StringComparison.Ordinal);
        Assert.DoesNotContain("Moscow", sender.Last, StringComparison.Ordinal);
        Assert.Equal(Samples.MoscowOffsetSeconds, user.TimeZoneOffsetSeconds);
    }

    [Fact]
    public async Task Tomorrow_ReportsForecastFailure()
    {
        var weather = new FakeWeatherService
        {
            ForecastException = new WeatherServiceException("Ответ OpenWeatherMap не распознан"),
        };
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        AuthorizedUser(state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/tomorrow"), CancellationToken.None);

        Assert.Contains("Не удалось получить прогноз", sender.Last, StringComparison.Ordinal);
        Assert.Contains("Ответ OpenWeatherMap не распознан", sender.Last, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_ShowsEmptyStateForNewUser()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/status"), CancellationToken.None);

        Assert.True(sender.LastContains("Доступ: закрыт"));
        Assert.True(sender.LastContains("Город: не выбран"));
        Assert.True(sender.LastContains("Сейчас в городе: —"));
        Assert.True(sender.LastContains("Последний прогноз: ещё не отправлялся"));
        Assert.True(sender.LastContains("рассылка в 18:00: включена"));
    }

    [Fact]
    public async Task Status_ShowsSavedSettings()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state);
        user.LastSentDate = "2026-09-26";
        user.IsSubscribed = false;

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/status"), CancellationToken.None);

        Assert.True(sender.LastContains("Доступ: открыт"));
        Assert.True(sender.LastContains("Город: Москва, RU"));
        Assert.True(sender.LastContains("Сейчас в городе: "));
        Assert.True(sender.LastContains("UTC+03:00"));
        Assert.True(sender.LastContains("рассылка в 18:00: выключена"));
        Assert.True(sender.LastContains("Последний прогноз: на 26.09.2026"));
    }

    [Fact]
    public async Task Start_ForAuthorizedUser_EnablesSubscription()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state);
        user.IsSubscribed = false;

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/start"), CancellationToken.None);

        Assert.True(user.IsSubscribed);
        Assert.Equal(BotMessages.Subscribed, sender.Last);
    }

    [Fact]
    public async Task Stop_RequiresAuthorization()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state);
        user.IsAuthorized = false;

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/stop"), CancellationToken.None);

        Assert.True(user.IsSubscribed);
        Assert.Equal(BotMessages.AskPassword, sender.Last);
    }

    [Fact]
    public async Task Stop_DisablesSubscription()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/stop"), CancellationToken.None);

        Assert.False(user.IsSubscribed);
        Assert.Equal(BotMessages.Unsubscribed, sender.Last);
    }

    [Fact]
    public async Task Subscribe_EnablesSubscription_AndKeepsLastSentDate()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state);
        user.IsSubscribed = false;
        user.LastSentDate = "2026-09-26";

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/unsubscribe"), CancellationToken.None);
        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/subscribe"), CancellationToken.None);

        Assert.True(user.IsSubscribed);
        Assert.Equal("2026-09-26", user.LastSentDate);
        Assert.Equal(BotMessages.Subscribed, sender.Last);
    }

    [Fact]
    public async Task Logout_RevokesAccess()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        var user = AuthorizedUser(state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/logout"), CancellationToken.None);

        Assert.False(user.IsAuthorized);
        Assert.Equal(BotMessages.LoggedOut, sender.Last);
    }

    [Fact]
    public async Task ChatId_Command_ReturnsChatIdentifier()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/chatid"), CancellationToken.None);

        Assert.Equal(ChatId, Assert.Single(sender.Messages).ChatId);
        Assert.True(sender.LastContains(ChatId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task UnknownCommand_SuggestsHelp()
    {
        var weather = new FakeWeatherService();
        var sender = new RecordingSender();
        var state = new BotState();
        var processor = CreateProcessor(weather, sender, state);
        AuthorizedUser(state);

        await processor.ProcessAsync(MessageFactory.Create(ChatId, "/hgfd"), CancellationToken.None);

        Assert.True(sender.LastContains("не поддерживается"));
        Assert.True(sender.LastContains("/help"));
    }

    [Theory]
    [InlineData("/start", "/start", "")]
    [InlineData("/CITY Минск", "/city", "Минск")]
    [InlineData("/city@MyWeatherBot  Москва , RU ", "/city", "Москва , RU")]
    [InlineData("/tomorrow@MyWeatherBot", "/tomorrow", "")]
    [InlineData("Москва", null, "Москва")]
    [InlineData("@MyWeatherBot", null, "@MyWeatherBot")]
    [InlineData("/", "/", "")]
    public void ParseCommand_SplitsCommandAndArgument(string text, string? expectedCommand, string expectedArgument)
    {
        var (command, argument) = MessageProcessor.ParseCommand(text);

        Assert.Equal(expectedCommand, command);
        Assert.Equal(expectedArgument, argument);
    }
}
