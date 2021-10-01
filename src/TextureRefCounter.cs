using Stride.Core;
using Stride.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VL.Lib.Basics.Resources;

namespace VL.MediaFoundation
{
    sealed class TextureRefCounter : IRefCounter<Texture>
    {
        public static readonly TextureRefCounter Default = new TextureRefCounter();

        private static readonly PropertyKey<bool> RefCountUnlocked = new PropertyKey<bool>(nameof(RefCountUnlocked), typeof(TextureRefCounter));

        public static readonly PropertyKey<Action<Texture>> CustomDisposeAction = new PropertyKey<Action<Texture>>(nameof(CustomDisposeAction), typeof(TextureRefCounter));

        public void Init(Texture resource)
        {
            if (resource is null)
                return;

            resource.Tags.Set(RefCountUnlocked, true);
        }

        public void AddRef(Texture resource)
        {
            if (resource is null)
                return;

            if (resource.Tags.ContainsKey(RefCountUnlocked))
            {
                IReferencable r = resource;
                r.AddReference();
            }
        }

        public void Release(Texture resource)
        {
            if (resource is null)
                return;

            if (resource.Tags.ContainsKey(RefCountUnlocked))
            {
                IReferencable r = resource;
                if (r.ReferenceCount == 1 && resource.Tags.TryGetValue(CustomDisposeAction, out var customDispose))
                    customDispose(resource);
                else
                    r.Release();
            }
        }
    }
}
