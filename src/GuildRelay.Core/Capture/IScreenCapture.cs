using System.Drawing;

namespace GuildRelay.Core.Capture;

public interface IScreenCapture
{
    CapturedFrame CaptureRegion(Rectangle screenSpaceRect);
}
