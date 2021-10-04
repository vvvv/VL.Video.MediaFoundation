using SharpDX.Direct3D11;
using Stride.Core;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using System;
using VL.Core;
using VL.Lib.Basics.Resources;
using VL.Stride;

namespace VL.MediaFoundation
{
    public class StrideDeviceProvider : DeviceProvider
    {
        private readonly IResourceHandle<RenderDrawContext> renderDrawContextHandle;

        public StrideDeviceProvider(NodeContext nodeContext)
        {
            renderDrawContextHandle = nodeContext.GetGameProvider()
                .Bind(g => RenderContext.GetShared(g.Services).GetThreadContext())
                .GetHandle() ?? throw new ServiceNotFoundException(typeof(IResourceProvider<Game>));

            var graphicsDevice = renderDrawContextHandle.Resource.GraphicsDevice;
            Device = SharpDXInterop.GetNativeDevice(graphicsDevice) as Device;
        }

        public override Device Device { get; }

        public override void Dispose()
        {
            renderDrawContextHandle.Dispose();
        }
    }
}
