using System;
using System.Globalization;

namespace TTSK_AutoDim_Plates
{
    public static class ManualDrawingScaleOverride
    {
        private static double? _manualScaleOverride;

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
            if (!int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out denominator) ||
                !IsAllowedScale(denominator))
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
                if (!IsAllowedScale(scale.Value))
                    throw new ArgumentOutOfRangeException("scale");

                _manualScaleOverride = scale.Value;
            }

            return new RunScope();
        }

        public static void Clear()
        {
            _manualScaleOverride = null;
        }

        private static bool IsAllowedScale(double scale)
        {
            return
                Math.Abs(scale - 5.0) < 0.0001 ||
                Math.Abs(scale - 10.0) < 0.0001 ||
                Math.Abs(scale - 15.0) < 0.0001 ||
                Math.Abs(scale - 20.0) < 0.0001 ||
                Math.Abs(scale - 30.0) < 0.0001;
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
