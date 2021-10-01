using SharpDX.Direct3D11;
using Stride.Core.Mathematics;
using System;
using System.Collections.Generic;

namespace VL.MediaFoundation
{
    abstract class TextureProvider<TImage> : IDisposable
    {
        private readonly Dictionary<(int, int, SharpDX.DXGI.Format, BindFlags), Stack<Texture2D>> cache = new Dictionary<(int, int, SharpDX.DXGI.Format, BindFlags), Stack<Texture2D>>();

        public abstract Device Device { get; }

        public Texture2D GetTexture(int width, int height, SharpDX.DXGI.Format format = SharpDX.DXGI.Format.B8G8R8A8_UNorm, BindFlags bindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource)
        {
            lock (cache)
            {
                var key = (width, height, format, bindFlags);
                var stack = cache.EnsureValue(key, s => new Stack<Texture2D>(1));
                if (stack.Count > 0)
                    return stack.Pop();

                return new Texture2D(Device, new Texture2DDescription()
                {
                    Width = width,
                    Height = height,
                    ArraySize = 1,
                    BindFlags = bindFlags,
                    CpuAccessFlags = CpuAccessFlags.None,
                    Format = format,
                    MipLevels = 1,
                    OptionFlags = ResourceOptionFlags.None,
                    SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                    Usage = ResourceUsage.Default
                })
                { 
                    Tag = this
                };
            }
        }

        public void Release(Texture2D texture)
        {
            if (texture.Tag == this)
            {
                lock (cache)
                {
                    var description = texture.Description;
                    var key = (description.Width, description.Height, description.Format, description.BindFlags);
                    if (!cache.TryGetValue(key, out var stack))
                        throw new InvalidOperationException();

                    stack.Push(texture);
                }
            }
            else
            {
                texture.Dispose();
            }
        }

        public void Recycle()
        {
            lock (cache)
            {
                foreach (var s in cache.Values)
                {
                    foreach (var t in s)
                        t.Dispose();
                }
                cache.Clear();
            }
        }

        public abstract TImage AsImage(Texture2D texture);

        public virtual void Dispose()
        {
            Recycle();
        }
    }
}
