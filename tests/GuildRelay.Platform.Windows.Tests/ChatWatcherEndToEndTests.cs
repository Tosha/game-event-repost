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

    private List<string> MatchWithChannelMatcher(List<OcrLine> lines, StructuredChatRule rule)
    {
        var matcher = new ChannelMatcher(new[] { rule });

        var inputs = new List<OcrLineInput>();
        foreach (var line in lines)
        {
            var n = TextNormalizer.Normalize(line.Text);
            if (!string.IsNullOrEmpty(n))
            {
                inputs.Add(new OcrLineInput(n, line.Text, line.Confidence));
                _output.WriteLine($"  Normalized: \"{n}\" conf={line.Confidence:F2}");
            }
        }

        // Threshold mirrors the default ChatConfig value used in production.
        var assembly = ChatMessageAssembler.Assemble(inputs, confidenceThreshold: 0.65);

        // End-to-end test covers a single-tick fixture, so treat any Trailing
        // message as a candidate too — there is no "next tick" to terminate it,
        // and the fixture represents the whole visible region.
        var candidates = new List<AssembledMessage>();
        candidates.AddRange(assembly.Completed);
        if (assembly.Trailing is not null) candidates.Add(assembly.Trailing);

        var matches = new List<string>();
        foreach (var msg in candidates)
        {
            var parsed = msg.ToParsedChatLine();
            var match = matcher.FindMatch(parsed);
            if (match is not null)
            {
                _output.WriteLine($"  MATCH rows {msg.StartRow}-{msg.EndRow}: \"{msg.OriginalText}\"");
                matches.Add(msg.OriginalText);
            }
        }
        return matches;
    }

    private static StructuredChatRule DefaultGameEventsRule =>
        RuleTemplates.BuiltIn["MO2 Game Events"][0];

    [Fact]
    public void NormalizerPreservesBrackets()
    {
        var input = "[20:27:33][Game] You received a task to kill Dire Wolf (8)";
        var normalized = TextNormalizer.Normalize(input);
        normalized.Should().Contain("[game]");
        var parsed = ChatLineParser.Parse(normalized);
        parsed.Channel.Should().Be("Game");
    }

    // ── Each fixture tested against the default MO2 Game Events rule ──

    [Fact]
    public async Task SylvanSanctum_MatchesDefaultRule()
    {
        // [Game] A large band of Profiteers ... pillaging the Sylvan Sanctum!
        var lines = await OcrFixture("mo2-chat-sylvan-sanctum.png");
        if (lines.Count == 0) return;
        _output.WriteLine("OCR lines:");
        foreach (var l in lines) _output.WriteLine($"  \"{l.Text}\"");

        var matches = MatchWithChannelMatcher(lines, DefaultGameEventsRule);
        _output.WriteLine($"\nMatches: {matches.Count}");
        matches.Should().NotBeEmpty("Sylvan Sanctum is in the default MO2 locations list");
    }

    [Fact]
    public async Task EasternHighlands_MatchesDefaultRule()
    {
        // [Game] A large band of Carvers ... harassing the Eastern Highlands!
        var lines = await OcrFixture("mo2-chat-eastern-highlands.png");
        if (lines.Count == 0) return;
        _output.WriteLine("OCR lines:");
        foreach (var l in lines) _output.WriteLine($"  \"{l.Text}\"");

        var matches = MatchWithChannelMatcher(lines, DefaultGameEventsRule);
        _output.WriteLine($"\nMatches: {matches.Count}");
        matches.Should().NotBeEmpty("Eastern Highlands is in the default MO2 locations list");
    }

    [Fact]
    public async Task PlainsOfMeduli_MatchesDefaultRule()
    {
        // [Game] A large band of Profiteers ... pillaging the Plains of Meduli!
        var lines = await OcrFixture("mo2-chat-plains-of-meduli.png");
        if (lines.Count == 0) return;
        _output.WriteLine("OCR lines:");
        foreach (var l in lines) _output.WriteLine($"  \"{l.Text}\"");

        var matches = MatchWithChannelMatcher(lines, DefaultGameEventsRule);
        _output.WriteLine($"\nMatches: {matches.Count}");
        matches.Should().NotBeEmpty("Plains of Meduli is in the default MO2 locations list");
    }

    [Fact]
    public async Task DireWolf_DoesNotMatchDefaultLocationRule()
    {
        // [Game] You received a task to kill Dire Wolf (8) -- not a location event
        var lines = await OcrFixture("mo2-chat-dire-wolf.png");
        if (lines.Count == 0) return;
        _output.WriteLine("OCR lines:");
        foreach (var l in lines) _output.WriteLine($"  \"{l.Text}\"");

        var matches = MatchWithChannelMatcher(lines, DefaultGameEventsRule);
        _output.WriteLine($"\nMatches: {matches.Count}");
        matches.Should().BeEmpty("Dire Wolf is a creature, not in the MO2 locations list");
    }
}
