using Stride.Graphics;
using VL.Core;

namespace VL.MediaFoundation
{
    public sealed class StrideVideoCapture : VideoCapture<Texture>
    {
        public StrideVideoCapture(NodeContext nodeContext) : base(new StrideTextureProvider(nodeContext))
        {
        }
    }
}
