using PowerSwitcher.TrayApp.Resources;
using PowerSwitcher.TrayApp.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace PowerSwitcher.TrayApp
{
    public partial class AboutWindow : ThemedDialogWindow
    {
        readonly IPowerManager pwrManager;
        readonly DispatcherTimer copiedFeedbackTimer;

        public AboutWindow(IPowerManager powerManager)
        {
            pwrManager = powerManager;
            InitializeComponent();

            AppNameText.Text = AppStrings.AppName;
            Title = $"About {AppStrings.AppName}";
            VersionText.Text = $"Version {appVersion.ToString(3)}  ·  {RuntimeInformation.FrameworkDescription}";

            var projectUrl = AppStrings.AboutAppURL.Replace("http://", "https://");
            ProjectLink.NavigateUri = new Uri(projectUrl);
            LicenceLink.NavigateUri = new Uri(projectUrl.TrimEnd('/') + "/blob/master/LICENCE");
            IconLink.NavigateUri = new Uri(AppStrings.IconLicenceURL.Replace("http://", "https://"));
            AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(Hyperlink_RequestNavigate));

            copiedFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            copiedFeedbackTimer.Tick += (s, e) => { copiedFeedbackTimer.Stop(); CopyButton.Content = "Copy details"; };

            updateStatus();
            pwrManager.PropertyChanged += PwrManager_PropertyChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            pwrManager.PropertyChanged -= PwrManager_PropertyChanged;
            copiedFeedbackTimer.Stop();
            base.OnClosed(e);
        }

        static Version appVersion => typeof(AboutWindow).Assembly.GetName().Version;

        private void PwrManager_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IPowerManager.CurrentSchema) || e.PropertyName == nameof(IPowerManager.CurrentPowerStatus))
            {
                updateStatus();
            }
        }

        private void updateStatus()
        {
            var schema = pwrManager.CurrentSchema;
            PlanText.Text = schema?.Name ?? "None";

            // Same colour as the tray icon's badge, so this doubles as its legend.
            var badge = TrayIconFactory.BadgeBrushFor(schema?.Guid, schema?.Name);
            PlanDot.Visibility = badge == null ? Visibility.Collapsed : Visibility.Visible;
            PlanDot.Fill = badge;

            PowerSourceText.Text = pwrManager.CurrentPowerStatus == PowerPlugStatus.Online ? "Plugged in" : "On battery";
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var details = new StringBuilder()
                .AppendLine($"{AppStrings.AppName} {appVersion}")
                .AppendLine($"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})")
                .AppendLine($"Windows {Environment.OSVersion.Version} ({RuntimeInformation.OSArchitecture})")
                .AppendLine($"Active plan: {pwrManager.CurrentSchema?.Name ?? "None"} {pwrManager.CurrentSchema?.Guid}")
                .AppendLine($"Theme: {(IsLightTheme ? "light" : "dark")}{(SystemParameters.HighContrast ? ", high contrast" : "")}")
                .ToString();

            try
            {
                Clipboard.SetText(details);
                CopyButton.Content = "Copied!";
            }
            catch (COMException)
            {
                CopyButton.Content = "Clipboard busy"; // Another app holds the clipboard open.
            }

            copiedFeedbackTimer.Stop();
            copiedFeedbackTimer.Start();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
