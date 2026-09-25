using System.Globalization;

namespace WeatherBot.Services;

/// <summary>Тексты сообщений бота (разметка Telegram HTML).</summary>
public static class BotMessages
{
    public const string AskPassword =
        "🔐 <b>Бот работает по паролю</b>\n\nПришлите пароль одним сообщением, чтобы получить доступ.";

    public const string WrongPassword =
        "❌ Неверный пароль. Попробуйте ещё раз.";

    public const string AskCity =
        "🏙 Пришлите название города одним сообщением.\n\nНапример: <b>Москва</b> или <b>Париж, FR</b>.";

    public const string AccessGranted =
        "✅ <b>Доступ открыт</b>\n\n" +
        "Пришлите название города — и я каждый день в 18:00 по времени этого города буду отправлять " +
        "прогноз погоды на завтра.\n\n" +
        "Например: <b>Москва</b>";

    public const string LoggedOut =
        "🔓 Вы вышли. Пришлите пароль, чтобы снова пользоваться ботом.";

    public const string Subscribed =
        "🔔 Ежедневная рассылка включена. Прогноз на завтра придёт в 18:00 по времени вашего города.";

    public const string Unsubscribed =
        "🔕 Ежедневная рассылка выключена. Включить обратно — /subscribe.";

    public const string OnlyTextSupported =
        "📝 Я понимаю только текстовые сообщения. Пришлите название города или команду /help.";

    public const string Help =
        "ℹ️ <b>Справка</b>\n\n" +
        "/city &lt;город&gt; — выбрать или изменить город\n" +
        "· можно просто прислать название города сообщением\n" +
        "/tomorrow — прогноз на завтра прямо сейчас\n" +
        "/status — текущие настройки\n" +
        "/stop — выключить ежедневную рассылку\n" +
        "/subscribe — включить ежедневную рассылку\n" +
        "/logout — выйти (потребуется пароль)\n" +
        "/chatid — показать идентификатор чата\n" +
        "/help — эта справка";

    public static string CityNotFound(string query) =>
        $"😕 Не удалось найти город <b>{Escape(query)}</b>.\n\n" +
        "Проверьте название или уточните страну: <i>Москва, RU</i>, <i>Алматы, KZ</i>, <i>Berlin, DE</i>.";

    public static string CitySaved(string displayName, string localTime, string utcOffset) =>
        $"✅ Город сохранён: <b>{Escape(displayName)}</b>\n" +
        $"🕒 Сейчас там {localTime} (UTC{utcOffset}).\n\n" +
        "Прогноз на завтра пришлю в 18:00 по местному времени города.\n" +
        "Посмотреть прямо сейчас — /tomorrow.";

    public static string CitySavedWithoutForecast(string displayName, string reason) =>
        $"✅ Город сохранён: <b>{Escape(displayName)}</b>\n\n" +
        $"⚠️ Прогноз пока недоступен: {Escape(reason)}\n" +
        "Попробуйте позже — рассылка всё равно включена.";

    public static string ForecastError(string reason) =>
        $"😔 Не удалось получить прогноз: {Escape(reason)}\n\nПопробуйте позже.";

    public static string Status(
        bool isAuthorized,
        string? cityDisplayName,
        string? localTimeWithOffset,
        bool isSubscribed,
        int sendHour,
        DateOnly? lastSentForecastDate)
    {
        var lastSent = lastSentForecastDate is null
            ? "ещё не отправлялся"
            : $"на {lastSentForecastDate.Value:dd.MM.yyyy}";

        return "👤 <b>Ваши настройки</b>\n" +
            $"🔐 Доступ: {(isAuthorized ? "открыт" : "закрыт")}\n" +
            $"🏙 Город: {(cityDisplayName is null ? "не выбран" : Escape(cityDisplayName))}\n" +
            $"🕒 Сейчас в городе: {localTimeWithOffset ?? "—"}\n" +
            $"🔔 Ежедневная рассылка в {sendHour}:00: {(isSubscribed ? "включена" : "выключена")}\n" +
            $"📤 Последний прогноз: {lastSent}";
    }

    public static string ChatId(long chatId) =>
        $"🆔 Идентификатор этого чата: <code>{chatId.ToString(CultureInfo.InvariantCulture)}</code>";

    public static string UnknownCommand(string command) =>
        $"🤷 Команда <b>{Escape(command)}</b> не поддерживается.\n\n{Help}";

    /// <summary>Экранирует текст для разметки Telegram HTML.</summary>
    public static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);
}
