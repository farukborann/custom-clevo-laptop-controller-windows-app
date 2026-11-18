using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Views
{
    public partial class OsdWindow : Window
    {
        private readonly DispatcherTimer _hideTimer;
        private int _displaySequence = 0;

        public OsdWindow()
        {
            InitializeComponent();

            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _hideTimer.Tick += HideTimer_Tick;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOPMOST);
            NativeMethods.DisableWindowRoundedCorners(hwnd);
        }

        public void Display(string imageName, string text)
        {
            _hideTimer.Stop();
            _displaySequence++;
            this.BeginAnimation(UIElement.OpacityProperty, null);

            OsdText.Text = text;

            try
            {
                string uriText = string.Concat("pack", "://application:,,,/BSCustomClevoController;component/Assets/OSD/", imageName);
                if (Uri.TryCreate(uriText, UriKind.Absolute, out Uri packUri))
                {
                    OsdIcon.Source = new BitmapImage(packUri);
                }
            }
            catch
            {
                try
                {
                    OsdIcon.Source = new BitmapImage(new Uri("/Assets/OSD/" + imageName, UriKind.Relative));
                }
                catch
                {
                    try
                    {
                        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "OSD", imageName);
                        if (File.Exists(localPath))
                        {
                            OsdIcon.Source = new BitmapImage(new Uri(localPath, UriKind.Absolute));
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("OsdWindow.LoadIcon", ex);
                    }
                }
            }

            Rect workArea = SystemParameters.WorkArea;
            this.Left = workArea.Left + (workArea.Width - this.Width) / 2;

            if (workArea.Bottom >= SystemParameters.PrimaryScreenHeight - 2)
            {
                // Taskbar is hidden or on another display edge: place 54px above the bottom
                this.Top = SystemParameters.PrimaryScreenHeight - this.Height - 54;
            }
            else
            {
                // Taskbar is docked at bottom: place just above the taskbar
                this.Top = workArea.Bottom - this.Height - 8;
            }

            if (this.Visibility != Visibility.Visible)
            {
                this.Opacity = 0;
                this.Show();
            }

            DoubleAnimation fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            this.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            _hideTimer.Start();
        }

        private void HideTimer_Tick(object sender, EventArgs e)
        {
            _hideTimer.Stop();
            int currentSequence = _displaySequence;

            DoubleAnimation fadeOut = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, args) =>
            {
                if (_displaySequence == currentSequence)
                {
                    this.Hide();
                }
            };

            this.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}
