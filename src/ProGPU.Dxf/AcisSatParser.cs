using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace ProGPU.Dxf;

public class AcisEntity
{
    public int Index;
    public string Type = string.Empty;
    public string RawLine = string.Empty;
    
    // Extracted pointers (indices starting with $)
    public List<int> Pointers = new();
    // Extracted double values
    public List<double> Values = new();
}

public struct Acis3dEdge
{
    public Vector3 StartPoint;
    public Vector3 EndPoint;
    public string CurveType;
}

public class AcisSatParser
{
    public static List<Acis3dEdge> ParseSat(string satText)
    {
        var edges = new List<Acis3dEdge>();
        var entities = new List<AcisEntity>();

        using var reader = new StringReader(satText);
        string? line;
        bool bodyStarted = false;

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Split into tokens
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            string type = parts[0].ToLowerInvariant();
            
            // Check if this is an entity definition line
            if (type == "body" || type == "lump" || type == "shell" || type == "face" || 
                type == "loop" || type == "coedge" || type == "edge" || type == "vertex" || 
                type == "point" || type == "straight" || type == "ellipse" || type == "intcurve")
            {
                bodyStarted = true;
            }

            if (!bodyStarted) continue;

            var entity = new AcisEntity
            {
                Index = entities.Count,
                Type = type,
                RawLine = line
            };

            // Parse pointers and double values from tokens
            for (int i = 1; i < parts.Length; i++)
            {
                string token = parts[i];
                if (token.StartsWith("$"))
                {
                    if (int.TryParse(token.Substring(1), out int ptrIdx))
                    {
                        entity.Pointers.Add(ptrIdx);
                    }
                    else
                    {
                        entity.Pointers.Add(-1);
                    }
                }
                else if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    entity.Values.Add(val);
                }
            }

            entities.Add(entity);
        }

        // Now, extract all edges
        foreach (var ent in entities)
        {
            if (ent.Type == "edge")
            {
                // In an edge record, pointers are:
                // Pointers[0]: coedge1
                // Pointers[1]: coedge2
                // Pointers[2]: start vertex
                // Pointers[3]: end vertex
                // Pointers[4]: curve geometry
                if (ent.Pointers.Count >= 4)
                {
                    int v1Idx = ent.Pointers[2];
                    int v2Idx = ent.Pointers[3];

                    if (v1Idx >= 0 && v1Idx < entities.Count && v2Idx >= 0 && v2Idx < entities.Count)
                    {
                        var v1 = entities[v1Idx];
                        var v2 = entities[v2Idx];

                        // In a vertex record, the second pointer is the point geometry
                        if (v1.Type == "vertex" && v1.Pointers.Count >= 2 &&
                            v2.Type == "vertex" && v2.Pointers.Count >= 2)
                        {
                            int p1Idx = v1.Pointers[1];
                            int p2Idx = v2.Pointers[1];

                            if (p1Idx >= 0 && p1Idx < entities.Count && p2Idx >= 0 && p2Idx < entities.Count)
                            {
                                var pt1 = entities[p1Idx];
                                var pt2 = entities[p2Idx];

                                // In a point record, the double values represent the coordinates (X, Y, Z)
                                if (pt1.Type == "point" && pt1.Values.Count >= 3 &&
                                    pt2.Type == "point" && pt2.Values.Count >= 3)
                                {
                                    var startPt = new Vector3((float)pt1.Values[0], (float)pt1.Values[1], (float)pt1.Values[2]);
                                    var endPt = new Vector3((float)pt2.Values[0], (float)pt2.Values[1], (float)pt2.Values[2]);

                                    string curveType = "straight";
                                    if (ent.Pointers.Count >= 5)
                                    {
                                        int curveIdx = ent.Pointers[4];
                                        if (curveIdx >= 0 && curveIdx < entities.Count)
                                        {
                                            curveType = entities[curveIdx].Type;
                                        }
                                    }

                                    edges.Add(new Acis3dEdge
                                    {
                                        StartPoint = startPt,
                                        EndPoint = endPt,
                                        CurveType = curveType
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }

        return edges;
    }
}
