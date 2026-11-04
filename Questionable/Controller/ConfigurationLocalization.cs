namespace Questionable.Controller;

internal sealed class ConfigurationLocalization : IDisposable
{
    private readonly Configuration _configuration;
    private readonly Action<string> _applyLanguage;
    private readonly object _lock = new();
    private string? _appliedLanguage;
    private bool _applying;
    private bool _disposed;

    public ConfigurationLocalization(Configuration configuration, Action<string> applyLanguage)
    {
        _configuration = configuration;
        _applyLanguage = applyLanguage;
        _configuration.LanguageChanged += OnLanguageChanged;
        try
        {
            Synchronize();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnLanguageChanged(string language) => Synchronize();

    private void Synchronize()
    {
        lock (_lock)
        {
            if (_disposed || _applying)
                return;

            _applying = true;
            try
            {
                while (!_disposed)
                {
                    string language = _configuration.General.Language;
                    if (string.Equals(_appliedLanguage, language, StringComparison.OrdinalIgnoreCase))
                        break;

                    _applyLanguage(language);
                    _appliedLanguage = language;
                    // A profile can change while localization loads, including from the callback itself.
                }
            }
            finally
            {
                _applying = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _configuration.LanguageChanged -= OnLanguageChanged;
        }
    }
}
