using WeatherBot.Models;

namespace WeatherBot.Tests;

public sealed class BotStateTests
{
    [Fact]
    public void GetOrCreate_AddsUserAndKeepsProfileData()
    {
        var state = new BotState();

        var user = state.GetOrCreate(100, "vasya", "Василий");

        Assert.Equal(100, user.ChatId);
        Assert.Equal("vasya", user.UserName);
        Assert.Equal("Василий", user.FirstName);
        Assert.True(user.IsSubscribed);
        Assert.False(user.IsAuthorized);
        Assert.Same(user, Assert.Single(state.Users));
    }

    [Fact]
    public void GetOrCreate_ReturnsSameUser_ForSameChat()
    {
        var state = new BotState();

        var first = state.GetOrCreate(100, "vasya", "Василий");
        first.City = "Москва";
        var second = state.GetOrCreate(100, null, null);

        Assert.Same(first, second);
        Assert.Single(state.Users);
        Assert.Equal("Москва", second.City);
        Assert.Equal("vasya", second.UserName);
        Assert.Equal("Василий", second.FirstName);
    }

    [Fact]
    public void GetOrCreate_UpdatesProfileNames()
    {
        var state = new BotState();

        state.GetOrCreate(100, "old", "Старый");
        var user = state.GetOrCreate(100, "new", "Новый");

        Assert.Equal("new", user.UserName);
        Assert.Equal("Новый", user.FirstName);
    }

    [Fact]
    public void Find_ReturnsNull_ForUnknownChat()
    {
        var state = new BotState();

        Assert.Null(state.Find(1));
        state.GetOrCreate(2, null, null);
        Assert.NotNull(state.Find(2));
        Assert.Null(state.Find(3));
    }

    private static BotUser Subscriber(long chatId, bool authorized = true, bool subscribed = true, bool withCity = true) =>
        new()
        {
            ChatId = chatId,
            IsAuthorized = authorized,
            IsSubscribed = subscribed,
            City = withCity ? "Москва" : null,
            Latitude = withCity ? 55.75 : null,
            Longitude = withCity ? 37.61 : null,
        };

    [Fact]
    public void GetSubscribers_ReturnsOnlyReadyUsers()
    {
        var state = new BotState
        {
            Users =
            [
                Subscriber(1),
                Subscriber(2, authorized: false),
                Subscriber(3, subscribed: false),
                Subscriber(4, withCity: false),
                new BotUser { ChatId = 5, IsAuthorized = true, IsSubscribed = true, City = "Казань" },
            ],
        };

        var subscribers = state.GetSubscribers().Select(user => user.ChatId).ToList();

        Assert.Equal([1], subscribers);
    }

    [Fact]
    public void GetSubscribers_IsEmpty_ForNewState() =>
        Assert.Empty(new BotState().GetSubscribers());
}
