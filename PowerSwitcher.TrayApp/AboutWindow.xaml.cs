using PowerSwitcher.TrayApp.Resources;
using PowerSwitcher.TrayApp.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
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
        readonly UpdateService updates;
        readonly Action restartToUpdate;
        readonly DispatcherTimer copiedFeedbackTimer;

        public AboutWindow(IPowerManager powerManager, UpdateService updateService, Action restartToUpdate)
        {
            pwrManager = powerManager;
            updates = updateService;
            this.restartToUpdate = restartToUpdate;
            InitializeComponent();

            AppNameText.Text = AppStrings.AppName;
            Title = $"About {AppStrings.AppName}";
            VersionText.Text = $"Version {displayVersion}  ·  {RuntimeInformation.FrameworkDescription}";

            var projectUrl = AppStrings.AboutAppURL.Replace("http://", "https://");
            ProjectLink.NavigateUri = new Uri(projectUrl);
            LicenceLink.NavigateUri = new Uri(projectUrl.TrimEnd('/') + "/blob/master/LICENCE");
            IconLink.NavigateUri = new Uri(AppStrings.IconLicenceURL.Replace("http://", "https://"));
            AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(Hyperlink_RequestNavigate));

            copiedFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            copiedFeedbackTimer.Tick += (s, e) => { copiedFeedbackTimer.Stop(); CopyButton.Content = "Copy details"; };

            updateStatus();
            updateUpdateStatus();
            pwrManager.PropertyChanged += PwrManager_PropertyChanged;
            updates.PropertyChanged += Updates_PropertyChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            pwrManager.PropertyChanged -= PwrManager_PropertyChanged;
            updates.PropertyChanged -= Updates_PropertyChanged;
            copiedFeedbackTimer.Stop();
            base.OnClosed(e);
        }

        /// <summary>The GitVersion-stamped version, e.g. "0.5.0-dev.3" (or "0.5.0-local" for a plain dotnet build).</summary>
        static string fullVersion =>
            typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AboutWindow).Assembly.GetName().Version.ToString(3);

        // Build metadata after '+' (branch, commit) is for bug reports, not the headline.
        static string displayVersion => fullVersion.Split('+')[0];

        #region Updates

        private void Updates_PropertyChanged(object sender, PropertyChangedEventArgs e) => updateUpdateStatus();

        private void updateUpdateStatus()
        {
            string text, action = null;
            switch (updates.State)
            {
                case UpdateState.NotInstalled: text = "Only for installed copies"; break;
                case UpdateState.Checking: text = "Checking…"; break;
                case UpdateState.UpToDate: text = "Up to date"; action = "Check again"; break;
                case UpdateState.Ready: text = updates.PendingVersion != null ? $"Version {updates.PendingVersion} is ready" : "An update is ready"; action = "Restart to update"; break;
                case UpdateState.Failed: text = "Couldn't check for updates"; action = "Try again"; break;
                default: text = "Not checked yet"; action = "Check now"; break;
            }

            UpdateText.Text = action == null ? text : text + "  ·  ";
            UpdateActionText.Text = action ?? "";
            UpdateAction.IsEnabled = action != null;
        }

        private async void UpdateAction_Click(object sender, RoutedEventArgs e)
        {
            if (updates.State == UpdateState.Ready) { restartToUpdate(); }
            else { await updates.CheckAsync(); }
        }

        #endregion

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
                .AppendLine($"{AppStrings.AppName} {fullVersion}{(updates.IsInstalled ? "" : " (not installed)")}")
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
