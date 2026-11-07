using System.Text.RegularExpressions;
namespace Questionable.Domain;

internal sealed class StringOrRegex
{
    private readonly Regex? _regex;
    private readonly string? _text;

    public StringOrRegex(Regex? regex)
    {
        ArgumentNullException.ThrowIfNull(regex);
        _regex = regex;
        _text = null;
    }

    public StringOrRegex(string? text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _regex = null;
        _text = text;
    }

    public bool IsMatch(string other)
    {
        if (_regex != null)
            return _regex.IsMatch(other);

        return GameFunctions.GameStringEquals(_text, other);
    }

    public string? GetString() => _text;

    public override string? ToString() => _regex?.ToString() ?? _text;
}
