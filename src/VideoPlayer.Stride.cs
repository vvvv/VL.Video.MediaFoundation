using SharpDX.Direct3D11;
using Stride.Core;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using VL.Core;
using VL.Lib.Basics.Resources;
using VL.Stride;

namespace VL.MediaFoundation
{
    public class StrideVideoPlayer : VideoPlayer<Texture>
    {
        private readonly IResourceHandle<RenderDrawContext> renderDrawContextHandle;
        private readonly ColorSpaceConverter colorSpaceConverter;

        public StrideVideoPlayer(NodeContext nodeContext)
        {
            renderDrawContextHandle = nodeContext.GetGameProvider()
                .Bind(g => RenderContext.GetShared(g.Services).GetThreadContext())
                .GetHandle() ?? throw new ServiceNotFoundException(typeof(IResourceProvider<Game>));

            colorSpaceConverter = new ColorSpaceConverter(renderDrawContextHandle.Resource);

            var graphicsDevice = renderDrawContextHandle.Resource.GraphicsDevice;
            var device = SharpDXInterop.GetNativeDevice(graphicsDevice) as Device;
            if (device != null)
                Initialize(device);
        }

        protected override Texture AsImage(Texture2D videoTexture)
        {
            var texture = SharpDXInterop.CreateTextureFromNative(renderDrawContextHandle.Resource.GraphicsDevice, videoTexture, takeOwnership: true);
            // Apply color space conversion if necessary
            return colorSpaceConverter.ToDeviceColorSpace(texture);
        }

        public override void Dispose()
        {
            base.Dispose();

            renderDrawContextHandle.Dispose();
        }
    }
}
