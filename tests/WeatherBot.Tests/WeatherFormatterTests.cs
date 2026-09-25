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
    [InlineData("01d", "☀️")]
    [InlineData("02d", "🌤️")]
    [InlineData("03d", "⛅")]
    [InlineData("04d", "☁️")]
    [InlineData("09d", "🌧️")]
    [InlineData("10d", "🌦️")]
    [InlineData("10n", "🌧️")]
    [InlineData("11d", "⛈️")]
    [InlineData("13d", "❄️")]
    [InlineData("50d", "🌫️")]
    [InlineData("ab", "🌡")]
    [InlineData("x", "🌡")]
    [InlineData(null, "☀️")]
    public void GetEmoji_MapsOpenWeatherIconCodes(string? icon, string expected) =>
        Assert.Equal(expected, WeatherFormatter.GetEmoji(icon));

    [Fact]
    public void ToHtml_FormatsFullForecast()
    {
        var html = WeatherFormatter.ToHtml(Forecast());

        Assert.Contains("<b>Погода на завтра — 26 сентября (суббота)</b>", html, StringComparison.Ordinal);
        Assert.Contains("📍 Москва, RU", html, StringComparison.Ordinal);
        Assert.Contains("🔻 Минимум: +6 °C", html, StringComparison.Ordinal);
        Assert.Contains("🔺 Максимум: +15 °C", html, StringComparison.Ordinal);
        Assert.Contains("🌬 Ощущается как: +4…+13 °C", html, StringComparison.Ordinal);
        Assert.Contains("Характер погоды: переменная облачность", html, StringComparison.Ordinal);
        Assert.Contains("🌧 Вероятность осадков: 40% (около 1.2 мм)", html, StringComparison.Ordinal);
        Assert.Contains("💧 Влажность: 63%", html, StringComparison.Ordinal);
        Assert.Contains("💨 Ветер: до 7.5 м/с, порывы до 12.4 м/с", html, StringComparison.Ordinal);
        Assert.Contains("<i>Источник: OpenWeatherMap</i>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToPlainText_ContainsNoMarkup()
    {
        var text = WeatherFormatter.ToPlainText(Forecast());

        Assert.Contains("Погода на завтра — 26 сентября (суббота)", text, StringComparison.Ordinal);
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
    public void ToHtml_EscapesCityAndDescription()
    {
        var daily = Samples.Daily(Date) with { Description = "гроза <сильная>" };
        var html = WeatherFormatter.ToHtml(Forecast(daily, city: "A&B <City>", country: null));

        Assert.Contains("A&amp;B &lt;City&gt;", html, StringComparison.Ordinal);
        Assert.Contains("гроза &lt;сильная&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("A&B", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToPlainText_KeepsRawCharacters()
    {
        var text = WeatherFormatter.ToPlainText(Forecast(city: "A&B", country: null));

        Assert.Contains("📍 A&B", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_Throws_ForNullForecast() =>
        Assert.Throws<ArgumentNullException>(() => WeatherFormatter.ToHtml(null!));
}
