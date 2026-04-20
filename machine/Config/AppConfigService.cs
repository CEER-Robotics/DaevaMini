using System;
using System.IO;
using DaevaMini.Services;

namespace DaevaMini.Config;

public sealed class AppConfigService
{
    private static AppConfigService? _instance;
    private static readonly object _lockObject = new();
    private AppConfig? _config;
    private string _configFileName = "appsettings.yaml";
    private readonly LocalMachineStore _store = LocalMachineStore.Instance;

    public static AppConfigService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lockObject)
                {
                    if (_instance == null)
                    {
                        _instance = new AppConfigService();
                    }
                }
            }
            return _instance;
        }
    }

    private AppConfigService()
    {
    }

    public event EventHandler<AppConfigChangedEventArgs>? ConfigChanged;

    public string CurrentProfileKey => _configFileName;

    public AppConfig Config
    {
        get
        {
            if (_config == null)
            {
                LoadConfig();
            }
            return _config ?? throw new InvalidOperationException("Failed to load configuration");
        }
    }

    public void LoadConfig(string fileName = "appsettings.yaml")
    {
        _configFileName = fileName;
        try
        {
            _store.Initialize();
            SeedConfiguredYamlProfiles();

            _config = _store.LoadConfig(fileName);
            if (_config == null)
            {
                Console.WriteLine($"[AppConfigService] No stored config found for {fileName}, falling back to YAML");
                _config = LoadConfigFromYaml(fileName);
                PersistCurrentConfig(source: "yaml-fallback", raiseChanged: false);
            }

            Console.WriteLine("[AppConfigService] Configuration loaded successfully");
            MachineTelemetryService.Instance.RecordConfigEvent(fileName, "loaded", "sqlite");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppConfigService] Error loading config: {ex.Message}, using defaults");
            _config = new AppConfig();
            MachineTelemetryService.Instance.RecordConfigEvent(fileName, "load-failed", "exception", new
            {
                ex.Message
            });
        }
    }

    public void SaveConfig(string source = "local-save")
    {
        if (_config == null) return;
        PersistCurrentConfig(source, raiseChanged: true);
    }

    public void ApplyConfig(AppConfig config, string source = "runtime-apply")
    {
        _config = AppConfigSerialization.Clone(config);
        PersistCurrentConfig(source, raiseChanged: true);
    }

    public void ReloadConfig()
    {
        _config = _store.LoadConfig(_configFileName) ?? LoadConfigFromYaml(_configFileName);
        NotifyConfigChanged("reload");
    }

    private void PersistCurrentConfig(string source, bool raiseChanged)
    {
        if (_config == null)
            return;

        try
        {
            string yaml = AppConfigSerialization.SerializeYaml(_config);
            WriteYamlMirror(_configFileName, yaml);
            _store.SaveConfig(_configFileName, _config, yaml, source);
            Console.WriteLine("[AppConfigService] Configuration saved successfully");
            MachineTelemetryService.Instance.RecordConfigEvent(_configFileName, "saved", source);

            if (raiseChanged)
                NotifyConfigChanged(source);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppConfigService] Error saving config: {ex.Message}");
            MachineTelemetryService.Instance.RecordConfigEvent(_configFileName, "save-failed", source, new
            {
                ex.Message
            });
        }
    }

    private void SeedConfiguredYamlProfiles()
    {
        SeedYamlProfile("appsettings.max.yaml");
        SeedYamlProfile("appsettings.mini.yaml");
        SeedSyncState("appsettings.max.yaml");
        SeedSyncState("appsettings.mini.yaml");
    }

    private void SeedYamlProfile(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
            return;

        string yaml = File.ReadAllText(path);
        var config = AppConfigSerialization.DeserializeYaml(yaml);
        _store.SeedConfigDocument(fileName, config, yaml);
    }

    private void SeedSyncState(string fileName)
    {
        _store.SeedSyncState(fileName, MachineProfileHelper.ResolveMachineId(fileName));
    }

    private static AppConfig LoadConfigFromYaml(string fileName)
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"[AppConfigService] Config file not found at {configPath}, using defaults");
            return new AppConfig();
        }

        string yaml = File.ReadAllText(configPath);
        return AppConfigSerialization.DeserializeYaml(yaml);
    }

    private static void WriteYamlMirror(string fileName, string yaml)
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, fileName);
        File.WriteAllText(configPath, yaml);
    }

    private void NotifyConfigChanged(string source)
    {
        if (_config == null)
            return;

        ConfigChanged?.Invoke(this, new AppConfigChangedEventArgs(
            _configFileName,
            AppConfigSerialization.Clone(_config),
            source));
    }
}

public sealed class AppConfigChangedEventArgs : EventArgs
{
    public AppConfigChangedEventArgs(string profileKey, AppConfig config, string source)
    {
        ProfileKey = profileKey;
        Config = config;
        Source = source;
    }

    public string ProfileKey { get; }
    public AppConfig Config { get; }
    public string Source { get; }
}
