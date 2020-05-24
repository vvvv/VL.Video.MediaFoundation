using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Reactive.Linq;
using VL.Core;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using MapMode = SharpDX.Direct3D11.MapMode;

namespace VL.MediaFoundation
{
    public static class ImageDownload
    {
        public static IObservable<IImage> Download(this IObservable<Texture2D> textures, NodeContext nodeContext)
        {
            var stagingTexture = default(Texture2D);
            var width = 0;
            var height = 0;
            var format = Format.Unknown;
            var pixelFormat = format.ToPixelFormat();

            var provider = nodeContext.Factory.CreateService<IResourceProvider<Device>>(nodeContext);
            return Using(provider, device =>
            {
                return Observable.Create<IImage>(observer =>
                {
                    return textures.Subscribe(texture =>
                    {
                        var description = texture.Description;
                        if (stagingTexture is null || description.Width != width || description.Height != height || description.Format != format)
                        {
                            width = description.Width;
                            height = description.Height;
                            format = description.Format;
                            pixelFormat = format.ToPixelFormat();

                            description.ArraySize = 1;
                            description.Usage = ResourceUsage.Staging;
                            description.BindFlags = BindFlags.None;
                            description.CpuAccessFlags = CpuAccessFlags.Read;

                            stagingTexture?.Dispose();
                            stagingTexture = new Texture2D(device, description);
                        }

                        var deviceContext = device.ImmediateContext;
                        deviceContext.CopyResource(texture, stagingTexture);
                        //deviceContext.Flush();

                        var data = deviceContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);
                        try
                        {
                            var info = new ImageInfo(width, height, pixelFormat);
                            using var image = new IntPtrImage(data.DataPointer, data.RowPitch * height, info);
                            observer.OnNext(image);
                        }
                        finally
                        {
                            deviceContext.UnmapSubresource(stagingTexture, 0);
                        }
                    });
                });
            });
        }

        // Taken from VL.CoreLib
        static IObservable<T> Using<TResource, T>(this IResourceProvider<TResource> provider, Func<TResource, IObservable<T>> observableFactory)
            where TResource : class, IDisposable
        {
            if (provider is null)
                throw new ArgumentNullException(nameof(provider));

            if (observableFactory is null)
                throw new ArgumentNullException(nameof(observableFactory));

            return Observable.Using(
                resourceFactory: () => provider.GetHandle(),
                observableFactory: h => observableFactory(h.Resource));
        }

        static PixelFormat ToPixelFormat(this Format format)
        {
            var isSRgb = false;
            switch (format)
            {
                case Format.Unknown:
                    return PixelFormat.Unknown;
                case Format.R8_UNorm:
                    return PixelFormat.R8;
                case Format.R16_UNorm:
                    return PixelFormat.R16;
                case Format.R32_Float:
                    return PixelFormat.R32F;
                case Format.R8G8B8A8_UNorm:
                    return PixelFormat.R8G8B8A8;
                case Format.R8G8B8A8_UNorm_SRgb:
                    isSRgb = true;
                    return PixelFormat.R8G8B8A8;
                case Format.B8G8R8X8_UNorm:
                    return PixelFormat.B8G8R8X8;
                case Format.B8G8R8X8_UNorm_SRgb:
                    isSRgb = true;
                    return PixelFormat.B8G8R8X8;
                case Format.B8G8R8A8_UNorm:
                    return PixelFormat.B8G8R8A8;
                case Format.B8G8R8A8_UNorm_SRgb:
                    isSRgb = true;
                    return PixelFormat.B8G8R8A8;
                case Format.R32G32_Float:
                    return PixelFormat.R32G32F;
                case Format.R32G32B32A32_Float:
                    return PixelFormat.R32G32B32A32F;
                default:
                    throw new Exception("Unsupported texture format");
            }
        }
    }
}
