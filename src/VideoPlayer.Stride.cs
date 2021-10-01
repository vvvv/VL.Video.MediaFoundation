using Stride.Graphics;
using VL.Core;

namespace VL.MediaFoundation
{
    public sealed class StrideVideoPlayer : VideoPlayer<Texture>
    {
        public StrideVideoPlayer(NodeContext nodeContext) : base(new StrideTextureProvider(nodeContext))
        {
        }
    }
}
