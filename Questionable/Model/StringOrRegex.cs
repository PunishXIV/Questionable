using Questionable.Functions;
using System;
using System.Text.RegularExpressions;

namespace Questionable.Model;

internal sealed class StringOrRegex
{
    private readonly Regex? _regex;
    private readonly string? _stringValue;

    public StringOrRegex(Regex? regex)
    {
        ArgumentNullException.ThrowIfNull(regex);
        _regex = regex;
        _stringValue = null;
    }

    public StringOrRegex(string? str)
    {
        ArgumentNullException.ThrowIfNull(str);
        _regex = null;
        _stringValue = str;
    }

    public bool IsMatch(string other)
    {
        if (_regex != null)
            return _regex.IsMatch(other);
        else
            return GameFunctions.GameStringEquals(_stringValue, other);
    }

    public string? GetString()
    {
        if (_stringValue == null)
            throw new InvalidOperationException();

        return _stringValue;
    }

    public override string? ToString() => _regex?.ToString() ?? _stringValue;
}
