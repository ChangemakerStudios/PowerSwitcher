using Petrroll.Helpers;
using PowerSwitcher.Settings;
using PowerSwitcher.TrayApp.Configuration;
using PowerSwitcher.Wrappers;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PlanOption = PowerSwitcher.TrayApp.SettingsWindow.PlanOption;

namespace PowerSwitcher.TrayApp
{
    /// <summary>Shows the settings that differ between two power plans. Read-only.</summary>
    public partial class PlanCompareWindow : ThemedDialogWindow
    {
        public sealed class DifferenceGroup
        {
            public string Name { get; }
            public List<DifferenceRow> Rows { get; }

            public DifferenceGroup(PowerSubgroupDifferences group)
            {
                Name = group.Subgroup.Name;
                Rows = group.Differences.Select(difference => new DifferenceRow(difference)).ToList();
            }
        }

        public sealed class DifferenceRow
        {
            public string Name { get; }
            public string HiddenNote { get; }
            public string AcLeft { get; }
            public string AcRight { get; }
            public string DcLeft { get; }
            public string DcRight { get; }
            public bool AcDiffers { get; }
            public bool DcDiffers { get; }

            public DifferenceRow(PowerSettingDifference difference)
            {
                var setting = difference.Setting;

                Name = setting.Name;
                HiddenNote = setting.IsHidden ? " · hidden" : string.Empty;
                AcLeft = setting.FormatValue(difference.Left.AcValue);
                AcRight = setting.FormatValue(difference.Right.AcValue);
                DcLeft = setting.FormatValue(difference.Left.DcValue);
                DcRight = setting.FormatValue(difference.Right.DcValue);
                AcDiffers = difference.AcDiffers;
                DcDiffers = difference.DcDiffers;
            }
        }

        readonly IPowerManager pwrManager;
        readonly ConfigurationInstance<PowerSwitcherSettings> configuration;
        readonly Win32PowSettingsWrapper settingsReader = new Win32PowSettingsWrapper();

        List<PowerSubgroup> subgroups = new List<PowerSubgroup>();
        List<PowerSettingDefinition> settings = new List<PowerSettingDefinition>();

        // True while the window itself is setting the drop-downs, so each change doesn't start a comparison.
        bool loading;

        public PlanCompareWindow(IPowerManager powerManager, ConfigurationInstance<PowerSwitcherSettings> config)
        {
            pwrManager = powerManager;
            configuration = config;

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

            ColumnHeadings.Visibility = Visibility.Visible;
            DifferenceGroups.ItemsSource = groups.Select(group => new DifferenceGroup(group)).ToList();
            DifferencesScroller.ScrollToTop();
        }

        private void showMessage(string message)
        {
            SummaryText.Text = message;
            ColumnHeadings.Visibility = Visibility.Collapsed;
            DifferenceGroups.ItemsSource = null;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
