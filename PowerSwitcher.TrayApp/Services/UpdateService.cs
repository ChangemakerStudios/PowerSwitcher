using Petrroll.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace PowerSwitcher.TrayApp.Services
{
    public enum UpdateState
    {
        /// <summary>Not a Velopack install (e.g. a dev build): updates are off.</summary>
        NotInstalled,
        Idle,
        Checking,
        UpToDate,
        /// <summary>An update is downloaded and applies on restart (or when the app exits).</summary>
        Ready,
        Failed
    }

    /// <summary>
    /// Velopack updates from GitHub releases, on the channel this build was packed with (win-x64-stable,
    /// win-x64-dev, …). Checks 30 seconds after starting, every two hours, and after waking up or unlocking.
    /// Updates download in the background; the user restarts from the tray or About, otherwise
    /// they're applied when the app exits. Ported from Papercut SMTP's ShellUpdater.
    /// Members are used from the UI thread; PropertyChanged is raised there.
    /// </summary>
    public sealed class UpdateService : ObservableObject
    {
        public const string RepositoryUrl = "https://github.com/ChangemakerStudios/PowerSwitcher";

        static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
        static readonly TimeSpan CheckInterval = TimeSpan.FromHours(2);

        /// <summary>Waking up, unlocking or opening About checks again if the last check is older than this.</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

        readonly UpdateManager manager;
        DispatcherTimer timer;

        public UpdateService()
        {
            try
            {
                // POWERSWITCHER_UPDATE_SOURCE: a local releases folder, for testing the update flow.
                var localSource = Environment.GetEnvironmentVariable("POWERSWITCHER_UPDATE_SOURCE");

                // prerelease: true lets -dev channels see their builds; a stable channel's releases.<channel>.json
                // only exists on stable releases, so stable installs are unaffected.
                IUpdateSource source = string.IsNullOrWhiteSpace(localSource)
                    ? new GithubSource(RepositoryUrl, null, true)
                    : new SimpleFileSource(new DirectoryInfo(localSource));

                manager = new UpdateManager(source);
            }
            catch (Exception)
            {
                manager = null; // Updates are a nicety; never fail startup over them.
            }

            State = IsInstalled ? UpdateState.Idle : UpdateState.NotInstalled;
        }

        public bool IsInstalled => manager?.IsInstalled == true;

        public UpdateState State { get; private set; }

        /// <summary>Version of the downloaded update, when <see cref="State"/> is Ready.</summary>
        public string PendingVersion => pendingUpdate?.Version?.ToString();

        public event Action UpdateReady;

        VelopackAsset pendingUpdate;

        public void StartChecking()
        {
            if (!IsInstalled || timer != null) { return; }

            timer = new DispatcherTimer { Interval = FirstCheckDelay };
            timer.Tick += async (s, e) =>
            {
                timer.Interval = CheckInterval;
                await CheckAsync();
            };
            timer.Start();

            // A PC left on or asleep overnight should find a new release in the morning, not up to two hours later.
            dispatcher = Dispatcher.CurrentDispatcher;
            Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == Microsoft.Win32.PowerModes.Resume) { checkSoonIfStale(); } };
            Microsoft.Win32.SystemEvents.SessionSwitch += (s, e) => { if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) { checkSoonIfStale(); } };
        }

        Dispatcher dispatcher;
        DateTime lastChecked = DateTime.MinValue;

        // SystemEvents arrive on their own thread. Waking up, the network can take a moment to come back, so wait a little.
        private void checkSoonIfStale() => dispatcher?.BeginInvoke(new Action(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            await CheckIfStaleAsync();
        }));

        /// <summary>Checks only if the last check is older than <see cref="StaleAfter"/>.</summary>
        public Task<UpdateState> CheckIfStaleAsync() =>
            DateTime.UtcNow - lastChecked < StaleAfter ? Task.FromResult(State) : CheckAsync();

        public async Task<UpdateState> CheckAsync()
        {
            if (!IsInstalled) { return UpdateState.NotInstalled; }
            if (State == UpdateState.Ready || State == UpdateState.Checking) { return State; }

            lastChecked = DateTime.UtcNow;
            setState(UpdateState.Checking);
            try
            {
                var update = await manager.CheckForUpdatesAsync();
                if (update == null)
                {
                    setState(UpdateState.UpToDate);
                    return State;
                }

                await manager.DownloadUpdatesAsync(update);

                pendingUpdate = update.TargetFullRelease;
                timer?.Stop(); // Nothing more to do until the user restarts.
                setState(UpdateState.Ready);
                UpdateReady?.Invoke();
            }
            catch (Exception)
            {
                setState(UpdateState.Failed); // Offline, GitHub rate limit, … — the next timer tick retries.
            }

            return State;
        }

        /// <summary>Restarts into the downloaded update. The caller tidies up (tray icon) first.</summary>
        public void ApplyAndRestart()
        {
            if (manager == null || pendingUpdate == null) { return; }
            manager.ApplyUpdatesAndRestart(pendingUpdate);
        }

        /// <summary>On a normal exit, applies a downloaded update after the process ends.</summary>
        public void ApplyOnExit()
        {
            if (manager == null || pendingUpdate == null) { return; }
            try { manager.WaitExitThenApplyUpdates(pendingUpdate, silent: true, restart: false); }
            catch (Exception) { } // The update is still downloaded; it'll be offered again next start.
        }

        private void setState(UpdateState state)
        {
            State = state;
            RaisePropertyChangedEvent(nameof(State));
        }
    }
}
