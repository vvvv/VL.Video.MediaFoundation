using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using VL.Core;
using VL.Lib.Basics.Resources;
using VL.Stride;

namespace VL.MediaFoundation
{
    // Good source: https://stackoverflow.com/questions/40913196/how-to-properly-use-a-hardware-accelerated-media-foundation-source-reader-to-dec
    public partial class VideoCapture : IDisposable
    {
        private static readonly Guid s_IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        private readonly SerialDisposable deviceSubscription = new SerialDisposable();
        private readonly IResourceHandle<RenderDrawContext> renderDrawContextHandle;
        private readonly ColorSpaceConverter colorSpaceConverter;
        private BlockingCollection<Texture> videoFrames;
        private string deviceSymbolicLink;
        private Int2 preferredSize;
        private float preferredFps;
        private bool enabled = true;
        private int discardedFrames;
        private float actualFps;

        public VideoCapture(NodeContext nodeContext)
        {
            renderDrawContextHandle = GetRenderDrawContextHandle(nodeContext) ?? throw new ServiceNotFoundException(typeof(IResourceProvider<Game>));
            colorSpaceConverter = new ColorSpaceConverter(renderDrawContextHandle.Resource);
        }

        static IResourceHandle<RenderDrawContext> GetRenderDrawContextHandle(NodeContext nodeContext)
        {
            return nodeContext.GetGameProvider()?
                .Bind(g => RenderContext.GetShared(g.Services).GetThreadContext())
                .GetHandle();
        }

        public VideoCaptureDeviceEnumEntry Device
        {
            set
            {
                var s = value?.Tag as string;
                if (s != deviceSymbolicLink)
                {
                    deviceSymbolicLink = s;
                    deviceSubscription.Disposable = null;
                }
            }
        }

        public Int2 PreferredSize
        {
            set
            {
                if (value != preferredSize)
                {
                    preferredSize = value;
                    deviceSubscription.Disposable = null;
                }
            }
        }

        public float PreferredFps
        {
            set
            {
                if (value != preferredFps)
                {
                    preferredFps = value;
                    deviceSubscription.Disposable = null;
                }
            }
        }

        public float? Exposure
        {
            set
            {
                if (value != exposure.Value)
                    exposure.OnNext(value);
            }
        }
        readonly BehaviorSubject<float?> exposure = new BehaviorSubject<float?>(default);

        public bool Enabled
        {
            get => enabled;
            set
            {
                if (value != Enabled)
                {
                    enabled = value;
                    deviceSubscription.Disposable = null;
                }
            }
        }

        public Texture CurrentVideoFrame
        {
            get => currentVideoFrame;
            private set
            {
                if (value != currentVideoFrame)
                {
                    currentVideoFrame?.Dispose();
                    currentVideoFrame = value;
                }
            }
        }
        Texture currentVideoFrame;

        public int DiscardedFrames => discardedFrames;
        public float ActualFPS => actualFps;

        public Texture Update(int waitTimeInMilliseconds)
        {
            if (enabled)
            {
                if (deviceSubscription.Disposable is null)
                {
                    deviceSubscription.Disposable = StartNewCapture();
                }

                FetchCurrentVideoFrame(waitTimeInMilliseconds);
            }
            return currentVideoFrame;

            MediaSource CreateMediaSource()
            {
                using var mediaSourceAttributes = new MediaAttributes();
                mediaSourceAttributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVideoCapture.Guid);

                if (!string.IsNullOrEmpty(deviceSymbolicLink))
                {
                    // Use symbolic link (https://docs.microsoft.com/en-us/windows/win32/medfound/audio-video-capture-in-media-foundation)
                    mediaSourceAttributes.Set(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink, deviceSymbolicLink);
                    MediaFactory.CreateDeviceSource(mediaSourceAttributes, out var mediaSource);
                    return mediaSource;
                }
                else
                {
                    // Auto select
                    using var activate = MediaFactory.EnumDeviceSources(mediaSourceAttributes).FirstOrDefault();
                    return activate?.ActivateObject<MediaSource>();
                }
            }

            IDisposable StartNewCapture()
            {
                var videoFrames = new BlockingCollection<Texture>(boundedCapacity: 1);

                var pollTask = Task.Run(() =>
                {
                    // Create the media source based on the selected symbolic link
                    using var mediaSource = CreateMediaSource();
                    if (mediaSource is null)
                        return;

                    // Setup source reader arguments
                    using var sourceReaderAttributes = new MediaAttributes();
                    // Enable low latency - we don't want frames to get buffered
                    sourceReaderAttributes.Set(SinkWriterAttributeKeys.LowLatency, true);
                    // Ensure DXVA is enabled
                    sourceReaderAttributes.Set(SourceReaderAttributeKeys.DisableDxva, 0);
                    // Needed in order to read data as Argb32
                    sourceReaderAttributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, true);

                    // Hardware acceleration
                    var graphicsDevice = renderDrawContextHandle?.Resource.GraphicsDevice;
                    if (graphicsDevice != null && SharpDXInterop.GetNativeDevice(graphicsDevice) is Device d3dDevice)
                    {
                        // Add multi thread protection on device (MF is multi-threaded)
                        var deviceMultithread = d3dDevice.QueryInterface<DeviceMultithread>();
                        deviceMultithread.SetMultithreadProtected(true);
                        // Reset device
                        using var manager = new DXGIDeviceManager();
                        manager.ResetDevice(d3dDevice);
                        sourceReaderAttributes.Set(SourceReaderAttributeKeys.D3DManager, manager);
                    }

                    // Connect camera controls
                    using var exposureSubscription = mediaSource.SetCameraValue(CameraControlPropertyName.Exposure, exposure);

                    // Find best capture format for device
                    var bestCaptureFormat = mediaSource.EnumerateCaptureFormats()
                        .FirstOrDefault(f => f.size == preferredSize && f.fr == preferredFps);
                    if (bestCaptureFormat != null)
                        mediaSource.SetCaptureFormat(bestCaptureFormat.mediaType);

                    // Create the source reader
                    using var reader = new SourceReader(mediaSource, sourceReaderAttributes);

                    // Set output format to BGRA
                    using var mt = new MediaType();
                    mt.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                    mt.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Argb32);
                    reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, mt);

                    // Read the actual FPS
                    using var currentMediaType = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
                    actualFps = VideoCaptureHelpers.ParseFrameRate(currentMediaType.Get(MediaTypeAttributeKeys.FrameRate));
                    
                    // Reset the discared frame count
                    discardedFrames = 0;

                    while (!videoFrames.IsAddingCompleted)
                    {
                        var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlags.None, out var streamIndex, out var streamFlags, out var timestamp);
                        if (sample is null)
                            continue;

                        if (sample.BufferCount == 0)
                        {
                            sample.Dispose();
                            continue;
                        }

                        var buffer = sample.BufferCount == 1 ? sample.GetBufferByIndex(0) : sample.ConvertToContiguousBuffer();

                        var dxgiBuffer = buffer.QueryInterfaceOrNull<DXGIBuffer>();
                        if (dxgiBuffer != null)
                        {
                            dxgiBuffer.GetResource(s_IID_ID3D11Texture2D, out var pTexture);
                            var dxTexture = new Texture2D(pTexture);
                            var texture = SharpDXInterop.CreateTextureFromNative(graphicsDevice, dxTexture, takeOwnership: true);

                            buffer.DisposeBy(texture);
                            dxgiBuffer.DisposeBy(texture);
                            sample.DisposeBy(texture);

                            try
                            {
                                videoFrames.Add(texture);
                            }
                            catch (InvalidOperationException)
                            {
                                texture.Dispose();
                            }
                        }
                        else
                        {
                            buffer.Dispose();
                            sample.Dispose();
                        }
                    }
                });

                // Make the queue available
                this.videoFrames = videoFrames;

                return Disposable.Create(() =>
                {
                    videoFrames.CompleteAdding();
                    try
                    {
                        pollTask.Wait();

                        foreach (var f in videoFrames)
                            f.Dispose();
                    }
                    catch (Exception e)
                    {
                        // Just log them
                        Trace.TraceError(e.ToString());
                    }
                    finally
                    {
                        pollTask.Dispose();
                        pollTask = default;
                        videoFrames.Dispose();
                        videoFrames = default;
                    }
                });
            }
        }

        void FetchCurrentVideoFrame(int waitTimeInMilliseconds)
        {
            // Fetch the texture
            if (videoFrames != null && videoFrames.TryTake(out var texture, waitTimeInMilliseconds))
            {
                // Set the texture as current output
                CurrentVideoFrame = ToDeviceColorSpace(texture);
                return;
            }
        }

        Texture ToDeviceColorSpace(Texture texture)
        {
            // The data coming from the capture device is in gamma space, but the texure (sadly) is not marked as such.
            // A subsequent sampler wouldn't convert the color to linear space, but when writing it back into our sRGB render target it would get (wrongly) converted.
            // What we'd really like to do here is simply changing the SRV but sadly that's not allowed for strongly typed resources (only TYPELESS and back buffers).
            // So we have to do a full copy :/

            //var viewDesc = value.ViewDescription;
            //viewDesc.Format = viewDesc.Format.ToSRgb();
            //viewDesc.Flags = TextureFlags.ShaderResource;
            //srgbVideoFrame?.Dispose();
            //srgbVideoFrame = value.ToTextureView(viewDesc);

            var deviceColorTexture = colorSpaceConverter.ToDeviceColorSpace(texture);

            // We don't need the original anymore
            if (deviceColorTexture != texture)
                texture.Dispose();

            return deviceColorTexture;
        }

        public void Dispose()
        {
            deviceSubscription.Dispose();
            currentVideoFrame?.Dispose();
            colorSpaceConverter.Dispose();
            renderDrawContextHandle.Dispose();
        }
    }
}
