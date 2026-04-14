using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace GuildRelay.Core.Config;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public ConfigStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public async Task<AppConfig> LoadOrCreateDefaultsAsync()
    {
        if (!File.Exists(_path))
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            await SaveAsync(AppConfig.Default).ConfigureAwait(false);
            return AppConfig.Default;
        }

        try
        {
            using var stream = File.OpenRead(_path);
            var loaded = await JsonSerializer.DeserializeAsync<AppConfig>(stream, Json).ConfigureAwait(false);
            if (loaded is null) return AppConfig.Default;

            // Detect old config format: System.Text.Json silently deserializes
            // old ChatRuleConfig fields into StructuredChatRule with null Channels/Keywords
            // instead of throwing. If any rule has null Channels, the config is stale.
            if (loaded.Chat?.Rules is not null &&
                loaded.Chat.Rules.Exists(r => r.Channels is null || r.Keywords is null))
            {
                var backup = _path + ".bak";
                File.Copy(_path, backup, overwrite: true);
                await SaveAsync(AppConfig.Default).ConfigureAwait(false);
                return AppConfig.Default;
            }

            return loaded;
        }
        catch (JsonException)
        {
            // Config format changed (e.g., ChatRuleConfig → StructuredChatRule).
            // Back up the old config and start fresh with defaults.
            var backup = _path + ".bak";
            File.Copy(_path, backup, overwrite: true);
            await SaveAsync(AppConfig.Default).ConfigureAwait(false);
            return AppConfig.Default;
        }
    }

    public async Task SaveAsync(AppConfig config)
    {
        var tmp = _path + ".tmp";
        using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, config, Json).ConfigureAwait(false);
        File.Copy(tmp, _path, overwrite: true);
        File.Delete(tmp);
    }
}
