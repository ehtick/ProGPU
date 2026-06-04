using System;
using System.Numerics;
using ProGPU.Backend;

namespace ProGPU.Scene
{
    public class ImageEffectParams
    {
        public GpuTexture Texture { get; set; }
        public Rect Rect { get; set; }
        public float Brightness { get; set; } = 0f; // Offset [-1, 1]
        public float Contrast { get; set; } = 1f;   // Multiplier [0, 2]
        public float Saturation { get; set; } = 1f; // Multiplier [0, 2]
        public float Grayscale { get; set; } = 0f;  // Weight [0, 1]
        public float Sepia { get; set; } = 0f;      // Weight [0, 1]
        public float Invert { get; set; } = 0f;     // Weight [0, 1]
        public float BlurSigma { get; set; } = 0f;  // Blur amount
        public GpuTexture? MaskTexture { get; set; }
    }
}
