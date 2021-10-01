using SkiaSharp;
using VL.Core;

namespace VL.MediaFoundation
{
    public sealed class SkiaVideoCapture : VideoCapture<SKImage>
    {
        public SkiaVideoCapture(NodeContext nodeContext) : base(new SkiaTextureProvider(nodeContext))
        {
        }
    }
}
