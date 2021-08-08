using SharpDX.MediaFoundation;
using System;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace VL.MediaFoundation
{
    static class MediaEngineExtensions
    {
        public static async Task LoadAsync(this MediaEngine engine, string url, CancellationToken token)
        {
            await engine.DoAsync(token, () => engine.Source = url, e => e == MediaEngineEvent.LoadedMetadata);
        }

        static async Task DoAsync(this MediaEngine engine, CancellationToken token, Action action, Func<MediaEngineEvent, bool> completionEvent)
        {
            var tcs = new TaskCompletionSource<Unit>();
            token.Register(() => tcs.TrySetCanceled());
            MediaEngineNotifyDelegate @delegate = (e, p1, p2) =>
            {
                if (completionEvent(e))
                    tcs.TrySetResult(Unit.Default);
                else if (e == MediaEngineEvent.Abort)
                    tcs.TrySetCanceled();
                else if (e == MediaEngineEvent.Error)
                    tcs.TrySetException(new Exception($"An error occured in the pipeline."));
            };
            try
            {
                engine.PlaybackEvent += @delegate;
                action();
                await tcs.Task;
            }
            finally
            {
                engine.PlaybackEvent -= @delegate;
            }
        }
    }
}
