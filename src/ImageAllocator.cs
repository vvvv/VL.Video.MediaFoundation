using SharpDX.Direct3D11;
using Stride.Core.Mathematics;
using System;
using System.Collections.Generic;

namespace VL.MediaFoundation
{
    abstract class ImageAllocator<TImage> : IDisposable
    {
        public abstract Device Device { get; }

        public abstract Texture2D GetTexture(TImage image);

        public abstract TImage AsImage(Texture2D texture);

        public abstract void Dispose();
    }
}
