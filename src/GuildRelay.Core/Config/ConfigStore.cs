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

            var migrated = await MigrateLegacyChatFieldsAsync(loaded).ConfigureAwait(false);
            return SanitizeIntervals(migrated);
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

    // Guards against silent zero-intervals left by prior field renames
    // (e.g. captureIntervalMs → captureIntervalSec). System.Text.Json
    // fills missing ctor parameters with default(int) = 0, which would
    // otherwise reach the UI as a blank/zero interval instead of the
    // documented 5-second default.
    private static AppConfig SanitizeIntervals(AppConfig cfg)
    {
        var chat = cfg.Chat;
        if (chat.CaptureIntervalSec <= 0)
            chat = chat with { CaptureIntervalSec = ChatConfig.Default.CaptureIntervalSec };

        var status = cfg.Status;
        if (status.CaptureIntervalSec <= 0)
            status = status with { CaptureIntervalSec = StatusConfig.Default.CaptureIntervalSec };

        return cfg with { Chat = chat, Status = status };
    }

    // Pre-rename configs serialise the chat toggle as `enabled`. The new schema uses
    // `eventRepostEnabled` + `statsEnabled` + `counterRules`. STJ silently leaves the
    // new properties at their default values when the legacy field is present.
    // Detect that case and fix it by reading the raw JSON.
    private async Task<AppConfig> MigrateLegacyChatFieldsAsync(AppConfig loaded)
    {
        var raw = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("chat", out var chatEl)) return loaded;

        var chat = loaded.Chat;
        bool changed = false;

        if (!chatEl.TryGetProperty("eventRepostEnabled", out _)
            && chatEl.TryGetProperty("enabled", out var enabledEl)
            && enabledEl.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
        {
            chat = chat with { EventRepostEnabled = enabledEl.GetBoolean() };
            changed = true;
        }

        if (!chatEl.TryGetProperty("counterRules", out _))
        {
            chat = chat with { CounterRules = new System.Collections.Generic.List<CounterRule>(ChatConfig.Default.CounterRules) };
            changed = true;
        }

        return changed ? loaded with { Chat = chat } : loaded;
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
