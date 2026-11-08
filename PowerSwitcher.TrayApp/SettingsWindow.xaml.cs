using Petrroll.Helpers;
using PowerSwitcher.TrayApp.Configuration;
using PowerSwitcher.TrayApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PowerSwitcher.TrayApp
{
    /// <summary>All of PowerSwitcher's settings in one window. Every change is applied and saved immediately.</summary>
    public partial class SettingsWindow : ThemedDialogWindow
    {
        public sealed class PlanOption
        {
            public Guid Guid { get; }
            public string Name { get; }
            public Brush Dot { get; }

            public PlanOption(IPowerSchema schema)
            {
                Guid = schema.Guid;
                Name = schema.Name;
                Dot = TrayIconFactory.BadgeBrushFor(schema.Guid, schema.Name);
            }
        }

        public sealed class Option<T>
        {
            public T Value { get; }
            public string Label { get; }
            public Option(T value, string label) { Value = value; Label = label; }
        }

        readonly IPowerManager pwrManager;
        readonly ConfigurationInstance<PowerSwitcherSettings> configuration;
        readonly App app;

        // True while the window itself is setting control values, so their change events don't write back to settings.
        bool loading;

        public SettingsWindow(IPowerManager powerManager, ConfigurationInstance<PowerSwitcherSettings> config)
        {
            pwrManager = powerManager;
            configuration = config;
            app = Application.Current as App;

            InitializeComponent();

            loading = true;
            var data = configuration.Data;

            AutoSwitchToggle.IsChecked = data.AutomaticOnACSwitch;
            HideAfterClickToggle.IsChecked = data.AutomaticFlyoutHideAfterClick;
            OnlyDefaultToggle.IsChecked = data.ShowOnlyDefaultSchemas;
            ShortcutToggle.IsChecked = data.ShowOnShortcutSwitch;

            pwrManager.UpdateSchemas();
            loadPlans();
            loadShortcutChoices();

            loading = false;
            updateEnabledStates();
            updateShortcutError();

            pwrManager.Schemas.CollectionChanged += Schemas_CollectionChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            pwrManager.Schemas.CollectionChanged -= Schemas_CollectionChanged;
            base.OnClosed(e);
        }

        #region Automatic switching

        private void loadPlans()
        {
            var wasLoading = loading;
            loading = true;

            var options = pwrManager.Schemas.Select(sch => new PlanOption(sch)).ToList();
            OnAcPlanBox.ItemsSource = options;
            OffAcPlanBox.ItemsSource = options;
            OnAcPlanBox.SelectedItem = options.FirstOrDefault(o => o.Guid == configuration.Data.AutomaticPlanGuidOnAC);
            OffAcPlanBox.SelectedItem = options.FirstOrDefault(o => o.Guid == configuration.Data.AutomaticPlanGuidOffAC);

            loading = wasLoading;
        }

        // Plans added or removed (e.g. in Control Panel) while the window is open.
        private void Schemas_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => loadPlans();

        private void AutoSwitchToggle_Changed(object sender, RoutedEventArgs e)
        {
            updateEnabledStates();
            if (loading) { return; }

            configuration.Data.AutomaticOnACSwitch = AutoSwitchToggle.IsChecked == true; // TrayApp applies the plan right away.
            configuration.Save();
        }

        private void PlanBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading) { return; }
            if (!(((ComboBox)sender).SelectedItem is PlanOption option)) { return; }

            if (sender == OnAcPlanBox) { configuration.Data.AutomaticPlanGuidOnAC = option.Guid; }
            else { configuration.Data.AutomaticPlanGuidOffAC = option.Guid; }
            configuration.Save();
        }

        #endregion

        #region Flyout

        private void HideAfterClickToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) { return; }
            configuration.Data.AutomaticFlyoutHideAfterClick = HideAfterClickToggle.IsChecked == true;
            configuration.Save();
        }

        private void OnlyDefaultToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) { return; }
            configuration.Data.ShowOnlyDefaultSchemas = OnlyDefaultToggle.IsChecked == true;
            configuration.Save();
        }

        #endregion

        #region Shortcut

        static readonly KeyModifier[] modifierChoices =
        {
            KeyModifier.Shift | KeyModifier.Win,
            KeyModifier.Ctrl | KeyModifier.Win,
            KeyModifier.Alt | KeyModifier.Win,
            KeyModifier.Ctrl | KeyModifier.Alt,
            KeyModifier.Ctrl | KeyModifier.Shift,
            KeyModifier.Alt | KeyModifier.Shift,
            KeyModifier.Ctrl | KeyModifier.Alt | KeyModifier.Shift,
        };

        static IEnumerable<Key> keyChoices =>
            Enumerable.Range(0, 26).Select(i => Key.A + i)
            .Concat(Enumerable.Range(0, 10).Select(i => Key.D0 + i))
            .Concat(Enumerable.Range(0, 12).Select(i => Key.F1 + i));

        public static string ModifierLabel(KeyModifier modifiers)
        {
            // Windows' conventional order: Ctrl, Alt, Shift, Win.
            var parts = new List<string>();
            if (modifiers.HasFlag(KeyModifier.Ctrl)) { parts.Add("Ctrl"); }
            if (modifiers.HasFlag(KeyModifier.Alt)) { parts.Add("Alt"); }
            if (modifiers.HasFlag(KeyModifier.Shift)) { parts.Add("Shift"); }
            if (modifiers.HasFlag(KeyModifier.Win)) { parts.Add("Win"); }
            return parts.Count == 0 ? "None" : string.Join(" + ", parts);
        }

        public static string KeyLabel(Key key) => key >= Key.D0 && key <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString();

        private void loadShortcutChoices()
        {
            var data = configuration.Data;

            var modifiers = modifierChoices.ToList();
            if (!modifiers.Contains(data.ShowOnShortcutKeyModifier)) { modifiers.Insert(0, data.ShowOnShortcutKeyModifier); } // e.g. hand-edited settings
            var modifierOptions = modifiers.Select(m => new Option<KeyModifier>(m, ModifierLabel(m))).ToList();
            ModifierBox.ItemsSource = modifierOptions;
            ModifierBox.SelectedItem = modifierOptions.First(o => o.Value == data.ShowOnShortcutKeyModifier);

            var keys = keyChoices.ToList();
            if (!keys.Contains(data.ShowOnShortcutKey)) { keys.Insert(0, data.ShowOnShortcutKey); }
            var keyOptions = keys.Select(k => new Option<Key>(k, KeyLabel(k))).ToList();
            KeyBox.ItemsSource = keyOptions;
            KeyBox.SelectedItem = keyOptions.First(o => o.Value == data.ShowOnShortcutKey);
        }

        private void ShortcutToggle_Changed(object sender, RoutedEventArgs e)
        {
            updateEnabledStates();
            if (loading) { return; }

            configuration.Data.ShowOnShortcutSwitch = ShortcutToggle.IsChecked == true; // App re-registers on this change.
            configuration.Save();
            updateShortcutError();
        }

        private void ShortcutBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading) { return; }
            if (!(ModifierBox.SelectedItem is Option<KeyModifier> modifier) || !(KeyBox.SelectedItem is Option<Key> key)) { return; }

            configuration.Data.ShowOnShortcutKeyModifier = modifier.Value;
            configuration.Data.ShowOnShortcutKey = key.Value;
            configuration.Save();

            app?.ApplyHotkeyFromConfiguration();
            updateShortcutError();
        }

        private void updateShortcutError()
        {
            var failed = configuration.Data.ShowOnShortcutSwitch && app != null && app.HotKeyFailed;
            ShortcutError.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
            if (failed)
            {
                var shortcut = $"{ModifierLabel(configuration.Data.ShowOnShortcutKeyModifier)} + {KeyLabel(configuration.Data.ShowOnShortcutKey)}";
                ShortcutError.Text = $"{shortcut} is already used by Windows or another app. Pick a different shortcut.";
            }
        }

        #endregion

        private void updateEnabledStates()
        {
            AutoPlansCard.IsEnabled = AutoSwitchToggle.IsChecked == true;
            ShortcutPicker.IsEnabled = ShortcutToggle.IsChecked == true;
        }

        private void OpenStartupSettings_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
