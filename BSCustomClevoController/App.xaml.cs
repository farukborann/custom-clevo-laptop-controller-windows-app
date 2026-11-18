using System;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ControlzEx.Theming;
using MahApps.Metro.Theming;

namespace BSCustomClevoController
{
    public partial class App : Application
    {
        private const string AppMutexName = "BSCustomClevoController_SingleInstance_Mutex";
        private const string WakeEventName = "BSCustomClevoController_WakeEvent";

        private static Mutex _appMutex;
        private static EventWaitHandle _wakeEvent;
        private static RegisteredWaitHandle _waitRegistration;
        private static ResourceDictionary _activeLocalizationDictionary;
        private static Views.OsdWindow _osdWindow;
        private static readonly object _osdLock = new object();

        private sealed class WpfEffectTimer : Utility.IEffectTimer
        {
            private readonly DispatcherTimer _timer;

            public WpfEffectTimer(TimeSpan interval, EventHandler tick)
            {
                _timer = new DispatcherTimer { Interval = interval };
                if (tick != null)
                {
                    _timer.Tick += tick;
                }
            }

            public bool IsEnabled => _timer.IsEnabled;

            public void Start() => _timer.Start();

            public void Stop() => _timer.Stop();

            public void Dispose() => _timer.Stop();
        }

        private static void RegisterWpfBridges()
        {
            Utility.UiDispatcher.PostHandler = action => Current?.Dispatcher?.BeginInvoke(action);
            Utility.UiDispatcher.EffectTimerFactory = (interval, tick) => new WpfEffectTimer(interval, tick);
            Utility.LocalizationManager.StringsApplied += ApplyWpfLocalizationDictionary;
            Utility.OsdOverlay.DisplayHandler = ShowWpfOsd;
        }

        private static void ApplyWpfLocalizationDictionary(IReadOnlyDictionary<string, string> strings)
        {
            if (Current == null || strings == null) return;

            var dict = new ResourceDictionary();
            foreach (var kvp in strings)
            {
                dict["Loc." + kvp.Key] = kvp.Value;
            }

            var merged = Current.Resources.MergedDictionaries;
            if (_activeLocalizationDictionary != null)
            {
                merged.Remove(_activeLocalizationDictionary);
            }
            merged.Add(dict);
            _activeLocalizationDictionary = dict;
        }

        private static void ShowWpfOsd(string imageName, string text)
        {
            var app = Current;
            if (app == null) return;

            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                lock (_osdLock)
                {
                    if (_osdWindow == null)
                    {
                        _osdWindow = new Views.OsdWindow();
                        _osdWindow.Closed += (s, e) =>
                        {
                            lock (_osdLock)
                            {
                                _osdWindow = null;
                            }
                        };
                    }
                    _osdWindow.Display(imageName, text);
                }
            }));
        }

        public App()
        {
            DispatcherUnhandledException += (s, args) =>
            {

                string logPath = Utility.AppLogger.Log("DispatcherUnhandledException", args.Exception);
                MessageBox.Show(

                    "An unexpected error occurred (saved to " + logPath + "):\n\n" + args.Exception.Message + "\n\nStack Trace:\n" + args.Exception.StackTrace,
                    "Application Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
    
                    string logPath = Utility.AppLogger.Log("UnhandledException", ex);
                    MessageBox.Show(
                        "A critical error occurred (saved to " + logPath + "):\n\n" + ex.Message + "\n\nStack Trace:\n" + ex.StackTrace,
                        "Critical Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            Utility.NativeMethods.HardenDllSearchOrder();

            if (HasUninstallArgument(e.Args))
            {
                Utility.StartupManager.SetRunAtStartup(false);
                Shutdown();
                return;
            }

            if (!TryAcquireSingleInstanceMutex())
            {
                SignalExistingInstanceAndExit();
                return;
            }

            RegisterWakeListener();
            RegisterWpfBridges();
            Utility.LocalizationManager.Initialize();

            Theme theme = AddCustomTheme();

            if (!EnsureAcpiDriverReady())
            {
                Shutdown();
                return;
            }

            EnsureStartupConfigured();
            base.OnStartup(e);

            ThemeManager.Current.ThemeSyncMode = ThemeSyncMode.DoNotSync;
            ThemeManager.Current.ChangeTheme(this, theme);
        }

        private static bool HasUninstallArgument(string[] args)
        {
            return args != null && Array.Exists(args, arg =>
                string.Equals(arg, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryAcquireSingleInstanceMutex()
        {
            bool createdNew;
            try
            {
                var mutexSecurity = new MutexSecurity();
                mutexSecurity.AddAccessRule(new MutexAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    MutexRights.FullControl,
                    AccessControlType.Allow));
                _appMutex = new Mutex(true, AppMutexName, out createdNew, mutexSecurity);
            }
            catch
            {
                _appMutex = new Mutex(true, AppMutexName, out createdNew);
            }
            return createdNew;
        }

        private void SignalExistingInstanceAndExit()
        {
            EventWaitHandle wakeHandle;
            if (EventWaitHandle.TryOpenExisting(WakeEventName, out wakeHandle))
            {
                using (wakeHandle)
                {
                    wakeHandle.Set();
                }
            }
            this.Shutdown();
        }

        private static void RegisterWakeListener()
        {
            try
            {
                var eventSecurity = new EventWaitHandleSecurity();
                eventSecurity.AddAccessRule(new EventWaitHandleAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    EventWaitHandleRights.FullControl,
                    AccessControlType.Allow));
                bool eventCreated;
                _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName, out eventCreated, eventSecurity);
            }
            catch
            {
                _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
            }

            _waitRegistration = ThreadPool.RegisterWaitForSingleObject(_wakeEvent, OnWakeRequested, null, -1, false);
        }

        private static Theme AddCustomTheme()
        {
            string themeUriText = string.Concat("pack", "://application:,,,/BSCustomClevoController;component/Dark.BlueBlack.xaml");
            Uri.TryCreate(themeUriText, UriKind.Absolute, out Uri themeLocation);
            return ThemeManager.Current.AddLibraryTheme(new LibraryTheme(
                themeLocation,
                MahAppsLibraryThemeProvider.DefaultInstance));
        }

        private static bool EnsureAcpiDriverReady()
        {
            if (Utility.AcpiDriverInstaller.IsDriverWorking())
            {
                return true;
            }

            var confirmResult = MessageBox.Show(
                Utility.LocalizationManager.T("DlgDriverSetupMsg"),
                Utility.LocalizationManager.T("DlgDriverSetupTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
            {
                return false;
            }

            bool rebootRequired;
            bool installed = Utility.AcpiDriverInstaller.InstallDriver(out rebootRequired);

            if (installed)
            {
                Utility.StartupManager.SetRunAtStartup(true);
                var restartChoice = MessageBox.Show(
                    Utility.LocalizationManager.T("DlgDriverInstalledMsg"),
                    Utility.LocalizationManager.T("DlgDriverInstalledTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (restartChoice == MessageBoxResult.Yes)
                {
                    Utility.AcpiDriverInstaller.RestartComputer();
                    Thread.Sleep(1000);
                }
            }
            else
            {
                MessageBox.Show(
                    Utility.LocalizationManager.T("DlgDriverErrorMsg"),
                    Utility.LocalizationManager.T("DlgDriverErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return false;
        }

        private static void EnsureStartupConfigured()
        {
            // Keep startup permanent once the AcpiBridge driver is installed.
            if (!Utility.StartupManager.IsRunAtStartupEnabled())
            {
                Utility.StartupManager.SetRunAtStartup(true);
            }
            else
            {
                Utility.StartupManager.EnsureConfiguredCorrectly();
            }
        }

        private static void OnWakeRequested(object state, bool timedOut)
        {
            Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (Current?.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.BringToForeground();
                }
            }));
        }

        private static bool _hasCleanedUp = false;
        private static readonly object _cleanupLock = new object();

        public static void CleanupOnExit()
        {
            lock (_cleanupLock)
            {
                if (_hasCleanedUp) return;
                _hasCleanedUp = true;

                try
                {
                    if (Controls.KeyboardEffects.IsAnySoftwareEffectActive())
                    {
                        Controls.KeyboardEffects.DisableSoftwareAnimationForExit();
                    }
                }
                catch (Exception ex)
                {
                    Utility.AppLogger.Log("App.CleanupOnExit.KeyboardEffects", ex);
                }

                try
                {
                    Utility.SystemPreferences.Shutdown();
                }
                catch (Exception ex)
                {
                    Utility.AppLogger.Log("App.CleanupOnExit.SystemPreferences", ex);
                }
            }
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            CleanupOnExit();
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            CleanupOnExit();
            ReleaseSingleInstanceResources();
            base.OnExit(e);
        }

        private static void ReleaseSingleInstanceResources()
        {
            if (_waitRegistration != null)
            {
                _waitRegistration.Unregister(null);
                _waitRegistration = null;
            }

            if (_wakeEvent != null)
            {
                _wakeEvent.Dispose();
                _wakeEvent = null;
            }

            if (_appMutex != null)
            {
                try
                {
                    _appMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex was not owned by exiting thread
                }
                _appMutex.Dispose();
                _appMutex = null;
            }
        }
    }
}
