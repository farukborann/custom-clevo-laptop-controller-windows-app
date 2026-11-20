using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Views
{
    public partial class PageAbout : Page
    {
        private const string GitHubHost = "github.com";

        public PageAbout()
        {
            InitializeComponent();
        }

        private void OpenUpstreamRepo_Click(object sender, RoutedEventArgs e)
        {
            OpenGitHubRepository("farukborann", "custom-clevo-laptop-controller-windows-app");
        }

        private void OpenCurrentRepo_Click(object sender, RoutedEventArgs e)
        {
            OpenGitHubRepository("AlberttFrgk", "CustomClevoController");
        }

        private void OpenVaioCenter_Click(object sender, RoutedEventArgs e)
        {
            OpenGitHubRepository("GuilhermeBHanzen", "vaio-center");
        }

        private void OpenInsydeKeyboardLight_Click(object sender, RoutedEventArgs e)
        {
            OpenGitHubRepository("FFeng123", "InsydeKeyboardLight");
        }

        private void OpenCppKeyboardColour_Click(object sender, RoutedEventArgs e)
        {
            OpenGitHubRepository("DeviceIoControl", "CppKeyboardColour");
        }

        private void OpenTuxedoKeyboard_Click(object sender, RoutedEventArgs e)
        {
            OpenGitHubRepository("tuxedocomputers", "tuxedo-keyboard");
        }

        private static void OpenGitHubRepository(string owner, string repository)
        {
            Uri repoUri = new UriBuilder(Uri.UriSchemeHttps, GitHubHost, -1, $"{owner}/{repository}").Uri;
            OpenUri(repoUri);
        }

        private static void OpenUri(Uri parsedUri)
        {
            if (parsedUri == null || !string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(parsedUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationManager.Format("CommonErrorFmt", ex.Message), LocalizationManager.T("CommonNotice"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
