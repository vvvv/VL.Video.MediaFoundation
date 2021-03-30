using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using SharpDX.MediaFoundation;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using System;
using System.Diagnostics;
using VL.Core;
using VL.Lib.Basics.Resources;
using VL.Stride;
using PixelFormat = Stride.Graphics.PixelFormat;

namespace VL.MediaFoundation
{
    // Good source: https://stackoverflow.com/questions/40913196/how-to-properly-use-a-hardware-accelerated-media-foundation-source-reader-to-dec
    public partial class VideoPlayer : IDisposable
    {
        private readonly IResourceHandle<RenderDrawContext> renderDrawContextHandle;
        private readonly ColorSpaceConverter colorSpaceConverter;
        private MediaEngine engine;
        private bool invalidated;

        public VideoPlayer(NodeContext nodeContext)
        {
            renderDrawContextHandle = nodeContext.GetGameProvider()
                .Bind(g => RenderContext.GetShared(g.Services).GetThreadContext())
                .GetHandle() ?? throw new ServiceNotFoundException(typeof(IResourceProvider<Game>));

            colorSpaceConverter = new ColorSpaceConverter(renderDrawContextHandle.Resource);

            // Initialize MediaFoundation
            MediaManagerService.Initialize();

            using var mediaEngineAttributes = new MediaEngineAttributes()
            {
                // _SRGB doesn't work :/ Getting invalid argument exception later in TransferVideoFrame
                AudioCategory = SharpDX.Multimedia.AudioStreamCategory.GameMedia,
                AudioEndpointRole = SharpDX.Multimedia.AudioEndpointRole.Multimedia,
                VideoOutputFormat = (int)SharpDX.DXGI.Format.B8G8R8A8_UNorm
            };

            var graphicsDevice = renderDrawContextHandle.Resource.GraphicsDevice;
            var device = SharpDXInterop.GetNativeDevice(graphicsDevice) as Device;
            if (device != null)
            {
                // Add multi thread protection on device (MF is multi-threaded)
                using var deviceMultithread = device.QueryInterface<DeviceMultithread>();
                deviceMultithread.SetMultithreadProtected(true);


                // Reset device
                using var manager = new DXGIDeviceManager();
                manager.ResetDevice(device);
                mediaEngineAttributes.DxgiManager = manager;
            }

            using var classFactory = new MediaEngineClassFactory();
            engine = new MediaEngine(classFactory, mediaEngineAttributes);
            engine.PlaybackEvent += Engine_PlaybackEvent;
        }

        private void Engine_PlaybackEvent(MediaEngineEvent mediaEvent, long param1, int param2)
        {
            Trace.TraceInformation(mediaEvent.ToString());
            switch (mediaEvent)
            {
                case MediaEngineEvent.LoadStart:
                    ErrorCode = MediaEngineErr.Noerror;
                    break;
                case MediaEngineEvent.Error:
                    ErrorCode = (MediaEngineErr)param1;
                    break;
                case MediaEngineEvent.LoadedMetadata:
                    invalidated = true;
                    break;
            }
        }

        /// <summary>
        /// The URL of the media to play.
        /// </summary>
        public string Url
        {
            set
            {
                if (value != url)
                {
                    url = value;
                    engine.Source = value;
                }
            }
        }
        string url;

        /// <summary>
        /// Set to true to start playback, false to pause playback.
        /// </summary>
        public bool Play { private get; set; }

        /// <summary>
        /// Gets or sets the rate at which the media is being played back.
        /// </summary>
        public float Rate
        {
            get => (float)engine.PlaybackRate;
            set => engine.PlaybackRate = value;
        }

        public float SeekTime { get; set; }

        public bool Seek { get; set; }

        public float LoopStartTime { get; set; }

        public float LoopEndTime { get; set; } = float.MaxValue;

        public bool Loop
        {
            get => engine.Loop;
            set => engine.Loop = value;
        }

        /// <summary>
        /// The audio volume.
        /// </summary>
        public float Volume
        {
            get => (float)engine.Volume;
            set => engine.Volume = VLMath.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// The normalized source rectangle.
        /// </summary>
        public RectangleF? SourceBounds { private get; set; }

        /// <summary>
        /// The border color.
        /// </summary>
        public Color4? BorderColor { private get; set; }

        /// <summary>
        /// The size of the output texture. Use zero to take the size from the video.
        /// </summary>
        public Size2 TextureSize
        {
            set
            {
                if (value != textureSize)
                {
                    textureSize = value;
                    invalidated = true;
                }
            }
        }
        Size2 textureSize;

        /// <summary>
        /// Whether or not playback started.
        /// </summary>
        public bool Playing => !engine.IsPaused;

        /// <summary>
        /// A Boolean which is true if the media contained in the element has finished playing.
        /// </summary>
        public bool IsEnded => engine.IsEnded;

        /// <summary>
        /// The current playback time in seconds
        /// </summary>
        public float CurrentTime => (float)engine.CurrentTime;

        /// <summary>
        /// The length of the element's media in seconds.
        /// </summary>
        public float Duration => (float)engine.Duration;

        /// <summary>
        /// The current state of the fetching of media over the network.
        /// </summary>
        public NetworkState NetworkState => (NetworkState)engine.NetworkState;

        /// <summary>
        /// The readiness state of the media.
        /// </summary>
        public ReadyState ReadyState => (ReadyState)engine.ReadyState;

        /// <summary>
        /// Gets the most recent error status.
        /// </summary>
        public MediaEngineErr ErrorCode { get; private set; }

        // This method is not really needed but makes it simpler to work with inside VL
        public Texture Update(
            string url,
            bool play = false,
            float rate = 1f,
            float seekTime = 0f,
            bool seek = false,
            float loopStartTime = 0f,
            float loopEndTime = -1f,
            bool loop = false,
            float volume = 1f,
            Int2 textureSize = default,
            RectangleF? sourceBounds = default,
            Color4? borderColor = default)
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
            TextureSize = new Size2(textureSize.X, textureSize.Y);
            SourceBounds = sourceBounds;
            BorderColor = borderColor;
            Update();
            return currentVideoFrame;
        }

        void Update()
        {
            if (ReadyState <= ReadyState.HaveNothing)
            {
                currentVideoFrame = null;
                renderTarget?.Dispose();
                renderTarget = null;
                invalidated = true;
                return;
            }

            if (ReadyState >= ReadyState.HaveMetadata)
            {
                if (Seek)
                {
                    var seekTime = VLMath.Clamp(SeekTime, 0, Duration);
                    engine.CurrentTime = seekTime;
                }

                if (Loop)
                {
                    var currentTime = CurrentTime;
                    var loopStartTime = VLMath.Clamp(LoopStartTime, 0f, Duration);
                    var loopEndTime = VLMath.Clamp(LoopEndTime < 0 ? float.MaxValue : LoopEndTime, 0f, Duration);
                    if (currentTime < loopStartTime || currentTime > loopEndTime)
                    {
                        if (Rate >= 0)
                            engine.CurrentTime = loopStartTime;
                        else
                            engine.CurrentTime = loopEndTime;
                    }
                }

                if (Play && engine.IsPaused)
                    engine.Play();
                else if (!Play && !engine.IsPaused)
                    engine.Pause();
            }

            if (ReadyState >= ReadyState.HaveCurrentData && engine.OnVideoStreamTick(out var presentationTimeTicks))
            {
                if (invalidated || currentVideoFrame is null)
                {
                    invalidated = false;

                    renderTarget?.Dispose();

                    engine.GetNativeVideoSize(out var width, out var height);

                    // Apply user specified size
                    var x = textureSize;
                    if (x.Width > 0)
                        width = x.Width;
                    if (x.Height > 0)
                        height = x.Height;

                    var graphicsDevice = renderDrawContextHandle.Resource.GraphicsDevice;

                    // _SRGB doesn't work :/ Getting invalid argument exception in TransferVideoFrame
                    renderTarget = Texture.New2D(graphicsDevice, width, height, PixelFormat.B8G8R8A8_UNorm, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
                }

                if (SharpDXInterop.GetNativeResource(renderTarget) is Texture2D nativeRenderTarget)
                {
                    engine.TransferVideoFrame(
                        nativeRenderTarget, 
                        ToVideoRect(SourceBounds), 
                        new RawRectangle(0, 0, renderTarget.ViewWidth, renderTarget.ViewHeight), 
                        ToRawColorBGRA(BorderColor));

                    // Apply color space conversion if necessary
                    currentVideoFrame = colorSpaceConverter.ToDeviceColorSpace(renderTarget);
                }
            }
        }
        Texture renderTarget;
        Texture currentVideoFrame;

        static VideoNormalizedRect? ToVideoRect(RectangleF? rect)
        {
            if (rect.HasValue)
            {
                var x = rect.Value;
                return new VideoNormalizedRect() 
                { 
                    Left = VLMath.Clamp(x.Left, 0f, 1f), 
                    Bottom = VLMath.Clamp(x.Bottom, 0f, 1f),
                    Right = VLMath.Clamp(x.Right, 0f, 1f), 
                    Top = VLMath.Clamp(x.Top, 0f, 1f)
                };
            }
            return default;
        }

        static RawColorBGRA? ToRawColorBGRA(Color4? color)
        {
            if (color.HasValue)
            {
                color.Value.ToBgra(out var r, out var g, out var b, out var a);
                return new RawColorBGRA(b, g, r, a);
            }
            return default;
        }

        public void Dispose()
        {
            engine.Shutdown();
            engine.PlaybackEvent -= Engine_PlaybackEvent;
            engine.Dispose();
            colorSpaceConverter.Dispose();
            renderTarget?.Dispose();
        }
    }

    public enum NetworkState : short
    {
        /// <summary>
        /// There is no data yet. Also, readyState is HaveNothing.
        /// </summary>
        Empty,
        /// <summary>
        /// HTMLMediaElement is active and has selected a resource, but is not using the network.
        /// </summary>
        Idle,
        /// <summary>
        /// The browser is downloading HTMLMediaElement data.
        /// </summary>
        Loading,
        /// <summary>
        /// No HTMLMediaElement src found.
        /// </summary>
        NoSource
    }

    public enum ReadyState : short
    {
        /// <summary>
        /// No information is available about the media resource.
        /// </summary>
        HaveNothing,
        /// <summary>
        /// Enough of the media resource has been retrieved that the metadata attributes are initialized. Seeking will no longer raise an exception.
        /// </summary>
        HaveMetadata,
        /// <summary>
        /// Data is available for the current playback position, but not enough to actually play more than one frame.
        /// </summary>
        HaveCurrentData,
        /// <summary>
        /// Data for the current playback position as well as for at least a little bit of time into the future is available (in other words, at least two frames of video, for example).
        /// </summary>
        HaveFutureData,
        /// <summary>
        /// Enough data is available—and the download rate is high enough—that the media can be played through to the end without interruption.
        /// </summary>
        HaveEnoughData
    }
}
