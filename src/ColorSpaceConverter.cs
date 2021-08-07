using Stride.Graphics;
using Stride.Rendering;

namespace VL.MediaFoundation
{
    sealed class ColorSpaceConverter
    {
        private readonly RenderDrawContext renderContext;

        public ColorSpaceConverter(RenderDrawContext renderContext)
        {
            this.renderContext = renderContext;
        }

        public Texture ToDeviceColorSpace(Texture texture)
        {
            var graphicsDevice = renderContext.GraphicsDevice;

            // Check if conversion is needed
            if (graphicsDevice.ColorSpace == ColorSpace.Gamma)
                return texture;

            var desc = texture.Description;
            if (desc.Format.IsSRgb() || !desc.Format.HasSRgbEquivalent())
                return texture;

            // Create texture with sRGB format
            desc.Format = desc.Format.ToSRgb();
            desc.Flags = TextureFlags.ShaderResource;
            var srgbTexture = Texture.New(graphicsDevice, desc);

            // Copy the texture
            renderContext.CommandList.Copy(texture, srgbTexture);

            // Release input texture
            texture.Dispose();

            return srgbTexture;
        }
    }
}
