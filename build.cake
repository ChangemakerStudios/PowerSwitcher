// PowerSwitcher build: restore, build, then publish + Velopack-pack the tray app for each architecture,
// and on master/develop in GitHub Actions upload the packages to a GitHub release.
// Modelled on Papercut SMTP's build (https://github.com/ChangemakerStudios/Papercut-SMTP). See RELEASING.md.

#tool "dotnet:?package=GitVersion.Tool&version=6.4.0"
#tool "dotnet:?package=vpk&version=1.2.158"

#nullable enable

#load "./build/Velopack.cake"

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var configuration = Argument("configuration", "Release");
var target = Argument("target", "All");
GitVersion versionInfo = GitVersion(new GitVersionSettings { OutputType = GitVersionOutput.Json });

var isRunningInGitHubActions = !string.IsNullOrEmpty(EnvironmentVariable("GITHUB_ACTIONS"));
var branchName = EnvironmentVariable("GITHUB_REF_NAME") ?? versionInfo.BranchName;
var isMasterBranch = StringComparer.OrdinalIgnoreCase.Equals("master", branchName);
var isDevelopBranch = StringComparer.OrdinalIgnoreCase.Equals("develop", branchName);
var githubToken = EnvironmentVariable<string?>("GITHUB_TOKEN", null);
var hasGithubToken = !string.IsNullOrEmpty(githubToken);

// Installed apps update from their own channel: master -> stable, develop -> dev, anything else -> alpha.
var channelPostfix = isMasterBranch ? "-stable" : isDevelopBranch ? "-dev" : "-alpha";

///////////////////////////////////////////////////////////////////////////////
// CONFIGURATION
///////////////////////////////////////////////////////////////////////////////

const string RepositoryUrl = "https://github.com/ChangemakerStudios/PowerSwitcher";

var trayAppProject = "./PowerSwitcher.TrayApp/PowerSwitcher.TrayApp.csproj";
var trayAppDir = Directory("./PowerSwitcher.TrayApp");
var publishRoot = Directory("./publish");
var releasesDirectory = Directory("./releases");

// x64 and Arm64 cover Windows 11; 32-bit Windows isn't targeted.
var runtimes = new[] { "win-x64", "win-arm64" };

var versionMSBuildSettings = new DotNetMSBuildSettings()
    .WithProperty("Version", versionInfo.FullSemVer)
    .WithProperty("AssemblyVersion", versionInfo.AssemblySemVer)
    .WithProperty("FileVersion", versionInfo.AssemblySemFileVer)
    .WithProperty("InformationalVersion", versionInfo.InformationalVersion);

// The version properties plus no .pdb files in the package. A fresh object, since WithProperty mutates.
DotNetMSBuildSettings publishMSBuildSettings()
{
    var settings = new DotNetMSBuildSettings();
    foreach (var property in versionMSBuildSettings.Properties) { settings.Properties[property.Key] = property.Value.ToList(); }
    return settings.WithProperty("DebugType", "none").WithProperty("DebugSymbols", "false");
}

Setup(ctx =>
{
    Information($"Building branch '{branchName}', version {versionInfo.FullSemVer}, channel suffix '{channelPostfix}'");
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectory(publishRoot);
    CleanDirectory(releasesDirectory);
});

Task("Restore")
    .IsDependentOn("Clean")
    .Does(() =>
{
    DotNetRestore("./PowerSwitcher.sln");
});

Task("Build")
    .IsDependentOn("Restore")
    .Does(() =>
{
    DotNetBuild("./PowerSwitcher.sln", new DotNetBuildSettings
    {
        Configuration = configuration,
        NoRestore = true,
        MSBuildSettings = versionMSBuildSettings
    });
});

foreach (var rid in runtimes)
{
    var runtime = rid;
    var arch = runtime.Substring("win-".Length); // x64 / arm64

    Task($"Package-{runtime}")
        .IsDependentOn("Build")
        .Does(() =>
    {
        var publishDirectory = publishRoot + Directory(runtime);

        // Framework-dependent: the installer brings in the .NET Desktop Runtime if it's missing
        // (see Framework below), which keeps the package and its delta updates small.
        DotNetPublish(trayAppProject, new DotNetPublishSettings
        {
            Configuration = configuration,
            Runtime = runtime,
            SelfContained = false,
            OutputDirectory = publishDirectory,
            MSBuildSettings = publishMSBuildSettings()
        });

        Velopack.Pack(Context, new VpkPackParams
        {
            Id = "PowerPlanSwitcher",
            Title = "PowerPlanSwitcher",
            Icon = trayAppDir + File("AppIcon.ico"),
            ReleaseNotes = "ReleaseNotesCurrent.md",
            Channel = runtime + channelPostfix,
            Version = versionInfo.FullSemVer,
            PublishDirectory = publishDirectory,
            ReleaseDirectory = releasesDirectory,
            ExeName = "PowerSwitcher.TrayApp.exe",
            Framework = $"net10.0-{arch}-desktop"
        });
    });
}

Task("Package")
    .IsDependentOn("Package-win-x64")
    .IsDependentOn("Package-win-arm64");

Task("DeployReleases")
    .WithCriteria(isRunningInGitHubActions && (isMasterBranch || isDevelopBranch) && hasGithubToken)
    .IsDependentOn("Package")
    .Does(() =>
{
    // vpk creates the GitHub release as a draft; it's published by hand (RELEASING.md).
    foreach (var runtime in runtimes)
    {
        Information($"Uploading PowerPlanSwitcher {runtime} {(isMasterBranch ? "release" : "pre-release")} {versionInfo.FullSemVer}");

        Velopack.UploadGithub(Context, new VpkUploadParams
        {
            Channel = runtime + channelPostfix,
            ReleaseDirectory = releasesDirectory,
            Token = githubToken ?? "",
            Repository = RepositoryUrl,
            IsPrelease = !isMasterBranch
        });
    }
});

Task("All")
    .IsDependentOn("Package")
    .IsDependentOn("DeployReleases");

RunTarget(target);
