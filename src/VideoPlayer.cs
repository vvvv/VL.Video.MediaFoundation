using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using VL.Core;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;

namespace VL.MediaFoundation
{
    // Good source: https://stackoverflow.com/questions/40913196/how-to-properly-use-a-hardware-accelerated-media-foundation-source-reader-to-dec
    public partial class VideoPlayer
    {
        readonly IResourceProvider<Device> deviceProvider;

        public VideoPlayer(NodeContext nodeContext)
        {
            // Our assembly initializer ensures that a device provider is registered
            deviceProvider = nodeContext.Factory.CreateService<IResourceProvider<Device>>(nodeContext);
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

        public IObservable<Texture2D> Frames { get; private set; } = Observable.Empty<Texture2D>();

        public string Url
        {
            get => url;
            set
            {
                if (value != url)
                {
                    url = value;

                    Frames = Observable.Create<Texture2D>((observer, token) => PlayUrl(Url, observer, token)).Publish().RefCount();
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

        async Task PlayUrl(string url, IObserver<Texture2D> observer, CancellationToken token)
        {
            using var deviceHandle = deviceProvider.GetHandle();
            var device = deviceHandle.Resource;

            // Add multi thread protection on device (MF is multi-threaded)
            using var deviceMultithread = device.QueryInterface<DeviceMultithread>();
            deviceMultithread.SetMultithreadProtected(true);

            // Initialize MediaFoundation
            MediaManagerService.Initialize();

            // Reset device
            using var manager = new DXGIDeviceManager();
            manager.ResetDevice(device);

            using var classFactory = new MediaEngineClassFactory();
            using var mediaEngineAttributes = new MediaEngineAttributes()
            {
                // To use WIC disable
                DxgiManager = manager,
                VideoOutputFormat = (int)SharpDX.DXGI.Format.B8G8R8A8_UNorm
            };
            using var engine = new MediaEngine(classFactory, mediaEngineAttributes);

            MediaEngineNotifyDelegate logger = (e, p1, p2) => Trace.TraceInformation(e.ToString());
            engine.PlaybackEvent += logger;
            try
            {
                // Run the playback on a background thread
                await Task.Run(() => PlayUrl(device, engine, url, observer, token));
            }
            finally
            {
                engine.PlaybackEvent -= logger;
                engine.Shutdown();
            }
        }

        async Task PlayUrl(Device device, MediaEngine engine, string url, IObserver<Texture2D> observer, CancellationToken token)
        {
            // Reset outputs
            CurrentTime = default;
            Duration = default;

            // Wait for MediaEngine to be ready
            await engine.LoadAsync(url, token);

            engine.GetNativeVideoSize(out var width, out var height);
            Duration = (float)engine.Duration;

            //var fac = new ImagingFactory();
            //using var bitmap = new Bitmap(fac, width, height, SharpDX.WIC.PixelFormat.Format32bppBGRA, BitmapCreateCacheOption.CacheOnLoad);
            // and later
            //engine.TransferVideoFrame(bitmap, default, new SharpDX.Mathematics.Interop.RawRectangle(0, 0, width, height), default);
            //using var bitmapLock = bitmap.Lock(BitmapLockFlags.Read);
            //var data = bitmapLock.Data;
            //using var image = new IntPtrImage(data.DataPointer, data.Pitch * height, info);

            var textureDesc = new Texture2DDescription()
            {
                Width = width,
                Height = height,
                MipLevels = 0,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            };

            using var outputTexture = new Texture2D(device, textureDesc);

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
                    engine.CurrentTime = seekTime;
                }

                var currentTime = CurrentTime = (float)engine.CurrentTime;
                if (Loop)
                {
                    var loopStartTime = VLMath.Clamp(LoopStartTime, 0f, Duration);
                    var loopEndTime = VLMath.Clamp(LoopEndTime < 0 ? float.MaxValue : LoopEndTime, 0f, Duration);
                    if (currentTime < loopStartTime || currentTime > loopEndTime)
                    {
                        if (Rate >= 0)
                            engine.CurrentTime = loopStartTime;
                        else
                            engine.CurrentTime = loopEndTime;

                        continue;
                    }
                }

                // Check playing state
                if (Play)
                {
                    if (engine.IsPaused)
                        engine.Play();
                }
                else
                {
                    if (!engine.IsPaused)
                        engine.Pause();
                }

                // It's imperative to call this function in a loop. Otherwise the pipeline might get stuck.
                if (engine.OnVideoStreamTick(out var presentationTimeTicks))
                {
                    engine.TransferVideoFrame(outputTexture, default, new SharpDX.Mathematics.Interop.RawRectangle(0, 0, width, height), default);

                    observer.OnNext(outputTexture);
                }

                await Task.Delay(10);
            }
        }
    }
}
