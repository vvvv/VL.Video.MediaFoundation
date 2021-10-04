using SharpDX.Direct3D11;
using Stride.Core;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using System;
using VL.Core;
using VL.Lib.Basics.Resources;
using VL.Stride;

namespace VL.Video.MediaFoundation
{
    public class StrideConverter : Converter<Texture2D, Texture>
    {
        private readonly IResourceHandle<RenderDrawContext> renderDrawContextHandle;

        public StrideConverter(NodeContext nodeContext)
            : base(nodeContext)
        {
            renderDrawContextHandle = nodeContext.GetGameProvider()
                .Bind(g => RenderContext.GetShared(g.Services).GetThreadContext())
                .GetHandle() ?? throw new ServiceNotFoundException(typeof(IResourceProvider<Game>));
        }

        public override void Dispose()
        {
            base.Dispose();
            renderDrawContextHandle.Dispose();
        }

        protected override Texture Convert(Texture2D resource, IDisposable resourceHandle)
        {
            var texture = SharpDXInterop.CreateTextureFromNative(renderDrawContextHandle.Resource.GraphicsDevice, resource, takeOwnership: true);
            resourceHandle.DisposeBy(texture);
            return texture;
        }
    }
}
