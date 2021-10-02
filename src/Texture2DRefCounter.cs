using SharpDX.Direct3D11;
using VL.Lib.Basics.Resources;

namespace VL.MediaFoundation
{
    sealed class Texture2DRefCounter : IRefCounter<Texture2D>
    {
        public static readonly Texture2DRefCounter Default = new Texture2DRefCounter();

        public void Init(Texture2D resource)
        {
            resource?.RefCounted();
        }

        public void AddRef(Texture2D resource)
        {
            resource?.AddRef();
        }

        public void Release(Texture2D resource)
        {
            resource?.Release();
        }
    }
}
