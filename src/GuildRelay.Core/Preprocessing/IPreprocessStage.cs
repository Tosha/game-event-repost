using GuildRelay.Core.Capture;

namespace GuildRelay.Core.Preprocessing;

/// <summary>
/// A single image-processing stage in the capture preprocessing pipeline.
/// Concrete implementations (grayscale, contrast, etc.) live in
/// Platform.Windows. The pipeline runner lives in Features.Chat.
/// </summary>
public interface IPreprocessStage
{
    string Name { get; }
    CapturedFrame Apply(CapturedFrame frame);
}
