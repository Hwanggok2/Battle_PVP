using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BattlePvp.Networking
{
    /// <summary>Network waits use Unity frames: managed timers never fire in WebGL.</summary>
    public static class UnityRealtimeTimer
    {
        public static Task DelayAsync(int milliseconds, CancellationToken cancellation = default)
        {
#if UNITY_EDITOR
            // Pure EditMode service tests have no player loop.
            if (!Application.isPlaying) return Task.Delay(milliseconds, cancellation);
#endif
            return DelayFramesAsync(milliseconds, cancellation);
        }

        private static async Task DelayFramesAsync(int milliseconds, CancellationToken cancellation)
        {
            if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, Application.exitCancellationToken);
            lifetime.Token.ThrowIfCancellationRequested();
            double deadline = Time.realtimeSinceStartupAsDouble + milliseconds / 1000d;
            while (Time.realtimeSinceStartupAsDouble < deadline)
                await Awaitable.NextFrameAsync(lifetime.Token);
        }

        public static CancellationTokenSource CreateTimeout(int milliseconds)
        {
            var source = new CancellationTokenSource();
            CancelAfter(source, milliseconds);
            return source;
        }

        public static void CancelAfter(CancellationTokenSource source, int milliseconds)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
#if UNITY_EDITOR
            if (!Application.isPlaying) { source.CancelAfter(milliseconds); return; }
#endif
            _ = CancelAfterFramesAsync(source, milliseconds);
        }

        private static async Task CancelAfterFramesAsync(CancellationTokenSource source, int milliseconds)
        {
            try
            {
                double deadline = Time.realtimeSinceStartupAsDouble + milliseconds / 1000d;
                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    // Token also throws after disposal, ending completed operations' timer loops.
                    source.Token.ThrowIfCancellationRequested();
                    await Awaitable.NextFrameAsync(Application.exitCancellationToken);
                }
                source.Cancel();
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
