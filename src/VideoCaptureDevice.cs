using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using VL.Lib.Basics.Imaging;

namespace VL.MediaFoundation
{
    // Good source: https://stackoverflow.com/questions/40913196/how-to-properly-use-a-hardware-accelerated-media-foundation-source-reader-to-dec
    public partial class VideoCaptureDevice : IDisposable
    {
        private readonly Activate activate;
        private readonly MediaSource mediaSource;
        private readonly Device d3dDevice;
        private readonly SourceReader reader;
        private readonly DXGIDeviceManager manager;

        public VideoCaptureDevice(bool useHardwareAcceleration, VideoCaptureDeviceEnumEntry device = default)
        {
            var mediaSourceAttributes = new MediaAttributes();
            mediaSourceAttributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVideoCapture.Guid);

            var symbolicLink = device?.Tag as string;
            if (!string.IsNullOrEmpty(symbolicLink))
            {
                // Use symbolic link (https://docs.microsoft.com/en-us/windows/win32/medfound/audio-video-capture-in-media-foundation)
                mediaSourceAttributes.Set(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink, symbolicLink);
                MediaFactory.CreateDeviceSource(mediaSourceAttributes, out mediaSource);
            }
            else
            {
                // Auto select
                activate = MediaFactory.EnumDeviceSources(mediaSourceAttributes)[0];
                mediaSource = activate.ActivateObject<MediaSource>();
            }

            // Setup source reader arguments
            var sourceReaderAttributes = new MediaAttributes();
            // Needed in order to read data as Argb32
            sourceReaderAttributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, true);

            // Hardware acceleration
            if (useHardwareAcceleration)
            {
                d3dDevice = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport);
                // Add multi thread protection on device (MF is multi-threaded)
                var deviceMultithread = d3dDevice.QueryInterface<DeviceMultithread>();
                deviceMultithread.SetMultithreadProtected(true);
                // Reset device
                manager = new DXGIDeviceManager();
                manager.ResetDevice(d3dDevice);

                sourceReaderAttributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1);
                sourceReaderAttributes.Set(SourceReaderAttributeKeys.D3DManager, manager);
            }

            // Create the source reader
            reader = new SourceReader(mediaSource, sourceReaderAttributes);

            // Set output format to BGRA
            var mt = new MediaType();
            mt.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            mt.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Argb32);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, mt);

            Frames = Observable.Create<IImage>(async (observer, token) =>
            {
                await Task.Run(() =>
                {
                    var currentMt = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
                    VideoCaptureHelpers.ParseSize(currentMt.Get(MediaTypeAttributeKeys.FrameSize), out var width, out var height);
                    var info = new ImageInfo(width, height, PixelFormat.B8G8R8A8);

                    while (!token.IsCancellationRequested)
                    {
                        using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlags.None, out var streamIndex, out var streamFlags, out var timestamp);
                        if (sample != null)
                        {
                            using var buffer = sample.ConvertToContiguousBuffer();
                            var ptr = buffer.Lock(out var maxLength, out var currentLength);
                            try
                            {
                                using var image = new IntPtrImage(ptr, currentLength, info);
                                // For 2020.2
                                //var image = new MFImage(info, buffer);
                                observer.OnNext(image);
                            }
                            finally
                            {
                                buffer.Unlock();
                            }
                        }
                    }
                }, token);
            });
        }

        public IObservable<IImage> Frames { get; }

        public void Dispose()
        {
            reader.Dispose();
            mediaSource.Dispose();
            activate?.DetachObject();
            manager?.Dispose();
            d3dDevice?.Dispose();
        }
    }
}
