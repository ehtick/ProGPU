using System.Numerics;
using ProGPU.Backend;
using ProGPU.Compute;
using Silk.NET.WebGPU;
using Xunit;

namespace ProGPU.Tests.Headless;

[Collection("HeadlessTests")]
public sealed class AnalyticTileRendererHeadlessTests
{
    [Fact]
    public unsafe void AnalyticFineShaderCreatesAndMatchesRectangleCoverage()
    {
        var context = HeadlessWindow.Shared.Context;
        bool hasError = false;
        string errors = string.Empty;

        void OnWebGpuError(ErrorType type, string message)
        {
            hasError = true;
            errors += $"{type}: {message}\n";
        }

        WgpuContext.OnWebGpuError += OnWebGpuError;
        try
        {
            using var renderer = new GpuAnalyticTileRenderer(context, 1, 1, 2);
            using var destination = new GpuTexture(
                context,
                32,
                16,
                TextureFormat.Rgba8Unorm,
                TextureUsage.StorageBinding | TextureUsage.TextureBinding | TextureUsage.CopySrc,
                "Analytic Fine Test Output");
            GpuAnalyticSegment[] segments =
            [
                new GpuAnalyticSegment
                {
                    Point0 = new Vector2(14f - 1e-6f, 2f),
                    Point1 = new Vector2(14f - 1e-6f, 14f),
                    YEdge = 1e9f
                },
                new GpuAnalyticSegment
                {
                    Point0 = new Vector2(2f - 1e-6f, 14f),
                    Point1 = new Vector2(2f - 1e-6f, 2f),
                    YEdge = 1e9f
                },
                new GpuAnalyticSegment
                {
                    Point0 = new Vector2(2.25f, 2.5f),
                    Point1 = new Vector2(13.5f, 4.25f),
                    YEdge = 1e9f
                },
                new GpuAnalyticSegment
                {
                    Point0 = new Vector2(13.5f, 4.25f),
                    Point1 = new Vector2(6.75f, 14.2f),
                    YEdge = 1e9f
                },
                new GpuAnalyticSegment
                {
                    Point0 = new Vector2(6.75f, 14.2f),
                    Point1 = new Vector2(2.25f, 2.5f),
                    YEdge = 1e9f
                }
            ];
            GpuAnalyticFill[] fills =
            [
                new GpuAnalyticFill
                {
                    SegmentCountAndRule = GpuAnalyticFill.PackSegmentCountAndRule(2, evenOdd: false),
                    PremultipliedColor = new Vector4(1f, 0f, 0f, 1f)
                },
                new GpuAnalyticFill
                {
                    SegmentOffset = 2,
                    SegmentCountAndRule = GpuAnalyticFill.PackSegmentCountAndRule(3, evenOdd: false),
                    PremultipliedColor = new Vector4(0f, 1f, 0f, 1f)
                }
            ];
            renderer.Upload(
                [
                    new GpuAnalyticTile { CommandCount = 1 },
                    new GpuAnalyticTile { X = 1, CommandOffset = 1, CommandCount = 1 }
                ],
                fills,
                segments);

            var encoderDescriptor = new CommandEncoderDescriptor();
            var encoder = context.Api.DeviceCreateCommandEncoder(context.Device, &encoderDescriptor);
            Assert.NotEqual(nint.Zero, (nint)encoder);
            renderer.Record(encoder, destination);
            var commandDescriptor = new CommandBufferDescriptor();
            var commandBuffer = context.Api.CommandEncoderFinish(encoder, &commandDescriptor);
            Assert.NotEqual(nint.Zero, (nint)commandBuffer);
            context.Api.QueueSubmit(context.Queue, 1, &commandBuffer);
            context.Api.CommandBufferRelease(commandBuffer);
            context.Api.CommandEncoderRelease(encoder);

            byte[] pixels = destination.ReadPixels();
            var expectedRectangle = new Vector4[256];
            GpuAnalyticTileRenderer.RasterizeTileCpu(fills.AsSpan(0, 1), segments, expectedRectangle);
            var expectedTriangle = new Vector4[256];
            GpuAnalyticTileRenderer.RasterizeTileCpu(fills.AsSpan(1, 1), segments, expectedTriangle);
            Assert.Contains(expectedTriangle, static pixel => pixel.W > 0f && pixel.W < 1f);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    Vector4 color = x < 16
                        ? expectedRectangle[y * 16 + x]
                        : expectedTriangle[y * 16 + x - 16];
                    int byteOffset = (y * 32 + x) * 4;
                    Assert.InRange(pixels[byteOffset], ToByte(color.X) - 1, ToByte(color.X) + 1);
                    Assert.InRange(pixels[byteOffset + 1], ToByte(color.Y) - 1, ToByte(color.Y) + 1);
                    Assert.InRange(pixels[byteOffset + 2], ToByte(color.Z) - 1, ToByte(color.Z) + 1);
                    Assert.InRange(pixels[byteOffset + 3], ToByte(color.W) - 1, ToByte(color.W) + 1);
                }
            }
        }
        finally
        {
            WgpuContext.OnWebGpuError -= OnWebGpuError;
        }

        Assert.False(hasError, $"WebGPU validation errors occurred:\n{errors}");
    }

    private static int ToByte(float value) => (int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
