using WeatherBot.Models;
using WeatherBot.Services;

namespace WeatherBot.Tests;

public sealed class WeatherFormatterTests
{
    private static readonly DateOnly Date = new(2026, 9, 26);

    private static CityForecast Forecast(DailyForecast? daily = null, string city = "Москва", string? country = "RU") =>
        Samples.TomorrowForecast() with
        {
            City = city,
            CountryCode = country,
            Forecast = daily ?? Samples.Daily(Date),
        };

    /// <summary>Почасовой прогноз для проверки блока «По часам»: ночь, дождь днём, ясно вечером.</summary>
    private static DailyForecast DailyWithHours() => Samples.Daily(
        Date,
        hours:
        [
            Samples.Hourly(0, temperature: 6, feelsLike: 4, windSpeed: 3),
            Samples.Hourly(
                13,
                temperature: 15.4,
                feelsLike: 14.2,
                conditionCode: 1183,
                description: "лёгкий дождь",
                windSpeed: 4.2,
                precipitationProbability: 60,
                precipitationMm: 0.4),
            Samples.Hourly(
                22,
                temperature: 8,
                feelsLike: 6,
                windSpeed: 2,
                isDay: false),
        ]);

    [Theory]
    [InlineData("2026-09-21", "21 сентября (понедельник)")]
    [InlineData("2026-09-26", "26 сентября (суббота)")]
    [InlineData("2026-09-27", "27 сентября (воскресенье)")]
    [InlineData("2026-01-01", "1 января (четверг)")]
    public void FormatDate_UsesRussianMonthAndWeekDay(string date, string expected) =>
        Assert.Equal(expected, WeatherFormatter.FormatDate(DateOnly.ParseExact(date, "yyyy-MM-dd")));

    [Theory]
    [InlineData(6.4, "+6")]
    [InlineData(14.6, "+15")]
    [InlineData(0.0, "0")]
    [InlineData(-0.4, "0")]
    [InlineData(-3.5, "-4")]
    [InlineData(-20.0, "-20")]
    public void FormatTemperature_RoundsAndSigns(double value, string expected) =>
        Assert.Equal(expected, WeatherFormatter.FormatTemperature(value));

    [Theory]
    [InlineData(1000, true, "☀️")]
    [InlineData(1000, false, "🌙")]
    [InlineData(1003, true, "🌤️")]
    [InlineData(1003, false, "☁️")]
    [InlineData(1006, true, "⛅")]
    [InlineData(1009, true, "☁️")]
    [InlineData(1030, true, "🌫️")]
    [InlineData(1063, true, "🌦️")]
    [InlineData(1150, true, "🌦️")]
    [InlineData(1183, true, "🌧️")]
    [InlineData(1195, true, "🌧️")]
    [InlineData(1204, true, "🌨️")]
    [InlineData(1213, true, "❄️")]
    [InlineData(1087, true, "⛈️")]
    [InlineData(ForecastBuilder.UnknownConditionCode, true, "🌡")]
    [InlineData(9999, true, "🌡")]
    public void GetEmoji_MapsWeatherApiConditionCodes(int code, bool isDay, string expected) =>
        Assert.Equal(expected, WeatherFormatter.GetEmoji(code, isDay));

    [Fact]
    public void ToHtml_FormatsFullForecast()
    {
        var html = WeatherFormatter.ToHtml(Forecast());

        Assert.Contains("🌤️ <b>Погода на завтра — 26 сентября (суббота)</b>", html, StringComparison.Ordinal);
        Assert.Contains("📍 Москва, RU", html, StringComparison.Ordinal);
        Assert.Contains("🔻 Минимум: +6 °C", html, StringComparison.Ordinal);
        Assert.Contains("🔺 Максимум: +15 °C", html, StringComparison.Ordinal);
        Assert.Contains("🌬 Ощущается как: +4…+13 °C", html, StringComparison.Ordinal);
        Assert.Contains("Характер погоды: переменная облачность", html, StringComparison.Ordinal);
        Assert.Contains("🌧 Вероятность осадков: 40% (около 1.2 мм)", html, StringComparison.Ordinal);
        Assert.Contains("💧 Влажность: 63%", html, StringComparison.Ordinal);
        Assert.Contains("💨 Ветер: до 7.5 м/с, порывы до 12.4 м/с", html, StringComparison.Ordinal);
        Assert.Contains("<i>Источник: WeatherAPI.com</i>", html, StringComparison.Ordinal);
        // Часовых записей нет — блок «По часам» не выводится.
        Assert.DoesNotContain("По часам", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_FormatsHourlyForecast()
    {
        var html = WeatherFormatter.ToHtml(Forecast(DailyWithHours()));

        Assert.Contains("<b>По часам</b>", html, StringComparison.Ordinal);
        Assert.Contains(
            "00:00 ☀️ +6° (ощущ. +4°) · ясно · ветер 3 м/с · осадки 0%",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "13:00 🌧️ +15° (ощущ. +14°) · лёгкий дождь · ветер 4.2 м/с · осадки 60% (0.4 мм)",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "22:00 🌙 +8° (ощущ. +6°) · ясно · ветер 2 м/с · осадки 0%",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToPlainText_ContainsNoMarkup()
    {
        var text = WeatherFormatter.ToPlainText(Forecast(DailyWithHours()));

        Assert.Contains("Погода на завтра — 26 сентября (суббота)", text, StringComparison.Ordinal);
        Assert.Contains("По часам", text, StringComparison.Ordinal);
        Assert.Contains("Источник: WeatherAPI.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("</b>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_SkipsPrecipitationAmount_WhenThereIsNoPrecipitation()
    {
        var daily = Samples.Daily(Date) with { TotalPrecipitationMm = 0.02, MaxPrecipitationProbability = 0 };
        var html = WeatherFormatter.ToHtml(Forecast(daily));

        Assert.Contains("🌧 Вероятность осадков: 0%", html, StringComparison.Ordinal);
        Assert.DoesNotContain("около", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_SkipsGust_WhenThereIsNoGustData()
    {
        var daily = Samples.Daily(Date) with { MaxWindGust = null };
        var html = WeatherFormatter.ToHtml(Forecast(daily));

        Assert.Contains("💨 Ветер: до 7.5 м/с", html, StringComparison.Ordinal);
        Assert.DoesNotContain("порывы", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_SkipsGust_WhenGustIsNotStrongerThanWind()
    {
        var daily = Samples.Daily(Date) with { MaxWindSpeed = 12, MaxWindGust = 11.1 };
        var html = WeatherFormatter.ToHtml(Forecast(daily));

        Assert.Contains("💨 Ветер: до 12 м/с", html, StringComparison.Ordinal);
        Assert.DoesNotContain("порывы", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_EscapesCityAndDescription()
    {
        var daily = Samples.Daily(Date) with { Description = "гроза <сильная>" };
        var html = WeatherFormatter.ToHtml(Forecast(daily, city: "A&B <City>", country: null));

        Assert.Contains("A&amp;B &lt;City&gt;", html, StringComparison.Ordinal);
        Assert.Contains("гроза &lt;сильная&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("A&B", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_EscapesHourlyDescription()
    {
        var daily = Samples.Daily(
            Date,
            hours: [Samples.Hourly(12, description: "гроза <сильная>")]);

        var html = WeatherFormatter.ToHtml(Forecast(daily));

        Assert.Contains("· гроза &lt;сильная&gt; ·", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<сильная>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToPlainText_KeepsRawCharacters()
    {
        var text = WeatherFormatter.ToPlainText(Forecast(city: "A&B", country: null));

        Assert.Contains("📍 A&B", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_DoesNotMentionSunTimes()
    {
        var html = WeatherFormatter.ToHtml(Forecast(DailyWithHours()));

        foreach (var fragment in new[] { "восход", "Восход", "закат", "Закат", "катат", "Катат" })
        {
            Assert.DoesNotContain(fragment, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Format_Throws_ForNullForecast() =>
        Assert.Throws<ArgumentNullException>(() => WeatherFormatter.ToHtml(null!));
}
