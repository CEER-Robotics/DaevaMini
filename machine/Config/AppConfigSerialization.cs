using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DaevaMini.Config;

internal static class AppConfigSerialization
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static AppConfig DeserializeYaml(string yaml)
    {
        return YamlDeserializer.Deserialize<AppConfig>(yaml) ?? new AppConfig();
    }

    public static string SerializeYaml(AppConfig config)
    {
        return YamlSerializer.Serialize(config);
    }

    public static AppConfig DeserializeJson(string json)
    {
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
    }

    public static string SerializeJson(AppConfig config)
    {
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    public static AppConfig Clone(AppConfig config)
    {
        return DeserializeJson(SerializeJson(config));
    }
}
