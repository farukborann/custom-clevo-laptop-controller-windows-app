using BSCustomClevoController.Utility;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BSCustomClevoController.Controls;


namespace BSCustomClevoController.Views
{
    public partial class PageSettings : Page
    {
        private bool _isLoadingLanguage;

        public PageSettings()
        {
            InitializeComponent();
            LoadLanguageState();
            LoadOsdState();
            LoadDriverState();
            LoadCapabilities();

            Loaded += (s, e) => LoadLanguageState();

            LocalizationManager.LanguageChanged += () =>
            {
                LoadDriverState();
                LoadCapabilities();
            };
        }

        private void LoadLanguageState()
        {
            _isLoadingLanguage = true;
            try
            {
                var languages = LocalizationManager.SupportedLanguages;
                LanguageComboBox.ItemsSource = languages;
                LanguageComboBox.SelectedValue = LocalizationManager.CurrentLanguage;
                if (LanguageComboBox.SelectedItem == null && languages != null)
                {
                    var matchingLanguage = languages.FirstOrDefault(
                        opt => string.Equals(opt.Code, LocalizationManager.CurrentLanguage, StringComparison.OrdinalIgnoreCase));
                    if (matchingLanguage != null)
                    {
                        LanguageComboBox.SelectedItem = matchingLanguage;
                    }
                }
            }
            finally
            {
                _isLoadingLanguage = false;
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingLanguage) return;
            string code = (LanguageComboBox.SelectedItem as LanguageOption)?.Code ?? (LanguageComboBox.SelectedValue as string);
            if (!string.IsNullOrWhiteSpace(code) &&
                !string.Equals(code, LocalizationManager.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                LocalizationManager.SetLanguage(code);
            }
        }

        private void LoadDriverState()
        {
            try
            {
                bool working = AcpiDriverInstaller.IsDriverWorking();
                if (working)
                {
                    this.DriverStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x2D, 0x42));
                    this.DriverStatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB));
                    this.DriverStatusText.Text = LocalizationManager.T("SettingsStatusInstalled");
                    this.DriverStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x79, 0xCF, 0xFF));
                    this.ReinstallDriverBtn.Content = LocalizationManager.T("SettingsReinstall");
                    this.UninstallDriverBtn.IsEnabled = true;
                }
                else
                {
                    this.DriverStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x1B, 0x1B));
                    this.DriverStatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26));
                    this.DriverStatusText.Text = LocalizationManager.T("SettingsStatusNotInstalled");
                    this.DriverStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFA, 0x79, 0x70));
                    this.ReinstallDriverBtn.Content = LocalizationManager.T("SettingsInstallDriver");
                    this.UninstallDriverBtn.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("PageSettings.LoadDriverState", ex);
            }
        }

        private async void ReinstallDriver_Click(object sender, RoutedEventArgs e)
        {
            bool isWorking = AcpiDriverInstaller.IsDriverWorking();

            var confirm = MessageBox.Show(
                isWorking ? LocalizationManager.T("DlgDriverReinstallConfirm") : LocalizationManager.T("DlgDriverInstallConfirm"),
                isWorking ? LocalizationManager.T("SettingsReinstall") : LocalizationManager.T("SettingsInstallDriver"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            ReinstallDriverBtn.IsEnabled = false;
            UninstallDriverBtn.IsEnabled = false;
            DriverStatusText.Text = LocalizationManager.T("SettingsStatusInstalling");
            DriverStatusDesc.Text = LocalizationManager.T("SettingsDriverInstalling");

            bool rebootRequired = false;
            bool success = await Task.Run(() => AcpiDriverInstaller.InstallDriver(out rebootRequired, force: true));

            ReinstallDriverBtn.IsEnabled = true;
            DriverStatusDesc.Text = LocalizationManager.T("SettingsDriverDesc");
            LoadDriverState();

            if (success)
            {
                StartupManager.SetRunAtStartup(true);
                Capabilities.InvalidateCache();
                LoadCapabilities();

                var res = MessageBox.Show(
                    LocalizationManager.T("DlgDriverInstalledMsg"),
                    LocalizationManager.T("DlgDriverInstalledTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    DriverStatusText.Text = LocalizationManager.T("SettingsStatusRestarting");
                    AcpiDriverInstaller.RestartComputer();
                    await Task.Delay(1000);
                    Application.Current.Shutdown();
                }
            }
            else
            {
                MessageBox.Show(
                    LocalizationManager.T("DlgDriverErrorMsg"),
                    LocalizationManager.T("DlgDriverErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void UninstallDriver_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                LocalizationManager.T("DlgDriverUninstallConfirm"),
                LocalizationManager.T("SettingsUninstall"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            ReinstallDriverBtn.IsEnabled = false;
            UninstallDriverBtn.IsEnabled = false;
            DriverStatusText.Text = LocalizationManager.T("SettingsStatusRemoving");
            DriverStatusDesc.Text = LocalizationManager.T("SettingsDriverRemoving");

            bool success = await Task.Run(() => AcpiDriverInstaller.UninstallDriver());

            ReinstallDriverBtn.IsEnabled = true;
            DriverStatusDesc.Text = LocalizationManager.T("SettingsDriverDesc");

            if (success)
            {
                StartupManager.SetRunAtStartup(false);
                Capabilities.InvalidateCache();
                try
                {
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\BSCustomClevoController", true)?.DeleteValue("DefaultsApplied", false);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Page_Settings.UninstallDriver.Registry", ex);
                }
                DriverStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x2E, 0x1B));
                DriverStatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD9, 0x77, 0x06));
                DriverStatusText.Text = LocalizationManager.T("SettingsStatusPendingRestart");
                DriverStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24));
                ReinstallDriverBtn.Content = LocalizationManager.T("SettingsInstallDriver");
                UninstallDriverBtn.IsEnabled = false;

                var restartRes = MessageBox.Show(
                    LocalizationManager.T("DlgDriverUninstallDone"),
                    LocalizationManager.T("DlgDriverInstalledTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (restartRes == MessageBoxResult.Yes)
                {
                    DriverStatusText.Text = LocalizationManager.T("SettingsStatusRestarting");
                    AcpiDriverInstaller.RestartComputer();
                    await Task.Delay(1000);
                    Application.Current.Shutdown();
                }
            }
            else
            {
                LoadDriverState();
                MessageBox.Show(
                    LocalizationManager.T("DlgDriverUninstallErr"),
                    LocalizationManager.T("SettingsUninstall"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadOsdState()
        {
            try
            {
                this.OsdCheckBox.IsChecked = OsdOverlay.IsEnabled;
            }
            catch (Exception ex)
            {
                AppLogger.Log("PageSettings.LoadOsdState", ex);
                this.OsdCheckBox.IsChecked = true;
            }
        }

        private void OsdCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = OsdCheckBox.IsChecked.GetValueOrDefault();
            OsdOverlay.IsEnabled = enabled;
            SystemPreferences.UpdateHookState();
        }

        private void ResetFactory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                LocalizationManager.T("DlgResetConfirmMsg"),
                LocalizationManager.T("DlgResetConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                SystemPreferences.ApplyFactoryDefaults();

                (Application.Current.MainWindow as MainWindow)?.ReloadCachedPages();

                MessageBox.Show(
                    LocalizationManager.T("DlgResetDone"),
                    LocalizationManager.T("DlgResetConfirmTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationManager.Format("SettingsResetErrorFmt", ex.Message),
                    LocalizationManager.T("SettingsResetErrorTitle", "Reset Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadCapabilities()
        {
            try
            {
                FeaturesListBox.ItemsSource = Capabilities.Check();
            }
            catch (Exception ex)
            {
                FeaturesListBox.ItemsSource = new System.Collections.Generic.List<CapabilityItem>
                {
                    new CapabilityItem
                    {
                        Name = LocalizationManager.Format("SettingsDiagQueryErrorFmt", ex.Message),
                        IsSupported = false
                    }
                };
            }
        }

        private void RefreshCapabilities_Click(object sender, RoutedEventArgs e)
        {
            LoadCapabilities();
        }

        private async void CopyCapabilities_Click(object sender, RoutedEventArgs e)
        {
            bool copied = CopyAllToClipboard();
            if (copied && CopyCapabilitiesBtn != null)
            {
                CopyCapabilitiesBtn.Content = LocalizationManager.T("SettingsCopied");
                await Task.Delay(1500);
                CopyCapabilitiesBtn.Content = LocalizationManager.T("SettingsCopyAll");
            }
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            CopyAllToClipboard();
        }

        private static bool SafeSetClipboardText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(text, true, 10, 50);
                return true;
            }
            catch (Exception wfEx)
            {
                AppLogger.Log("Page_Settings.SafeSetClipboardText.WinForms", wfEx);
            }

            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (Exception wpfEx)
            {
                AppLogger.Log("Page_Settings.SafeSetClipboardText.Wpf", wpfEx);
                return false;
            }
        }

        private bool CopyAllToClipboard()
        {
            if (this.FeaturesListBox.ItemsSource == null) return false;

            var sb = new StringBuilder();
            sb.AppendLine(LocalizationManager.T("SettingsDiagHeader", "=== Hardware EC Capabilities Diagnostic ==="));
            sb.AppendLine(LocalizationManager.Format("SettingsDiagTimestampFmt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", LocalizationManager.CurrentCulture)));
            sb.AppendLine();

            foreach (var item in this.FeaturesListBox.ItemsSource)
            {
                if (item != null)
                {
                    sb.AppendLine(item.ToString());
                }
            }

            bool success = SafeSetClipboardText(sb.ToString());
            if (!success)
            {
                MessageBox.Show(
                    LocalizationManager.T("SettingsCopyError"),
                    LocalizationManager.T("CommonNotice"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return success;
        }
    }
}
