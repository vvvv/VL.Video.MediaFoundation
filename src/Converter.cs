using System;
using System.Reactive.Disposables;
using VL.Core;
using VL.Lib.Basics.Resources;

namespace VL.Video.MediaFoundation
{
    public abstract class Converter<TIn, TOut> : IDisposable
        where TIn: class
        where TOut: class
    {
        private readonly Producing<TOut> output = new Producing<TOut>();
        private readonly IRefCounter<TIn> refCounter;
        private TIn current;

        public Converter(NodeContext nodeContext)
        {
            refCounter = nodeContext.GetRefCounter<TIn>();
        }

        public virtual void Dispose()
        {
            output.Dispose();
        }

        public TOut Update(TIn resource)
        {
            if (resource != current)
            {
                this.current = resource;

                if (resource is null)
                    output.Resource = null;
                else
                {
                    refCounter?.AddRef(resource);
                    output.Resource = Convert(resource, Disposable.Create(() => refCounter?.Release(resource)));
                }
            }
            return output.Resource;
        }

        protected abstract TOut Convert(TIn resource, IDisposable resourceHandle);
    }
}
