using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VL.MediaFoundation
{
    static class MediaEngineExtensions
    {
        public static async Task LoadAsync(this MediaEngine engine, string url, CancellationToken token)
        {
            await engine.DoAsync(token, e => e == MediaEngineEvent.LoadedMetadata, () =>
            {
                engine.Source = url;
            });
        }

        public static async Task PlayAsync(this MediaEngine engine, CancellationToken token)
        {
            await engine.DoAsync(token, e => e == MediaEngineEvent.Playing || e == MediaEngineEvent.TimeUpdate, () => engine.Play());
        }

        public static async Task PauseAsync(this MediaEngine engine, CancellationToken token)
        {
            await engine.DoAsync(token, e => e == MediaEngineEvent.Pause, () => engine.Pause());
        }

        public static async Task SetCurrentTimeAsync(this MediaEngine engine, double value, CancellationToken token)
        {
            if (engine.IsPaused)
            {
                await engine.DoAsync(token, e => e == MediaEngineEvent.TimeUpdate, () => engine.CurrentTime = value);
            }
            else
            {
                await engine.DoAsync(token, e => e == MediaEngineEvent.Seeked, () => engine.CurrentTime = value);
            }
        }

        static async Task DoAsync(this MediaEngine engine, CancellationToken token, Func<MediaEngineEvent, bool> completionEvent, Action action)
        {
            var tcs = new TaskCompletionSource<Unit>();
            token.Register(() => tcs.TrySetCanceled());
            MediaEngineNotifyDelegate @delegate = (e, p1, p2) =>
            {
                Trace.TraceInformation(e.ToString());
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
