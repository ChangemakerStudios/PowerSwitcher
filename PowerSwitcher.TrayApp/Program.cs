using PowerSwitcher.TrayApp.Services;
using System;
using Velopack;

namespace PowerSwitcher.TrayApp
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            // Must run first: Velopack's install/update/uninstall hooks run through here (and then exit).
            VelopackApp.Build()
                // Like the old Store package's startup task, a fresh install starts with Windows; Settings can turn it off.
                .OnAfterInstallFastCallback(_ => setRunAtStartup(true))
                .OnBeforeUninstallFastCallback(_ => setRunAtStartup(false))
                .Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        private static void setRunAtStartup(bool enabled)
        {
            try { RunAtStartupService.Set(enabled); }
            catch (Exception) { } // Never block an install or uninstall over this.
        }
    }
}
