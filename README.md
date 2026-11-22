# NoUpdate

**Play now, update later.**

NoUpdate is a PS5 payload that removes the "update required" block globally and persistently for every installed game. It does not rely on a hardcoded title list: it reads the console's own metadata (`/user/appmeta/<TITLEID>/param.json`) at runtime, so the same ELF works with any library.

## Features

- Global in-memory patch of the update check in SceShellCore.
- Pattern scanning, no fixed offsets.
- Firmware range 6.xx to 13.60.
- Persistent metadata rewrite for games that require a newer firmware or have a pending update, with automatic backup and read-back verification.
- Nomadic DNS (`62.210.38.117`) applied to the active profile and to the `eth`/`wifi` entries, with a restart prompt.
- On every launch, reports **every installed game** by title ID and name, with status (`FW x.xx`, `update`, `blocked`, `ok`).
- Manual launch only: it does not create an OnionHEN autostart marker and removes a stale one left by older builds.

## Installation

1. Copy `release/NoUpdate.elf` to `/data/OnionHEN/payloads/` on the PS5.
2. Launch it manually from the OnionHEN Toolbox.
3. Reboot when prompted, so the DNS change takes effect.
4. Launch it again whenever you want to re-apply the patch and see the game list.

There is no autostart entry: the payload runs only when you launch it. The metadata changes are written to disk and survive a reboot even without the payload, and the DNS setting is stored in the console registry.

## What it does

- Patches the update check in SceShellCore in memory (pattern scan, no fixed offsets).
- For each game that needs it, rewrites the metadata and keeps a backup under `/data/NoUpdate/backup/<TITLEID>/`:
  - `/user/appmeta/<TITLEID>/param.json`
  - `/user/app/<TITLEID>/sce_sys/param.json`
  - `/user/patch/<TITLEID>/sce_sys/param.json`
- Treats `contentVersion` starting with `99.` as already blocked.
- Applies the Nomadic DNS to the profile, `eth` and `wifi` registry entries; a restart is required for the resolver to use it.

## Where the game names come from

No title ID or game name is compiled into the ELF. At runtime the payload:

1. Scans `/user/appmeta` for 9-character title IDs (`[A-Z]{4}\d{5}`).
2. Reads `/user/appmeta/<TITLEID>/param.json`.
3. Resolves the name from `localizedParameters` -> `defaultLanguage` -> `titleName`; if missing, it uses the first `titleName` in `localizedParameters`; if that is missing too, it uses the title ID.
4. Reads `contentVersion`, `targetContentVersion`, `requiredSystemSoftwareVersion`, `contentId`, `contentBadgeType` and `applicationCategoryType` to decide whether the entry is a game and whether it must be blocked.

Because the list is built from the console every time, installing or removing a game changes the report automatically. The payload handles up to 96 games; notification popups truncate names to 40 characters, while the full names are written to the klog.

## Building

Requirements:

- .NET 10 SDK
- The SharpProspero payload toolchain (NativeAOT settings and PS5 runtime support)

Set `SHARPPROSPERO_ROOT` to your SharpProspero checkout and run:

```powershell
$env:SHARPPROSPERO_ROOT = "C:\path\to\SharpProspero"
.\build.ps1
```

Or call the toolchain directly:

```powershell
& "$env:SHARPPROSPERO_ROOT\build\build-app.ps1" `
  -ProjectPath ".\src\NoUpdate.csproj" `
  -Payload -Output Folder -OutputFolder ".\out"
```

The resulting ELF is written to the output folder.

## Diagnostics

The payload writes diagnostic lines to the klog with the `sp:cpb:` prefix (`titles`, `title`, `dns`, `persist`, `autostart-off`, ...). They are not written to `/data/OnionHEN/OnionHEN.log`.

## Tested on

- PS5 firmware **10.01**.

The payload accepts firmware from 6.xx to 13.60, but 10.01 is the version it was tested on.

## Notes

- Use it on your own console, offline, for single-player use.
- Before changing a metadata file, the payload creates a backup under `/data/NoUpdate/backup/<titleId>/`.
- The DNS change is written to the active network profile and needs a reboot or a reconnect to be used by the resolver.
- No warranty. Use at your own risk.

## Support my work

If NoUpdate saves you some time, you can support my work on Ko-fi:

**https://ko-fi.com/zzer0dayyy**

## Credits

Made by **ZER0DAY**.
