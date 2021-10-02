using SharpDX.Direct3D11;
using System;

namespace VL.MediaFoundation
{
    public abstract class DeviceProvider : IDisposable
    {
        public abstract Device Device { get; }

        public abstract void Dispose();
    }
}
