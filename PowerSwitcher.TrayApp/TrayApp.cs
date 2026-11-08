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
        #endregion

        #region Contructor
        public TrayApp(IPowerManager powerManager, ConfigurationInstance<PowerSwitcherSettings> config)
        {
            this.pwrManager = powerManager;
            pwrManager.PropertyChanged += PwrManager_PropertyChanged;

            configuration = config;

            // WPF doesn't do this for WinForms, and without it ToolStrip renderers ignore their colour table.
            WF.Application.EnableVisualStyles();

            _trayIcon = new WF.NotifyIcon();
            _trayIcon.MouseClick += TrayIcon_MouseClick;

            UpdateTheme();
            _trayIcon.Visible = true;

            this.ShowFlyout += (((App)Application.Current).MainWindow as MainWindow).ToggleWindowVisibility;

            //Run automatic on-off-AC change at boot
            powerStatusChanged();
        }

        public void CreateAltMenu()
        {
            var contextMenuRoot = new WF.ContextMenuStrip();
            contextMenuRoot.Opening += ContextMenu_Opening;
            contextMenuRoot.Renderer = TrayMenuRenderer.Create(ThemeService.IsHighContrast, ThemeService.IsLightTheme);

            _trayIcon.ContextMenuStrip = contextMenuRoot;

            var contextMenuRootItems = contextMenuRoot.Items;
            contextMenuRootItems.Add(new WF.ToolStripSeparator());

            var contextMenuSettings = addMenuItem(contextMenuRootItems, AppStrings.Settings);
            contextMenuSettings.Name = "settings";

            var settingsOnACItem = addMenuItem(contextMenuSettings.DropDownItems, AppStrings.SchemaToSwitchOnAc);
            settingsOnACItem.Name = "settingsOnAC";

            var settingsOffACItem = addMenuItem(contextMenuSettings.DropDownItems, AppStrings.SchemaToSwitchOffAc);
            settingsOffACItem.Name = "settingsOffAC";

            var automaticSwitchItem = addMenuItem(contextMenuSettings.DropDownItems, AppStrings.AutomaticOnOffACSwitch);
            automaticSwitchItem.Checked = configuration.Data.AutomaticOnACSwitch;
            automaticSwitchItem.Click += AutomaticSwitchItem_Click;

            var automaticHideItem = addMenuItem(contextMenuSettings.DropDownItems, AppStrings.HideFlyoutAfterSchemaChangeSwitch);
            automaticHideItem.Checked = configuration.Data.AutomaticFlyoutHideAfterClick;
            automaticHideItem.Click += AutomaticHideItem_Click;

            var onlyDefaultSchemasItem = addMenuItem(contextMenuSettings.DropDownItems, AppStrings.ShowOnlyDefaultSchemas);
            onlyDefaultSchemasItem.Checked = configuration.Data.ShowOnlyDefaultSchemas;
            onlyDefaultSchemasItem.Click += OnlyDefaultSchemas_Click;

            var enableShortcutsToggleItem = addMenuItem(contextMenuSettings.DropDownItems, $"{AppStrings.ToggleOnShowrtcutSwitch} ({configuration.Data.ShowOnShortcutKeyModifier} + {configuration.Data.ShowOnShortcutKey})");
            enableShortcutsToggleItem.Enabled = !(Application.Current as App).HotKeyFailed;
            enableShortcutsToggleItem.Checked = configuration.Data.ShowOnShortcutSwitch;
            enableShortcutsToggleItem.Click += EnableShortcutsToggleItem_Click;

            var aboutItem = addMenuItem(contextMenuRootItems, $"{AppStrings.About} ({Assembly.GetEntryAssembly().GetName().Version})");
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

        private Guid? iconSchemaGuid;
        private string iconSchemaName;
        private bool? iconLightTheme;
        private System.Drawing.Size? iconSize;

        /// <summary>Re-renders the tray icon and tooltip for the active plan; cheap no-op when nothing changed.</summary>
        private void refreshTrayIcon()
        {
            var schema = pwrManager.CurrentSchema;
            _trayIcon.Text = trayTooltip(schema);

            var schemaGuid = schema?.Guid;
            var schemaName = schema?.Name;
            var isLight = appliedLightTheme ?? ThemeService.IsLightTheme;
            var size = WF.SystemInformation.SmallIconSize;
            if (_trayIcon.Icon != null && schemaGuid == iconSchemaGuid && schemaName == iconSchemaName && isLight == iconLightTheme && size == iconSize) { return; }

            var previousIcon = _trayIcon.Icon;
            _trayIcon.Icon = TrayIconFactory.Create(schemaGuid, schemaName, isLight, size);
            previousIcon?.Dispose();

            iconSchemaGuid = schemaGuid;
            iconSchemaName = schemaName;
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

        #region SettingsTogglesRegion
        private void EnableShortcutsToggleItem_Click(object sender, EventArgs e)
        {
            var enableShortcutsToggleItem = (WF.ToolStripMenuItem)sender;

            configuration.Data.ShowOnShortcutSwitch = !configuration.Data.ShowOnShortcutSwitch;
            enableShortcutsToggleItem.Checked = configuration.Data.ShowOnShortcutSwitch;
            enableShortcutsToggleItem.Enabled = !(Application.Current as App).HotKeyFailed;

            configuration.Save();
        }

        private void AutomaticHideItem_Click(object sender, EventArgs e)
        {
            var automaticHideItem = (WF.ToolStripMenuItem)sender;

            configuration.Data.AutomaticFlyoutHideAfterClick = !configuration.Data.AutomaticFlyoutHideAfterClick;
            automaticHideItem.Checked = configuration.Data.AutomaticFlyoutHideAfterClick;

            configuration.Save();
        }

        private void OnlyDefaultSchemas_Click(object sender, EventArgs e)
        {
            var onlyDefaultSchemasItem = (WF.ToolStripMenuItem)sender;

            configuration.Data.ShowOnlyDefaultSchemas = !configuration.Data.ShowOnlyDefaultSchemas;
            onlyDefaultSchemasItem.Checked = configuration.Data.ShowOnlyDefaultSchemas;

            configuration.Save();
        }

        private void AutomaticSwitchItem_Click(object sender, EventArgs e)
        {
            var automaticSwitchItem = (WF.ToolStripMenuItem)sender;

            configuration.Data.AutomaticOnACSwitch = !configuration.Data.AutomaticOnACSwitch;
            automaticSwitchItem.Checked = configuration.Data.AutomaticOnACSwitch;

            if (configuration.Data.AutomaticOnACSwitch) { powerStatusChanged(); }

            configuration.Save();
        }

        #endregion

        #region AutomaticOnACSwitchRelated

        private void PwrManager_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IPowerManager.CurrentPowerStatus)) { powerStatusChanged(); }
            if (e.PropertyName == nameof(IPowerManager.CurrentSchema)) { refreshTrayIcon(); }
        }

        private void powerStatusChanged()
        {
            if(!configuration.Data.AutomaticOnACSwitch) { return; }

            var currentPowerPlugStatus = pwrManager.CurrentPowerStatus;
            Guid schemaGuidToSwitch = default(Guid);

            switch (currentPowerPlugStatus)
            {
                case PowerPlugStatus.Online:
                    schemaGuidToSwitch = configuration.Data.AutomaticPlanGuidOnAC;
                    break;
                case PowerPlugStatus.Offline:
                    schemaGuidToSwitch = configuration.Data.AutomaticPlanGuidOffAC;
                    break;
                default:
                    break;
            }

            IPowerSchema schemaToSwitchTo = pwrManager.Schemas.FirstOrDefault(sch => sch.Guid == schemaGuidToSwitch);
            if(schemaToSwitchTo == null) { return; }

            pwrManager.SetPowerSchema(schemaToSwitchTo);
        }

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

            var newItemSettingsOffAC = getNewPowerSchemaItem(
                powerSchema,
                (s, ea) => setPowerSchemaAsOffAC(powerSchema),
                (powerSchema.Guid == configuration.Data.AutomaticPlanGuidOffAC)
                );
            getSettingsSubmenu("settingsOffAC").DropDownItems.Insert(0, newItemSettingsOffAC);

            var newItemSettingsOnAC = getNewPowerSchemaItem(
                powerSchema,
                (s, ea) => setPowerSchemaAsOnAC(powerSchema),
                (powerSchema.Guid == configuration.Data.AutomaticPlanGuidOnAC)
                );

            getSettingsSubmenu("settingsOnAC").DropDownItems.Insert(0, newItemSettingsOnAC);
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

            clearDropDownItems(getSettingsSubmenu("settingsOffAC"));
            clearDropDownItems(getSettingsSubmenu("settingsOnAC"));
        }

        private static void clearDropDownItems(WF.ToolStripMenuItem menuItem)
        {
            for (int i = menuItem.DropDownItems.Count - 1; i >= 0; i--)
            {
                menuItem.DropDownItems[i].Dispose();
            }
        }

        private WF.ToolStripMenuItem getSettingsSubmenu(string name)
        {
            var settings = (WF.ToolStripMenuItem)_trayIcon.ContextMenuStrip.Items["settings"];
            return (WF.ToolStripMenuItem)settings.DropDownItems[name];
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
        private void setPowerSchemaAsOffAC(IPowerSchema powerSchema)
        {
            configuration.Data.AutomaticPlanGuidOffAC = powerSchema.Guid;
            configuration.Save();
        }

        private void setPowerSchemaAsOnAC(IPowerSchema powerSchema)
        {
            configuration.Data.AutomaticPlanGuidOnAC = powerSchema.Guid;
            configuration.Save();
        }

        private void switchToPowerSchema(IPowerSchema powerSchema)
        {
            pwrManager.SetPowerSchema(powerSchema);
        }
        #endregion

        #region OtherItemsClicked

        void About_Click(object sender, EventArgs e)
        {
            openUrl(AppStrings.AboutAppURL);
        }

        private void IconLicenceItem_Click(object sender, EventArgs e)
        {
            openUrl(AppStrings.IconLicenceURL);
        }

        private static void openUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }


        void Exit_Click(object sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            pwrManager.Dispose();

            Application.Current.Shutdown();
        }
        #endregion

    }
}
