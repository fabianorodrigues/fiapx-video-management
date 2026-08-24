using System.Text.RegularExpressions;

namespace FiapX.VideoManagement.Application.Videos.Processamento;

internal static partial class SanitizadorEventoProcessamento
{
    private const int MaxErrorCodeLength = 100;
    private const int MaxErrorMessageLength = 1000;

    public static string NormalizeErrorCode(string errorCode)
    {
        var trimmed = errorCode.Trim();
        var normalized = ErrorCodeUnsafeCharacters().Replace(trimmed, "_").Trim('_');

        if (normalized.Length > MaxErrorCodeLength)
        {
            normalized = normalized[..MaxErrorCodeLength];
        }

        return normalized.ToUpperInvariant();
    }

    public static string SanitizeErrorMessage(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return "Processing failed.";
        }

        var withoutStackTrace = string.Join(
            ' ',
            errorMessage
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !StackTraceLine().IsMatch(line)));

        var sanitized = WindowsPath().Replace(withoutStackTrace, "[path]");
        sanitized = UnixPath().Replace(sanitized, "[path]");
        sanitized = SecretAssignment().Replace(sanitized, "$1=[redacted]");
        sanitized = Whitespace().Replace(sanitized, " ").Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Processing failed.";
        }

        return sanitized.Length <= MaxErrorMessageLength
            ? sanitized
            : sanitized[..MaxErrorMessageLength];
    }

    public static string ToUserSafeMessage(string errorCode) =>
        $"Could not process the video. Code: {errorCode}.";

    [GeneratedRegex("[^A-Za-z0-9_.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCodeUnsafeCharacters();

    [GeneratedRegex("^\\s*at\\s+.+", RegexOptions.CultureInvariant)]
    private static partial Regex StackTraceLine();

    [GeneratedRegex("[A-Za-z]:\\\\[^\\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPath();

    [GeneratedRegex("(?<!:)\\/(?:[^\\s\\/]+\\/)+[^\\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnixPath();

    [GeneratedRegex("(?i)\\b(password|secret|token|key)\\s*=\\s*[^\\s;]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignment();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
