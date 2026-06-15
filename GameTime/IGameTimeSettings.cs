namespace GameTime;

public interface IGameTimeSettings
{
    string Get(string section, string key, string defaultValue);
    T Get<T>(string section, string key, T defaultValue);
    void Set(string section, string key, string value);
    void Flush();
    IEnumerable<string> GetSettings(string section);
}

#if GAMETIME_USE_SETTINGSFILE
public sealed class SettingsFileAdapter : IGameTimeSettings
{
    private readonly SettingsFile _file;
    public SettingsFileAdapter(SettingsFile file) => _file = file;
    public string Get(string section, string key, string defaultValue) => _file.Get(section, key, defaultValue);
    public T Get<T>(string section, string key, T defaultValue) => _file.Get<T>(section, key, defaultValue);
    public void Set(string section, string key, string value) => _file.Set(section, key, value);
    public void Flush() => _file.Flush();
    public IEnumerable<string> GetSettings(string section) => _file.GetSettings(section);
}
#endif