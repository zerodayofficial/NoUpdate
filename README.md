# NoUpdate — Play now, update later.

NoUpdate is a PS5 payload that removes the 'update required' block globally and persistently for every installed game. It does not rely on a hardcoded title list: it reads the console's own metadata and neutralizes the update check, so the same ELF works with any library.

## Features

- Global, persistent removal of the update-required block
- Works with any installed title, no hardcoded list
- Automatic detection of blocked titles
- Automatic backup and read-back verification before any metadata change
- Autostart support through OnionHEN
- Optional Nomadic DNS integration
- Startup banner and notification with the list of unblocked titles

## Firmware support

- Supported firmware: **6.xx to 13.60**
- Tested firmware: **10.01**

## Installation

1. Copy [`release/NoUpdate.elf`](release/NoUpdate.elf) to `/data/OnionHEN/payloads/` on your PS5.
2. Launch `NoUpdate.elf` once from the Toolbox.
3. Reboot after the first run if the DNS change is applied.

## Building from source

Requirements:

- **.NET 10 SDK**
- **SharpProspero toolchain**

Set the `SHARPPROSPERO_ROOT` environment variable to your SharpProspero checkout, then build from the source directory:

```sh
cd src
dotnet build -c Release
```

The build produces `NoUpdate.elf`.

## Project structure

```text
NoUpdate/
├── README.md
├── LICENSE
├── .gitignore
├── src/
│   ├── NoUpdate.csproj
│   └── Program.cs
└── release/
    ├── NoUpdate.elf
    └── SHA256SUMS.txt
```

The `src/` directory contains the complete C# source and build project. The `release/` directory contains the ready-to-use ELF and its SHA-256 checksum.

## Notes and disclaimer

For use on your own console in offline, single-player scenarios only. NoUpdate does not provide game licenses or make unsupported firmware compatible with a game. Provided **as is**, without warranty of any kind. You are responsible for how you use it.

## Credits

Made by **ZER0DAY** — https://ko-fi.com/zzer0dayyy
