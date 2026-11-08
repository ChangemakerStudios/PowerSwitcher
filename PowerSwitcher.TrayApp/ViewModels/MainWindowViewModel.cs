using Petrroll.Helpers;
using PowerSwitcher.TrayApp.Configuration;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace PowerSwitcher.TrayApp.ViewModels
{
    public class MainWindowViewModel : ObservableObject
    {
        private IPowerManager pwrManager;
        private ConfigurationInstance<PowerSwitcherSettings> config;

        public INotifyCollectionChanged Schemas { get; private set; }
        public IPowerSchema ActiveSchema
        {
            get { return pwrManager.CurrentSchema; }
            set { if (value != null && !value.IsActive) { pwrManager.SetPowerSchema(value); } }
        }

        public MainWindowViewModel()
        {
            App currApp = System.Windows.Application.Current as App;
            if (currApp == null) { return; }

            this.pwrManager = currApp.PowerManager;
            this.config = currApp.Configuration;

            pwrManager.PropertyChanged += PwrManager_PropertyChanged;
            config.Data.PropertyChanged += SettingsData_PropertyChanged;

            Schemas = pwrManager.Schemas.WhereObservableSwitchable<ObservableCollection<IPowerSchema>, IPowerSchema>
                (
                sch => WellKnownSchemas.IsStandardPlan(sch.Guid, sch.Name) || sch.IsActive,
                config.Data.ShowOnlyDefaultSchemas
                );
        }

        private void SettingsData_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PowerSwitcherSettings.ShowOnlyDefaultSchemas))
            {
                updateOnlyDefaultSchemasSetting();
            }
        }

        private void updateOnlyDefaultSchemasSetting()
        {

            (Schemas as ObservableCollectionWhereSwitchableShim<ObservableCollection<IPowerSchema>, IPowerSchema>).FilterOn = config.Data.ShowOnlyDefaultSchemas;
        }

        private void PwrManager_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IPowerManager.CurrentSchema)) { RaisePropertyChangedEvent(nameof(ActiveSchema)); }
        }

        public void SetGuidAsActive(Guid guid)
        {
            pwrManager.SetPowerSchema(guid);
        }

        public void Refresh()
        {
            pwrManager.UpdateSchemas();
        }
    }
}
