using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using BattlePvp.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BattlePvp.EditorTests
{
    public sealed class UnityRealtimeTimerTests
    {
        [UnityTest]
        public IEnumerator NetworkWaitAndTimeoutCompleteOnPlayerFramesWithTimeScaleZero()
        {
            yield return new EnterPlayMode();
            float scale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                Task delay = UnityRealtimeTimer.DelayAsync(30);
                using (var timeout = UnityRealtimeTimer.CreateTimeout(40))
                {
                    double limit = Time.realtimeSinceStartupAsDouble + 2;
                    while ((!delay.IsCompleted || !timeout.IsCancellationRequested) && Time.realtimeSinceStartupAsDouble < limit)
                        yield return null;
                    Assert.That(delay.IsCompletedSuccessfully, Is.True);
                    Assert.That(timeout.IsCancellationRequested, Is.True);
                }

                using (var cancelled = new CancellationTokenSource())
                {
                    Task abandoned = UnityRealtimeTimer.DelayAsync(30000, cancelled.Token);
                    cancelled.Cancel();
                    yield return null;
                    Assert.That(abandoned.IsCanceled, Is.True);
                }

                var disposed = UnityRealtimeTimer.CreateTimeout(30000);
                disposed.Dispose();
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally { Time.timeScale = scale; }
            yield return new ExitPlayMode();
        }
    }
}
