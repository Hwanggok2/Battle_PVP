#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Unity.Profiling;

namespace BattlePvp.Diagnostics
{
    internal sealed class RuntimePerformanceCounters : IDisposable
    {
        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[10];
        public readonly PerformanceMetricRecord[] Records = new PerformanceMetricRecord[10];
        private readonly double[] _scales = new double[10];

        public RuntimePerformanceCounters()
        {
            Add(0, ProfilerCategory.Internal, "Main Thread", "ms", 1e-6);
            Add(1, ProfilerCategory.Render, "GPU Frame Time", "ms", 1e-6);
            Add(2, ProfilerCategory.Memory, "GC Allocated In Frame", "bytes", 1);
            Add(3, ProfilerCategory.Memory, "Total Used Memory", "bytes", 1);
            Add(4, ProfilerCategory.Render, "Draw Calls Count", "count", 1);
            Add(5, ProfilerCategory.Render, "SetPass Calls Count", "count", 1);
            Add(6, ProfilerCategory.Render, "Triangles Count", "count", 1);
            Add(7, ProfilerCategory.Scripts, "BattlePvp.CharacterPose", "ms", 1e-6);
            Add(8, ProfilerCategory.Scripts, "BattlePvp.HookChain", "ms", 1e-6);
            Add(9, ProfilerCategory.Internal, "Render Thread", "ms", 1e-6);
        }
        private void Add(int index, ProfilerCategory category, string name, string unit, double scale)
        {
            var record = Records[index] = new PerformanceMetricRecord { Name = name, Unit = unit };
            _scales[index] = scale;
            try
            {
                _recorders[index] = ProfilerRecorder.StartNew(category, name, 1,
                    ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame);
                record.Supported = _recorders[index].Valid;
            }
            catch (ArgumentException) { record.Supported = false; }
        }
        public void Sample()
        {
            for (int i = 0; i < _recorders.Length; i++)
            {
                if (!_recorders[i].Valid || _recorders[i].Count == 0) continue;
                double value = _recorders[i].LastValue * _scales[i];
                var record = Records[i];
                record.Samples++;
                record.Mean += (value - record.Mean) / record.Samples;
                record.Maximum = Math.Max(record.Maximum, value);
            }
        }
        public void Dispose() { for (int i = 0; i < _recorders.Length; i++) _recorders[i].Dispose(); }
    }
}
#endif
