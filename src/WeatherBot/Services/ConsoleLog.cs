namespace WeatherBot.Services;

/// <summary>Вывод в консоль: эти строки видны в логе запуска GitHub Actions.</summary>
public static class ConsoleLog
{
    public static void Info(string message) => Write("INFO", message);

    public static void Warning(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception exception) =>
        Write("ERROR", $"{message} ({exception.GetType().Name}: {exception.Message})");

    private static void Write(string level, string message) =>
        Console.WriteLine($"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] [{level}] {message}");
}
