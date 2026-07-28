# GitHub cleanup recommendations

The final source ZIP has already excluded the clearly generated or local-only
items below. Remove them from GitHub if they are currently tracked.

## Safe to remove now

- `AdGuardTray/bin/`
  - Compiled output. Recreated by every build.
- `AdGuardTray/obj/`
  - NuGet and compiler intermediate files. Recreated by restore/build.
- `.vs/`
  - Local Visual Studio workspace data.
- `AdGuardTray/AdGuardTray.csproj.user`
  - Per-user Visual Studio settings.
- `AdGuardTray/AdGuardTray.csproj.snippet.txt`
  - Temporary/reference snippet rather than a build input.
- `AdGuardTray/appsettings.json.bak`
  - Old backup configuration. Git already provides version history.
- `AdGuardTray/README_v1.2.txt`
  - Superseded release-specific notes now that the project is v1.3.
- Any generated `*.pdb`, `*.dll`, `*.exe`, `*.deps.json`,
  `*.runtimeconfig.json` and temporary `*_wpftmp.*` files.
  - These belong in release artefacts, not the source branch.

Your existing `.gitignore` already covers most generated build and user files.
Tracked files remain tracked after adding them to `.gitignore`, so remove them
from the Git index once:

```powershell
git rm -r --cached AdGuardTray/bin AdGuardTray/obj .vs
git rm --cached AdGuardTray/AdGuardTray.csproj.user
git rm --cached AdGuardTray/AdGuardTray.csproj.snippet.txt
git rm --cached AdGuardTray/appsettings.json.bak
git rm --cached AdGuardTray/README_v1.2.txt
git commit -m "chore: remove generated and obsolete files"
```

Omit any path from the command when it is not currently tracked.

## Keep

- `AdGuardTray/Views/DiagnosticsWindow.xaml` and `.xaml.cs`
  - Still opened from `MainWindow.xaml.cs`.
- `AdGuardTray/Views/SettingsWindow.xaml` and `.xaml.cs`
  - Used by first-run setup and main-window settings actions.
- `AdGuardTray/Services/RouterService.cs`
  - Still instantiated from `MainWindow.xaml.cs`.
- `AdGuardTray/appsettings.json`
  - Included by the project and copied to build output.
- `AdGuardTray/CHANGELOG.md`
  - Loaded in the Support page and copied to build output.
- `AdGuardTray/LICENSE`
  - Opened from About and copied to build output.
- `AdGuardTray/THIRD_PARTY_NOTICES.txt`
  - Dependency and acknowledgement notice for distribution.

## Review before removing

These files appear isolated in the current source tree, but should be removed
only after a successful Windows build and a quick feature test:

- `AdGuardTray/Services/AdGuardApiClient.cs`
- `AdGuardTray/Models/StatusResponse.cs`
  - These two form an older standalone `/control/stats` test client. Current
    production statistics are handled inside `RouterManager`.
- `AdGuardTray/Models/DomainInsight.cs`
  - No current references were found.

Do not remove `AdGuardProtectionManagement.cs`: it contains several actively
used protection models even though the file name itself is not referenced as a
type.
