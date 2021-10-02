using SharpDX.Direct3D11;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using System.Runtime.CompilerServices;
using VL.Core;
using VL.Lib.Basics.Resources;
using VL.Stride;

namespace VL.MediaFoundation
{
    sealed class StrideImageAllocator : ImageAllocator<Texture>
    {
        private readonly ConditionalWeakTable<Texture2D, Texture> textureMap = new ConditionalWeakTable<Texture2D, Texture>();
        private readonly IResourceHandle<RenderDrawContext> renderDrawContextHandle;
        private readonly RenderDrawContext renderContext;

        public StrideImageAllocator(NodeContext nodeContext)
        {
            renderDrawContextHandle = nodeContext.GetGameProvider()
                .Bind(g => RenderContext.GetShared(g.Services).GetThreadContext())
                .GetHandle() ?? throw new ServiceNotFoundException(typeof(IResourceProvider<Game>));
            renderContext = renderDrawContextHandle.Resource;

            var graphicsDevice = renderDrawContextHandle.Resource.GraphicsDevice;
            Device = SharpDXInterop.GetNativeDevice(graphicsDevice) as Device;
        }

        public override Device Device { get; }

        public override Texture AsImage(Texture2D videoTexture)
        {
            var texture = ToDeviceColorSpace(videoTexture);
            return FromNative(texture);
        }

        private Texture FromNative(Texture2D nativeTexture)
        {
            return textureMap.GetValue(nativeTexture, n =>
            {
                var texture = SharpDXInterop.CreateTextureFromNative(renderDrawContextHandle.Resource.GraphicsDevice, n, takeOwnership: false);
                texture.Tags.Set(TextureRefCounter.CustomDisposeAction, t => Release(nativeTexture));
                return texture;
            });
        }

        private Texture2D ToDeviceColorSpace(Texture2D texture)
        {
            var graphicsDevice = renderContext.GraphicsDevice;

            // Check if conversion is needed
            if (graphicsDevice.ColorSpace == ColorSpace.Gamma)
                return texture;

            var desc = texture.Description;

            // Create texture with sRGB format
            var srgbTexture = GetTexture(desc.Width, desc.Height, SharpDX.DXGI.Format.B8G8R8A8_UNorm_SRgb, BindFlags.ShaderResource);

            // Copy the texture
            Device.ImmediateContext.CopyResource(texture, srgbTexture);

            // Release input texture
            Release(texture);

            return srgbTexture;
        }

        public override void Dispose()
        {
            base.Dispose();
            renderDrawContextHandle.Dispose();
        }
    }
}
