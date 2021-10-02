using SharpDX.Direct3D11;
using Stride.Core;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VL.Core;
using VL.Lib.Basics.Resources;
using VL.Stride;

namespace VL.Video.MediaFoundation
{
    class StrideConverter
    {
        private readonly IResourceHandle<RenderDrawContext> renderDrawContextHandle;

        public StrideConverter(NodeContext nodeContext)
        {
            renderDrawContextHandle = nodeContext.GetGameProvider()
                .Bind(g => RenderContext.GetShared(g.Services).GetThreadContext())
                .GetHandle() ?? throw new ServiceNotFoundException(typeof(IResourceProvider<Game>));
        }

        public Texture AsTexture(Texture2D nativeTexture)
        {
            if (nativeTexture is null)
                return null;

            var texture = SharpDXInterop.CreateTextureFromNative(renderDrawContextHandle.Resource.GraphicsDevice, nativeTexture, takeOwnership: false);
            if (texture != null)
            {
                nativeTexture.AddRef();
                texture.Destroyed += (s, e) =>
                {
                    nativeTexture.Release();
                };
            }
            return texture;
        }
    }
}
