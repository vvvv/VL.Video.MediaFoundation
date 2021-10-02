using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;

namespace VL.Video.MediaFoundation
{
    class TexturePool : IDisposable
    {
        private readonly Dictionary<Texture2DDescription, Stack<Texture2D>> cache = new Dictionary<Texture2DDescription, Stack<Texture2D>>();

        public TexturePool(Device device)
        {
            Device = device;
        }

        public Device Device { get; }

        public Texture2D Rent(Texture2DDescription description)
        {
            lock (cache)
            {
                var stack = cache.EnsureValue(description, s => new Stack<Texture2D>(1));
                if (stack.Count > 0)
                    return stack.Pop();

                return new Texture2D(Device, description);
            }
        }

        public void Return(Texture2D texture)
        {
            lock (cache)
            {
                var description = texture.Description;
                if (!cache.TryGetValue(description, out var stack))
                    throw new InvalidOperationException();

                stack.Push(texture);
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

        public void Dispose()
        {
            Recycle();
        }
    }
}
