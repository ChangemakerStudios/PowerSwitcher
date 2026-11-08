# Release Notes

## PowerPlanSwitcher v0.5.0

### New Features
- **Installer and automatic updates** - PowerPlanSwitcher now installs with a regular setup program and keeps itself up to date from GitHub releases.
- **The tray icon shows the active plan** - A coloured dot marks Power saver (green), Balanced (blue), High performance (orange) and Ultimate Performance (red); custom plans get their own colour. The tooltip names the plan.
- **Settings window** - All settings in one place, including choosing the flyout's keyboard shortcut.
- **About window** - Version details, the active plan and power source, update status, and credits.

### Improvements
- **Follows the Windows theme** - The flyout, menus, windows and tray icon match light, dark and high-contrast themes, and switch live.
- **Notices plan changes made elsewhere** - Changing the plan in Control Panel or with `powercfg` updates the app straight away.
- **Start with Windows** - Now a switch in Settings.
- **.NET 10** - Runs on the current long-term-support .NET.

### Bug Fixes
- **Keyboard shortcut** - Turning the shortcut off after it failed to register no longer crashes; a taken shortcut is reported in Settings.
