using Microsoft.Win32;
using Petrroll.Helpers;
using PowerSwitcher.TrayApp.Configuration;
using PowerSwitcher.TrayApp.Services;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PowerSwitcher.TrayApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public HotKeyService HotKeyManager { get; private set; }
        public bool HotKeyFailed { get; private set; }

        public IPowerManager PowerManager { get; private set; }
        public TrayApp TrayApp { get; private set; }
        public ConfigurationInstance<PowerSwitcherSettings> Configuration { get; private set; }

        private Mutex _mMutex;
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (!tryToCreateMutex()) return;

            var configurationManager = new ConfigurationManagerXML<PowerSwitcherSettings>(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "Petrroll", "PowerSwitcher", "PowerSwitcherSettings.xml"
                ));

            Configuration = new ConfigurationInstance<PowerSwitcherSettings>(configurationManager);
            migrateSettings();

            HotKeyManager = new HotKeyService();
            HotKeyFailed = false;

            PowerManager = new PowerManager();
            MainWindow = new MainWindow();
            TrayApp = new TrayApp(PowerManager, Configuration); //Has to be last because it hooks to MainWindow

            Configuration.Data.PropertyChanged += Configuration_PropertyChanged;
            ApplyHotkeyFromConfiguration();

            TrayApp.CreateAltMenu();

            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Queued so that WPF has seen the same notification and refreshed its SystemParameters and SystemColors.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                (MainWindow as MainWindow).RefreshThemeIfVisible();
                TrayApp.UpdateTheme();
            }));
        }

        private void migrateSettings()
        {
            //Migration of shortcut because Creators update uses WinShift + S for screenshots
            if(Configuration.Data.ShowOnShortcutKey == System.Windows.Input.Key.S &&
                Configuration.Data.ShowOnShortcutKeyModifier == (KeyModifier.Shift | KeyModifier.Win))
            {
                Configuration.Data.ShowOnShortcutKey = System.Windows.Input.Key.L;
                Configuration.Save();
            }
        }

        private void Configuration_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if(e.PropertyName == nameof(PowerSwitcherSettings.ShowOnShortcutSwitch))
            {
                ApplyHotkeyFromConfiguration();
            }
        }

        private HotKey registeredHotKey;

        /// <summary>
        /// Makes the registered flyout shortcut match the configuration: drops the current one, then registers the
        /// configured key if the shortcut is enabled. Returns false (and sets <see cref="HotKeyFailed"/>) when another
        /// app already owns that key combination.
        /// </summary>
        public bool ApplyHotkeyFromConfiguration()
        {
            if (registeredHotKey != null)
            {
                HotKeyManager.Unregister(registeredHotKey);
                registeredHotKey = null;
            }

            HotKeyFailed = false;
            if (!Configuration.Data.ShowOnShortcutSwitch) { return true; }

            var newHotKey = new HotKey(Configuration.Data.ShowOnShortcutKey, Configuration.Data.ShowOnShortcutKeyModifier);
            bool registered;
            try { registered = HotKeyManager.Register(newHotKey); }
            catch (PowerSwitcher.Wrappers.PowerSwitcherWrappersException) { registered = false; } // e.g. a combination Windows reserves
            if (!registered) { HotKeyFailed = true; return false; }

            newHotKey.HotKeyFired += (this.MainWindow as MainWindow).ToggleWindowVisibility;
            registeredHotKey = newHotKey;
            return true;
        }

        private bool tryToCreateMutex()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var mutexName = string.Format(CultureInfo.InvariantCulture, "Local\\{{{0}}}{{{1}}}", assembly.GetType().GUID, assembly.GetName().Name);

            bool mutexCreated;

            _mMutex = new Mutex(true, mutexName, out mutexCreated);
            if (mutexCreated) { return true; }

            _mMutex = null;
            Current.Shutdown();
            return false;
        }

        private void DisposeMutex()
        {
            if (_mMutex == null) return;
            _mMutex.ReleaseMutex();
            _mMutex.Close();
            _mMutex = null;
        }

        private void App_OnExit(object sender, ExitEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            DisposeMutex();
            PowerManager?.Dispose();
            HotKeyManager?.Dispose();
        }

        ~App()
        {
            DisposeMutex();
        }

    }
}
