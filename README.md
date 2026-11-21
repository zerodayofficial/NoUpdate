# NoUpdate — Play now, update later.

**NoUpdate** is a PlayStation 5 payload designed to neutralize the **"update required"** block in a modified console environment, **without relying on a predefined list of games**. It combines an immediate patch of the system check in memory with persistent changes to the metadata of titles identified as blocked.

> **The key distinction:** the in-memory patch works while the payload remains active; metadata changes are saved to storage and can survive a reboot. These are two different mechanisms. Neither one automatically makes a game compatible with firmware that lacks the features it actually requires.

**Version:** [v1.0.0](https://github.com/zerodayofficial/NoUpdate/releases/tag/v1.0.0) · **Author:** ZER0DAY · **License:** [GPL-3.0](LICENSE)

## The problem NoUpdate addresses

Some games may encounter a system check that requires an update before they can launch. NoUpdate attempts to change the check responsible for this restriction instead of downloading the update or modifying the game's executable.

The payload uses **two complementary approaches**:

| Mechanism | What it does | How long it lasts |
| --- | --- | --- |
| **In-memory bypass** | Locates and modifies recognized update-check locations in the system process, then monitors the patch. | While the process and payload remain active; the patch can be applied again after the next startup. |
| **Persistent metadata changes** | Examines installed titles and, where necessary, updates version fields in their `param.json` files after creating backups. | Written values remain on storage after a reboot, unless the system or another process changes or regenerates them. |

Game discovery is **dynamic**: NoUpdate does not use a hardcoded title-ID allowlist. However, the current build can store **up to 96 game records per run**.

## Compatibility

- **Firmware range targeted by the code:** PS5 **6.xx through 13.60**.
- **Only firmware tested so far:** **10.01**.
- **Required environment:** a console already capable of running compatible payloads, for example through OnionHEN.
- **Intended use:** your own console, in offline and single-player scenarios.

**Supported in code does not mean tested in practice.** Although the source defines a firmware range from 6.xx to 13.60, **the only firmware currently verified through testing is PS5 firmware 10.01**. The other versions in that range have **not yet been confirmed**. Outside the defined range, the payload logs a warning but does not automatically stop every attempted operation. Actual behavior depends on the system version and console environment.

## What happens when NoUpdate starts

1. **Startup and identification.** Writes a welcome message to the kernel log and displays a notification on the PS5.
2. **Firmware check.** Reads the installed firmware version, logs the result, and indicates whether it falls outside the expected range.
3. **Update-check discovery.** Locates the relevant system process, searches for recognized patch sites, and attempts to patch them in memory. If a patch is already present, it recognizes that state and avoids an unnecessary write.
4. **Autostart setup.** If it finds the ELF in one of the expected OnionHEN locations, it attempts to create the corresponding autostart marker. **It does not copy the ELF automatically** and cannot guarantee autostart in environments that do not support this mechanism.
5. **DNS configuration.** Attempts to apply the alternative DNS address to an available network profile and reports the outcome. **In v1.0.0, this attempt runs automatically; there is no payload option to disable it.**
6. **Game library scan.** Reads available metadata, excludes entries it does not recognize as games, and identifies conditions that may trigger the update block.
7. **Persistent changes.** For each flagged title, attempts to back up and modify the relevant metadata files, then reads the written fields back for verification.
8. **Status report.** Produces notifications and log entries describing the number of titles found, detected issues, and operation results.
9. **Patch monitoring.** If at least one in-memory patch site is active, stays running and checks it **every 5 seconds**. If the original behavior returns, it attempts to reapply the patch.

**If the in-memory patch cannot be found or applied**, NoUpdate reports the failure and does not enter its monitoring loop. Other stages reached before that point may already have run, so a single notification should not be treated as proof that every operation succeeded.

## 1. Immediate bypass: in-memory patching

NoUpdate searches the system process for instruction patterns associated with the update check. When it finds a recognized location, it attempts to neutralize the check and verifies the result by reading the process memory again.

The mechanism is designed to act **globally on the recognized check**: it does not need a separate memory patch for every title ID. It also recognizes previously modified locations to avoid rewriting them unnecessarily.

While running, the payload rechecks the discovered patch sites at **5-second intervals**. If it detects that a patched site has been restored, it tries to apply the patch again. If it loses the ability to read the process, it stops monitoring and records the event.

This stage changes **system-process memory**, not the executable files of installed games. However, the RAM modification itself is **not persistent**: after a restart, the system process must be patched again if necessary.

## 2. Persistent bypass: game metadata

The second stage examines titles under `/user/appmeta`. It identifies directories matching the expected title-ID format and reads their `param.json` metadata.

### Which issues are detected?

- **Firmware requirement:** `requiredSystemSoftwareVersion` indicates a firmware version higher than the one installed on the PS5.
- **Content-version mismatch:** `targetContentVersion` and `contentVersion` do not match, except in cases explicitly excluded by the source logic.

The payload also checks category, badge, and certain content identifiers to avoid treating system entries and other non-game content as ordinary games. **If none of the relevant conditions is detected, it does not attempt to rewrite that title's metadata.**

### Which files can be examined?

For an affected title, the code checks **up to three locations**:

- `/user/appmeta/<titleId>/param.json`
- `/user/app/<titleId>/sce_sys/param.json`
- `/user/patch/<titleId>/sce_sys/param.json`

**"Up to three" does not mean all three files always exist or can be modified.** Missing or unreadable copies, or files lacking the expected fields, are skipped. File availability depends on the title and the state of the console.

### Backups and write verification

Before changing an eligible file, NoUpdate attempts to save a copy under:

`/data/NoUpdate/backup/<titleId>/`

Backup filenames indicate the source location: `appmeta.param.json`, `app.param.json`, and `patch.param.json`. **Existing backup files are preserved and are not overwritten.**

If the required backup fails, the payload does **not** modify that particular metadata copy. After writing, it reads back the changed bytes and logs whether verification succeeded. A successful operation at one path does not imply that every other copy was changed successfully.

### What does the payload change?

The code targets specific recognized version fields in the JSON rather than rewriting the entire document:

- If a higher firmware requirement was detected, it attempts to align the firmware-requirement field with the console's current firmware version.
- If the problem is **only** a content-version mismatch, it may write a sentinel value to `contentVersion`. That rewrite is not attempted in the same pass when the firmware-requirement flag is also set.

These changes are **persistent at the file level**, but they are not an absolute guarantee against the system regenerating or updating the metadata later. They also do not provide missing firmware libraries, functions, or other capabilities that a game may genuinely require.

## 3. Alternative DNS: behavior in v1.0.0

NoUpdate includes an alternative primary DNS address:

**`62.210.38.117`**

The code first attempts to identify the active network profile. If that does not produce a usable result, it falls back to the general **Ethernet** and **Wi-Fi** settings.

To reduce unintended changes, it:

- reads the existing value before writing;
- proceeds only when the field is empty or resembles an IPv4 address; **this check is not a complete IPv4 validator**;
- leaves the value unchanged if the configured DNS already matches;
- makes several verification reads after writing;
- reports whether the DNS was updated, was already present, or could not be changed.

**Important:** if another primary DNS address is present in IPv4-like format, NoUpdate may replace it. The released **v1.0.0 build has no interactive opt-out for this stage**. DNS changes can affect domain resolution, privacy, and network connectivity; they may require a **network reconnection or console restart** to take effect. The IP address is embedded in the source code, and this README does not guarantee the availability or operation of that external DNS service.

## 4. Autostart and behavior after reboot

NoUpdate attempts to enable OnionHEN autostart **only when it finds its ELF** at one of these recognized locations:

- `/user/data/OnionHEN/payloads/NoUpdate.elf`
- `/data/OnionHEN/payloads/NoUpdate.elf`

It then checks for the associated `.auto_start` marker and tries to create it if it does not exist. An existing marker is left unchanged.

This feature **does not install OnionHEN**, transfer the payload, or guarantee identical behavior across all OnionHEN versions. Autostart allows the temporary memory patch to be attempted again at a subsequent startup. **Previously written game metadata, by contrast, is a separate form of persistence.**

## 5. Notifications and diagnostics

NoUpdate uses two complementary reporting channels.

**On-screen notifications** summarize immediate results:

- payload startup;
- DNS successfully set, already active, or unchanged due to an error;
- global patch not applied, active with no forced updates detected, or active while the game list is unreadable;
- number of titles matching update-block conditions;
- name and title ID of the **first 8 affected titles**, with additional entries referenced in the kernel log;
- count of games whose persistent metadata changes were reported as successful.

**Kernel log (`klog`)** provides more detailed diagnostics:

- detected firmware, process information, and patch-search results;
- title scanning, required versions, and **FW** / **UPD** flags;
- backup, write, and verification outcomes for individual metadata locations;
- DNS and autostart operation results;
- loss of the memory patch and attempts to restore it.

Notifications are a summary, **not proof that every game will work**. When investigating a partial result or failure, the kernel log provides the more detailed record.

## 6. Safety measures, limitations, and recovery

**Safeguards implemented in the source**

- Backup attempts before eligible metadata writes.
- Existing backups are kept rather than overwritten.
- Read-back verification of modified fields.
- Detection of in-memory patches that are already active.
- Explicit logging of failures rather than treating every attempt as successful.
- No explicit procedure for deleting installed games or their save data.

**Known limitations**

- The payload stores **up to 96 game records** per execution.
- The stated firmware range is not a comprehensive test matrix; **10.01 remains the only tested firmware**.
- Metadata files may be missing, inaccessible, or unexpectedly formatted.
- A successful backup cannot rule out write errors, later system changes, or data loss from other causes.
- There is **no automatic restore feature** in v1.0.0; existing backups may support carefully performed manual recovery.
- DNS configuration is attempted **automatically** and may change existing network settings.
- Bypassing an update prompt does not eliminate a game's actual technical dependencies.

Before using software that changes persistent metadata, keep an independent backup of important data. The presence of automatic backups is not a substitute for a tested recovery procedure.

## What NoUpdate does not do

- It does not download or install game updates.
- It does not modify game executables or provide game content.
- It does not add missing firmware features and **cannot guarantee that every title will launch**.
- It does not supply game licenses or tools for bypassing license validation.
- It does not require a predefined title-ID list.
- It does not include an automatic metadata-restore command in v1.0.0.
- It does not guarantee operation on every firmware within the stated range.

## Summary

**NoUpdate approaches the same problem in two different ways:** it attempts to neutralize the update check in real time and, separately, updates metadata for games identified as blocked while preserving copies of the original files. It also provides notifications and logs, attempts OnionHEN autostart, and automatically applies the DNS address configured in the build.

Results depend on firmware, payload environment, metadata format, and each game's actual requirements.

**Tested only on PS5 firmware 10.01 so far. Firmware range targeted by the code: 6.xx–13.60. Other firmware versions are not yet verified.**

## Project files

- [`src/Program.cs`](src/Program.cs) — C# payload source code.
- [`src/NoUpdate.csproj`](src/NoUpdate.csproj) — project definition.
- [`release/NoUpdate.elf`](release/NoUpdate.elf) — compiled payload included in this release.
- [`release/SHA256SUMS.txt`](release/SHA256SUMS.txt) — SHA-256 checksum for the ELF.
- [`LICENSE`](LICENSE) — GPL-3.0 license.

**Release:** [NoUpdate v1.0.0 and downloadable assets](https://github.com/zerodayofficial/NoUpdate/releases/tag/v1.0.0).

## Disclaimer and credits

Intended for use on **your own console**, in **offline and single-player** scenarios. Distributed **as is**, without any warranty of functionality, compatibility, or freedom from unintended effects. You are responsible for your console configuration and data.

**Made by ZER0DAY** · [Support on Ko-fi](https://ko-fi.com/zzer0dayyy)
