namespace LLib.ImGui;

public interface IPersistableWindowConfig
{
    WindowConfig? WindowConfig { get; }

    void SaveWindowConfig();
}

public interface IPersistableWindowConfig<out T> : IPersistableWindowConfig
    where T : WindowConfig
{
    new T? WindowConfig { get; }

    WindowConfig? IPersistableWindowConfig.WindowConfig => WindowConfig;
}
