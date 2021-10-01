using SkiaSharp;
using VL.Core;

namespace VL.MediaFoundation
{
    public sealed class SkiaVideoPlayer : VideoPlayer<SKImage>
    {
        public SkiaVideoPlayer(NodeContext nodeContext) : base(new SkiaTextureProvider(nodeContext))
        {
        }
    }
}
