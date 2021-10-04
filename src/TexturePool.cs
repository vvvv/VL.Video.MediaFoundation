using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;

namespace VL.MediaFoundation
{
    sealed partial class TexturePool : IDisposable
    {
        private readonly Dictionary<Texture2DDescription, Stack<PooledTextured2D>> cache = new Dictionary<Texture2DDescription, Stack<PooledTextured2D>>();

        public TexturePool(Device device)
        {
            Device = device;
        }

        public Device Device { get; }

        internal PooledTextured2D Rent(in Texture2DDescription description)
        {
            lock (cache)
            {
                var stack = GetStack(in description);
                if (stack.Count > 0)
                    return stack.Pop();

                return new PooledTextured2D(this, Device, description);
            }
        }

        internal void Return(PooledTextured2D texture)
        {
            lock (cache)
            {
                var description = texture.Description;
                var stack = GetStack(in description);
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

        private Stack<PooledTextured2D> GetStack(in Texture2DDescription description)
        {
            return cache.EnsureValue(description, s => new Stack<PooledTextured2D>(2));
        }
    }
}
