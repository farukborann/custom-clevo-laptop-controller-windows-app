using System;

namespace BSCustomClevoController.Utility
{
    /// <summary>Abstracts periodic UI-safe timers across WPF, WinUI 3, and headless test hosts.</summary>
    public interface IEffectTimer : IDisposable
    {
        bool IsEnabled { get; }
        void Start();
        void Stop();
    }

    /// <summary>Bridges UI-thread posting and animation timers without coupling Core hardware logic to WPF or WinUI.</summary>
    public static class UiDispatcher
    {
        public static Action<Action> PostHandler { get; set; }

        public static Func<TimeSpan, EventHandler, IEffectTimer> EffectTimerFactory { get; set; }

        public static void Post(Action action)
        {
            if (action == null) return;

            Action<Action> handler = PostHandler;
            if (handler != null)
            {
                handler(action);
            }
            else
            {
                action();
            }
        }

        public static IEffectTimer CreateEffectTimer(TimeSpan interval, EventHandler tick)
        {
            Func<TimeSpan, EventHandler, IEffectTimer> factory = EffectTimerFactory;
            if (factory != null)
            {
                return factory(interval, tick);
            }

            return new ThreadingEffectTimer(interval, tick);
        }

        private sealed class ThreadingEffectTimer : IEffectTimer
        {
            private readonly System.Timers.Timer _timer;
            private int _inTick;

            public ThreadingEffectTimer(TimeSpan interval, EventHandler tick)
            {
                _timer = new System.Timers.Timer(Math.Max(1.0, interval.TotalMilliseconds));
                _timer.AutoReset = true;
                if (tick != null)
                {
                    // Skip overlapping ticks when hardware WMI/ACPI writes exceed the 16ms cadence.
                    _timer.Elapsed += (s, e) =>
                    {
                        if (System.Threading.Interlocked.CompareExchange(ref _inTick, 1, 0) != 0)
                        {
                            return;
                        }
                        try
                        {
                            tick(s, e);
                        }
                        finally
                        {
                            System.Threading.Interlocked.Exchange(ref _inTick, 0);
                        }
                    };
                }
            }

            public bool IsEnabled => _timer.Enabled;

            public void Start() => _timer.Start();

            public void Stop() => _timer.Stop();

            public void Dispose() => _timer.Dispose();
        }
    }
}
