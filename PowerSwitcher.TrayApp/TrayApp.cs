using Petrroll.Helpers;
using PowerSwitcher.TrayApp.Configuration;
using PowerSwitcher.TrayApp.Resources;
using PowerSwitcher.TrayApp.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using WF = System.Windows.Forms;


namespace PowerSwitcher.TrayApp
{

    public class TrayApp
    {

        #region PrivateObjects
        readonly WF.NotifyIcon _trayIcon;
        public event Action ShowFlyout;
        IPowerManager pwrManager;
        ConfigurationInstance<PowerSwitcherSettings> configuration;
        UpdateService updates;
        #endregion

        #region Contructor
        public TrayApp(IPowerManager powerManager, ConfigurationInstance<PowerSwitcherSettings> config, UpdateService updateService)
        {
            this.pwrManager = powerManager;
            pwrManager.PropertyChanged += PwrManager_PropertyChanged;

            configuration = config;
            configuration.Data.PropertyChanged += Settings_PropertyChanged;

            updates = updateService;
            updates.UpdateReady += Updates_UpdateReady;

            // WPF doesn't do this for WinForms, and without it ToolStrip renderers ignore their colour table.
            WF.Application.EnableVisualStyles();

            _trayIcon = new WF.NotifyIcon();
            _trayIcon.MouseClick += TrayIcon_MouseClick;
            _trayIcon.BalloonTipClicked += (s, e) => About_Click(s, e);

            UpdateTheme();
            _trayIcon.Visible = true;

            this.ShowFlyout += (((App)Application.Current).MainWindow as MainWindow).ToggleWindowVisibility;

            // Apply the plugged-in/battery and schedule rules at start, then keep watching the schedule.
            applyAutomaticPlan();
            startScheduleChecks();
        }

        public void CreateAltMenu()
        {
            var contextMenuRoot = new WF.ContextMenuStrip();
            contextMenuRoot.Opening += ContextMenu_Opening;
            contextMenuRoot.Renderer = TrayMenuRenderer.Create(ThemeService.IsHighContrast, ThemeService.IsLightTheme);

            _trayIcon.ContextMenuStrip = contextMenuRoot;

            var contextMenuRootItems = contextMenuRoot.Items;
            contextMenuRootItems.Add(new WF.ToolStripSeparator());

            // Shown once an update has downloaded; see Updates_UpdateReady.
            var updateItem = addMenuItem(contextMenuRootItems, "");
            updateItem.Name = "update";
            updateItem.Font = new System.Drawing.Font(updateItem.Font, System.Drawing.FontStyle.Bold);
            updateItem.Click += (s, e) => RestartToUpdate();
            updateItem.Visible = false;

            var editItem = addMenuItem(contextMenuRootItems, "Edit power plans…");
            editItem.Click += Edit_Click;

            var compareItem = addMenuItem(contextMenuRootItems, "Compare plans…");
            compareItem.Click += Compare_Click;

            var settingsItem = addMenuItem(contextMenuRootItems, $"{AppStrings.Settings}…");
            settingsItem.Name = "settings";
            settingsItem.Click += Settings_Click;

            var aboutItem = addMenuItem(contextMenuRootItems, $"{AppStrings.About}…");
            aboutItem.Click += About_Click;

            var exitItem = addMenuItem(contextMenuRootItems, AppStrings.Exit);
            exitItem.Click += Exit_Click;
        }

        private static WF.ToolStripMenuItem addMenuItem(WF.ToolStripItemCollection items, string text)
        {
            var item = new WF.ToolStripMenuItem(text);
            items.Add(item);
            return item;
        }

        #endregion

        #region ThemeRelated
        private bool? appliedLightTheme;
        private bool? appliedHighContrast;

        public void UpdateTheme()
        {
            var isLight = ThemeService.IsLightTheme;
            var isHighContrast = ThemeService.IsHighContrast;

            if (_trayIcon.ContextMenuStrip != null && (isLight != appliedLightTheme || isHighContrast != appliedHighContrast))
            {
                _trayIcon.ContextMenuStrip.Renderer = TrayMenuRenderer.Create(isHighContrast, isLight);
            }

            appliedLightTheme = isLight;
            appliedHighContrast = isHighContrast;

            refreshTrayIcon();
        }

        private System.Drawing.Color? iconBadgeColor;
        private bool? iconLightTheme;
        private System.Drawing.Size? iconSize;

        /// <summary>Re-renders the tray icon and tooltip for the active plan; cheap no-op when nothing changed.</summary>
        private void refreshTrayIcon()
        {
            var schema = pwrManager.CurrentSchema;
            _trayIcon.Text = trayTooltip(schema);

            var badgeColor = TrayIconFactory.BadgeColorFor(schema?.Guid, schema?.Name, configuration.Data);
            var isLight = appliedLightTheme ?? ThemeService.IsLightTheme;
            var size = WF.SystemInformation.SmallIconSize;
            if (_trayIcon.Icon != null && badgeColor == iconBadgeColor && isLight == iconLightTheme && size == iconSize) { return; }

            var previousIcon = _trayIcon.Icon;
            _trayIcon.Icon = TrayIconFactory.Create(badgeColor, isLight, size);
            previousIcon?.Dispose();

            iconBadgeColor = badgeColor;
            iconLightTheme = isLight;
            iconSize = size;
        }

        private static string trayTooltip(IPowerSchema schema)
        {
            var text = schema == null ? AppStrings.AppName : $"{AppStrings.AppName}: {schema.Name}";
            const int maxTooltipLength = 127; // NotifyIcon.Text throws above this.
            return text.Length <= maxTooltipLength ? text : text.Substring(0, maxTooltipLength);
        }
        #endregion

        #region FlyoutRelated
        void TrayIcon_MouseClick(object sender, WF.MouseEventArgs e)
        {
            if (e.Button == WF.MouseButtons.Left)
            {
                ShowFlyout?.Invoke();
            }
        }

        #endregion

        #region Automatic switching

        // Settings that change which plan automatic switching wants; changing one applies it straight away.
        static readonly string[] automaticSwitchingSettings =
        {
            nameof(PowerSwitcherSettings.AutomaticOnACSwitch),
            nameof(PowerSwitcherSettings.AutomaticPlanGuidOnAC),
            nameof(PowerSwitcherSettings.AutomaticPlanGuidOffAC),
            nameof(PowerSwitcherSettings.ScheduleEnabled),
            nameof(PowerSwitcherSettings.ScheduleDays),
            nameof(PowerSwitcherSettings.ScheduleStartMinutes),
            nameof(PowerSwitcherSettings.ScheduleEndMinutes),
            nameof(PowerSwitcherSettings.SchedulePlanDuring),
            nameof(PowerSwitcherSettings.SchedulePlanOutside),
        };

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (automaticSwitchingSettings.Contains(e.PropertyName)) { applyAutomaticPlan(); }
            else if (e.PropertyName == nameof(PowerSwitcherSettings.PlanColors)) { refreshTrayIcon(); }
        }

        private void PwrManager_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IPowerManager.CurrentPowerStatus)) { applyAutomaticPlan(); }
            if (e.PropertyName == nameof(IPowerManager.CurrentSchema)) { refreshTrayIcon(); }
        }

        // Whether it was work time when automatic switching last ran, so the schedule only acts when work hours start or end.
        bool? appliedWorkTime;
        System.Windows.Threading.DispatcherTimer scheduleTimer;

        private void applyAutomaticPlan()
        {
            var now = DateTime.Now;
            appliedWorkTime = configuration.Data.GetWorkSchedule().IsWorkTime(now);

            var target = PowerSwitcher.Scheduling.AutomaticPlan.For(configuration.Data.GetAutomaticSwitching(), pwrManager.CurrentPowerStatus, now);
            var schema = pwrManager.Schemas.FirstOrDefault(sch => sch.Guid == target);
            if (schema == null) { return; }

            pwrManager.SetPowerSchema(schema);
        }

        /// <summary>
        /// Switches plan when work hours have started or ended since the last switch. A plan chosen by hand in between
        /// stays until then. Runs every half minute, and after waking up or a clock change, so it doesn't need an exact timer.
        /// </summary>
        private void checkSchedule()
        {
            if (!configuration.Data.ScheduleEnabled) { return; }
            if (configuration.Data.GetWorkSchedule().IsWorkTime(DateTime.Now) == appliedWorkTime) { return; }

            applyAutomaticPlan();
        }

        private void startScheduleChecks()
        {
            scheduleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            scheduleTimer.Tick += (s, e) => checkSchedule();
            scheduleTimer.Start();

            Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            Microsoft.Win32.SystemEvents.TimeChanged += SystemEvents_TimeChanged;
        }

        private void stopScheduleChecks()
        {
            scheduleTimer?.Stop();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            Microsoft.Win32.SystemEvents.TimeChanged -= SystemEvents_TimeChanged;
        }

        // SystemEvents can arrive on their own thread.
        private void SystemEvents_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) { Application.Current?.Dispatcher.BeginInvoke(new Action(checkSchedule)); }
        }

        private void SystemEvents_TimeChanged(object sender, EventArgs e) => Application.Current?.Dispatcher.BeginInvoke(new Action(checkSchedule));

        #endregion

        #region ContextMenuItemRelatedStuff

        private void ContextMenu_Opening(object sender, CancelEventArgs e)
        {
            clearPowerSchemasInTray();

            pwrManager.UpdateSchemas();
            foreach (var powerSchema in pwrManager.Schemas)
            {
                updateTrayMenuWithPowerSchema(powerSchema);
            }
        }

        private void updateTrayMenuWithPowerSchema(IPowerSchema powerSchema)
        {
            var newItemMain = getNewPowerSchemaItem(
                powerSchema,
                (s, ea) => switchToPowerSchema(powerSchema),
                powerSchema.IsActive
                );
            _trayIcon.ContextMenuStrip.Items.Insert(0, newItemMain);
        }

        private void clearPowerSchemasInTray()
        {
            var rootItems = _trayIcon.ContextMenuStrip.Items;
            for (int i = rootItems.Count - 1; i >= 0; i--)
            {
                var item = rootItems[i];
                if (item.Name.StartsWith("pwrScheme", StringComparison.Ordinal))
                {
                    item.Dispose();
                }
            }
        }

        private WF.ToolStripMenuItem getNewPowerSchemaItem(IPowerSchema powerSchema, EventHandler clickedHandler, bool isChecked)
        {
            var newItemMain = new WF.ToolStripMenuItem(powerSchema.Name);
            newItemMain.Name = $"pwrScheme{powerSchema.Guid}";
            newItemMain.Checked = isChecked;
            newItemMain.Click += clickedHandler;

            return newItemMain;
        }

        #endregion

        #region OnSchemaClickMethods
        private void switchToPowerSchema(IPowerSchema powerSchema)
        {
            pwrManager.SetPowerSchema(powerSchema);
        }
        #endregion

        #region OtherItemsClicked

        #region Updates

        private void Updates_UpdateReady()
        {
            var updateItem = _trayIcon.ContextMenuStrip?.Items["update"];
            if (updateItem != null)
            {
                updateItem.Text = $"Restart to update to {updates.PendingVersion}";
                updateItem.Visible = true;
            }

            _trayIcon.ShowBalloonTip(10000, $"{AppStrings.AppName} {updates.PendingVersion} is ready",
                "Restart from the tray menu to update, or it'll be applied the next time the app closes.", WF.ToolTipIcon.Info);
        }

        /// <summary>Restarts into a downloaded update.</summary>
        public void RestartToUpdate()
        {
            if (updates.State != UpdateState.Ready) { return; }

            // Velopack ends the process without WPF's Exit, so don't leave a ghost icon in the tray.
            _trayIcon.Visible = false;
            updates.ApplyAndRestart();
        }

        #endregion

        private AboutWindow aboutWindow;
        private SettingsWindow settingsWindow;
        private PlanCompareWindow compareWindow;

        private PlanEditorWindow editorWindow;

        void Edit_Click(object sender, EventArgs e)
        {
            showSingleWindow(ref editorWindow, () => new PlanEditorWindow(pwrManager, configuration, PowerSwitcher.Settings.PowerPlanEditor.CreateDefault()), w => editorWindow = null);
        }

        void Compare_Click(object sender, EventArgs e)
        {
            showSingleWindow(ref compareWindow, () => new PlanCompareWindow(pwrManager, configuration, PowerSwitcher.Settings.PowerPlanEditor.CreateDefault()), w => compareWindow = null);
        }

        void About_Click(object sender, EventArgs e)
        {
            showSingleWindow(ref aboutWindow, () => new AboutWindow(pwrManager, updates, RestartToUpdate), w => aboutWindow = null);
        }

        void Settings_Click(object sender, EventArgs e)
        {
            showSingleWindow(ref settingsWindow, () => new SettingsWindow(pwrManager, configuration, () => Edit_Click(this, EventArgs.Empty)), w => settingsWindow = null);
        }

        /// <summary>Shows the window, creating it only if it isn't already open; a second click brings it to the front.</summary>
        private static void showSingleWindow<T>(ref T window, Func<T> create, Action<T> onClosed) where T : Window
        {
            if (window == null)
            {
                var created = create();
                created.Closed += (s, ea) => onClosed(created);
                created.Show();
                window = created;
            }

            if (window.WindowState == WindowState.Minimized) { window.WindowState = WindowState.Normal; }
            window.Activate();
        }


        void Exit_Click(object sender, EventArgs e)
        {
            stopScheduleChecks();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            pwrManager.Dispose();

            Application.Current.Shutdown();
        }
        #endregion

    }
}
