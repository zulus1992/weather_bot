using WeatherBot.Models;
using WeatherBot.Models.OpenWeather;
using WeatherBot.Services;

namespace WeatherBot.Tests;

public sealed class ForecastBuilderTests
{
    private static readonly DateOnly Date = new(2026, 9, 26);

    /// <summary>Записи трёхчасового прогноза вокруг указанных суток (Москва, UTC+3).</summary>
    private static ForecastResponseDto ResponseWithThreeEntries() => Samples.Response(
        Samples.MoscowOffsetSeconds,
        // 23:00 25 сентября по Москве — прошлый день.
        Samples.Entry(Samples.UnixUtc(2026, 9, 25, 20), minTemperature: 9, maxTemperature: 11, weatherId: 800, icon: "01n"),
        // 00:00 26 сентября по Москве.
        Samples.Entry(
            Samples.UnixUtc(2026, 9, 25, 21),
            minTemperature: 8,
            maxTemperature: 12,
            feelsLike: 6,
            humidity: 70,
            cloudiness: 80,
            windSpeed: 4,
            windGust: 9,
            precipitationProbability: 0.2,
            rain: 0.5,
            weatherId: 500,
            main: "Rain",
            description: "дождь",
            icon: "10n"),
        // 09:00 26 сентября.
        Samples.Entry(
            Samples.UnixUtc(2026, 9, 26, 6),
            minTemperature: 10.5,
            maxTemperature: 16.4,
            feelsLike: 9.1,
            humidity: 50,
            cloudiness: 30,
            windSpeed: 8.4,
            precipitationProbability: 0.7,
            rain: 1.2,
            weatherId: 500,
            main: "Rain",
            description: "дождь",
            icon: "10d"),
        // 18:00 26 сентября.
        Samples.Entry(
            Samples.UnixUtc(2026, 9, 26, 15),
            minTemperature: 12.2,
            maxTemperature: 18.9,
            feelsLike: 11,
            humidity: 45,
            cloudiness: 20,
            windSpeed: 6.1,
            windGust: 15.4,
            precipitationProbability: 0.3,
            snow: 0.3,
            weatherId: 800,
            main: "Clear",
            description: "ясно",
            icon: "01d"),
        // 00:00 27 сентября по Москве — следующий день.
        Samples.Entry(Samples.UnixUtc(2026, 9, 26, 21), minTemperature: 5, maxTemperature: 7, weatherId: 800, icon: "01n"));

    [Fact]
    public void GetLocalDate_AppliesCityOffset() =>
        Assert.Equal(
            Date,
            ForecastBuilder.GetLocalDate(Samples.UnixUtc(2026, 9, 25, 21), Samples.MoscowOffsetSeconds));

    [Fact]
    public void BuildForDate_AggregatesOnlyEntriesOfThatLocalDay()
    {
        var forecast = ForecastBuilder.BuildForDate(ResponseWithThreeEntries(), Date);

        Assert.NotNull(forecast);
        Assert.Equal(Date, forecast.Date);
        Assert.Equal(8, forecast.MinTemperature);
        Assert.Equal(18.9, forecast.MaxTemperature);
        Assert.Equal(6, forecast.MinFeelsLike);
        Assert.Equal(11, forecast.MaxFeelsLike);
        Assert.Equal(8.4, forecast.MaxWindSpeed);
        Assert.Equal(15.4, forecast.MaxWindGust);
        Assert.Equal(55, forecast.AverageHumidity);
        Assert.Equal(2, forecast.TotalPrecipitationMm);
        Assert.Equal(70, forecast.MaxPrecipitationProbability);
        Assert.Equal(43, forecast.AverageCloudiness);
    }

    [Fact]
    public void BuildForDate_PicksMostFrequentCondition()
    {
        var forecast = ForecastBuilder.BuildForDate(ResponseWithThreeEntries(), Date);

        Assert.NotNull(forecast);
        // «дождь» встречается дважды против одного «ясно».
        Assert.Equal("дождь", forecast.Description);
        Assert.Equal("10d", forecast.Icon);
    }

    [Fact]
    public void BuildForDate_PrefersDayIcon()
    {
        var response = Samples.Response(
            Samples.MoscowOffsetSeconds,
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 3), weatherId: 500, description: "небольшой дождь", icon: "10n"),
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 9), weatherId: 500, description: "небольшой дождь", icon: "10d"));

        var forecast = ForecastBuilder.BuildForDate(response, Date);

        Assert.NotNull(forecast);
        Assert.Equal("10d", forecast.Icon);
    }

    [Fact]
    public void BuildForDate_UsesNightIcon_WhenDayIconIsAbsent()
    {
        var response = Samples.Response(
            Samples.MoscowOffsetSeconds,
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 0), weatherId: 500, description: "дождь", icon: "10n"),
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 3), weatherId: 500, description: "дождь", icon: "10n"));

        var forecast = ForecastBuilder.BuildForDate(response, Date);

        Assert.NotNull(forecast);
        Assert.Equal("10n", forecast.Icon);
        Assert.Equal("дождь", forecast.Description);
    }

    [Fact]
    public void BuildForDate_PrefersLargerConditionId_WhenCountsAreEqual()
    {
        var response = Samples.Response(
            Samples.MoscowOffsetSeconds,
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 3), weatherId: 500, description: "дождь", icon: "10d"),
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 6), weatherId: 800, description: "ясно", icon: "01d"));

        var forecast = ForecastBuilder.BuildForDate(response, Date);

        Assert.NotNull(forecast);
        // Результат должен быть предсказуемым: при равной частоте побеждает больший идентификатор.
        Assert.Equal("ясно", forecast.Description);
    }

    [Fact]
    public void BuildForDate_FallsBackToMain_WhenDescriptionIsEmpty()
    {
        var response = Samples.Response(
            Samples.MoscowOffsetSeconds,
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 3), weatherId: 500, main: "Rain", description: string.Empty));

        var forecast = ForecastBuilder.BuildForDate(response, Date);

        Assert.NotNull(forecast);
        Assert.Equal("Rain", forecast.Description);
    }

    [Fact]
    public void BuildForDate_UsesPlaceholder_WhenWeatherListIsEmpty()
    {
        var entry = Samples.Entry(Samples.UnixUtc(2026, 9, 26, 3));
        entry.Weather = [];
        var response = Samples.Response(Samples.MoscowOffsetSeconds, entry);

        var forecast = ForecastBuilder.BuildForDate(response, Date);

        Assert.NotNull(forecast);
        Assert.Equal("нет данных", forecast.Description);
        Assert.Equal("01d", forecast.Icon);
    }

    [Fact]
    public void BuildForDate_ReturnsNull_WhenThereAreNoEntriesForTheDay()
    {
        var response = Samples.Response(
            Samples.MoscowOffsetSeconds,
            Samples.Entry(Samples.UnixUtc(2026, 9, 25, 0)),
            Samples.Entry(Samples.UnixUtc(2026, 9, 28, 0)));

        Assert.Null(ForecastBuilder.BuildForDate(response, Date));
    }

    [Fact]
    public void BuildForDate_ReturnsNull_ForEmptyList() =>
        Assert.Null(ForecastBuilder.BuildForDate(Samples.Response(Samples.MoscowOffsetSeconds), Date));

    [Fact]
    public void BuildForDate_UsesUtc_WhenCitySectionIsMissing()
    {
        var response = new ForecastResponseDto
        {
            List = [Samples.Entry(Samples.UnixUtc(2026, 9, 26, 3), minTemperature: 1, maxTemperature: 2)],
        };

        var forecast = ForecastBuilder.BuildForDate(response, Date);

        Assert.NotNull(forecast);
        Assert.Equal(1, forecast.MinTemperature);
    }

    [Fact]
    public void BuildForDate_Throws_ForNullResponse() =>
        Assert.Throws<ArgumentNullException>(() => ForecastBuilder.BuildForDate(null!, Date));

    [Fact]
    public void GetPrecipitation_SumsRainAndSnow()
    {
        var entry = Samples.Entry(Samples.UnixUtc(2026, 9, 26, 3), rain: 0.5, snow: 0.3);

        Assert.Equal(0.8, ForecastBuilder.GetPrecipitation(entry));
        Assert.Equal(0, ForecastBuilder.GetPrecipitation(Samples.Entry(Samples.UnixUtc(2026, 9, 26, 3))));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.05, false)]
    [InlineData(0.06, true)]
    [InlineData(2.4, true)]
    public void HasPrecipitation_UsesThreshold(double precipitation, bool expected)
    {
        var forecast = Samples.Daily(Date) with { TotalPrecipitationMm = precipitation };

        Assert.Equal(expected, forecast.HasPrecipitation);
    }
}
