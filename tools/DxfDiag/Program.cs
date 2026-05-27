using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using netDxf.Entities;
using netDxf.Objects;

using SysVector2 = System.Numerics.Vector2;
using SysMatrix4x4 = System.Numerics.Matrix4x4;

namespace ProGPU.Tools;

public static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        Console.WriteLine("[DxfDiag] DXF CLI Diagnostic Tool");
        Console.WriteLine("=================================");

        if (args.Length < 1)
        {
            Console.WriteLine("Usage: dotnet run --project tools/DxfDiag -- <dxf-path> [--layout <name>]");
            return 1;
        }

        string dxfPath = args[0];
        if (!File.Exists(dxfPath))
        {
            Console.WriteLine($"[Error] DXF file not found: {dxfPath}");
            return 1;
        }

        string? targetLayout = null;
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--layout", StringComparison.OrdinalIgnoreCase))
            {
                targetLayout = args[i + 1];
                break;
            }
        }

        Console.WriteLine($"[DxfDiag] Loading DXF file: {dxfPath}");
        netDxf.DxfDocument doc;
        try
        {
            doc = netDxf.DxfDocument.Load(dxfPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to load DXF file: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"[DxfDiag] Total Layers: {doc.Layers.Count}");

        // Print Layouts
        Console.WriteLine("\n--- Available Layouts ---");
        foreach (var layout in doc.Layouts)
        {
            int entityCount = layout.AssociatedBlock?.Entities?.Count ?? 0;
            Console.WriteLine($"  Layout: '{layout.Name}' | Active: {(doc.ActiveLayout == layout.Name ? "Yes" : "No")} | Entities: {entityCount}");
        }

        string selectedLayoutName = targetLayout ?? doc.ActiveLayout ?? "Model";
        if (!doc.Layouts.Contains(selectedLayoutName))
        {
            Console.WriteLine($"\n[Warning] Target layout '{selectedLayoutName}' not found. Falling back to active layout: '{doc.ActiveLayout}'");
            selectedLayoutName = doc.ActiveLayout ?? "Model";
        }

        Console.WriteLine($"\n[DxfDiag] Analyzing layout: '{selectedLayoutName}'");
        var layoutObj = doc.Layouts[selectedLayoutName];
        var entities = (layoutObj != null && layoutObj.AssociatedBlock != null && layoutObj.AssociatedBlock.Entities != null)
            ? (IEnumerable<EntityObject>)layoutObj.AssociatedBlock.Entities
            : Array.Empty<EntityObject>();

        // We will collect detailed information and write to outliers.txt
        using var writer = new StreamWriter("outliers.txt", false);
        writer.WriteLine("================================================================================");
        writer.WriteLine($"DxfDiag Report: {dxfPath}");
        writer.WriteLine($"Layout analyzed: {selectedLayoutName}");
        writer.WriteLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine("================================================================================");

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        bool hasData = false;

        List<string> outliers = new();
        int totalEntitiesScanned = 0;
        int insertCount = 0;
        int textCount = 0;
        int attrCount = 0;

        void UpdateBounds(SysVector2 pt, string entityDesc)
        {
            if (float.IsNaN(pt.X) || float.IsNaN(pt.Y) || float.IsInfinity(pt.X) || float.IsInfinity(pt.Y))
            {
                outliers.Add($"[NaN/Infinity] {entityDesc} -> Pt: {pt}");
                return;
            }

            if (Math.Abs(pt.X) > 1000000f || Math.Abs(pt.Y) > 1000000f)
            {
                outliers.Add($"[Outlier Coordinate] {entityDesc} -> Pt: {pt}");
            }

            minX = Math.Min(minX, pt.X);
            minY = Math.Min(minY, pt.Y);
            maxX = Math.Max(maxX, pt.X);
            maxY = Math.Max(maxY, pt.Y);
            hasData = true;
        }

        void ScanEntity(EntityObject entity, SysMatrix4x4 transform, string path, int depth)
        {
            totalEntitiesScanned++;
            string indent = new string(' ', depth * 2);
            string entityDesc = $"{path}/{entity.Type} [{entity.Handle}]";

            writer.WriteLine($"{indent}- {entity.Type} (Handle: {entity.Handle}, Layer: {entity.Layer.Name})");

            switch (entity)
            {
                case Line line:
                    var start = SysVector2.Transform(new SysVector2((float)line.StartPoint.X, (float)line.StartPoint.Y), transform);
                    var end = SysVector2.Transform(new SysVector2((float)line.EndPoint.X, (float)line.EndPoint.Y), transform);
                    UpdateBounds(start, $"{entityDesc} StartPoint");
                    UpdateBounds(end, $"{entityDesc} EndPoint");
                    break;

                case Circle circle:
                    var cCenter = SysVector2.Transform(new SysVector2((float)circle.Center.X, (float)circle.Center.Y), transform);
                    float cRadius = (float)circle.Radius;
                    UpdateBounds(cCenter + new SysVector2(-cRadius, -cRadius), $"{entityDesc} MinBounds");
                    UpdateBounds(cCenter + new SysVector2(cRadius, cRadius), $"{entityDesc} MaxBounds");
                    break;

                case Arc arc:
                    var aCenter = SysVector2.Transform(new SysVector2((float)arc.Center.X, (float)arc.Center.Y), transform);
                    float aRadius = (float)arc.Radius;
                    UpdateBounds(aCenter + new SysVector2(-aRadius, -aRadius), $"{entityDesc} MinBounds");
                    UpdateBounds(aCenter + new SysVector2(aRadius, aRadius), $"{entityDesc} MaxBounds");
                    break;

                case Ellipse ellipse:
                    var eCenter = SysVector2.Transform(new SysVector2((float)ellipse.Center.X, (float)ellipse.Center.Y), transform);
                    float maxR = (float)ellipse.MajorAxis;
                    UpdateBounds(eCenter + new SysVector2(-maxR, -maxR), $"{entityDesc} MinBounds");
                    UpdateBounds(eCenter + new SysVector2(maxR, maxR), $"{entityDesc} MaxBounds");
                    break;

                case LwPolyline lw:
                    foreach (var v in lw.Vertexes)
                    {
                        var pt = SysVector2.Transform(new SysVector2((float)v.Position.X, (float)v.Position.Y), transform);
                        UpdateBounds(pt, $"{entityDesc} Vertex");
                    }
                    break;

                case Polyline poly:
                    foreach (var v in poly.Vertexes)
                    {
                        var pt = SysVector2.Transform(new SysVector2((float)v.Position.X, (float)v.Position.Y), transform);
                        UpdateBounds(pt, $"{entityDesc} Vertex");
                    }
                    break;

                case Spline spline:
                    foreach (var cp in spline.ControlPoints)
                    {
                        var pt = SysVector2.Transform(new SysVector2((float)cp.Position.X, (float)cp.Position.Y), transform);
                        UpdateBounds(pt, $"{entityDesc} ControlPoint");
                    }
                    break;

                case netDxf.Entities.Text textObj:
                    textCount++;
                    var tPos = SysVector2.Transform(new SysVector2((float)textObj.Position.X, (float)textObj.Position.Y), transform);
                    UpdateBounds(tPos, $"{entityDesc} Position");
                    writer.WriteLine($"{indent}  Text Value: \"{textObj.Value}\" | Position: ({textObj.Position.X:F2}, {textObj.Position.Y:F2})");
                    break;

                case MText mtext:
                    textCount++;
                    var mtPos = SysVector2.Transform(new SysVector2((float)mtext.Position.X, (float)mtext.Position.Y), transform);
                    UpdateBounds(mtPos, $"{entityDesc} Position");
                    writer.WriteLine($"{indent}  MText Value: \"{mtext.Value}\" | Position: ({mtext.Position.X:F2}, {mtext.Position.Y:F2})");
                    break;

                case Insert insert:
                    insertCount++;
                    var pos = insert.Position;
                    var scale = insert.Scale;
                    float radAngle = (float)(insert.Rotation * Math.PI / 180.0);
                    var origin = insert.Block.Origin;

                    var localMat = SysMatrix4x4.CreateTranslation(-(float)origin.X, -(float)origin.Y, -(float)origin.Z) *
                                   SysMatrix4x4.CreateScale((float)scale.X, (float)scale.Y, (float)scale.Z) *
                                   SysMatrix4x4.CreateRotationZ(radAngle) *
                                   GetOcsMatrix(insert.Normal) *
                                   SysMatrix4x4.CreateTranslation((float)pos.X, (float)pos.Y, (float)pos.Z);

                    var combined = localMat * transform;

                    writer.WriteLine($"{indent}  Block: '{insert.Block.Name}' | Inserts at: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}) | Scale: ({scale.X:F2}, {scale.Y:F2})");

                    // Walk nested block entities recursively
                    foreach (var child in insert.Block.Entities)
                    {
                        ScanEntity(child, combined, $"{path}/{insert.Block.Name}", depth + 1);
                    }

                    // Walk insert attributes
                    foreach (var attr in insert.Attributes)
                    {
                        attrCount++;
                        var aPos = SysVector2.Transform(new SysVector2((float)attr.Position.X, (float)attr.Position.Y), transform);
                        UpdateBounds(aPos, $"{entityDesc}/Attribute[{attr.Tag}] Position");

                        bool isHidden = attr.Flags.HasFlag(AttributeFlags.Hidden);
                        writer.WriteLine($"{indent}    Attribute [{attr.Tag}] Value: \"{attr.Value}\" | Hidden: {isHidden} | Position: ({attr.Position.X:F2}, {attr.Position.Y:F2})");
                    }
                    break;
            }
        }

        foreach (var entity in entities)
        {
            ScanEntity(entity, SysMatrix4x4.Identity, selectedLayoutName, 0);
        }

        // If the layout has no entities or is empty, fallback to scanning document flat collections
        if (totalEntitiesScanned == 0)
        {
            Console.WriteLine("[DxfDiag] No entities found in layout block. Scanning flat collections...");
            writer.WriteLine("\n--- Flat Collections Backup Scanned ---");

            foreach (var line in doc.Lines) ScanEntity(line, SysMatrix4x4.Identity, "flat/lines", 0);
            foreach (var circle in doc.Circles) ScanEntity(circle, SysMatrix4x4.Identity, "flat/circles", 0);
            foreach (var arc in doc.Arcs) ScanEntity(arc, SysMatrix4x4.Identity, "flat/arcs", 0);
            foreach (var ellipse in doc.Ellipses) ScanEntity(ellipse, SysMatrix4x4.Identity, "flat/ellipses", 0);
            foreach (var lw in doc.LwPolylines) ScanEntity(lw, SysMatrix4x4.Identity, "flat/lwpolylines", 0);
            foreach (var poly in doc.Polylines) ScanEntity(poly, SysMatrix4x4.Identity, "flat/polylines", 0);
            foreach (var spline in doc.Splines) ScanEntity(spline, SysMatrix4x4.Identity, "flat/splines", 0);
            foreach (var text in doc.Texts) ScanEntity(text, SysMatrix4x4.Identity, "flat/texts", 0);
            foreach (var mtext in doc.MTexts) ScanEntity(mtext, SysMatrix4x4.Identity, "flat/mtexts", 0);
            foreach (var insert in doc.Inserts) ScanEntity(insert, SysMatrix4x4.Identity, "flat/inserts", 0);
        }

        writer.WriteLine();
        writer.WriteLine("================================================================================");
        writer.WriteLine("Diagnostics Summary");
        writer.WriteLine("================================================================================");
        writer.WriteLine($"Total Entities Scanned: {totalEntitiesScanned}");
        writer.WriteLine($"Total Inserts (Block References): {insertCount}");
        writer.WriteLine($"Total Texts (Text & MText): {textCount}");
        writer.WriteLine($"Total Block Attributes: {attrCount}");
        writer.WriteLine($"Calculated Bounds: Min({minX:F4}, {minY:F4}) | Max({maxX:F4}, {maxY:F4})");
        writer.WriteLine($"Span: Width = {Math.Max(0f, maxX - minX):F4} | Height = {Math.Max(0f, maxY - minY):F4}");
        writer.WriteLine();
        writer.WriteLine("--- Coordinate Outliers Detected ---");
        if (outliers.Count == 0)
        {
            writer.WriteLine("None! All coordinates reside within normal range (< 1,000,000).");
        }
        else
        {
            writer.WriteLine($"Found {outliers.Count} coordinate outlier entries:");
            foreach (var outlier in outliers)
            {
                writer.WriteLine($"  {outlier}");
            }
        }
        writer.WriteLine("================================================================================");

        Console.WriteLine("\n=================================");
        Console.WriteLine("Diagnostics Summary");
        Console.WriteLine("=================================");
        Console.WriteLine($"Total Entities Scanned: {totalEntitiesScanned}");
        Console.WriteLine($"Total Inserts: {insertCount}");
        Console.WriteLine($"Total Texts: {textCount}");
        Console.WriteLine($"Total Attributes: {attrCount}");
        if (hasData)
        {
            Console.WriteLine($"Calculated Bounds: Min({minX:F2}, {minY:F2}) | Max({maxX:F2}, {maxY:F2})");
            Console.WriteLine($"Span: Width = {(maxX - minX):F2} | Height = {(maxY - minY):F2}");
        }
        else
        {
            Console.WriteLine("Calculated Bounds: [No Data]");
        }
        Console.WriteLine($"Outlier Coordinates Found: {outliers.Count}");
        Console.WriteLine("Detailed trace report saved directly to: outliers.txt");
        Console.WriteLine("=================================");

        return 0;
    }

    private static SysMatrix4x4 GetOcsMatrix(netDxf.Vector3 normal)
    {
        var N = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3((float)normal.X, (float)normal.Y, (float)normal.Z));
        
        System.Numerics.Vector3 Wx;
        System.Numerics.Vector3 Wy;
        
        const float limit = 1.0f / 64.0f;
        if (Math.Abs(N.X) < limit && Math.Abs(N.Y) < limit)
        {
            Wx = System.Numerics.Vector3.Cross(new System.Numerics.Vector3(0f, 1f, 0f), N);
        }
        else
        {
            Wx = System.Numerics.Vector3.Cross(new System.Numerics.Vector3(0f, 0f, 1f), N);
        }
        
        Wx = System.Numerics.Vector3.Normalize(Wx);
        Wy = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(N, Wx));
        
        return new SysMatrix4x4(
            Wx.X, Wx.Y, Wx.Z, 0f,
            Wy.X, Wy.Y, Wy.Z, 0f,
            N.X,  N.Y,  N.Z,  0f,
            0f,   0f,   0f,   1f
        );
    }
}
