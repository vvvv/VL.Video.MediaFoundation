using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;

namespace VL.MediaFoundation
{
    public struct VideoFrame
    {
        public VideoFrame(Device graphicsDevice, MediaEngine engine, int widht, int height)
        {
            GraphicsDevice = graphicsDevice;
            Engine = engine;
            Width = widht;
            Height = height;
        }

        public Device GraphicsDevice { get; }
        public MediaEngine Engine { get; }
        public int Width { get; }
        public int Height { get; }
    }
}
