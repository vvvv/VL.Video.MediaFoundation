using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using System;
using System.Diagnostics;
using System.Threading;

namespace VL.Video.MediaFoundation
{
    public sealed class VideoFrame : IDisposable
    {
        internal readonly Texture2D NativeTexture;

        private readonly IDisposable Handle;
        private int RefCount;

        private readonly int initialRefCount;

        internal VideoFrame(Texture2D nativeTexture, IDisposable handle)
        {
            NativeTexture = nativeTexture;
            Handle = handle;
            RefCount = 1;
            initialRefCount = GetRefCount(nativeTexture);
        }

        static int GetRefCount(IUnknown unknown)
        {
            unknown.AddReference();
            return unknown.Release();
        }

        internal void AddRef()
        {
            Interlocked.Increment(ref RefCount);
        }

        internal void Release()
        {
            if (Interlocked.Decrement(ref RefCount) == 0)
                Destroy();
        }

        public void Dispose()
        {
            Release();
        }

        void Destroy()
        {
            Debug.Assert(initialRefCount == GetRefCount(NativeTexture));
            Handle.Dispose();
        }
    }
}
