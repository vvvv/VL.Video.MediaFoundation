using Stride.Graphics;
using Stride.Rendering;
using System;

namespace VL.MediaFoundation
{
    sealed class ColorSpaceConverter : IDisposable
    {
        private readonly RenderDrawContext renderContext;
        private Texture srgbTexture;

        public ColorSpaceConverter(RenderDrawContext renderContext)
        {
            this.renderContext = renderContext;
        }

        public void Dispose()
        {
            srgbTexture?.Dispose();
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

            desc.Format = desc.Format.ToSRgb();
            desc.Flags = TextureFlags.ShaderResource;

            // Ensure sRGB texture has required format and dimension
            if (srgbTexture is null || desc != srgbTexture.Description)
            {
                srgbTexture?.Dispose();
                srgbTexture = Texture.New(graphicsDevice, desc);
            }

            // Copy the texture
            renderContext.CommandList.Copy(texture, srgbTexture);

            return srgbTexture;
        }
    }
}
