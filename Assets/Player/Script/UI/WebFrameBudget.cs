using System;

namespace BattlePvp.UI
{
    /// <summary>Session-only visual quality. Never changes simulation, input, or network timing.</summary>
    public sealed class WebFrameBudget
    {
        private int _ceiling = -1, _fps, _frames, _slowWindows;
        private double _seconds, _stableSeconds, _warmup;
        public int Tier { get; private set; }
        public int Quality => Math.Max(0, Tier - 1);
        public int PixelBudget => Tier == 0 ? 960 * 540 : Tier < 3 ? 1280 * 720 : 1920 * 1080;
        public float MaximumScale => Tier == 0 ? .65f : Tier == 1 ? .75f : Tier == 2 ? .85f : 1f;

        public void Configure(int quality, int fps)
        {
            int ceiling = Math.Clamp(quality, 0, 2) + 1;
            fps = fps == 30 ? 30 : 60;
            if (ceiling == _ceiling && fps == _fps) return;
            _ceiling = ceiling; _fps = fps;
            // Start conservatively even when a previous browser session saved high quality.
            Tier = Math.Min(2, ceiling);
            ResetObservation();
        }

        public void ResetObservation()
        {
            _frames = _slowWindows = 0;
            _seconds = _stableSeconds = 0;
            _warmup = 3;
        }

        public bool Observe(double frameSeconds, bool focused)
        {
            if (!focused || !double.IsFinite(frameSeconds) || frameSeconds <= 0)
            { ResetObservation(); return false; }
            if (_warmup > 0) { _warmup -= frameSeconds; return false; }
            _seconds += frameSeconds; _frames++;
            if (_seconds < 2) return false;
            double average = _seconds / _frames;
            _stableSeconds = average <= 1.05 / _fps ? _stableSeconds + _seconds : 0;
            _slowWindows = average > 1.2 / _fps ? _slowWindows + 1 : 0;
            _seconds = 0; _frames = 0;
            if (_slowWindows >= 2 && Tier > 0)
            { Tier--; ResetObservation(); return true; }
            // Recovery is deliberately slower than reduction, avoiding visible oscillation.
            if (_stableSeconds >= 20 && Tier < _ceiling)
            { Tier++; ResetObservation(); return true; }
            return false;
        }

        public float RenderScale(int width, int height) => (float)Math.Clamp(
            Math.Min(MaximumScale, Math.Sqrt(PixelBudget / (Math.Max(1, width) * (double)Math.Max(1, height)))), .1, 1);
    }
}
