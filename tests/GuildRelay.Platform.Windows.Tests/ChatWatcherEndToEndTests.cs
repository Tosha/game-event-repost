using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Config;
using GuildRelay.Core.Ocr;
using GuildRelay.Core.Preprocessing;
using GuildRelay.Features.Chat;
using GuildRelay.Features.Chat.Preprocessing;
using GuildRelay.Platform.Windows.Ocr;
using GuildRelay.Platform.Windows.Preprocessing;
using Xunit;
using Xunit.Abstractions;

namespace GuildRelay.Platform.Windows.Tests;

/// <summary>
/// End-to-end tests: real screenshot → OCR → normalize → parse channel → match rule.
/// Uses the new structured ChatLineParser + ChannelMatcher pipeline.
/// </summary>
public class ChatWatcherEndToEndTests
{
    private readonly ITestOutputHelper _output;

    public ChatWatcherEndToEndTests(ITestOutputHelper output) { _output = output; }

    private static CapturedFrame BitmapToFrame(Bitmap bmp)
    {
        var bmp32 = bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), PixelFormat.Format32bppArgb);
        var lockBits = bmp32.LockBits(
            new Rectangle(0, 0, bmp32.Width, bmp32.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[lockBits.Stride * lockBits.Height];
            Marshal.Copy(lockBits.Scan0, bytes, 0, bytes.Length);
            return new CapturedFrame(bytes, lockBits.Width, lockBits.Height, lockBits.Stride);
        }
        finally
        {
            bmp32.UnlockBits(lockBits);
            bmp32.Dispose();
        }
    }

    private static string FixturesDir => Path.Combine(
        Path.GetDirectoryName(typeof(ChatWatcherEndToEndTests).Assembly.Location)!,
        "Fixtures");

    private async Task<List<OcrLine>> OcrFixture(string filename)
    {
        var path = Path.Combine(FixturesDir, filename);
        if (!File.Exists(path))
        {
            _output.WriteLine($"SKIP: {filename} not found");
            return new List<OcrLine>();
        }

        using var bmp = new Bitmap(path);
        using var frame = BitmapToFrame(bmp);
        var pipeline = new PreprocessPipeline(new IPreprocessStage[]
        {
            new GrayscaleStage(),
            new ContrastStretchStage(0.1, 0.9),
            new UpscaleStage(2),
            new AdaptiveThresholdStage(15)
        });
        using var processed = pipeline.Apply(frame);

        var engine = new WindowsMediaOcrEngine();
        var result = await engine.RecognizeAsync(
            processed.BgraPixels, processed.Width, processed.Height, processed.Stride,
            CancellationToken.None);

        return result.Lines.ToList();
    }

    [Fact]
    public void NormalizerPreservesBrackets_FixedBug()
    {
        var input = "[20:27:33][Game] You received a task to kill Dire Wolf (8)";
        var normalized = TextNormalizer.Normalize(input);

        _output.WriteLine($"Input:      \"{input}\"");
        _output.WriteLine($"Normalized: \"{normalized}\"");

        normalized.Should().Contain("[game]", "brackets are preserved");

        // Now test with the new structured pipeline
        var parsed = ChatLineParser.Parse(normalized);
        parsed.Channel.Should().Be("Game");
        parsed.Body.Should().Contain("dire wolf");
    }

    /// <summary>
    /// Simulates ChatWatcher's line-joining + channel matching on OCR output.
    /// </summary>
    private List<string> MatchWithChannelMatcher(List<OcrLine> lines, StructuredChatRule rule)
    {
        var matcher = new ChannelMatcher(new[] { rule });

        var normalizedLines = new List<(string normalized, string original)>();
        foreach (var line in lines)
        {
            var n = TextNormalizer.Normalize(line.Text);
            if (!string.IsNullOrEmpty(n))
            {
                normalizedLines.Add((n, line.Text));
                _output.WriteLine($"  Normalized: \"{n}\"");
            }
        }

        var matches = new List<string>();
        for (int i = 0; i < normalizedLines.Count; i++)
        {
            var (norm, orig) = normalizedLines[i];

            // Try single line
            var parsed = ChatLineParser.Parse(norm);
            var match = matcher.FindMatch(parsed);
            if (match is not null)
            {
                _output.WriteLine($"  MATCH (single): \"{orig}\"");
                matches.Add(orig);
                continue;
            }

            // Try joined with next line
            if (i + 1 < normalizedLines.Count)
            {
                var joined = norm + " " + normalizedLines[i + 1].normalized;
                var joinedOrig = orig + " " + normalizedLines[i + 1].original;
                var parsedJoined = ChatLineParser.Parse(joined);
                var matchJoined = matcher.FindMatch(parsedJoined);
                if (matchJoined is not null)
                {
                    _output.WriteLine($"  MATCH (joined): \"{joinedOrig}\"");
                    matches.Add(joinedOrig);
                }
            }
        }
        return matches;
    }

    [Fact]
    public async Task Example2_SylvanSanctum_FullPipeline()
    {
        var lines = await OcrFixture("mo2-chat-example2.png");
        if (lines.Count == 0) return;

        _output.WriteLine("OCR lines:");
        foreach (var line in lines)
            _output.WriteLine($"  \"{line.Text}\"");

        var rule = new StructuredChatRule("r1", "Game Events",
            new List<string> { "Game" },
            new List<string> { "Sylvan Sanctum" },
            MatchMode.ContainsAny);

        var matches = MatchWithChannelMatcher(lines, rule);

        _output.WriteLine($"\nMatches found: {matches.Count}");
        matches.Should().NotBeEmpty("should find the Sylvan Sanctum game event");
    }

    [Fact]
    public async Task Example3_DireWolf_FullPipeline()
    {
        var lines = await OcrFixture("mo2-chat-example3.png");
        if (lines.Count == 0) return;

        _output.WriteLine("OCR lines:");
        foreach (var line in lines)
            _output.WriteLine($"  \"{line.Text}\"");

        var rule = new StructuredChatRule("r1", "Game Events",
            new List<string> { "Game" },
            new List<string> { "Dire Wolf" },
            MatchMode.ContainsAny);

        var matches = MatchWithChannelMatcher(lines, rule);

        _output.WriteLine($"\nMatches found: {matches.Count}");
        matches.Should().NotBeEmpty("should find the Dire Wolf task event");
    }
}
