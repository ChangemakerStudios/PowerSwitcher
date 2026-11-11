using Petrroll.Helpers;
using PowerSwitcher.Settings;
using PowerSwitcher.TrayApp.Configuration;
using PowerSwitcher.Wrappers;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PlanOption = PowerSwitcher.TrayApp.SettingsWindow.PlanOption;

namespace PowerSwitcher.TrayApp
{
    /// <summary>Shows the settings that differ between two power plans, and can copy a setting from one to the other.</summary>
    public partial class PlanCompareWindow : ThemedDialogWindow
    {
        /// <summary>The four column backgrounds: each plan's tray colour, see <see cref="PlanTints"/>.</summary>
        public sealed class ColumnTints
        {
            public Brush LeftAc { get; }
            public Brush LeftDc { get; }
            public Brush RightAc { get; }
            public Brush RightDc { get; }

            public ColumnTints(Brush left, Brush right)
            {
                LeftAc = PlanTints.PluggedIn(left);
                LeftDc = PlanTints.OnBattery(left);
                RightAc = PlanTints.PluggedIn(right);
                RightDc = PlanTints.OnBattery(right);
            }
        }

        public sealed class DifferenceGroup
        {
            public string Name { get; }
            public string Glyph { get; }
            public List<DifferenceRow> Rows { get; }

            public DifferenceGroup(PowerSubgroupDifferences group, string leftName, string rightName, ColumnTints tints)
            {
                Name = group.Subgroup.Name;
                Glyph = ViewModels.SubgroupIcons.For(group.Subgroup.Guid);
                Rows = group.Differences.Select(difference => new DifferenceRow(difference, leftName, rightName, tints)).ToList();
            }
        }

        public sealed class DifferenceRow
        {
            public PowerSettingDifference Difference { get; }
            public string Name { get; }
            public bool IsHidden { get; }
            public string AcLeft { get; }
            public string AcRight { get; }
            public string DcLeft { get; }
            public string DcRight { get; }
            public bool AcDiffers { get; }
            public bool DcDiffers { get; }
            public bool CanCopyToRight { get; }
            public bool CanCopyToLeft { get; }

            // Spelled out in full, because an arrow alone doesn't say which plan changes.
            public string CopyToRightHint { get; }
            public string CopyToLeftHint { get; }
            public string CopyToRightName { get; }
            public string CopyToLeftName { get; }

            public ColumnTints Tints { get; }

            public DifferenceRow(PowerSettingDifference difference, string leftName, string rightName, ColumnTints tints)
            {
                var setting = difference.Setting;

                Difference = difference;
                Tints = tints;
                Name = setting.Name;
                IsHidden = setting.IsHidden;
                CopyToRightHint = $"Make {rightName} match {leftName} for this setting";
                CopyToLeftHint = $"Make {leftName} match {rightName} for this setting";
                CopyToRightName = $"Copy {setting.Name} from {leftName} to {rightName}";
                CopyToLeftName = $"Copy {setting.Name} from {rightName} to {leftName}";
                AcLeft = setting.FormatValue(difference.Left.AcValue);
                AcRight = setting.FormatValue(difference.Right.AcValue);
                DcLeft = setting.FormatValue(difference.Left.DcValue);
                DcRight = setting.FormatValue(difference.Right.DcValue);
                AcDiffers = difference.AcDiffers;
                DcDiffers = difference.DcDiffers;
                CanCopyToRight = canCopy(difference, difference.Left);
                CanCopyToLeft = canCopy(difference, difference.Right);
            }

            private static bool canCopy(PowerSettingDifference difference, PowerSettingValue source)
            {
                bool accepted(uint? value) => value != null && difference.Setting.AcceptsValue(value.Value);

                return (!difference.AcDiffers || accepted(source.AcValue)) && (!difference.DcDiffers || accepted(source.DcValue));
            }
        }

        readonly IPowerManager pwrManager;
        readonly ConfigurationInstance<PowerSwitcherSettings> configuration;
        readonly PowerPlanEditor editor;
        readonly Win32PowSettingsWrapper settingsReader = new Win32PowSettingsWrapper();

        List<PowerSubgroup> subgroups = new List<PowerSubgroup>();
        List<PowerSettingDefinition> settings = new List<PowerSettingDefinition>();

        // True while the window itself is setting the drop-downs, so each change doesn't start a comparison.
        bool loading;

        public PlanCompareWindow(IPowerManager powerManager, ConfigurationInstance<PowerSwitcherSettings> config, PowerPlanEditor planEditor)
        {
            pwrManager = powerManager;
            configuration = config;
            editor = planEditor;

            InitializeComponent();

            pwrManager.UpdateSchemas();
            loadPlans();

            try
            {
                subgroups = settingsReader.GetSubgroups();
                settings = subgroups.SelectMany(subgroup => settingsReader.GetSettings(subgroup.Guid)).ToList();
                compare();
            }
            catch (PowerSwitcherWrappersException ex)
            {
                showMessage($"Windows couldn't read the power settings. {ex.Message}");
            }

            pwrManager.Schemas.CollectionChanged += Schemas_CollectionChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            pwrManager.Schemas.CollectionChanged -= Schemas_CollectionChanged;
            base.OnClosed(e);
        }

        /// <summary>Fills the drop-downs, keeping the current choices if those plans still exist.</summary>
        private void loadPlans()
        {
            var left = (LeftPlanBox.SelectedItem as PlanOption)?.Guid ?? pwrManager.CurrentSchema?.Guid;
            var right = (RightPlanBox.SelectedItem as PlanOption)?.Guid;

            var options = pwrManager.Schemas.Select(schema => new PlanOption(schema, configuration.Data)).ToList();

            loading = true;
            LeftPlanBox.ItemsSource = options;
            RightPlanBox.ItemsSource = options;
            LeftPlanBox.SelectedItem = options.FirstOrDefault(option => option.Guid == left) ?? options.FirstOrDefault();

            var leftOption = LeftPlanBox.SelectedItem as PlanOption;
            RightPlanBox.SelectedItem = options.FirstOrDefault(option => option.Guid == right) ?? options.FirstOrDefault(option => option != leftOption);
            loading = false;
        }

        // Plans added or removed (e.g. in Control Panel) while the window is open.
        private void Schemas_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            loadPlans();
            compareSafely();
        }

        private void PlanBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loading) { compareSafely(); }
        }

        private void compareSafely()
        {
            try { compare(); }
            catch (PowerSwitcherWrappersException ex) { showMessage($"Windows couldn't read the power settings. {ex.Message}"); }
        }

        private void compare()
        {
            if (!(LeftPlanBox.SelectedItem is PlanOption left) || !(RightPlanBox.SelectedItem is PlanOption right))
            {
                showMessage("Choose two plans to compare. This PC needs at least two power plans.");
                return;
            }

            if (left.Guid == right.Guid)
            {
                showMessage("Choose two different plans to compare.");
                return;
            }

            var groups = PowerPlanComparer.Compare(subgroups, settings, settingsReader.GetValues(left.Guid, settings), settingsReader.GetValues(right.Guid, settings));
            if (groups.Count == 0)
            {
                showMessage($"{left.Name} and {right.Name} have identical settings.");
                return;
            }

            var differences = groups.SelectMany(group => group.Differences).ToList();
            var hidden = differences.Count(difference => difference.Setting.IsHidden);

            SummaryText.Text = (differences.Count == 1 ? "1 setting differs" : $"{differences.Count} settings differ")
                + (hidden == 0 ? "." : $", {hidden} of them hidden from Control Panel.");

            var tints = new ColumnTints(left.Dot, right.Dot);
            ColumnHeadings.DataContext = tints;
            ColumnHeadings.Visibility = Visibility.Visible;
            DifferenceGroups.ItemsSource = groups.Select(group => new DifferenceGroup(group, left.Name, right.Name, tints)).ToList();
            DifferencesScroller.ScrollToTop();
        }

        private void showMessage(string message)
        {
            SummaryText.Text = message;
            ColumnHeadings.Visibility = Visibility.Collapsed;
            DifferenceGroups.ItemsSource = null;
        }

        private void CopyToRight_Click(object sender, RoutedEventArgs e) => copy(sender, toRight: true);

        private void CopyToLeft_Click(object sender, RoutedEventArgs e) => copy(sender, toRight: false);

        private void copy(object sender, bool toRight)
        {
            if (!(((Button)sender).DataContext is DifferenceRow row)) { return; }

            var difference = row.Difference;
            var source = toRight ? difference.Left : difference.Right;
            var target = toRight ? difference.Right : difference.Left;
            var sourceName = ((toRight ? LeftPlanBox : RightPlanBox).SelectedItem as PlanOption)?.Name;
            var targetName = ((toRight ? RightPlanBox : LeftPlanBox).SelectedItem as PlanOption)?.Name;

            attempt($"{targetName} now matches {sourceName} for {row.Name}.", $"copy {row.Name}", () => editor.SetValues(
                target.SchemeGuid,
                difference.Setting,
                difference.AcDiffers ? source.AcValue : null,
                difference.DcDiffers ? source.DcValue : null));
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            attempt("Undid the last copy.", "undo that", () => editor.Undo());
        }

        /// <summary>Runs an edit, reports how it went, and redraws the list without losing the place in it.</summary>
        private void attempt(string success, string description, Action action)
        {
            try
            {
                action();
                StatusText.Text = success;
            }
            catch (Exception ex) when (ex is PowerSwitcherWrappersException || ex is ArgumentException || ex is InvalidOperationException
                || ex is IOException || ex is UnauthorizedAccessException)
            {
                StatusText.Text = $"Couldn't {description}. {ex.Message}";
            }

            UndoButton.IsEnabled = editor.CanUndo;

            var offset = DifferencesScroller.VerticalOffset;
            compareSafely();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => DifferencesScroller.ScrollToVerticalOffset(offset)));
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
