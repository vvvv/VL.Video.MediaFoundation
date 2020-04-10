using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using SharpDX.WIC;
using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using VL.Core;
using VL.Lib.Basics.Imaging;
using MapMode = SharpDX.Direct3D11.MapMode;
using PixelFormat = VL.Lib.Basics.Imaging.PixelFormat;

namespace VL.MediaFoundation
{
    // Good source: https://stackoverflow.com/questions/40913196/how-to-properly-use-a-hardware-accelerated-media-foundation-source-reader-to-dec
    public partial class VideoPlayer : IDisposable
    {
        private readonly Subject<IImage> videoFrames = new Subject<IImage>();
        private (Task, CancellationTokenSource) currentPlayback;

        public VideoPlayer()
        {
        }

        public void Update(
            string url = "http://www.peach.themazzone.com/durian/movies/sintel-1024-surround.mp4",
            bool play = false,
            float rate = 1f,
            float seekTime = 0f,
            bool seek = false,
            float loopStartTime = 0f,
            float loopEndTime = -1f,
            bool loop = false,
            float volume = 1f)
        {
            Url = url;
            Play = play;
            Rate = rate;
            SeekTime = seekTime;
            Seek = seek;
            LoopStartTime = loopStartTime;
            LoopEndTime = loopEndTime;
            Loop = loop;
            Volume = volume;
        }

        public IObservable<IImage> Frames => videoFrames;

        public string Url
        {
            get => url;
            set
            {
                if (value != url)
                {
                    url = value;

                    StopCurrentPlayback();

                    var cts = new CancellationTokenSource();
                    var task = Task.Run(() => PlayUrl(url, cts.Token));
                    currentPlayback = (task, cts);
                }
            }
        }
        string url;

        public bool Play { get; set; } = true;

        public float Rate { get; set; } = 1f;

        public float SeekTime { get; set; }

        public bool Seek { get; set; }

        public float LoopStartTime { get; set; }

        public float LoopEndTime { get; set; } = float.MaxValue;

        public bool Loop { get; set; } = true;

        public float Volume { get; set; } = 1f;

        public float CurrentTime { get; private set; }

        public float Duration { get; private set; }

        async Task PlayUrl(string url, CancellationToken token)
        {
            // Reset outputs
            CurrentTime = default;
            Duration = default;

            // Initialize MediaFoundation
            MediaManagerService.Initialize();
            // Hardware acceleration
            using var d3dDevice = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport);
            // Add multi thread protection on device (MF is multi-threaded)
            using var deviceMultithread = d3dDevice.QueryInterface<DeviceMultithread>();
            deviceMultithread.SetMultithreadProtected(true);
            // Reset device
            using var manager = new DXGIDeviceManager();
            manager.ResetDevice(d3dDevice);

            using var classFactory = new MediaEngineClassFactory();
            using var mediaEngineAttributes = new MediaEngineAttributes()
            {
                // To use WIC disable
                DxgiManager = manager,
                VideoOutputFormat = (int)SharpDX.DXGI.Format.B8G8R8A8_UNorm
            };
            using var engine = new MediaEngine(classFactory, mediaEngineAttributes);
            using var engineEx = engine.QueryInterface<MediaEngineEx>();

            // Wait for MediaEngine to be ready
            await engine.LoadAsync(url, token);

            engine.GetNativeVideoSize(out var width, out var height);
            Duration = (float)engine.Duration;

            //var fac = new ImagingFactory();
            //using var bitmap = new Bitmap(fac, width, height, SharpDX.WIC.PixelFormat.Format32bppBGRA, BitmapCreateCacheOption.CacheOnLoad);
            var textureDesc = new Texture2DDescription()
            {
                Width = width,
                Height = height,
                MipLevels = 0,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read,
                OptionFlags = ResourceOptionFlags.None
            };
            using var dstTexture = new Texture2D(d3dDevice, textureDesc);

            textureDesc.Usage = ResourceUsage.Default;
            textureDesc.BindFlags = BindFlags.RenderTarget;
            textureDesc.CpuAccessFlags = CpuAccessFlags.None;

            using var renderTexture = new Texture2D(d3dDevice, textureDesc);

            var info = new ImageInfo(width, height, PixelFormat.B8G8R8A8);

            while (!token.IsCancellationRequested)
            {
                if (Loop != engine.Loop)
                    engine.Loop = Loop;

                if (Rate != engine.PlaybackRate)
                {
                    engine.PlaybackRate = Rate;
                    engine.DefaultPlaybackRate = Rate;
                }

                var volume = VLMath.Clamp(Volume, 0f, 1f);
                if (volume != engine.Volume)
                    engine.Volume = volume;

                if (Seek)
                {
                    var seekTime = VLMath.Clamp(SeekTime, 0, Duration);
                    await engine.SetCurrentTimeAsync(seekTime, token);
                }

                // Check playing state
                if (Play)
                {
                    if (engine.IsPaused)
                        await engine.PlayAsync(token);
                }
                else
                {
                    if (!engine.IsPaused)
                        await engine.PauseAsync(token);
                    else
                        await Task.Delay(10);

                    continue;
                }

                if (engine.OnVideoStreamTick(out var presentationTimeTicks))
                {
                    var currentTime = CurrentTime = (float)TimeSpan.FromTicks(presentationTimeTicks).TotalSeconds;

                    if (Loop || presentationTimeTicks < 0)
                    {
                        var loopStartTime = VLMath.Clamp(LoopStartTime, 0f, Duration);
                        var loopEndTime = VLMath.Clamp(LoopEndTime < 0 ? float.MaxValue : LoopEndTime, 0f, Duration);
                        if (currentTime < loopStartTime || currentTime > loopEndTime)
                        {
                            if (Rate >= 0)
                                await engine.SetCurrentTimeAsync(loopStartTime, token);
                            else
                                await engine.SetCurrentTimeAsync(loopEndTime, token);

                            continue;
                        }
                    }

                    //engine.TransferVideoFrame(bitmap, default, new SharpDX.Mathematics.Interop.RawRectangle(0, 0, width, height), default);
                    //using var bitmapLock = bitmap.Lock(BitmapLockFlags.Read);
                    //var data = bitmapLock.Data;
                    //using var image = new IntPtrImage(data.DataPointer, data.Pitch * height, info);

                    engine.TransferVideoFrame(renderTexture, default, new SharpDX.Mathematics.Interop.RawRectangle(0, 0, width, height), default);

                    var deviceContext = d3dDevice.ImmediateContext;
                    deviceContext.CopyResource(renderTexture, dstTexture);
                    //deviceContext.Flush();

                    var data = deviceContext.MapSubresource(dstTexture, 0, MapMode.Read, MapFlags.None);
                    try
                    {
                        using var image = new IntPtrImage(data.DataPointer, data.RowPitch * height, info);
                        videoFrames.OnNext(image);
                    }
                    finally
                    {
                        deviceContext.UnmapSubresource(dstTexture, 0);
                    }
                }
            }

            engine.Shutdown();
        }

        void StopCurrentPlayback()
        {
            var (currentTask, currentCts) = currentPlayback;
            if (currentTask != null)
            {
                currentCts.Cancel();
                try
                {
                    currentTask.Wait();
                }
                catch (Exception e)
                {
                    Trace.TraceError(e.ToString());
                }
                currentCts.Dispose();
            }
            currentPlayback = default;
        }

        public void Dispose()
        {
            StopCurrentPlayback();
            videoFrames.Dispose();
        }
    }
}
