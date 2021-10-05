using SharpDX.Direct3D11;
using VL.Core;
using VL.Skia;

namespace VL.Video.MediaFoundation
{
    public class SkiaDeviceProvider : DeviceProvider
    {
        private readonly RenderContext renderContext;

        public SkiaDeviceProvider(NodeContext nodeContext)
        {
            renderContext = RenderContext.ForCurrentThread();

            if (renderContext.EglContext.Dislpay.TryGetD3D11Device(out var d3dDevice))
            {
                Device = new Device(d3dDevice);
            }
        }

        public override Device Device { get; }

        public override void Dispose()
        {
            renderContext.Dispose();
        }
    }
}
