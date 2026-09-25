using System.Text;
using Telegram.Bot;
using WeatherBot.Configuration;
using WeatherBot.Models;
using WeatherBot.Services;

namespace WeatherBot;

/// <summary>
/// Точка входа. Бот предназначен для запуска по расписанию (GitHub Actions): каждое выполнение
/// читает очередь сообщений Telegram, отвечает на команды, рассылает прогноз на завтра тем,
/// у кого наступил час рассылки, и сохраняет состояние в файл.
/// </summary>
internal static class Program
{
    private const int ExitCodeError = 1;

    private static async Task<int> Main(string[] args)
    {
        ConfigureConsole();

        BotOptions options;
        try
        {
            options = BotOptions.Parse(args);
        }
        catch (InvalidOperationException exception)
        {
            ConsoleLog.Error(exception.Message);
            return ExitCodeError;
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var weather = new WeatherService(
            httpClient,
            options.OpenWeatherApiKey,
            options.DefaultTimeZoneOffsetSeconds);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            if (options.PrintForecastCity is { Length: > 0 } cityQuery)
            {
                return await PrintForecastAsync(weather, options, cityQuery, cancellation.Token)
                    .ConfigureAwait(false);
            }

            var bot = new TelegramBotClient(options.TelegramBotToken);
            var stateStore = new JsonStateStore(options.StateFilePath);
            var state = await stateStore.LoadAsync(cancellation.Token).ConfigureAwait(false);
            var notifier = new TelegramNotifier(bot, options.DryRun);
            var messageProcessor = new MessageProcessor(weather, notifier, options, state);
            var runner = new BotRunner(bot, weather, notifier, stateStore, messageProcessor, state, options);

            await runner.RunAsync(cancellation.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            ConsoleLog.Warning("Работа прервана по запросу (Ctrl+C).");
            return ExitCodeError;
        }
        catch (Exception exception)
        {
            ConsoleLog.Error("Бот завершился с ошибкой", exception);
            return ExitCodeError;
        }
    }

    /// <summary>
    /// Режим <c>--print-forecast "Москва"</c>: печатает прогноз в консоль и ничего не отправляет.
    /// Удобно для проверки ключей OpenWeatherMap и текста сообщения.
    /// </summary>
    private static async Task<int> PrintForecastAsync(
        IWeatherService weather,
        BotOptions options,
        string cityQuery,
        CancellationToken cancellationToken)
    {
        var city = await weather.FindCityAsync(cityQuery, cancellationToken).ConfigureAwait(false);
        if (city is null)
        {
            ConsoleLog.Error($"Город «{cityQuery}» не найден. Уточните название или добавьте страну: «{cityQuery}, RU».");
            return ExitCodeError;
        }

        var forecast = await weather
            .GetTomorrowForecastAsync(city.Latitude, city.Longitude, cancellationToken)
            .ConfigureAwait(false);

        var cityForecast = forecast with { City = city.Name, CountryCode = city.CountryCode };
        var localNow = DailySchedule.GetLocalNow(
            DateTimeOffset.UtcNow,
            forecast.TimeZoneOffsetSeconds,
            options.DefaultTimeZoneOffsetSeconds);

        Console.WriteLine();
        Console.WriteLine(WeatherFormatter.ToPlainText(cityForecast));
        Console.WriteLine();
        ConsoleLog.Info(
            $"Сейчас в городе {localNow:HH:mm} (UTC{localNow.Offset:hh\\:mm}), час рассылки {options.DailySendHour}:00.");

        var probe = new BotUser
        {
            ChatId = 0,
            City = city.Name,
            CountryCode = city.CountryCode,
            Latitude = city.Latitude,
            Longitude = city.Longitude,
            TimeZoneOffsetSeconds = forecast.TimeZoneOffsetSeconds,
        };

        ConsoleLog.Info(
            "Рассылка уже должна была сработать: " +
            (DailySchedule.IsDue(probe, DateTimeOffset.UtcNow, options.DefaultTimeZoneOffsetSeconds, options.DailySendHour)
                ? "да"
                : "нет"));

        return 0;
    }

    /// <summary>Кириллица в консоли Windows отображается корректно только в UTF-8.</summary>
    private static void ConfigureConsole()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Вывод перенаправлен — кодировку менять не нужно.
        }
        catch (PlatformNotSupportedException)
        {
            // Среда без консоли.
        }
    }
}
