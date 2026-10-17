# VR Audio Auto Switcher

Tiny Windows tray app that switches your default headset and mic between a **Desktop** profile and a **VR** profile when SteamVR starts or stops, and keeps Discord in sync.

- .NET 8 / WPF (Fluent UI), runs as a normal user: no UAC prompt, ever
- Starts with Windows via the per-user `HKCU\...\Run` key
- Idle in tray: ~30 MB, ~0% CPU (SteamVR check every 3 s)
- Discord modes:
  - **Native**: follows the Windows default device (set Discord to "Default"); optional Discord restart
  - **Discord RPC**: unmodded, explicit device pick via a free Discord developer app (client ID/secret)
  - **BetterDiscord**: plugin sets Discord devices directly (client mod, against Discord ToS)

## Build
```
cd src/VrAudioSwitcher
dotnet build -c Release
```

Status: early version; Discord modes are not yet verified against live clients.
