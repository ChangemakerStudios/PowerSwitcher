# Plan: Power plan settings editor

Status: proposed · Target: `develop` via `feature/settings-editor`

## Goal

Give PowerPlanSwitcher a real editor for power plan settings: the full tree that the Control Panel "Advanced settings" dialog shows, plus the settings Windows hides. It should be searchable, show plugged-in (AC) and battery (DC) values side by side, show what differs from the default, and be able to diff two plans.

The tray switcher stays as it is. The editor is a separate window opened from the tray menu and from Settings.

## Why

The Windows dialog is a modal tree with no search. It shows one plan at a time, hides many settings, and gives no hint of what has been changed. The main existing alternative, PowerSettingsExplorer (mbk1969, Guru3D forums), fixes some of that, but it is closed source, plain, and only distributed as a forum zip.

## Where it fits in the current code

- `PowerSwitcher/` (class library) holds the Win32 layer. `Wrappers/PowerSchemasWrapper.cs` already calls `PowerEnumerate` for schemes, and `AccessFlags` already declares `ACCESS_SUBGROUP` / `ACCESS_INDIVIDUAL_SETTING` (not used yet).
- `PowerSwitcher.TrayApp/` (WPF) has `ThemedDialogWindow`, `Themes/DialogResources.xaml` (cards, toggles, section headers) and `ThemeService`. The editor should reuse these so it matches Settings and About.
- `ActiveSchemeWatcher` only listens for changes to *which plan is active* (`GUID_ACTIVE_POWERSCHEME`). Renaming, duplicating or deleting a plan doesn't signal it, so the editor has to tell the tray to refresh (see Phase 3).
- `Program.Main` runs the Velopack hooks first. Any new launch mode has to keep that order.

## Phase 1 — Settings layer (library, read-only)

Add `PowerSwitcher/Wrappers/Win32PowSettingsWrapper.cs` and model types in `PowerSwitcher/Settings/`:

- `PowerSubgroup` — Guid, Name, Description, IsHidden.
- `PowerSettingDefinition` — Guid, SubgroupGuid, Name, Description, IsHidden, and Kind: `Range` (Min, Max, Increment, Units), `Index` (a list of choices, each a name and value), or `Unknown` (no range, no choices, or not a DWORD value; shown read-only).
- `PowerSettingValue` — SchemeGuid, SettingGuid, AcValue, DcValue, AcDefault, DcDefault, plus `IsAcModified` / `IsDcModified`.

The powrprof.dll calls to cover:

| Purpose | Calls |
|---|---|
| Walk the tree | `PowerEnumerate` with `ACCESS_SUBGROUP`, then `ACCESS_INDIVIDUAL_SETTING` per subgroup |
| Labels | `PowerReadFriendlyName`, `PowerReadDescription` |
| Value shape | `PowerIsSettingRangeDefined`; if a range: `PowerReadValueMin` / `PowerReadValueMax` / `PowerReadValueIncrement` / `PowerReadValueUnitsSpecifier`; if a list: `PowerReadPossibleFriendlyName` / `PowerReadPossibleValue` |
| Current values | `PowerReadACValueIndex` / `PowerReadDCValueIndex` |
| Defaults | `PowerReadACDefaultIndex` / `PowerReadDCDefaultIndex` |
| Hidden flag | `PowerReadSettingAttributes` (`POWER_ATTRIBUTE_HIDE` = 1) |

Settings that belong to no subgroup sit under `NO_SUBGROUP_GUID` (`fea3413e-7e05-4911-9a71-700331f1c294`). The tree view needs a home for them.

The existing `PowerEnumerate` import takes `IntPtr` for the GUID pointers so `null` can be passed. The new calls need both null and real GUID pointers. Use `LibraryImport` with `Guid*` everywhere in the new wrapper, rather than overloads for every null/non-null combination. This needs `AllowUnsafeBlocks` in `PowerSwitcher.csproj`, which isn't set yet.

**Findings** (checked on the dev machine: 179 settings, 151 hidden, four plans):
- `PowerEnumerate` returns the same 179 settings with a scheme GUID or with null, matching `powercfg /qh`. The wrapper passes null. `NO_SUBGROUP_GUID` isn't listed as a subgroup and has no friendly name, but its settings can be enumerated, so the wrapper adds it and uses powercfg's label.
- `PowerIsSettingRangeDefined` returns TRUE for settings with a range, the opposite of how the Win32 docs read. All 107 TRUE results matched range settings in `powercfg`.
- Attribute value 1 (`POWER_ATTRIBUTE_HIDE`) exactly separates the settings `/q` leaves out. Eighteen visible settings carry value 2, which isn't hidden.
- Four settings (`HETEROINCREASETHRESHOLD`, `HETERODECREASETHRESHOLD` and their `1` variants) have neither a range nor choices, in `powercfg` as well. They're `Unknown` and read-only.
- A plan's personality is the choice its personality setting selects, read as a 16-byte GUID through `PowerReadPossibleValue`. Defaults read correctly for all four plans, three of which are copies.
- Personality defaults work as the baseline. A personality can only be Power saver, High performance or Balanced, so the Ultimate Performance copy (personality Balanced) is compared with Balanced's defaults, but that shows only 6 changed settings out of 179. Keep this baseline, and have the UI name it (for example "Changed from Balanced defaults") so those rows make sense.

Done: `PowerSwitcher.Tests` compares every subgroup, setting, range, choice list and AC/DC value against `powercfg /qh` for every listed plan, and the hidden flag against `powercfg /q`.

## Phase 2 — Plan comparison (read-only)

This comes before any write code. It needs no admin rights, can't change a power setting, and is the feature that sets the editor apart, so it can ship on its own.

- A window based on `ThemedDialogWindow`, which Phase 4 grows into the full editor.
- Pick two plans and show only the settings that differ, AC and DC.
- This is the most useful view for Ultimate Performance vs High Performance, and for checking what a vendor tool (Razer Cortex and similar) changed.

## Phase 3 — Writing

Add to the wrapper:

| Purpose | Calls |
|---|---|
| Set values | `PowerWriteACValueIndex` / `PowerWriteDCValueIndex` |
| Apply to the active plan | `PowerSetActiveScheme(activeGuid)` again after a write |
| Hide / unhide | `PowerWriteSettingAttributes` |
| Plan management | `PowerDuplicateScheme` (output GUID freed with `LocalFree`), `PowerDeleteScheme`, `PowerWriteFriendlyName`, `PowerImportPowerScheme` |
| Export | No API for this; run `powercfg /export` |

**Admin rights — find out, don't assume:**
- Hide/unhide writes under `HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings`, so expect it to need admin.
- Whether value writes need admin may depend on the plan's security descriptor. Test as a standard user on a built-in plan and on a duplicated one, and record what comes back (`ERROR_ACCESS_DENIED` = 5).
- The answer decides Phase 5.

**Backup before the first write.** The first time a plan is edited, run `powercfg /export` to a backups folder under `%LOCALAPPDATA%`, so there's a way back after the editor closes. Hidden settings can cause real trouble, and session undo isn't enough on its own.

**Tell the tray about changes.** `ActiveSchemeWatcher` won't report renames, new plans or deleted plans, so the editor calls `UpdateSchemas` after those. When the editor runs elevated in its own process, it needs a cross-process signal, such as a named event the tray waits on.

Done when: values written through the wrapper show up in `powercfg /q`, writing to the active plan takes effect without switching plans by hand, and a backup exists before the first write to a plan.

## Phase 4 — Editor window (WPF)

`PowerSwitcher.TrayApp/PlanEditorWindow.xaml` + view model, based on `ThemedDialogWindow`:

- **Left:** list of plans with their tray colour dots (same `PlanTemplate` style as Settings), with duplicate / rename / delete / export actions.
- **Right:** settings tree grouped by subgroup. Columns: Setting, Plugged in, On battery. Range settings get a numeric box with units; list settings get a drop-down.
- **Filters:** search box (name and description), "Changed from default", "Show hidden settings".
- **Changed values** shown with an accent marker, with a per-row "Reset to default". Both use the baseline settled in Phase 1.
- **Undo** for the editing session (keep the original values until the window closes). Longer-term recovery comes from the Phase 3 backup.
- **Comparison view** from Phase 2 gains "Copy this value to the other plan" per row.
- Follows light/dark/high-contrast themes like the other windows.

Entry points: a tray menu item "Edit power plans…" and a button in Settings.

## Phase 5 — Admin rights

Depends on the Phase 3 findings. The tray app should keep running without admin. One approach:

- Add an editor launch mode (for example `--editor [--scheme <guid>]`) handled in `Program.Main` after the Velopack hooks.
- Give that mode its own startup path. `App.Application_Startup` exits if the single-instance mutex is already held (`App.xaml.cs:31`) and otherwise sets up the tray, so the editor process has to skip both.
- If an operation needs admin and the process doesn't have it, offer to reopen the editor with admin rights (`runas`), passing the same arguments. Mark admin-only actions in the UI (shield icon) so this isn't a surprise.

If testing shows that value writes don't need admin, only hide/unhide and plan management need this path, and that may be enough to keep everything else in-process.

## Testing

`PowerSwitcher.Tests` (xUnit, `net10.0-windows`) holds read-only integration tests against the PC's real power settings, including the Phase 1 comparison with `powercfg /qh`. They parse powercfg's English labels, so they need English Windows. Run them with `dotnet test PowerSwitcher.Tests`; CI doesn't run them.

Write tests run against a scratch plan made with `PowerDuplicateScheme` and deleted at the end. Never write to the user's active or built-in plans in tests.

## Related issue found on the dev machine

The built-in High Performance (`8c5e7fda…`) and Power Saver (`a1841308-3541…`) plans existed under `HKLM\...\Power\User\PowerSchemes` and could be read with `powercfg /query <guid>`, but neither `powercfg /list` nor `PowerEnumerate` returned them. Duplicating them created copies with new GUIDs. The usual explanation is Modern Standby, but this PC doesn't use it (`powercfg /a` reports S3 available and S0 Low Power Idle unsupported), so the cause is still unknown.

Copies are recognised by English name, for tray colours (`TrayIconFactory`) and for the "Show only the built-in plans" filter (`WellKnownSchemas.IsStandardPlan`). Personality can't replace this (see Phase 1), and name matching won't recognise copies on non-English Windows.

Still to decide: whether the editor should detect "built-in plan exists but isn't listed" and offer to fix it, once the cause is known.

## Out of scope for now

- Settings in the Windows 11 Settings app (power mode slider / overlays). Those are the overlay schemes, not plan settings.
- Group Policy-managed settings, beyond showing them as read-only if Windows reports them that way.