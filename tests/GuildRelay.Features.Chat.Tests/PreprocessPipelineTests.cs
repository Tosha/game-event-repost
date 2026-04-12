using System;
using FluentAssertions;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Preprocessing;
using GuildRelay.Features.Chat.Preprocessing;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class PreprocessPipelineTests
{
    private sealed class FakeBlueStage : IPreprocessStage
    {
        public string Name => "fakeBlue";
        public CapturedFrame Apply(CapturedFrame frame)
        {
            var pixels = (byte[])frame.BgraPixels.Clone();
            for (int i = 0; i < pixels.Length; i += 4)
                pixels[i] = 0xFF;
            return new CapturedFrame(pixels, frame.Width, frame.Height, frame.Stride);
        }
    }

    private sealed class FakeGreenStage : IPreprocessStage
    {
        public string Name => "fakeGreen";
        public CapturedFrame Apply(CapturedFrame frame)
        {
            var pixels = (byte[])frame.BgraPixels.Clone();
            for (int i = 1; i < pixels.Length; i += 4)
                pixels[i] = 0xFF;
            return new CapturedFrame(pixels, frame.Width, frame.Height, frame.Stride);
        }
    }

    private static CapturedFrame BlackFrame()
        => new(new byte[4 * 4], width: 2, height: 2, stride: 8);

    [Fact]
    public void EmptyPipelineReturnsInputUnchanged()
    {
        var pipeline = new PreprocessPipeline(Array.Empty<IPreprocessStage>());
        var input = BlackFrame();

        var output = pipeline.Apply(input);

        output.BgraPixels.Should().Equal(input.BgraPixels);
    }

    [Fact]
    public void StagesAreAppliedInOrder()
    {
        var pipeline = new PreprocessPipeline(new IPreprocessStage[]
        {
            new FakeBlueStage(),
            new FakeGreenStage()
        });
        var input = BlackFrame();

        var output = pipeline.Apply(input);

        output.BgraPixels[0].Should().Be(0xFF); // Blue from first stage
        output.BgraPixels[1].Should().Be(0xFF); // Green from second stage
    }
}
