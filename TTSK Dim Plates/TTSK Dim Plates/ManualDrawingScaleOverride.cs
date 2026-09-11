using System;
using System.Globalization;

namespace TTSK_AutoDim_Plates
{
    public static class ManualDrawingScaleOverride
    {
        private static double? _manualScaleOverride;
        private static double? _excludedScale;

        // The fallback must also respect exclusions, including an excluded 1:30.
        public static double ChooseAutoScale(double requiredScale)
        {
            double lastAllowed = 0.0;
            foreach (double candidate in new double[] { 5.0, 10.0, 15.0, 20.0, 30.0 })
            {
                if (_excludedScale.HasValue && Math.Abs(candidate - _excludedScale.Value) < 0.0001)
                    continue;
                lastAllowed = candidate;
                if (candidate >= requiredScale)
                    return candidate;
            }
            return lastAllowed;
        }

        public static double? ManualScaleOverride
        {
            get { return _manualScaleOverride; }
        }

        public static bool HasOverride
        {
            get { return _manualScaleOverride.HasValue; }
        }

        public static bool TryGet(out double scale)
        {
            if (_manualScaleOverride.HasValue)
            {
                scale = _manualScaleOverride.Value;
                return true;
            }

            scale = 0.0;
            return false;
        }

        public static bool TryParseInput(string text, out double? scale)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                scale = null;
                return true;
            }

            int denominator;
            if (
                !int.TryParse(
                    value,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out denominator
                ) || !IsAllowedScale(Math.Abs((double)denominator))
            )
            {
                scale = null;
                return false;
            }

            scale = denominator;
            return true;
        }

        public static IDisposable BeginRun(double? scale)
        {
            Clear();

            if (scale.HasValue)
            {
                if (!IsAllowedScale(Math.Abs(scale.Value)))
                    throw new ArgumentOutOfRangeException("scale");

                if (scale.Value < 0.0)
                    _excludedScale = -scale.Value;
                else
                    _manualScaleOverride = scale.Value;
            }

            return new RunScope();
        }

        public static void Clear()
        {
            _manualScaleOverride = null;
            _excludedScale = null;
        }

        private static bool IsAllowedScale(double scale)
        {
            return Math.Abs(scale - 5.0) < 0.0001
                || Math.Abs(scale - 10.0) < 0.0001
                || Math.Abs(scale - 15.0) < 0.0001
                || Math.Abs(scale - 20.0) < 0.0001
                || Math.Abs(scale - 30.0) < 0.0001;
        }

        private sealed class RunScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                Clear();
            }
        }
    }
}
