using Petrroll.Helpers;
using PowerSwitcher.Settings;
using PowerSwitcher.TrayApp.Configuration;
using PowerSwitcher.TrayApp.ViewModels;
using PowerSwitcher.Wrappers;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlanOption = PowerSwitcher.TrayApp.SettingsWindow.PlanOption;

namespace PowerSwitcher.TrayApp
{
    /// <summary>Edits power plans and their settings. Every change is written straight away, through <see cref="PowerPlanEditor"/>.</summary>
    public partial class PlanEditorWindow : ThemedDialogWindow
    {
        enum PendingAction { None, Rename, Delete, Restore, RestoreDeleted }

        readonly IPowerManager pwrManager;
        readonly ConfigurationInstance<PowerSwitcherSettings> configuration;
        readonly PowerPlanEditor editor;
        readonly Win32PowSettingsWrapper reader = new Win32PowSettingsWrapper();

        readonly List<PowerSubgroup> subgroups;
        readonly List<PowerSettingDefinition> definitions;

        List<SettingRow> rows = new List<SettingRow>();
        PendingAction pending;

        // True while the window itself is filling the plan list, so that doesn't count as choosing a plan.
        bool loading;

        Guid SelectedPlan => (PlanList.SelectedItem as PlanOption)?.Guid ?? Guid.Empty;

        public PlanEditorWindow(IPowerManager powerManager, ConfigurationInstance<PowerSwitcherSettings> config, PowerPlanEditor planEditor)
        {
            pwrManager = powerManager;
            configuration = config;
            editor = planEditor;

            InitializeComponent();

            subgroups = reader.GetSubgroups().OrderBy(subgroup => subgroup.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            definitions = subgroups.SelectMany(subgroup => reader.GetSettings(subgroup.Guid)).ToList();

            editor.PlansChanged += pwrManager.UpdateSchemas;

            pwrManager.UpdateSchemas();
            loadPlans(pwrManager.CurrentSchema?.Guid);
            closeConfirm();

            pwrManager.Schemas.CollectionChanged += Schemas_CollectionChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            pwrManager.Schemas.CollectionChanged -= Schemas_CollectionChanged;
            editor.PlansChanged -= pwrManager.UpdateSchemas;
            base.OnClosed(e);
        }

        #region Plans

        private void loadPlans(Guid? select)
        {
            var options = pwrManager.Schemas.Select(schema => new PlanOption(schema, configuration.Data)).ToList();

            loading = true;
            PlanList.ItemsSource = options;
            PlanList.SelectedItem = options.FirstOrDefault(option => option.Guid == select)
                ?? options.FirstOrDefault(option => option.Guid == pwrManager.CurrentSchema?.Guid)
                ?? options.FirstOrDefault();
            loading = false;

            loadSelectedPlan();
        }

        // Plans added or removed, here or in Control Panel, while the window is open.
        private void Schemas_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            loadPlans(SelectedPlan);
        }

        private void PlanList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading) { return; }

            closeConfirm();
            loadSelectedPlan();
        }

        private void loadSelectedPlan()
        {
            if (!(PlanList.SelectedItem is PlanOption plan))
            {
                rows = new List<SettingRow>();
                PlanHeading.Text = string.Empty;
                PlanNote.Text = string.Empty;
                applyFilter();
                updatePlanActions();
                return;
            }

            if (!attempt("read this plan", () =>
            {
                var values = reader.GetValues(plan.Guid, definitions);
                rows = definitions.Select((definition, index) => new SettingRow(definition, values[index])).ToList();
                ChangedToggleText.Text = $"Changed from {personalityName(plan.Guid)} defaults";
            }))
            {
                rows = new List<SettingRow>();
            }

            PlanHeading.Text = plan.Name;
            PlanNote.Text = plan.Guid == pwrManager.CurrentSchema?.Guid ? "Your active plan, so changes apply straight away." : "Changes apply whenever this plan is active.";

            applyFilter();
            updatePlanActions();
        }

        private string personalityName(Guid plan)
        {
            var personality = reader.GetPersonality(plan);

            if (personality == WellKnownSchemas.PowerSaver) { return "Power saver"; }
            if (personality == WellKnownSchemas.HighPerformance) { return "High performance"; }
            if (personality == WellKnownSchemas.Balanced) { return "Balanced"; }
            return "plan";
        }

        private void updatePlanActions()
        {
            var plan = SelectedPlan;
            var hasPlan = plan != Guid.Empty;

            DuplicateButton.IsEnabled = hasPlan;
            RenameButton.IsEnabled = hasPlan;
            DeleteButton.IsEnabled = hasPlan && PowerPlanEditor.CanDelete(plan, pwrManager.CurrentSchema?.Guid ?? Guid.Empty);
            RestoreButton.IsEnabled = hasPlan && editor.HasBackup(plan);
            RestoreDeletedButton.Visibility = editor.GetDeletedPlanBackups().Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DuplicateButton_Click(object sender, RoutedEventArgs e)
        {
            var source = PlanList.SelectedItem as PlanOption;
            if (source == null) { return; }

            Guid copy = Guid.Empty;
            if (attempt("duplicate the plan", () => copy = editor.DuplicatePlan(source.Guid, $"{source.Name} copy")))
            {
                loadPlans(copy);
                showStatus($"Created {source.Name} copy.");
            }
        }

        private void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(PlanList.SelectedItem is PlanOption plan)) { return; }

            openConfirm(PendingAction.Rename, $"New name for {plan.Name}:", "Rename");
            RenameBox.Text = plan.Name;
            RenameBox.SelectAll();
            RenameBox.Focus();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlanList.SelectedItem is PlanOption plan)
            {
                openConfirm(PendingAction.Delete, $"Delete {plan.Name}? You can bring it back with Restore a deleted plan.", "Delete");
            }
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlanList.SelectedItem is PlanOption plan)
            {
                openConfirm(PendingAction.Restore, $"Put {plan.Name} back as it was before it was first edited here? Changes since then will be lost.", "Restore");
            }
        }

        private void RestoreDeletedButton_Click(object sender, RoutedEventArgs e)
        {
            var deleted = editor.GetDeletedPlanBackups();

            openConfirm(PendingAction.RestoreDeleted, "Which plan do you want back?", "Restore");
            DeletedPlanBox.ItemsSource = deleted;
            DeletedPlanBox.SelectedIndex = 0;
        }

        private void openConfirm(PendingAction action, string message, string confirmLabel)
        {
            pending = action;
            ConfirmText.Text = message;
            ConfirmButton.Content = confirmLabel;
            RenameBox.Visibility = action == PendingAction.Rename ? Visibility.Visible : Visibility.Collapsed;
            DeletedPlanBox.Visibility = action == PendingAction.RestoreDeleted ? Visibility.Visible : Visibility.Collapsed;
            ConfirmPanel.Visibility = Visibility.Visible;
        }

        private void closeConfirm()
        {
            pending = PendingAction.None;
            ConfirmPanel.Visibility = Visibility.Collapsed;
        }

        private void RenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { confirm(); e.Handled = true; }
            else if (e.Key == Key.Escape) { closeConfirm(); e.Handled = true; }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e) => confirm();

        private void CancelButton_Click(object sender, RoutedEventArgs e) => closeConfirm();

        private void confirm()
        {
            var action = pending;
            var plan = PlanList.SelectedItem as PlanOption;
            closeConfirm();

            switch (action)
            {
                case PendingAction.Rename when plan != null:
                    var name = RenameBox.Text;
                    if (attempt("rename the plan", () => editor.RenamePlan(plan.Guid, name)))
                    {
                        loadPlans(plan.Guid);
                        showStatus($"Renamed to {name.Trim()}.");
                    }
                    break;

                case PendingAction.Delete when plan != null:
                    if (attempt("delete the plan", () => editor.DeletePlan(plan.Guid)))
                    {
                        loadPlans(pwrManager.CurrentSchema?.Guid);
                        showStatus($"Deleted {plan.Name}.");
                    }
                    break;

                case PendingAction.Restore when plan != null:
                    restore(plan.Guid, plan.Name);
                    break;

                case PendingAction.RestoreDeleted when DeletedPlanBox.SelectedItem is PowerPlanBackup backup:
                    restore(backup.Plan, backup.Name);
                    break;
            }
        }

        private void restore(Guid plan, string name)
        {
            PowerPlanRestoreResult result = null;
            if (!attempt("restore the plan", () => result = editor.RestoreBackup(plan))) { return; }

            loadPlans(result.Plan);
            showStatus(result.RefusedSettings.Count == 0
                ? $"Restored {name}."
                : $"Restored {name}, except for settings Windows wouldn't change: {string.Join(", ", result.RefusedSettings)}.");
        }

        #endregion

        #region Filters

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (IsInitialized) { applyFilter(); }
        }

        private void applyFilter()
        {
            var search = SearchBox.Text;
            var includeHidden = HiddenToggle.IsChecked == true;
            var onlyChanged = ChangedToggle.IsChecked == true;

            var items = new List<object>();
            var shown = 0;

            foreach (var subgroup in subgroups)
            {
                var matching = rows
                    .Where(row => row.Definition.SubgroupGuid == subgroup.Guid && PowerSettingFilter.Matches(row.Definition, row.Value, search, includeHidden, onlyChanged))
                    .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (matching.Count == 0) { continue; }

                items.Add(new SubgroupHeaderRow(subgroup.Guid, subgroup.Name, matching.Count, isFirst: items.Count == 0));
                items.AddRange(matching);
                shown += matching.Count;
            }

            // Settings the search and filters would show, but that are left out only because Windows hides them.
            var hiddenOut = includeHidden ? 0 : rows.Count(row => row.Definition.IsHidden && PowerSettingFilter.Matches(row.Definition, row.Value, search, true, onlyChanged));

            SettingsList.ItemsSource = items;
            ShownCount.Text = $"Showing {shown} of {rows.Count} settings" + (hiddenOut > 0 ? $" ({hiddenOut} hidden)" : string.Empty);
            EmptyText.Text = hiddenOut > 0 ? "No settings match. Turn on Show hidden settings to see the hidden ones that do." : "No settings match.";
            EmptyText.Visibility = shown == 0 && rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Editing values

        private void ValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            var box = (TextBox)sender;

            if (e.Key == Key.Enter)
            {
                commitText(box);
                box.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && box.DataContext is ValueCell cell)
            {
                // Handled, or the window would take it as Esc to close.
                box.SetCurrentValue(TextBox.TextProperty, cell.Text);
                e.Handled = true;
            }
        }

        private void ValueBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => commitText((TextBox)sender);

        private void commitText(TextBox box)
        {
            if (!(box.DataContext is ValueCell cell)) { return; }

            var text = box.Text.Trim();
            if (text == cell.Text) { return; }

            var definition = cell.Row.Definition;
            if (!uint.TryParse(text, out var value) || !definition.AcceptsValue(value))
            {
                box.SetCurrentValue(TextBox.TextProperty, cell.Text);
                showStatus($"{cell.Row.Name} must be a whole number from {definition.Min} to {definition.Max}.");
                return;
            }

            applyValues(cell.Row, cell.IsAc ? value : null, cell.IsAc ? null : value);
        }

        private void ChoiceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var box = (ComboBox)sender;

            // Only a person using this drop-down can change a plan; filling it in as a row appears must not.
            if (!box.IsDropDownOpen && !box.IsKeyboardFocusWithin) { return; }
            if (!(box.DataContext is ValueCell cell) || !(box.SelectedItem is PowerSettingChoice choice) || choice.Index == cell.Value) { return; }

            applyValues(cell.Row, cell.IsAc ? choice.Index : null, cell.IsAc ? null : choice.Index);
        }

        // A focused, closed drop-down changes value under the mouse wheel: scrolling the list must not edit a plan.
        private void ChoiceBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (((ComboBox)sender).IsDropDownOpen) { return; }

            e.Handled = true;
            SettingsList.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender });
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is SettingRow row)
            {
                applyValues(row, row.Ac.CanReset ? row.Ac.Default : null, row.Dc.CanReset ? row.Dc.Default : null);
            }
        }

        private void applyValues(SettingRow row, uint? acValue, uint? dcValue)
        {
            var plan = row.Value.SchemeGuid;

            if (attempt($"change {row.Name}", () => editor.SetValues(plan, row.Definition, acValue, dcValue)))
            {
                showStatus($"Changed {row.Name}.");
                updatePlanActions();
            }

            refreshRow(row);
            UndoButton.IsEnabled = editor.CanUndo;
        }

        private void refreshRow(SettingRow row)
        {
            attempt("read the setting back", () => row.Update(reader.GetValues(row.Value.SchemeGuid, new[] { row.Definition }).Single()));
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            PowerPlanEdit undone = null;
            if (attempt("undo that", () => undone = editor.Undo()))
            {
                var row = rows.FirstOrDefault(candidate => candidate.Value.SchemeGuid == undone.Plan && candidate.Definition.Guid == undone.Setting.Guid);
                if (row != null) { refreshRow(row); }
                showStatus($"Undid the change to {undone.Setting.Name}.");
            }

            UndoButton.IsEnabled = editor.CanUndo;
        }

        #endregion

        private void showStatus(string message) => StatusText.Text = message;

        /// <summary>Runs an action on the plans, reporting any failure in the status line. True if it worked.</summary>
        private bool attempt(string description, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex) when (ex is PowerSwitcherWrappersException || ex is ArgumentException || ex is InvalidOperationException
                || ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                showStatus($"Couldn't {description}. {ex.Message}");
                return false;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
