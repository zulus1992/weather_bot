using WeatherBot.Models;
using WeatherBot.Models.WeatherApi;
using WeatherBot.Services;

namespace WeatherBot.Tests;

public sealed class ForecastBuilderTests
{
    private static readonly DateOnly Date = new(2026, 9, 26);

    /// <summary>Сутки 26 сентября: сводка дня и три часовые записи (Москва, UTC+3).</summary>
    private static ForecastResponseDto ResponseWithHours() => Samples.Response(
        Samples.Day(
            "2026-09-26",
            minTemperature: 8d,
            maxTemperature: 18.9d,
            maxWindSpeedKph: 30.24d,
            totalPrecipitationMm: 0d,
            averageHumidity: 55,
            dailyChanceOfRain: 70,
            conditionCode: 1183,
            condition: "лёгкий дождь",
            hours:
            [
                // 00:00 26 сентября по Москве — ночь.
                Samples.HourEntry(
                    "2026-09-26 00:00",
                    temperature: 8d,
                    feelsLike: 6d,
                    windKph: 14.4d,
                    gustKph: 32.4d,
                    precipitationMm: 0.5d,
                    chanceOfRain: 20,
                    cloudiness: 80,
                    conditionCode: 1183,
                    condition: "лёгкий дождь",
                    isDay: 0),
                // 09:00 26 сентября.
                Samples.HourEntry(
                    "2026-09-26 09:00",
                    temperature: 16.4d,
                    feelsLike: 9.1d,
                    windKph: 30.24d,
                    precipitationMm: 1.2d,
                    chanceOfRain: 70,
                    cloudiness: 30,
                    conditionCode: 1183,
                    condition: "лёгкий дождь"),
                // 18:00 26 сентября.
                Samples.HourEntry(
                    "2026-09-26 18:00",
                    temperature: 12.2d,
                    feelsLike: 11d,
                    windKph: 21.96d,
                    gustKph: 55.44d,
                    precipitationMm: 0.3d,
                    chanceOfRain: 30,
                    cloudiness: 20,
                    conditionCode: 1000,
                    condition: "ясно"),
            ]),
        // Следующие сутки — чтобы проверить выбор нужной даты.
        Samples.Day("2026-09-27", minTemperature: 5d, maxTemperature: 7d));

    [Fact]
    public void GetLocalDate_AppliesCityOffset() =>
        Assert.Equal(
            Date,
            ForecastBuilder.GetLocalDate(Samples.UnixUtc(2026, 9, 25, 21), Samples.MoscowOffsetSeconds));

    [Theory]
    [InlineData("2026-09-26", "2026-09-26")]
    [InlineData("2026-01-01", "2026-01-01")]
    public void ParseDate_ParsesIsoDate(string value, string expected) =>
        Assert.Equal(DateOnly.ParseExact(expected, "yyyy-MM-dd"), ForecastBuilder.ParseDate(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("26.09.2026")]
    [InlineData("2026-09-26 09:00")]
    public void ParseDate_ReturnsNull_ForBadValue(string? value) =>
        Assert.Null(ForecastBuilder.ParseDate(value));

    [Fact]
    public void GetLocalToday_UsesLocalTimeOfCity()
    {
        var response = Samples.Response(Samples.Day("2026-09-26"));

        Assert.Equal(new DateOnly(2026, 9, 25), ForecastBuilder.GetLocalToday(response));
    }

    [Fact]
    public void GetLocalToday_ReturnsNull_WhenLocalTimeIsMissing()
    {
        var response = Samples.Response(Samples.Day("2026-09-26"));
        response.Location!.LocalTime = null;

        Assert.Null(ForecastBuilder.GetLocalToday(response));
    }

    [Fact]
    public void ResolveTimeZoneOffsetSeconds_UsesTimeZoneIdentifier()
    {
        var response = Samples.Response(Samples.Day("2026-09-26"));

        Assert.Equal(
            Samples.MoscowOffsetSeconds,
            ForecastBuilder.ResolveTimeZoneOffsetSeconds(response, Date, defaultTimeZoneOffsetSeconds: 0));
    }

    [Fact]
    public void ResolveTimeZoneOffsetSeconds_FallsBackToHourTimestamps()
    {
        var response = Samples.Response(Samples.Day(
            "2026-09-26",
            hours:
            [
                // Местное время 09:00, а метка времени — 06:00 UTC: смещение +3 часа.
                Samples.HourEntry("2026-09-26 09:00", timeUtcEpoch: Samples.UnixUtc(2026, 9, 26, 6)),
            ]));
        response.Location!.TimeZoneId = null;

        Assert.Equal(
            Samples.MoscowOffsetSeconds,
            ForecastBuilder.ResolveTimeZoneOffsetSeconds(response, Date, defaultTimeZoneOffsetSeconds: 0));
    }

    [Fact]
    public void ResolveTimeZoneOffsetSeconds_UsesDefault_WhenNothingIsKnown()
    {
        var response = Samples.Response(Samples.Day("2026-09-26"));
        response.Location!.TimeZoneId = null;

        Assert.Equal(7200, ForecastBuilder.ResolveTimeZoneOffsetSeconds(response, Date, defaultTimeZoneOffsetSeconds: 7200));
    }

    [Fact]
    public void BuildForDate_AggregatesDaySummaryAndHours()
    {
        var forecast = ForecastBuilder.BuildForDate(ResponseWithHours(), Date, Samples.MoscowOffsetSeconds);

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
        Assert.Equal("лёгкий дождь", forecast.Description);
        Assert.Equal(1183, forecast.ConditionCode);
        Assert.True(forecast.HasPrecipitation);
    }

    [Fact]
    public void BuildForDate_BuildsHourlyForecast()
    {
        var forecast = ForecastBuilder.BuildForDate(ResponseWithHours(), Date, Samples.MoscowOffsetSeconds);

        Assert.NotNull(forecast);
        Assert.Equal(3, forecast.Hours.Count);

        var first = forecast.Hours[0];
        Assert.Equal(0, first.Hour);
        Assert.False(first.IsDay);
        Assert.Equal("лёгкий дождь", first.Description);
        Assert.Equal(4, first.WindSpeed);
        Assert.Equal(9, first.WindGust);
        Assert.Equal(20, first.PrecipitationProbability);
        Assert.True(first.HasPrecipitation);
        Assert.Equal(1183, first.ConditionCode);

        // Порывов нет — сервис отдал нули.
        Assert.Null(forecast.Hours[1].WindGust);

        var last = forecast.Hours[^1];
        Assert.Equal(18, last.Hour);
        Assert.True(last.IsDay);
        Assert.Equal("ясно", last.Description);
        Assert.Equal(15.4, last.WindGust);
    }

    [Fact]
    public void BuildForDate_OrdersHoursByTime()
    {
        var day = Samples.Day(
            "2026-09-26",
            hours:
            [
                Samples.HourEntry("2026-09-26 18:00"),
                Samples.HourEntry("2026-09-26 06:00"),
                Samples.HourEntry("2026-09-26 12:00"),
            ]);

        var forecast = ForecastBuilder.BuildForDate(
            Samples.Response(day),
            Date,
            Samples.MoscowOffsetSeconds);

        Assert.NotNull(forecast);
        Assert.Equal([6, 12, 18], forecast.Hours.Select(hour => hour.Hour));
    }

    [Fact]
    public void BuildForDate_UsesDaySummary_WhenThereAreNoHourlyEntries()
    {
        var forecast = ForecastBuilder.BuildForDate(
            Samples.Response(Samples.Day("2026-09-26", totalPrecipitationMm: 4.5d, maxWindSpeedKph: 18d)),
            Date,
            Samples.MoscowOffsetSeconds);

        Assert.NotNull(forecast);
        Assert.Empty(forecast.Hours);
        Assert.Equal(4.5, forecast.TotalPrecipitationMm);
        Assert.Equal(5, forecast.MaxWindSpeed);
        Assert.Null(forecast.MaxWindGust);
        // «Ощущается как» берётся из сводки дня, если часовых записей нет.
        Assert.Equal(8, forecast.MinFeelsLike);
        Assert.Equal(18, forecast.MaxFeelsLike);
    }

    [Fact]
    public void BuildForDate_UsesPlaceholder_WhenConditionIsMissing()
    {
        var forecast = ForecastBuilder.BuildForDate(
            Samples.Response(Samples.Day("2026-09-26", conditionCode: 0)),
            Date,
            Samples.MoscowOffsetSeconds);

        Assert.NotNull(forecast);
        Assert.Equal("нет данных", forecast.Description);
        Assert.Equal(ForecastBuilder.UnknownConditionCode, forecast.ConditionCode);
    }

    [Fact]
    public void BuildForDate_UsesHourFallback_WhenTimeStringIsMissing()
    {
        var day = Samples.Day(
            "2026-09-26",
            hours:
            [
                new HourDto
                {
                    Time = null,
                    TimeEpoch = Samples.UnixUtc(2026, 9, 26, 6),
                    Temperature = 12,
                    FeelsLike = 11,
                    Condition = new ConditionDto { Code = 1000, Text = "ясно" },
                    WindSpeed = 18,
                    IsDay = 1,
                },
            ]);

        var forecast = ForecastBuilder.BuildForDate(
            Samples.Response(day),
            Date,
            Samples.MoscowOffsetSeconds);

        Assert.NotNull(forecast);
        // 06:00 UTC при смещении +3 часа — это 09:00 местного времени.
        Assert.Equal(9, Assert.Single(forecast.Hours).Hour);
    }

    [Fact]
    public void BuildForDate_ReturnsNull_WhenThereAreNoDaysForTheDate() =>
        Assert.Null(ForecastBuilder.BuildForDate(
            ResponseWithHours(),
            new DateOnly(2026, 9, 30),
            Samples.MoscowOffsetSeconds));

    [Fact]
    public void BuildForDate_ReturnsNull_ForEmptyResponse() =>
        Assert.Null(ForecastBuilder.BuildForDate(new ForecastResponseDto(), Date, Samples.MoscowOffsetSeconds));

    [Fact]
    public void BuildForDate_Throws_ForNullResponse() =>
        Assert.Throws<ArgumentNullException>(
            () => ForecastBuilder.BuildForDate(null!, Date, Samples.MoscowOffsetSeconds));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(18, 5)]
    [InlineData(27, 7.5)]
    public void KilometersPerHourToMetersPerSecond_Converts(double kph, double expected) =>
        Assert.Equal(expected, ForecastBuilder.KilometersPerHourToMetersPerSecond(kph));

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

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.05, false)]
    [InlineData(0.06, true)]
    public void HourlyHasPrecipitation_UsesThreshold(double precipitation, bool expected)
    {
        var hour = Samples.Hourly(12, precipitationMm: precipitation);

        Assert.Equal(expected, hour.HasPrecipitation);
    }
}
