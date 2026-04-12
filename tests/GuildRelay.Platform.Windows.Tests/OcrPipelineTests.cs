using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Ocr;
using GuildRelay.Core.Preprocessing;
using GuildRelay.Features.Chat.Preprocessing;
using GuildRelay.Platform.Windows.Ocr;
using GuildRelay.Platform.Windows.Preprocessing;
using Xunit;
using Xunit.Abstractions;

namespace GuildRelay.Platform.Windows.Tests;

/// <summary>
/// Diagnostic tests for the OCR pipeline. These run Windows.Media.Ocr
/// against both a generated MO2-style chat image and any real screenshot
/// dropped into the Fixtures/ directory. Output goes to test console via
/// ITestOutputHelper so you can see exactly what OCR reads.
///
/// Run with: dotnet test tests/GuildRelay.Platform.Windows.Tests --verbosity normal -l "console;verbosity=detailed"
/// </summary>
public class OcrPipelineTests
{
    private readonly ITestOutputHelper _output;

    public OcrPipelineTests(ITestOutputHelper output) { _output = output; }

    // ── Helpers ──────────────────────────────────────────────────────

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

    private static Bitmap GenerateMo2ChatImage()
    {
        // Simulate MO2 chat: colored text on a dark semi-transparent background
        var width = 400;
        var height = 140;
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.HighQuality;

        // Dark game-like background
        g.Clear(Color.FromArgb(255, 20, 15, 10));

        var font = new Font("Segoe UI", 11f, FontStyle.Regular);
        var chatLines = new (string text, Color color)[]
        {
            ("[Nave] [Stormbrew] they should just add it", Color.FromArgb(200, 200, 160)),
            ("[Nave] [Leyawiin] nah it would make blokes easy to hide", Color.FromArgb(200, 200, 160)),
            ("[Nave] [Stormbrew] theyl just remake", Color.FromArgb(200, 200, 160)),
            ("[20:27:33][Game] You received a task to kill Dire Wolf", Color.FromArgb(180, 220, 180)),
        };

        var y = 5f;
        foreach (var (text, color) in chatLines)
        {
            g.DrawString(text, font, new SolidBrush(color), 5f, y);
            y += 30f;
        }

        font.Dispose();
        return bmp;
    }

    private async Task<OcrResult> RunOcr(CapturedFrame frame)
    {
        var engine = new WindowsMediaOcrEngine();
        return await engine.RecognizeAsync(
            frame.BgraPixels, frame.Width, frame.Height, frame.Stride,
            CancellationToken.None);
    }

    private void PrintResults(string label, OcrResult result)
    {
        _output.WriteLine($"\n=== {label} ===");
        _output.WriteLine($"Lines found: {result.Lines.Count}");
        foreach (var line in result.Lines)
            _output.WriteLine($"  [{line.Confidence:F2}] \"{line.Text}\"");
        if (result.Lines.Count == 0)
            _output.WriteLine("  (no text detected)");
    }

    private static PreprocessPipeline MakePipeline(params IPreprocessStage[] stages)
        => new(stages);

    // ── Tests on generated image ────────────────────────────────────

    [Fact]
    public async Task Generated_RawImageNoPreprocessing()
    {
        using var bmp = GenerateMo2ChatImage();
        using var frame = BitmapToFrame(bmp);

        var result = await RunOcr(frame);
        PrintResults("Generated: Raw (no preprocessing)", result);

        result.Lines.Should().NotBeEmpty("OCR should find some text on the generated image");
    }

    [Fact]
    public async Task Generated_GrayscaleOnly()
    {
        using var bmp = GenerateMo2ChatImage();
        using var frame = BitmapToFrame(bmp);
        using var processed = MakePipeline(new GrayscaleStage()).Apply(frame);

        var result = await RunOcr(processed);
        PrintResults("Generated: Grayscale only", result);

        result.Lines.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Generated_GrayscalePlusUpscale()
    {
        using var bmp = GenerateMo2ChatImage();
        using var frame = BitmapToFrame(bmp);
        using var processed = MakePipeline(
            new GrayscaleStage(),
            new UpscaleStage(2)
        ).Apply(frame);

        var result = await RunOcr(processed);
        PrintResults("Generated: Grayscale + Upscale 2x", result);

        result.Lines.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Generated_FullDefaultPipeline()
    {
        using var bmp = GenerateMo2ChatImage();
        using var frame = BitmapToFrame(bmp);
        using var processed = MakePipeline(
            new GrayscaleStage(),
            new ContrastStretchStage(0.1, 0.9),
            new UpscaleStage(2),
            new AdaptiveThresholdStage(15)
        ).Apply(frame);

        var result = await RunOcr(processed);
        PrintResults("Generated: Full default pipeline (gray+contrast+upscale+threshold)", result);

        // This may or may not find text — that's what we're diagnosing
        _output.WriteLine($"  → Found {result.Lines.Count} lines with full pipeline");
    }

    // ── Tests on real screenshot (drop PNG into Fixtures/) ──────────

    private static string? FindFixtureImage()
    {
        var dir = Path.Combine(
            Path.GetDirectoryName(typeof(OcrPipelineTests).Assembly.Location)!,
            "Fixtures");
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, "*.png").FirstOrDefault()
            ?? Directory.GetFiles(dir, "*.jpg").FirstOrDefault()
            ?? Directory.GetFiles(dir, "*.bmp").FirstOrDefault();
    }

    [Fact]
    public async Task RealScreenshot_RawNoPreprocessing()
    {
        var path = FindFixtureImage();
        if (path is null) { _output.WriteLine("SKIP: No fixture image in Fixtures/. Drop a PNG there."); return; }
        _output.WriteLine($"Loading fixture: {path}");

        using var bmp = new Bitmap(path);
        using var frame = BitmapToFrame(bmp);
        _output.WriteLine($"Image size: {frame.Width}x{frame.Height}");

        var result = await RunOcr(frame);
        PrintResults("Real screenshot: Raw (no preprocessing)", result);
    }

    [Fact]
    public async Task RealScreenshot_GrayscaleOnly()
    {
        var path = FindFixtureImage();
        if (path is null) { _output.WriteLine("SKIP: No fixture image."); return; }

        using var bmp = new Bitmap(path);
        using var frame = BitmapToFrame(bmp);
        using var processed = MakePipeline(new GrayscaleStage()).Apply(frame);

        var result = await RunOcr(processed);
        PrintResults("Real screenshot: Grayscale only", result);
    }

    [Fact]
    public async Task RealScreenshot_GrayscalePlusUpscale()
    {
        var path = FindFixtureImage();
        if (path is null) { _output.WriteLine("SKIP: No fixture image."); return; }

        using var bmp = new Bitmap(path);
        using var frame = BitmapToFrame(bmp);
        using var processed = MakePipeline(
            new GrayscaleStage(),
            new UpscaleStage(2)
        ).Apply(frame);

        var result = await RunOcr(processed);
        PrintResults("Real screenshot: Grayscale + Upscale 2x", result);
    }

    [Fact]
    public async Task RealScreenshot_FullDefaultPipeline()
    {
        var path = FindFixtureImage();
        if (path is null) { _output.WriteLine("SKIP: No fixture image."); return; }

        using var bmp = new Bitmap(path);
        using var frame = BitmapToFrame(bmp);
        using var processed = MakePipeline(
            new GrayscaleStage(),
            new ContrastStretchStage(0.1, 0.9),
            new UpscaleStage(2),
            new AdaptiveThresholdStage(15)
        ).Apply(frame);

        var result = await RunOcr(processed);
        PrintResults("Real screenshot: Full default pipeline", result);
    }

    [Fact]
    public async Task RealScreenshot_UpscaleOnly()
    {
        var path = FindFixtureImage();
        if (path is null) { _output.WriteLine("SKIP: No fixture image."); return; }

        using var bmp = new Bitmap(path);
        using var frame = BitmapToFrame(bmp);
        using var processed = MakePipeline(new UpscaleStage(3)).Apply(frame);

        var result = await RunOcr(processed);
        PrintResults("Real screenshot: Upscale 3x only (no grayscale, no threshold)", result);
    }
}
