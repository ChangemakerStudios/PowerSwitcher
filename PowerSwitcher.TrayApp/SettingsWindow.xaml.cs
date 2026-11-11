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

        readonly Action openPlanEditor;

        public SettingsWindow(IPowerManager powerManager, ConfigurationInstance<PowerSwitcherSettings> config, Action openPlanEditor)
        {
            pwrManager = powerManager;
            configuration = config;
            this.openPlanEditor = openPlanEditor;
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
            loadSchedule();
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

        private void EditPlansButton_Click(object sender, RoutedEventArgs e) => openPlanEditor();

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
            loadSchedulePlans();
            loadPlanColors();
            updateScheduleStatus();
        }

        private void AutoSwitchToggle_Changed(object sender, RoutedEventArgs e)
        {
            updateEnabledStates();
            if (loading) { return; }

            configuration.Data.AutomaticOnACSwitch = AutoSwitchToggle.IsChecked == true; // TrayApp applies the plan right away.
            configuration.Save();
            updateScheduleStatus(); // It mentions the battery plan when this is on.
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

        #region Schedule

        private void loadSchedule()
        {
            var wasLoading = loading;
            loading = true;
            var data = configuration.Data;

            ScheduleToggle.IsChecked = data.ScheduleEnabled;

            // Days in the order the user's calendar starts its week, with its own short names.
            var format = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
            DayChips.Children.Clear();
            for (var i = 0; i < 7; i++)
            {
                var day = (DayOfWeek)(((int)format.FirstDayOfWeek + i) % 7);
                var chip = new CheckBox
                {
                    Style = (Style)FindResource("FilterChip"),
                    Content = format.AbbreviatedDayNames[(int)day],
                    Tag = day,
                    IsChecked = data.IsWorkDay(day),
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 6, 0),
                };
                System.Windows.Automation.AutomationProperties.SetName(chip, format.GetDayName(day));
                chip.Checked += DayChip_Changed;
                chip.Unchecked += DayChip_Changed;
                DayChips.Children.Add(chip);
            }

            StartBox.ItemsSource = timeOptions(data.ScheduleStartMinutes);
            StartBox.SelectedItem = ((List<Option<int>>)StartBox.ItemsSource).First(o => o.Value == data.ScheduleStartMinutes);
            EndBox.ItemsSource = timeOptions(data.ScheduleEndMinutes);
            EndBox.SelectedItem = ((List<Option<int>>)EndBox.ItemsSource).First(o => o.Value == data.ScheduleEndMinutes);

            loadSchedulePlans();
            loading = wasLoading;
            updateScheduleStatus();
        }

        private void loadSchedulePlans()
        {
            var wasLoading = loading;
            loading = true;

            var options = pwrManager.Schemas.Select(sch => new PlanOption(sch, configuration.Data)).ToList();
            DuringPlanBox.ItemsSource = options;
            OutsidePlanBox.ItemsSource = options;
            DuringPlanBox.SelectedItem = options.FirstOrDefault(o => o.Guid == configuration.Data.SchedulePlanDuring);
            OutsidePlanBox.SelectedItem = options.FirstOrDefault(o => o.Guid == configuration.Data.SchedulePlanOutside);

            loading = wasLoading;
        }

        /// <summary>Every half hour of the day, in the user's time format, plus the saved time if it's in between.</summary>
        private static List<Option<int>> timeOptions(int saved)
        {
            var minutes = Enumerable.Range(0, 48).Select(i => i * 30).ToList();
            if (saved >= 0 && saved < 24 * 60 && !minutes.Contains(saved)) { minutes.Add(saved); minutes.Sort(); }

            return minutes.Select(m => new Option<int>(m, DateTime.Today.AddMinutes(m).ToString("t"))).ToList();
        }

        private void ScheduleToggle_Changed(object sender, RoutedEventArgs e)
        {
            updateEnabledStates();
            if (loading) { return; }

            var data = configuration.Data;
            var on = ScheduleToggle.IsChecked == true;

            // First time on: start from High performance for work and Balanced for the rest, if this PC has them.
            if (on && data.SchedulePlanDuring == Guid.Empty) { data.SchedulePlanDuring = findPlan(WellKnownSchemas.HighPerformance, "high performance") ?? Guid.Empty; }
            if (on && data.SchedulePlanOutside == Guid.Empty) { data.SchedulePlanOutside = findPlan(WellKnownSchemas.Balanced, "balanced") ?? Guid.Empty; }

            data.ScheduleEnabled = on; // TrayApp applies the plan right away.
            configuration.Save();

            loadSchedulePlans();
            updateScheduleStatus();
        }

        // A built-in plan, or a copy of it with the same name (some PCs only list copies).
        private Guid? findPlan(Guid builtIn, string englishName) =>
            pwrManager.Schemas.FirstOrDefault(sch => sch.Guid == builtIn)?.Guid
            ?? pwrManager.Schemas.FirstOrDefault(sch => string.Equals(sch.Name?.Trim(), englishName, StringComparison.OrdinalIgnoreCase))?.Guid;

        private void DayChip_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) { return; }

            var chip = (CheckBox)sender;
            configuration.Data.SetWorkDay((DayOfWeek)chip.Tag, chip.IsChecked == true);
            configuration.Save();
            updateScheduleStatus();
        }

        private void HoursBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading) { return; }
            if (!(((ComboBox)sender).SelectedItem is Option<int> time)) { return; }

            if (sender == StartBox) { configuration.Data.ScheduleStartMinutes = time.Value; }
            else { configuration.Data.ScheduleEndMinutes = time.Value; }
            configuration.Save();
            updateScheduleStatus();
        }

        private void SchedulePlanBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading) { return; }
            if (!(((ComboBox)sender).SelectedItem is PlanOption option)) { return; }

            if (sender == DuringPlanBox) { configuration.Data.SchedulePlanDuring = option.Guid; }
            else { configuration.Data.SchedulePlanOutside = option.Guid; }
            configuration.Save();
            updateScheduleStatus();
        }

        /// <summary>Says what the schedule is doing right now, in plain words, so the setup can be checked at a glance.</summary>
        private void updateScheduleStatus()
        {
            var data = configuration.Data;
            ScheduleStatus.Text = ScheduleStatusText(data, DateTime.Now, guid => pwrManager.Schemas.FirstOrDefault(sch => sch.Guid == guid)?.Name);
        }

        public static string ScheduleStatusText(PowerSwitcherSettings data, DateTime now, Func<Guid, string> planName)
        {
            var schedule = data.GetWorkSchedule();
            if (schedule.Days.Count == 0) { return "Choose at least one work day."; }
            if (!schedule.HasWorkHours) { return "Choose different start and end times."; }

            var working = schedule.IsWorkTime(now);
            var plan = planName(working ? data.SchedulePlanDuring : data.SchedulePlanOutside);
            var next = schedule.NextChange(now);

            var when = next == null ? "" : next.Value.Date == now.Date
                ? $" until {next.Value:t}"
                : next.Value.Date == now.Date.AddDays(1) ? $" until {next.Value:t} tomorrow" : $" until {next.Value:dddd} at {next.Value:t}";

            var text = plan == null
                ? $"It's {(working ? "work hours" : "outside work hours")} now. Choose a plan for {(working ? "work hours" : "outside them")}."
                : $"It's {(working ? "work hours" : "outside work hours")} now, so {plan} applies{when}.";

            if (data.AutomaticOnACSwitch) { text += " On battery, the battery plan above comes first."; }
            if (data.SchedulePlanDuring != Guid.Empty && data.SchedulePlanDuring == data.SchedulePlanOutside) { text += " Both times use the same plan."; }

            return text;
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
            Dispatcher.BeginInvoke(new Action(() => { loadPlanColors(); loadPlans(); loadSchedulePlans(); }));
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
            ScheduleCard.IsEnabled = ScheduleToggle.IsChecked == true;
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
