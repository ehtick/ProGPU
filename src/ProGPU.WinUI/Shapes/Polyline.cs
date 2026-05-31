using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.Numerics;

namespace Microsoft.UI.Xaml.Shapes;

public class Polyline : Shape
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(
            "Points",
            typeof(List<Vector2>),
            typeof(Polyline),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public List<Vector2> Points
    {
        get
        {
            var pts = (List<Vector2>)GetValue(PointsProperty);
            if (pts == null)
            {
                pts = new List<Vector2>();
                SetValue(PointsProperty, pts);
            }
            return pts;
        }
        set => SetValue(PointsProperty, value);
    }

    private readonly PathGeometry _geometry = new();

    public override Geometry DefiningGeometry
    {
        get
        {
            _geometry.Figures.Clear();
            var pts = Points;
            if (pts != null && pts.Count > 0)
            {
                var fig = new PathFigure(pts[0], isClosed: false);
                for (int i = 1; i < pts.Count; i++)
                {
                    fig.Segments.Add(new LineSegment(pts[i]));
                }
                _geometry.Figures.Add(fig);
            }
            return _geometry;
        }
    }
}
