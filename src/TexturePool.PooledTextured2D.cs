using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using System;

namespace VL.MediaFoundation
{
    interface IRefCounted
    {
        void AddRef();
        void Release();
    }

    sealed class PooledTextured2D : Texture2D, IRefCounted
    {
        private readonly TexturePool pool;
        private int refCount;

        public PooledTextured2D(TexturePool pool, Device device, Texture2DDescription description)
            : base(device, description)
        {
            this.pool = pool;
        }

        public void AddRef()
        {
            refCount++;
        }

        public void Release()
        {
            if (--refCount == 0)
                Return();
        }

        public void Return()
        {
            this.pool?.Return(this);
        }
    }

    sealed class LinkedTexture2D : Texture2D, IRefCounted
    {
        private readonly IDisposable handle;
        private int refCount;

        public LinkedTexture2D(IntPtr nativePtr, IDisposable handle)
            : base(nativePtr)
        {
            this.handle = handle;
        }

        public void AddRef()
        {
            refCount++;
        }

        public void Release()
        {
            if (--refCount == 0)
                Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            handle.Dispose();
        }
    }
}
