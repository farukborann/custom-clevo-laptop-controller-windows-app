using System.Diagnostics;

namespace BSCustomClevoController.Utility
{
    public static class MonotonicClock
    {
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
        private static readonly double MsToTicks = Stopwatch.Frequency / 1000.0;

        public static long NowTicks() => Stopwatch.GetTimestamp();

        public static double ElapsedMs(long sinceTicks)
        {
            if (sinceTicks <= 0)
            {
                return double.MaxValue;
            }
            return (Stopwatch.GetTimestamp() - sinceTicks) * TicksToMs;
        }

        public static long DeadlineFromNowMs(double delayMs)
        {
            return Stopwatch.GetTimestamp() + (long)(delayMs * MsToTicks);
        }

        public static bool IsBefore(long deadlineTicks)
        {
            return deadlineTicks > 0 && Stopwatch.GetTimestamp() < deadlineTicks;
        }
    }
}

