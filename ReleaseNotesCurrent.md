# Release Notes

## PowerPlanSwitcher v0.6.0

### New Features
- **Power plan editor** - Choose "Edit power plans…" in the tray menu, or Edit… in Settings, to change any plan's settings, including the ones Windows hides. Plugged-in and battery values sit side by side in the plan's colour. Search by name or description, show only the settings changed from the defaults (each one shows its default), and reset any of them.
- **Manage plans** - Duplicate, rename and delete plans. Each plan is backed up before its first change, so it can be put back as it was, even after it's been deleted. Undo reverses changes until the window is closed.
- **Compare plans** - "Compare plans…" in the tray menu lists only the settings that differ between two plans, and copies a setting from one plan to the other.

### Improvements
- **Show only the built-in plans** - Now includes copies of Power saver, Balanced and High performance, which some PCs list instead of the originals.

### Known Issues
- Windows won't let two settings be changed without administrator rights: "Require a password on wakeup" and one processor core parking setting. The editor says so if you try.
