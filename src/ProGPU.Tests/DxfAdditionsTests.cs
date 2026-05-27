using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ProGPU.Dxf;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Tests;

public class DxfAdditionsTests
{
    [Fact]
    public void AcisSatParser_ParseSampleSat_ReconstructsEdgesCorrectly()
    {
        // A minimal, valid ACIS SAT textual block containing vertex, point, and edge definitions
        string sampleSat = @"ACIS 20800 0 1 0
body $-1 $-1 $-1 $-1
lump $-1 $-1 $-1 $-1
shell $-1 $-1 $-1 $-1
face $-1 $-1 $-1 $-1
loop $-1 $-1 $-1 $-1
coedge $-1 $-1 $-1 $-1
edge $-1 $-1 $7 $8 $11
vertex $-1 $9
vertex $-1 $10
point $-1 0.0 0.0 0.0
point $-1 10.0 5.0 3.0
straight $-1
End of ACIS Solid";

        var edges = AcisSatParser.ParseSat(sampleSat);

        Assert.Single(edges);
        var edge = edges[0];
        Assert.Equal(new Vector3(0f, 0f, 0f), edge.StartPoint);
        Assert.Equal(new Vector3(10f, 5f, 3f), edge.EndPoint);
        Assert.Equal("straight", edge.CurveType.ToLowerInvariant());
    }

    [Fact]
    public void AcisSabParser_ParseSampleSab_ReconstructsEdgesCorrectly()
    {
        // Construct a manual binary SAB stream with standard tags
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            // Write a helper to write tagged string
            void WriteTaggedString(string s)
            {
                writer.Write((byte)0x03); // Short string tag
                writer.Write((byte)s.Length);
                writer.Write(System.Text.Encoding.ASCII.GetBytes(s));
            }

            // Write a helper to write tagged double
            void WriteTaggedDouble(double d)
            {
                writer.Write((byte)0x02); // Double tag
                writer.Write(d);
            }

            // Write a helper to write tagged pointer
            void WriteTaggedPointer(int p)
            {
                writer.Write((byte)0x0C); // Pointer tag
                writer.Write(p);
            }

            // 0: body $-1 $-1 $-1 $-1
            WriteTaggedString("body");
            WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1);

            // 1: lump $-1 $-1 $-1 $-1
            WriteTaggedString("lump");
            WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1);

            // 2: shell $-1 $-1 $-1 $-1
            WriteTaggedString("shell");
            WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1);

            // 3: face $-1 $-1 $-1 $-1
            WriteTaggedString("face");
            WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1);

            // 4: loop $-1 $-1 $-1 $-1
            WriteTaggedString("loop");
            WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1);

            // 5: coedge $-1 $-1 $-1 $-1
            WriteTaggedString("coedge");
            WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(-1);

            // 6: edge $-1 $-1 $7 $8 $11
            WriteTaggedString("edge");
            WriteTaggedPointer(-1); WriteTaggedPointer(-1); WriteTaggedPointer(7); WriteTaggedPointer(8); WriteTaggedPointer(11);

            // 7: vertex $-1 $9
            WriteTaggedString("vertex");
            WriteTaggedPointer(-1); WriteTaggedPointer(9);

            // 8: vertex $-1 $10
            WriteTaggedString("vertex");
            WriteTaggedPointer(-1); WriteTaggedPointer(10);

            // 9: point $-1 0.0 0.0 0.0
            WriteTaggedString("point");
            WriteTaggedPointer(-1); WriteTaggedDouble(0.0); WriteTaggedDouble(0.0); WriteTaggedDouble(0.0);

            // 10: point $-1 10.0 5.0 3.0
            WriteTaggedString("point");
            WriteTaggedPointer(-1); WriteTaggedDouble(10.0); WriteTaggedDouble(5.0); WriteTaggedDouble(3.0);

            // 11: straight $-1
            WriteTaggedString("straight");
            WriteTaggedPointer(-1);
        }

        byte[] sabBytes = ms.ToArray();
        var edges = AcisSabParser.ParseSab(sabBytes);

        Assert.Single(edges);
        var edge = edges[0];
        Assert.Equal(new Vector3(0f, 0f, 0f), edge.StartPoint);
        Assert.Equal(new Vector3(10f, 5f, 3f), edge.EndPoint);
        Assert.Equal("straight", edge.CurveType.ToLowerInvariant());
    }

    [Fact]
    public void AcisSabParser_ParseTaglessSab_ReconstructsEdgesCorrectly()
    {
        // Construct a manual binary SAB stream WITHOUT standard tags (direct raw format)
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            // Write a helper to write raw length-prefixed string
            void WriteRawString(string s)
            {
                writer.Write((byte)s.Length);
                writer.Write(System.Text.Encoding.ASCII.GetBytes(s));
            }

            // 0: body $-1 $-1 $-1 $-1 (4 pointers)
            WriteRawString("body");
            writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);

            // 1: lump $-1 $-1 $-1 $-1 (4 pointers)
            WriteRawString("lump");
            writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);

            // 2: shell $-1 $-1 $-1 $-1 (4 pointers)
            WriteRawString("shell");
            writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);

            // 3: face $-1 $-1 $-1 $-1 (4 pointers)
            WriteRawString("face");
            writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);

            // 4: loop $-1 $-1 $-1 $-1 (4 pointers)
            WriteRawString("loop");
            writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);

            // 5: coedge $-1 $-1 $-1 $-1 (4 pointers)
            WriteRawString("coedge");
            writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);

            // 6: edge $-1 $-1 $7 $8 $11 (5 pointers)
            WriteRawString("edge");
            writer.Write(-1); writer.Write(-1); writer.Write(7); writer.Write(8); writer.Write(11);

            // 7: vertex $-1 $9 (2 pointers)
            WriteRawString("vertex");
            writer.Write(-1); writer.Write(9);

            // 8: vertex $-1 $10 (2 pointers)
            WriteRawString("vertex");
            writer.Write(-1); writer.Write(10);

            // 9: point $-1 0.0 0.0 0.0 (1 pointer, 3 doubles)
            WriteRawString("point");
            writer.Write(-1); writer.Write(0.0); writer.Write(0.0); writer.Write(0.0);

            // 10: point $-1 10.0 5.0 3.0 (1 pointer, 3 doubles)
            WriteRawString("point");
            writer.Write(-1); writer.Write(10.0); writer.Write(5.0); writer.Write(3.0);

            // 11: straight $-1 (1 pointer)
            WriteRawString("straight");
            writer.Write(-1);
        }

        byte[] sabBytes = ms.ToArray();
        var edges = AcisSabParser.ParseSab(sabBytes);

        Assert.Single(edges);
        var edge = edges[0];
        Assert.Equal(new Vector3(0f, 0f, 0f), edge.StartPoint);
        Assert.Equal(new Vector3(10f, 5f, 3f), edge.EndPoint);
        Assert.Equal("straight", edge.CurveType.ToLowerInvariant());
    }

    [Fact]
    public void SplineEvaluation_RationalBSpline_ProducesWeightedInterpolation()
    {
        var drawingContext = new ProGPU.Scene.DrawingContext();
        var pen = new Pen(new SolidColorBrush(new Vector4(1f, 0f, 0f, 1f)), 2f);
        
        var controlPoints = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(5f, 10f),
            new Vector2(10f, 0f)
        };
        var knots = new double[] { 0, 0, 0, 1, 1, 1 };
        var weights = new double[] { 1.0, 2.0, 1.0 }; // Weighted NURBS

        // Should not throw and successfully record the command
        drawingContext.DrawSpline(pen, controlPoints, knots, weights, 2, false);

        Assert.Single(drawingContext.Commands);
        var cmd = drawingContext.Commands[0];
        Assert.Equal(ProGPU.Scene.RenderCommandType.DrawSpline, cmd.Type);
        Assert.NotNull(cmd.SplineWeights);
        Assert.Equal(2.0, cmd.SplineWeights[1]);
    }

    [Fact]
    public void DrawLine3D_AddsCommandCorrectly()
    {
        var drawingContext = new ProGPU.Scene.DrawingContext();
        var pen = new Pen(new SolidColorBrush(new Vector4(1f, 1f, 1f, 1f)), 1.5f);
        var p1 = new Vector3(10f, 20f, 30f);
        var p2 = new Vector3(40f, 50f, 60f);

        drawingContext.DrawLine3D(pen, p1, p2);

        Assert.Single(drawingContext.Commands);
        var cmd = drawingContext.Commands[0];
        Assert.Equal(ProGPU.Scene.RenderCommandType.DrawLine3D, cmd.Type);
        Assert.Equal(p1, cmd.Position3D1);
        Assert.Equal(p2, cmd.Position3D2);
        Assert.NotNull(cmd.Pen);
        Assert.Equal(1.5f, cmd.Pen.Thickness);
    }

    [Fact]
    public void CrossHatchBrush_Creation_And_Properties()
    {
        var color = new Vector4(0f, 1f, 0f, 1f);
        var brush = new CrossHatchBrush(45f, 10f, 2f, color);

        Assert.Equal(45f, brush.Angle);
        Assert.Equal(10f, brush.Spacing);
        Assert.Equal(2f, brush.Thickness);
        Assert.Equal(color, brush.Color);
    }

    [Fact]
    public void DxfRenderContext_MLeaderScanning_CachesCorrectly()
    {
        // Construct a mock DXF stream with a MULTILEADER block
        string mockDxf = @"  0
SECTION
  2
ENTITIES
  0
MULTILEADER
  8
test-layer
300
CONTEXT_DATA{
 10
100.0
 20
200.0
 30
300.0
140
4.5
304
{\fArial;Hello MLeader}
301
}
302
LEADER{
304
LEADER_LINE{
 10
1.0
 20
2.0
 30
3.0
 10
10.0
 20
20.0
 30
30.0
305
}
303
}
  0
ENDSEC
  0
EOF";

        string tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, mockDxf);

        try
        {
            var ctx = new DxfRenderContext(new ProGPU.Scene.DrawingContext(), null!);
            ctx.FilePath = tempPath;

            Assert.Single(ctx.CachedMLeaders);
            var mleader = ctx.CachedMLeaders[0];
            Assert.Equal("test-layer", mleader.Layer);
            Assert.Equal(new Vector3(100f, 200f, 300f), mleader.TextInsertionPoint);
            Assert.Equal(4.5f, mleader.TextHeight);
            Assert.Equal(@"{\fArial;Hello MLeader}", mleader.TextValue);
            
            Assert.Single(mleader.LeaderLines);
            var line = mleader.LeaderLines[0];
            Assert.Equal(2, line.Count);
            Assert.Equal(new Vector3(1f, 2f, 3f), line[0]);
            Assert.Equal(new Vector3(10f, 20f, 30f), line[1]);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void DxfRenderContext_AcisSabHexScanning_CachesCorrectly()
    {
        // 1. Create binary SAB data
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            void WriteRawString(string s)
            {
                writer.Write((byte)s.Length);
                writer.Write(System.Text.Encoding.ASCII.GetBytes(s));
            }
            // Minimal body -> lump -> shell -> face -> loop -> coedge -> edge -> vertex -> point -> straight
            WriteRawString("body"); writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);
            WriteRawString("lump"); writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);
            WriteRawString("shell"); writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);
            WriteRawString("face"); writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);
            WriteRawString("loop"); writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);
            WriteRawString("coedge"); writer.Write(-1); writer.Write(-1); writer.Write(-1); writer.Write(-1);
            WriteRawString("edge"); writer.Write(-1); writer.Write(-1); writer.Write(7); writer.Write(8); writer.Write(11);
            WriteRawString("vertex"); writer.Write(-1); writer.Write(9);
            WriteRawString("vertex"); writer.Write(-1); writer.Write(10);
            WriteRawString("point"); writer.Write(-1); writer.Write(0.0); writer.Write(0.0); writer.Write(0.0);
            WriteRawString("point"); writer.Write(-1); writer.Write(5.0); writer.Write(6.0); writer.Write(7.0);
            WriteRawString("straight"); writer.Write(-1);
        }

        byte[] sabBytes = ms.ToArray();
        string hex = BitConverter.ToString(sabBytes).Replace("-", "");

        // 2. Wrap inside DXF format with 3DSOLID entity and group code 310
        string mockDxf = $@"  0
SECTION
  2
ENTITIES
  0
3DSOLID
  8
0
310
{hex}
  0
ENDSEC
  0
EOF";

        string tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, mockDxf);

        try
        {
            var ctx = new DxfRenderContext(new ProGPU.Scene.DrawingContext(), null!);
            ctx.FilePath = tempPath;

            Assert.Single(ctx.Cached3dSolids);
            var solid = ctx.Cached3dSolids[0];
            Assert.Single(solid.Edges);
            var edge = solid.Edges[0];
            Assert.Equal(new Vector3(0f, 0f, 0f), edge.StartPoint);
            Assert.Equal(new Vector3(5f, 6f, 7f), edge.EndPoint);
            Assert.Equal("straight", edge.CurveType.ToLowerInvariant());
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

