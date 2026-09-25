using System.Net;
using System.Text;
using WeatherBot.Models;
using WeatherBot.Services;

namespace WeatherBot.Tests;

/// <summary>Управляемый сервис погоды: без сетевых вызовов.</summary>
internal sealed class FakeWeatherService : IWeatherService
{
    public GeoCity? City { get; set; }

    public Func<CityForecast>? ForecastFactory { get; set; }

    public Exception? FindCityException { get; set; }

    public Exception? ForecastException { get; set; }

    public List<string> Queries { get; } = [];

    public List<(double Latitude, double Longitude)> ForecastRequests { get; } = [];

    public Task<GeoCity?> FindCityAsync(string query, CancellationToken cancellationToken)
    {
        Queries.Add(query);

        return FindCityException is null
            ? Task.FromResult(City)
            : Task.FromException<GeoCity?>(FindCityException);
    }

    public Task<CityForecast> GetForecastAsync(
        double latitude, double longitude, DateOnly localDate, CancellationToken cancellationToken) =>
        BuildForecast(latitude, longitude);

    public Task<CityForecast> GetTomorrowForecastAsync(
        double latitude, double longitude, CancellationToken cancellationToken) =>
        BuildForecast(latitude, longitude);

    private Task<CityForecast> BuildForecast(double latitude, double longitude)
    {
        ForecastRequests.Add((latitude, longitude));

        if (ForecastException is not null)
        {
            return Task.FromException<CityForecast>(ForecastException);
        }

        var factory = ForecastFactory ?? (() => Samples.TomorrowForecast());
        return Task.FromResult(factory() with { Latitude = latitude, Longitude = longitude });
    }
}

/// <summary>Запоминает отправленные сообщения вместо обращения к Telegram.</summary>
internal sealed class RecordingSender : IMessageSender
{
    public List<(long ChatId, string Html)> Messages { get; } = [];

    public SendStatus Result { get; set; } = SendStatus.Sent;

    public bool NothingSent => Messages.Count == 0;

    public string Last => Messages.Count == 0 ? string.Empty : Messages[^1].Html;

    public bool LastContains(string value) => Last.Contains(value, StringComparison.Ordinal);

    public Task<SendStatus> SendHtmlAsync(long chatId, string html, CancellationToken cancellationToken)
    {
        Messages.Add((chatId, html));
        return Task.FromResult(Result);
    }
}

/// <summary>Подменяет переменные окружения на время теста и возвращает их обратно.</summary>
internal sealed class EnvironmentScope : IDisposable
{
    private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);

    public EnvironmentScope(params string[] names)
    {
        foreach (var name in names)
        {
            _original[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public EnvironmentScope Set(string name, string? value)
    {
        // Если значение ещё не запоминали — сохраняем исходное, чтобы вернуть его после теста.
        _original.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
        return this;
    }

    public void Dispose()
    {
        foreach (var (name, value) in _original)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}

/// <summary>Подменяет HTTP-ответы, чтобы проверять WeatherService без сети.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<string> Requests { get; } = [];

    public StubHttpMessageHandler EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    public StubHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responses.Enqueue(responder);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.OriginalString);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"Неожиданный HTTP-запрос: {request.RequestUri}");
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }
}

/// <summary>Фиксированное «сейчас» для тестов, зависящих от времени.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

/// <summary>Временный каталог, который удаляется после теста.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        FullPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "weatherbot-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public string File(string fileName) => System.IO.Path.Combine(FullPath, fileName);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(FullPath))
            {
                Directory.Delete(FullPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Каталог уже недоступен — для теста это не проблема.
        }
        catch (UnauthorizedAccessException)
        {
            // То же самое.
        }
    }
}
