using WeatherBot.Models;
using WeatherBot.Services;

namespace WeatherBot.Tests;

public sealed class DailyScheduleTests
{
    private const int Moscow = 3 * 3600;
    private const int NewYork = -5 * 3600;

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void GetLocalNow_UsesUserTimeZone()
    {
        var localNow = DailySchedule.GetLocalNow(Utc(2026, 9, 25, 15), Moscow, NewYork);

        Assert.Equal(new DateTimeOffset(2026, 9, 25, 18, 0, 0, TimeSpan.FromHours(3)), localNow);
    }

    [Fact]
    public void GetLocalNow_FallsBackToDefaultTimeZone()
    {
        var localNow = DailySchedule.GetLocalNow(Utc(2026, 9, 25, 15), null, Moscow);

        Assert.Equal(18, localNow.Hour);
        Assert.Equal(TimeSpan.FromHours(3), localNow.Offset);
    }

    [Theory]
    // 01:00 по Москве 26 сентября — «завтра» уже 27 сентября.
    [InlineData(Moscow, 22, 27)]
    // 17:00 в Нью-Йорке 25 сентября — «завтра» ещё 26 сентября.
    [InlineData(NewYork, 22, 26)]
    public void GetTargetDate_UsesCityTimeZone(int offsetSeconds, int utcHour, int expectedDay)
    {
        var target = DailySchedule.GetTargetDate(Utc(2026, 9, 25, utcHour), offsetSeconds, Moscow);

        Assert.Equal(new DateOnly(2026, 9, expectedDay), target);
    }

    [Fact]
    public void FormatDate_ProducesRoundTripFormat() =>
        Assert.Equal("2026-09-26", DailySchedule.FormatDate(new DateOnly(2026, 9, 26)));

    private static BotUser User(string? lastSentDate = null, int? offset = Moscow) => new()
    {
        ChatId = 42,
        IsAuthorized = true,
        IsSubscribed = true,
        City = "Москва",
        Latitude = 55.75,
        Longitude = 37.61,
        TimeZoneOffsetSeconds = offset,
        LastSentDate = lastSentDate,
    };

    [Fact]
    public void IsDue_IsFalse_BeforeSendHour()
    {
        // 17:59 по Москве.
        var due = DailySchedule.IsDue(User(), Utc(2026, 9, 25, 14, 59), Moscow, sendHour: 18);

        Assert.False(due);
    }

    [Fact]
    public void IsDue_IsTrue_AtSendHour()
    {
        // Ровно 18:00 по Москве.
        var due = DailySchedule.IsDue(User(), Utc(2026, 9, 25, 15), Moscow, sendHour: 18);

        Assert.True(due);
    }

    [Fact]
    public void IsDue_IsTrue_AfterSendHour()
    {
        // 23:00 по Москве: если запуск расписания был пропущен, прогноз всё равно уйдёт.
        var due = DailySchedule.IsDue(User(), Utc(2026, 9, 25, 20), Moscow, sendHour: 18);

        Assert.True(due);
    }

    [Fact]
    public void IsDue_IsFalse_WhenForecastAlreadySentToday()
    {
        var due = DailySchedule.IsDue(User("2026-09-26"), Utc(2026, 9, 25, 15), Moscow, sendHour: 18);

        Assert.False(due);
    }

    [Fact]
    public void IsDue_IsTrue_WhenLastSentDateIsAnotherDay()
    {
        var due = DailySchedule.IsDue(User("2026-09-25"), Utc(2026, 9, 25, 15), Moscow, sendHour: 18);

        Assert.True(due);
    }

    [Fact]
    public void IsDue_IgnoresChecks_WhenForced()
    {
        // Ручной запуск «Отправить сейчас»: 05:00 по Москве, прогноз уже отправлялся.
        var due = DailySchedule.IsDue(User("2026-09-26"), Utc(2026, 9, 25, 2), Moscow, sendHour: 18, force: true);

        Assert.True(due);
    }

    [Fact]
    public void IsDue_UsesDefaultTimeZone_WhenCityTimeZoneIsUnknown()
    {
        var user = User(lastSentDate: null, offset: null);

        // 14:00 UTC: при смещении по умолчанию +3 это 17:00 — ещё рано.
        Assert.False(DailySchedule.IsDue(user, Utc(2026, 9, 25, 14), Moscow, sendHour: 18));

        // 15:00 UTC: это уже 18:00 — пора.
        Assert.True(DailySchedule.IsDue(user, Utc(2026, 9, 25, 15), Moscow, sendHour: 18));
    }

    [Fact]
    public void IsDue_Throws_ForNullUser() =>
        Assert.Throws<ArgumentNullException>(() =>
            DailySchedule.IsDue(null!, Utc(2026, 9, 25, 15), Moscow, sendHour: 18));
}
