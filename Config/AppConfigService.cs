using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DaevaMini.Config;

public sealed class AppConfigService
{
    private static AppConfigService? _instance;
    private static readonly object _lockObject = new();
    private AppConfig? _config;

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

    public void LoadConfig()
    {
        try
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.yaml");
            
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"[AppConfigService] Config file not found at {configPath}, using defaults");
                _config = new AppConfig();
                return;
            }

            string yaml = File.ReadAllText(configPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(NullNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            _config = deserializer.Deserialize<AppConfig>(yaml);
            
            if (_config == null)
            {
                Console.WriteLine("[AppConfigService] Failed to deserialize config, using defaults");
                _config = new AppConfig();
            }
            else
            {
                Console.WriteLine("[AppConfigService] Configuration loaded successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppConfigService] Error loading config: {ex.Message}, using defaults");
            _config = new AppConfig();
        }
    }

    public void ReloadConfig()
    {
        _config = null;
        LoadConfig();
    }
}
