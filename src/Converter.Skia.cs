using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SkiaSharp;
using VL.Core;
using VL.Lib.Basics.Resources;
using VL.Skia;
using VL.Skia.Egl;

namespace VL.Video.MediaFoundation
{
    class SkiaConverter
    {
        private readonly RenderContext renderContext;

        public SkiaConverter(NodeContext nodeContext)
        {
            renderContext = RenderContext.ForCurrentThread();
        }

        public SKImage AsImage(Texture2D nativeTexture)
        {
            var eglContext = renderContext.EglContext;
            var eglDisplay = eglContext.Dislpay;
            var eglImage = eglContext.CreateImageFromD3D11Texture(nativeTexture.NativePointer);

            uint textureId = 0;
            NativeGles.glGenTextures(1, ref textureId);
            NativeGles.glBindTexture(NativeGles.GL_TEXTURE_2D, textureId);
            NativeGles.glEGLImageTargetTexture2DOES(NativeGles.GL_TEXTURE_2D, eglImage);
            NativeGles.glBindTexture(NativeGles.GL_TEXTURE_2D, 0);

            var description = nativeTexture.Description;
            var colorType = GetColorType(description.Format);
            var glInfo = new GRGlTextureInfo(
                id: textureId,
                target: NativeGles.GL_TEXTURE_2D,
                format: colorType.ToGlSizedFormat());

            using var backendTexture = new GRBackendTexture(
                width: description.Width,
                height: description.Height,
                mipmapped: false,
                glInfo: glInfo);

            var image = SKImage.FromTexture(
                renderContext.SkiaContext,
                backendTexture,
                GRSurfaceOrigin.TopLeft,
                colorType,
                SKAlphaType.Premul,
                colorspace: SKColorSpace.CreateSrgb(),
                releaseProc: _ =>
                {
                    NativeGles.glDeleteTextures(1, ref textureId);
                    eglImage.Dispose();
                    nativeTexture.Release();
                });

            if (image != null)
                nativeTexture.AddRef();

            return image;
        }

        static SKColorType GetColorType(Format format)
        {
            switch (format)
            {
                case Format.B5G6R5_UNorm:
                    return SKColorType.Rgb565;
                case Format.B8G8R8A8_UNorm:
                case Format.B8G8R8A8_UNorm_SRgb:
                    return SKColorType.Bgra8888;
                case Format.R8G8B8A8_UNorm:
                case Format.R8G8B8A8_UNorm_SRgb:
                    return SKColorType.Rgba8888;
                case Format.R10G10B10A2_UNorm:
                    return SKColorType.Rgba1010102;
                case Format.R16G16B16A16_Float:
                    return SKColorType.RgbaF16;
                case Format.R16G16B16A16_UNorm:
                    return SKColorType.Rgba16161616;
                case Format.R32G32B32A32_Float:
                    return SKColorType.RgbaF32;
                case Format.R16G16_Float:
                    return SKColorType.RgF16;
                case Format.R16G16_UNorm:
                    return SKColorType.Rg1616;
                case Format.R8G8_UNorm:
                    return SKColorType.Rg88;
                case Format.A8_UNorm:
                    return SKColorType.Alpha8;
                case Format.R8_UNorm:
                    return SKColorType.Gray8;
                default:
                    return SKColorType.Unknown;
            }
        }
    }
}
