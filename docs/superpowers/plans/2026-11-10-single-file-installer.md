# Single-File Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One `AvasRoutingTool-Setup-<version>.exe` that a user downloads, runs once, and ends up with the app installed (all dependencies bundled) and a desktop shortcut.

**Architecture:** A PowerShell build script publishes the app self-contained, adds the Fixed Version WebView2 runtime from a local git-ignored cache, resets `appsettings.json` to the shipped defaults, and hands the staging folder to an Inno Setup 6 script. The installer is per-user (no admin prompt) so the app's existing writes next to its exe keep working. A second script installs, launches, upgrades and uninstalls it silently to prove it.

**Tech Stack:** Inno Setup 6, PowerShell 5.1, .NET 8 `dotnet publish` (win-x64, self-contained), existing xUnit suite (unchanged).

**Spec:** Grill answers Q1 to Q9, 2026-11-10 (decisions below; no separate spec file).

## Decisions (from the grill, 2026-11-10)

| # | Decision |
|---|---|
| Q1 | Inno Setup 6 builds the installer. |
| Q2 | Per-user install to `{autopf}\AVAS Routing Tool` with `PrivilegesRequired=lowest` (resolves to `%LOCALAPPDATA%\Programs`). No admin rights. |
| Q3 | Start Menu entry and an Apps & Features uninstaller. Desktop shortcut task ticked by default (user can untick). "Launch" checkbox ticked on the finish page. |
| Q4 | Upgrade keeps the user's `appsettings.json`. Uninstall removes the program but keeps settings unless the user answers Yes to a delete prompt. |
| Q5 | Unsigned for now. Optional signing step in the build script, off unless `$env:AVAS_SIGN_CMD` is set. The Quick Start documents the SmartScreen "More info > Run anyway" step. |
| Q6 | WebView2 runtime comes from a git-ignored local cache `installer/vendor/WebView2Runtime/`. The build script stops with instructions if it is missing and never downloads it. |
| Q7 | Silent install is not documented (skipped by the user). The test script still uses silent mode internally. |
| Q8 | No GitHub Release. The installer goes to a shared folder by hand. The plan only builds and tests. The portable zip is not part of this plan. |
| Q9 | Inno Setup is installed on this PC with `winget install JRSoftware.InnoSetup`, announced to the user first. |

## Global Constraints

- App version comes from `<Version>` in `src/AvasRoutingTool/AvasRoutingTool.csproj` (currently `1.1.0`). Nothing else holds a copy.
- Release defaults must ship: `BavmUrl` `http://localhost:80`, `RestPort` `8080`, `ShowLogs` `false`, `ShowAdvanced` `false` (the file at `src/AvasRoutingTool/appsettings.json`). The window opens maximized (already in code).
- Publish is `win-x64`, self-contained, not single-file, not trimmed (the LibVLC and WebView2 natives must stay as files).
- The staging folder must not contain `WebView2_UserData`, `app.log`, or any file the app generates at run time.
- Installer output file name: `AvasRoutingTool-Setup-<version>.exe`, in `installer/Output/` (git-ignored).
- `installer/vendor/WebView2Runtime/` and `installer/Output/` are git-ignored. No binaries are committed.
- No test installs under `Program Files`; the test script installs to a temp directory.

## Review Focus

1. **Settings overwrite on upgrade:** reinstalling over an existing install must not replace a user-edited `appsettings.json` (the lab file with a room camera is the real case).
2. **App running during install or uninstall:** a running `AvasRoutingTool.exe` or its `msedgewebview2.exe` children lock files; the installer must close it rather than fail halfway.
3. **Generated files at uninstall:** `WebView2_UserData` and `app.log` are not installer-tracked, so uninstall would leave the folder behind unless explicitly deleted.
4. **Missing runtime cache:** a build without `installer/vendor/WebView2Runtime/msedgewebview2.exe` must fail loudly, not produce an installer whose BAVM tab errors.
5. **Install path with spaces / a different user:** shortcuts and the launch step must work from `...\AVAS Routing Tool\` and for a non-admin account.

---

## File Structure

| File | Responsibility |
|---|---|
| `scripts/build-installer.ps1` (create) | Version read, publish, runtime copy, staging cleanup, defaults reset, ISCC call, optional signing. |
| `installer/AvasRoutingTool.iss` (create) | Inno Setup definition: files, tasks, shortcuts, uninstall behaviour, close-running-app. |
| `scripts/test-installer.ps1` (create) | End-to-end check: install, shortcut, launch, upgrade keeps settings, uninstall. |
| `installer/vendor/README.md` (create) | Where the WebView2 runtime comes from and how to refresh it. |
| `.gitignore` (modify) | Ignore the runtime cache and installer output. |
| `docs/QUICKSTART.md`, `README.md` (modify) | Installer-first install steps, SmartScreen note. |

---

### Task 1: Staging and build script

**Files:**
- Create: `scripts/build-installer.ps1`, `installer/vendor/README.md`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `scripts/build-installer.ps1 [-SkipPublish] [-Staging <dir>]` which writes the staging folder `publish/installer-staging/` (default) and, when `installer/AvasRoutingTool.iss` exists, runs ISCC with `/DAppVersion=<csproj version> /DSourceDir=<staging abs path>` and prints the output path. Exit code 0 on success, non-zero with a one-line reason on any failure.
- Produces: `Get-AppVersion` (function inside the script, returns the `<Version>` string).

- [ ] **Step 1: Write the failing check.** Run `powershell -File scripts/build-installer.ps1` with `installer/vendor/WebView2Runtime` absent. Expected now: the script file does not exist.
- [ ] **Step 2: Add `.gitignore` lines** `installer/vendor/WebView2Runtime/` and `installer/Output/`. Write `installer/vendor/README.md`: runtime is Microsoft Edge WebView2 Fixed Version 157.0.4341.46 x64, from `https://developer.microsoft.com/en-us/microsoft-edge/webview2/` (Fixed Version, after accepting Microsoft's licence), unpacked so `msedgewebview2.exe` sits directly in `installer/vendor/WebView2Runtime/`.
- [ ] **Step 3: Implement `scripts/build-installer.ps1`** with these ordered stages, each failing fast with `throw` and a clear message: (1) read version via `Get-AppVersion` from the csproj; (2) fail if `installer/vendor/WebView2Runtime/msedgewebview2.exe` is missing (message: "see installer/vendor/README.md"); (3) unless `-SkipPublish`, delete the staging dir and run `dotnet publish src/AvasRoutingTool/AvasRoutingTool.csproj -c Release -r win-x64 --self-contained true -o <staging>`; (4) copy the runtime folder to `<staging>/WebView2Runtime`; (5) overwrite `<staging>/appsettings.json` with `src/AvasRoutingTool/appsettings.json`; (6) delete `<staging>/WebView2_UserData` and `<staging>/app.log` if present; (7) assert staging contains `AvasRoutingTool.exe`, `libvlc/win-x64/libvlc.dll`, `WebView2Runtime/msedgewebview2.exe`; (8) if the `.iss` exists, locate `ISCC.exe` (PATH, then `%ProgramFiles(x86)%\Inno Setup 6`, `%LOCALAPPDATA%\Programs\Inno Setup 6`) and run it, then sign the output with `$env:AVAS_SIGN_CMD` (format string with `{0}` = file path) only if that variable is set.
- [ ] **Step 4: Verify the guard.** With the runtime cache temporarily absent: `powershell -File scripts/build-installer.ps1` must exit non-zero and print the README hint. Then restore the cache (copy from `publish/AvasRoutingTool-v1.1/WebView2Runtime`) and run with the `.iss` still absent: it must stop after stage 7 with the staging folder passing the assertions and no `WebView2_UserData` or `app.log` inside.
- [ ] **Step 5: Commit** `git add scripts/build-installer.ps1 installer/vendor/README.md .gitignore` with message `build: add installer staging script`.

---

### Task 2: Installer definition with end-to-end test

**Files:**
- Create: `installer/AvasRoutingTool.iss`, `scripts/test-installer.ps1`

**Interfaces:**
- Consumes: Task 1's `build-installer.ps1` and its `/DAppVersion`, `/DSourceDir` defines.
- Produces: `installer/Output/AvasRoutingTool-Setup-<AppVersion>.exe`. Silent switches used by the test: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="<dir>" /TASKS="desktopicon" /LOG="<file>"` for install, and `unins000.exe /VERYSILENT /SUPPRESSMSGBOXES` for uninstall (the settings-delete prompt defaults to No when silent).
- Produces: `scripts/test-installer.ps1 [-Installer <path>]`, exit 0 only if every check passes; prints one `PASS`/`FAIL` line per check.

- [ ] **Step 1: Write `scripts/test-installer.ps1` first.** It uses a temp install directory and the real user Desktop. Checks, in order, each with a named result: `installer-exists`; `install-ok` (exit code 0); `exe-present` (`AvasRoutingTool.exe` in the dir); `libvlc-present` (`libvlc\win-x64\libvlc.dll`); `webview2-present` (`WebView2Runtime\msedgewebview2.exe`); `defaults-shipped` (installed `appsettings.json` has `"BavmUrl": "http://localhost:80"` and `"RestPort": 8080`); `desktop-shortcut` (`AVAS Routing Tool.lnk` on the Desktop whose `TargetPath` is the installed exe); `launches` (start the exe, alive after 10 s, then stop it and its `msedgewebview2` children); `upgrade-keeps-settings` (write `"BavmUrl": "http://TEST-KEEP:80"` into the installed `appsettings.json`, run the installer again silently, assert the value is still there); `uninstall-ok` (exit 0, install dir gone except the kept `appsettings.json` is allowed, desktop shortcut gone, Start Menu folder gone). The script always attempts uninstall and shortcut cleanup in a `finally` block.
- [ ] **Step 2: Run it to see it fail.** `powershell -File scripts/test-installer.ps1` Expected: `FAIL installer-exists`.
- [ ] **Step 3: Install Inno Setup.** Tell the user, then run `winget install --id JRSoftware.InnoSetup -e`. Verify `ISCC.exe` is found as Task 1 stage 8 looks for it.
- [ ] **Step 4: Implement `installer/AvasRoutingTool.iss`.** Required settings: `AppId` a fixed GUID (generated once, never changed), `AppName=AVAS Routing Tool`, `AppVersion={#AppVersion}`, `AppPublisher=Advantech`, `DefaultDirName={autopf}\AVAS Routing Tool`, `PrivilegesRequired=lowest`, `OutputBaseFilename=AvasRoutingTool-Setup-{#AppVersion}`, `OutputDir=Output`, `SetupIconFile=..\src\AvasRoutingTool\Assets\AppIcon.ico`, `UninstallDisplayIcon={app}\AvasRoutingTool.exe`, `Compression=lzma2/ultra64`, `SolidCompression=yes`, `ArchitecturesAllowed=x64compatible`, `ArchitecturesInstallIn64BitMode=x64compatible`, `CloseApplications=yes`, `RestartApplications=no`, `DisableProgramGroupPage=yes`. `[Files]`: all of `{#SourceDir}\*` with `recursesubdirs createallsubdirs` excluding `appsettings.json`, plus `appsettings.json` with `onlyifdoesntexist uninsneveruninstall`. `[Tasks]`: `desktopicon` (not unchecked, so ticked by default). `[Icons]`: Start Menu `{autoprograms}\AVAS Routing Tool` and `{autodesktop}\AVAS Routing Tool` (task `desktopicon`), both targeting `{app}\AvasRoutingTool.exe`. `[Run]`: `postinstall nowait skipifsilent` launch entry. `[UninstallDelete]`: `filesandordirs` for `{app}\WebView2_UserData` and `{app}\app.log`. `[Code]`: `CurUninstallStepChanged(usPostUninstall)` asks "Also delete your saved settings?" and deletes `{app}\appsettings.json` only on Yes; before install, `InitializeSetup`/`PrepareToInstall` ends running `AvasRoutingTool.exe` via the Restart Manager (`CloseApplications`) so locked files do not abort the copy.
- [ ] **Step 5: Build.** `powershell -File scripts/build-installer.ps1` Expected: ends by printing `installer/Output/AvasRoutingTool-Setup-1.1.0.exe`, roughly 350 to 450 MB.
- [ ] **Step 6: Run the test.** `powershell -File scripts/test-installer.ps1` Expected: every check prints `PASS`, exit code 0. If `launches` fails, check that no earlier instance still holds the settings or WebView2 folder.
- [ ] **Step 7: Review Focus checks.** (a) Start `AvasRoutingTool.exe` from the installed dir, run the installer again silently, expect exit 0 and the app closed (covers focus 2). (b) Install to a temp path containing a space, such as `<temp>\AVAS Test Dir`, and confirm `desktop-shortcut` and `launches` still pass (focus 5). (c) After uninstall confirm `WebView2_UserData` and `app.log` are gone (focus 3). These can be extra named checks in the same script.
- [ ] **Step 8: Commit** `git add installer/AvasRoutingTool.iss scripts/test-installer.ps1` with message `build: add Inno Setup installer and install test`.

---

### Task 3: Documentation

**Files:**
- Modify: `docs/QUICKSTART.md`, `README.md`

- [ ] **Step 1: Update `docs/QUICKSTART.md` section 2 ("Install and launch").** Lead with the installer: download `AvasRoutingTool-Setup-1.1.0.exe`, run it, no admin needed, keep "Create a desktop shortcut" ticked, finish. Add the SmartScreen note: "If Windows says it protected your PC, choose More info, then Run anyway." Add where settings live and that uninstall asks whether to delete them. Remove the "copy the folder" steps and the size wording that no longer applies; keep the 1 GB disk-space row.
- [ ] **Step 2: Update `README.md` "Standalone Release Packaging".** Add the build commands: `winget install JRSoftware.InnoSetup`, place the runtime per `installer/vendor/README.md`, run `powershell -File scripts/build-installer.ps1`, then `powershell -File scripts/test-installer.ps1`.
- [ ] **Step 3: Verify.** Every file name, path and command in both documents exists (`Get-ChildItem` each path). Expected: no missing path.
- [ ] **Step 4: Commit** `git add docs/QUICKSTART.md README.md` with message `docs: installer-first install steps`.

---

## Self-Review

- **Spec coverage:** Q1 to Task 2 step 4; Q2 `PrivilegesRequired=lowest` and `{autopf}`; Q3 `[Tasks]`/`[Icons]`/`[Run]`; Q4 `onlyifdoesntexist` plus the uninstall prompt; Q5 `AVAS_SIGN_CMD` stage and the SmartScreen docs; Q6 Task 1 stage 2; Q7 left out of the docs on purpose; Q8 no release task; Q9 Task 2 step 3.
- **Type and name consistency:** the define names `AppVersion` and `SourceDir`, the output file name pattern, and the staging default `publish/installer-staging/` are the same in Tasks 1 and 2.
- **Proportion:** three tasks. The `.iss` is specified by settings, not pasted, because the settings fully determine it.
- **Open risk, not a decision:** the Fixed Version runtime is 678 MB unpacked, so the installer stays large (the earlier zip was 403 MB, expect about the same). A shared folder handles that.
