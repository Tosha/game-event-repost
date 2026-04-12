using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public sealed record PreprocessStageConfig(
    string Stage,
    Dictionary<string, double>? Parameters = null);
