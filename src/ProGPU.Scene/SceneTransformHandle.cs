using System.Numerics;

namespace ProGPU.Scene;

/// <summary>
/// Mutable placement for retained picture commands. Updating the matrix advances a lightweight
/// GPU-patch version without invalidating or rebuilding the picture's vector and glyph data.
/// </summary>
public sealed class SceneTransformHandle
{
    private Vector2 _translation;
    private long _version = 1;

    /// <summary>
    /// Gets or sets the retained picture translation in logical coordinates. The compositor
    /// snaps the combined translation to a physical pixel so cached glyph coverage keeps the
    /// phase at which it was rasterized.
    /// </summary>
    public Vector2 Translation
    {
        get => _translation;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Scene translation must be finite.");
            }

            if (_translation == value)
            {
                return;
            }

            _translation = value;
            unchecked
            {
                _version++;
                if (_version <= 0)
                {
                    _version = 1;
                }
            }
        }
    }

    public long Version => Volatile.Read(ref _version);

    internal Matrix4x4 Matrix => Matrix4x4.CreateTranslation(_translation.X, _translation.Y, 0f);
}
