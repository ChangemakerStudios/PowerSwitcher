# PowerPlanSwitcher

[![Latest release](https://img.shields.io/github/v/release/ChangemakerStudios/PowerSwitcher?label=release)](https://github.com/ChangemakerStudios/PowerSwitcher/releases/latest)
[![Build](https://github.com/ChangemakerStudios/PowerSwitcher/actions/workflows/build.yml/badge.svg?branch=develop)](https://github.com/ChangemakerStudios/PowerSwitcher/actions/workflows/build.yml)

Switch Windows power plans from the tray — by hand, or automatically when you plug in or unplug.

<p>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="ReadmeAssets/flyout-dark.png">
    <img src="ReadmeAssets/flyout-light.png" width="360" alt="The flyout listing Power saver, Balanced, High performance and Ultimate Performance, with Balanced selected">
  </picture>
</p>

The tray icon carries a coloured dot for the active plan, so you can tell at a glance what you're running:

<img src="ReadmeAssets/tray-icons.png" width="384" alt="Tray icons for Power saver (green), Balanced (blue), High performance (orange) and Ultimate Performance (red), on dark and light taskbars">

## Features

- **Quick switching** — click the tray icon for a Windows-style flyout of your power plans. It works from the keyboard too, and can be limited to the built-in plans.
- **The active plan at a glance** — a coloured dot on the tray icon, one colour per plan. Pick each plan's colour in Settings.
- **Automatic switching** — choose one plan for when you're plugged in and another for battery.
- **Keyboard shortcut** — open the flyout from anywhere (Shift+Win+L by default, configurable).
- **Fits in** — follows the Windows light, dark and high-contrast themes and your accent colour, switching live.
- **Notices outside changes** — switching plans in Control Panel or with `powercfg` updates the tray straight away.
- **Starts with Windows** and **keeps itself up to date**.

## Install

Download **`PowerPlanSwitcher-win-x64-stable-Setup.exe`** from the [latest release](https://github.com/ChangemakerStudios/PowerSwitcher/releases/latest) and run it — or the `-win-arm64-` one on an Arm PC. It installs for your user account, adds a Start menu shortcut and starts with Windows. If the .NET 10 Desktop Runtime is missing, Setup installs that first (which Windows may ask you to approve).

> **"Windows protected your PC"?** The installer isn't code-signed yet, so SmartScreen may warn about it. Choose **More info → Run anyway**.

Prefer not to install? The `-Portable.zip` runs from any folder.

**Updates:** PowerPlanSwitcher checks GitHub shortly after it starts and every 12 hours, downloads new versions in the background, and offers **Restart to update** in the tray menu and the About window. Otherwise the update is applied the next time it exits.

**Uninstall** from Windows Settings → Apps → Installed apps.

**Coming from the Microsoft Store version?** That's a separate app — uninstall it so the two don't both sit in the tray, then set your preferences again in Settings.

## Settings

Right-click the tray icon → **Settings…**. Changes apply and save immediately (to `%LOCALAPPDATA%\Petrroll\PowerSwitcher\PowerSwitcherSettings.xml`, which survives updates).

<p>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="ReadmeAssets/settings-dark.png">
    <img src="ReadmeAssets/settings-light.png" width="547" alt="The Settings window: automatic switching, flyout options, keyboard shortcut, plan colours and startup">
  </picture>
  &nbsp;
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="ReadmeAssets/about-dark.png">
    <img src="ReadmeAssets/about-light.png" width="427" alt="The About window: version, active plan, power source, update status and credits">
  </picture>
</p>

## Building

Requires the .NET 10 SDK.

```powershell
dotnet build PowerSwitcher.sln     # just compile
.\build.ps1                        # build and package the x64 and Arm64 installers into .\releases
```

See [RELEASING.md](RELEASING.md) for versioning, branches and how releases are published.

## Credits

PowerPlanSwitcher was created by [Petr Houška](https://github.com/petrroll/PowerSwitcher) — if you like it, feel free to [buy him a beer](https://www.paypal.com/cgi-bin/webscr?cmd=_donations&business=houskape%40gmail%2ecom&lc=CZ&item_name=PowerPlanSwitcher&item_number=042&currency_code=USD&bn=PP%2dDonationsBF%3abtn_donate_SM%2egif%3aNonHosted). This fork is maintained by [Changemaker Studios](https://github.com/ChangemakerStudios).

Special thanks to:
- Icon made by [Cursor creative](http://www.flaticon.com/authors/cursor-creative) from [flaticon.com](http://www.flaticon.com/)
- [EarTrumpet](https://github.com/File-New-Project/EarTrumpet) for the flyout, interop stuff, and some other help :)
  - David Golden (@GoldenTao)
  - Rafael Rivera (@WithinRafael)
- [Andy Belsky's power-plan-switcher](https://github.com/andy722/power-plan-switcher) for some interop inspiration
- [Mahdi Safsafi's ImmersiveColors](https://github.com/MahdiSafsafi/ImmersiveColors) for making working with undocumented accent colors easy :)

## License

[MIT](LICENCE)
