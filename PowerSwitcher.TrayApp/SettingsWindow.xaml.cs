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

            public PlanOption(IPowerSchema schema, PowerSwitcherSettings settings)
            {
                Guid = schema.Guid;
                Name = schema.Name;
                Dot = TrayIconFactory.BadgeBrushFor(schema.Guid, schema.Name, settings);
            }
        }

        /// <summary>One entry in a plan's color drop-down.</summary>
        public sealed class ColorOption
        {
            public string Name { get; }
            public Brush Dot { get; }
            /// <summary>"#RRGGBB"; null for the "Choose a color…" entry.</summary>
            public string Hex { get; }
            /// <summary>The "Choose a color…" entry, which opens the color dialog instead of being a color itself.</summary>
            public bool OpensPicker => Hex == null;

            public ColorOption(string name, System.Drawing.Color? color)
            {
                Name = name;
                Dot = color == null ? null : TrayIconFactory.ToBrush(color.Value);
                Hex = color == null ? null : TrayIconFactory.ToHex(color.Value);
            }
        }

        /// <summary>A plan and the colors it can have; the row's drop-down binds to this.</summary>
        public sealed class PlanColorRow
        {
            public Guid Guid { get; }
            public string Name { get; }
            public System.Drawing.Color CurrentColor { get; }
            /// <summary>The plan's own color; choosing it clears the saved choice rather than storing a copy.</summary>
            public string DefaultHex { get; }
            public List<ColorOption> Options { get; }
            public ColorOption Selected { get; }

            public PlanColorRow(IPowerSchema schema, PowerSwitcherSettings settings)
            {
                Guid = schema.Guid;
                Name = schema.Name;
                DefaultHex = TrayIconFactory.ToHex(TrayIconFactory.DefaultBadgeColorFor(schema.Guid, schema.Name));
                CurrentColor = TrayIconFactory.BadgeColorFor(schema.Guid, schema.Name, settings).Value;

                // Every default is a palette color, so the drop-down always shows the color by name.
                Options = TrayIconFactory.Palette.Select(p => new ColorOption(p.Name, p.Color)).ToList();

                var currentHex = TrayIconFactory.ToHex(CurrentColor);
                Selected = Options.FirstOrDefault(o => string.Equals(o.Hex, currentHex, StringComparison.OrdinalIgnoreCase));
                if (Selected == null)
                {
                    Selected = new ColorOption($"Custom ({currentHex})", CurrentColor);
                    Options.Add(Selected);
                }

                Options.Add(new ColorOption("Choose a color…", null));
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
            StartupToggle.IsChecked = RunAtStartupService.IsEnabled;

            pwrManager.UpdateSchemas();
            loadPlans();
            loadPlanColors();
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

            var options = pwrManager.Schemas.Select(sch => new PlanOption(sch, configuration.Data)).ToList();
            OnAcPlanBox.ItemsSource = options;
            OffAcPlanBox.ItemsSource = options;
            OnAcPlanBox.SelectedItem = options.FirstOrDefault(o => o.Guid == configuration.Data.AutomaticPlanGuidOnAC);
            OffAcPlanBox.SelectedItem = options.FirstOrDefault(o => o.Guid == configuration.Data.AutomaticPlanGuidOffAC);

            loading = wasLoading;
        }

        // Plans added or removed (e.g. in Control Panel) while the window is open.
        private void Schemas_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            loadPlans();
            loadPlanColors();
        }

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

        #region Tray icon

        private void loadPlanColors() =>
            PlanColorList.ItemsSource = pwrManager.Schemas.Select(sch => new PlanColorRow(sch, configuration.Data)).ToList();

        private void PlanColorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var box = (ComboBox)sender;
            if (!(box.DataContext is PlanColorRow row) || !(box.SelectedItem is ColorOption option)) { return; }
            if (option == row.Selected) { return; } // The initial selection being applied, not a change.

            string hex = option.Hex;
            if (option.OpensPicker)
            {
                hex = pickColor(row.CurrentColor);
                if (hex == null) { box.SelectedItem = row.Selected; return; } // Cancelled: put the old choice back.
            }

            if (string.Equals(hex, row.DefaultHex, StringComparison.OrdinalIgnoreCase)) { hex = null; } // Back to the default.
            configuration.Data.SetPlanColor(row.Guid, hex); // TrayApp redraws the icon on this change.
            configuration.Save();

            // Rebuild after this event finishes: the rows (and the auto-switch pickers' dots) now show the new color.
            Dispatcher.BeginInvoke(new Action(() => { loadPlanColors(); loadPlans(); }));
        }

        /// <summary>The Windows color dialog, starting at <paramref name="current"/>. Returns "#RRGGBB", or null if cancelled.</summary>
        private string pickColor(System.Drawing.Color current)
        {
            using (var dialog = new System.Windows.Forms.ColorDialog { Color = current, FullOpen = true, AnyColor = true })
            {
                var owner = new Win32Owner(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                return dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK ? TrayIconFactory.ToHex(dialog.Color) : null;
            }
        }

        private sealed class Win32Owner : System.Windows.Forms.IWin32Window
        {
            public Win32Owner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
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

        #region Startup

        private void StartupToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) { return; }

            var enable = StartupToggle.IsChecked == true;
            try
            {
                RunAtStartupService.Set(enable);
                StartupError.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.IO.IOException)
            {
                StartupError.Text = $"Couldn't {(enable ? "turn on" : "turn off")} starting with Windows: {ex.Message}";
                StartupError.Visibility = Visibility.Visible;

                loading = true;
                StartupToggle.IsChecked = RunAtStartupService.IsEnabled; // Show what's actually in effect.
                loading = false;
            }
        }

        #endregion

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
