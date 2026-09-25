using System.Security.Cryptography;
using System.Text;

namespace WeatherBot.Security;

/// <summary>Проверка пароля пользователя.</summary>
public static class PasswordChecker
{
    /// <summary>
    /// Сравнивает введённый пароль с паролем из секрета. Оба значения хешируются SHA-256,
    /// а сами хеши сравниваются за постоянное время, чтобы сравнение не зависело от длины и содержимого пароля.
    /// </summary>
    public static bool IsMatch(string? candidate, string expected)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Trim()));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));

        return CryptographicOperations.FixedTimeEquals(candidateHash, expectedHash);
    }
}
