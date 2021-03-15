using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
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
        private readonly RingBuffer<Texture> ringBuffer = new RingBuffer<Texture>(2);
        private readonly object ringBufferLock = new object();
        private readonly ManualResetEventSlim videoFrameArrived = new ManualResetEventSlim();
        private readonly ColorSpaceConverter colorSpaceConverter;
        private string deviceSymbolicLink;
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

        public Texture Update(Int2 size, float fps, int waitTimeInMilliseconds)
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
                var cancellationTokenSource = new CancellationTokenSource();
                var pollTask = Task.Run(() =>
                {
                    // Create the media source based on the selected symbolic link
                    using var mediaSource = CreateMediaSource();
                    if (mediaSource is null)
                        return;

                    // Setup source reader arguments
                    using var sourceReaderAttributes = new MediaAttributes();
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

                    // Create the source reader
                    using var reader = new SourceReader(mediaSource, sourceReaderAttributes);


                    // Set output format to BGRA
                    using var mt = new MediaType();
                    mt.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                    mt.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Argb32);
                    mt.Set(MediaTypeAttributeKeys.FrameSize, VideoCaptureHelpers.MakeSize(size.X, size.Y));
                    mt.Set(MediaTypeAttributeKeys.FrameRate, VideoCaptureHelpers.MakeFrameRate(fps));
                    mt.Set(MediaTypeAttributeKeys.InterlaceMode, (int)VideoInterlaceMode.Progressive);
                    mt.Set(MediaTypeAttributeKeys.PixelAspectRatio, VideoCaptureHelpers.MakeSize(1, 1));
                    reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, mt);
                    actualFps = VideoCaptureHelpers.ParseFrameRate(reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream).Get(MediaTypeAttributeKeys.FrameRate));
                    
                    // Reset the discared frame count
                    discardedFrames = 0;

                    while (!cancellationTokenSource.Token.IsCancellationRequested)
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

                            lock (ringBufferLock)
                            {
                                // In case the buffer is full the element at the front will be popped.
                                if (ringBuffer.IsFull)
                                {
                                    Interlocked.Increment(ref discardedFrames);
                                    ringBuffer.Front().Dispose();
                                }

                                // Place the texture in the buffer
                                ringBuffer.PushBack(texture);
                            }

                            // Signal render thread that a new frame is available
                            videoFrameArrived.Set();
                        }
                        else
                        {
                            buffer.Dispose();
                            sample.Dispose();
                        }
                    }
                }, cancellationTokenSource.Token);

                return Disposable.Create(() =>
                {
                    cancellationTokenSource.Cancel();
                    try
                    {
                        pollTask.Wait();
                    }
                    catch (Exception e)
                    {
                        // Just log them
                        Trace.TraceError(e.ToString());
                    }
                    finally
                    {
                        cancellationTokenSource.Dispose();
                        cancellationTokenSource = default;
                        pollTask.Dispose();
                        pollTask = default;
                    }
                });
            }
        }

        void FetchCurrentVideoFrame(int waitTimeInMilliseconds)
        {
            // Fetch the texture
            lock (ringBufferLock)
            {
                if (!ringBuffer.IsEmpty)
                {
                    // Set the texture as current output
                    var texture = ringBuffer.Front();
                    ringBuffer.PopFront();
                    CurrentVideoFrame = ToDeviceColorSpace(texture);
                    return;
                }
            }

            // The buffer was empty, wait for the next video frame to arrive
            if (waitTimeInMilliseconds > 0 && videoFrameArrived.Wait(waitTimeInMilliseconds))
            {
                // Reset the wait handle
                videoFrameArrived.Reset();

                lock (ringBufferLock)
                {
                    if (!ringBuffer.IsEmpty)
                    {
                        // Set the texture as current output
                        var texture = ringBuffer.Front();
                        ringBuffer.PopFront();
                        CurrentVideoFrame = ToDeviceColorSpace(texture);
                        return;
                    }
                }
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
            foreach (var t in ringBuffer)
                t?.Dispose();
            currentVideoFrame?.Dispose();
            videoFrameArrived.Dispose();
            colorSpaceConverter.Dispose();
            renderDrawContextHandle.Dispose();
        }
    }
}
