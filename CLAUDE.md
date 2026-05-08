# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Publish

```bash
# Build (debug)
dotnet build SmartScanner/SmartScanner.csproj

# Build (release)
dotnet build SmartScanner/SmartScanner.csproj -c Release

# Publish self-contained win-x86 executable (output → publish/)
dotnet publish SmartScanner/SmartScanner.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -o publish

# Build full installer (requires Inno Setup 6 installed)
build-installer.bat
```

No test project exists in this repository.

## Architecture

**SmartScanner** is a WPF desktop app (net8.0-windows, x86) for scanning documents via TWAIN and emailing them as PDF.

### MVVM structure

- `MainViewModel` is the single ViewModel. The view (`MainWindow.xaml`) binds entirely to it via `DataContext`. `RelayCommand` (defined in `MainViewModel.cs`) is the only ICommand implementation.
- `SettingsWindow` is a dialog that receives the live `MainViewModel` instance and operates on it directly. It uses a **local-staging pattern**: changes to sender/recipient/email-profile collections are buffered in `_localSenders`, `_localRecipients`, `_localProfiles` and only committed to the VM on "Save All" (`BtnSaveAll_Click`). SMTP fields and dark mode are applied immediately.

### Service layer

Five interfaces define the service contracts. All are constructor-injected into `MainViewModel`:

| Interface | Concrete (wired in `MainWindow.xaml.cs`) |
|---|---|
| `ISettingsService` | `DatabaseService` |
| `ISentItemsService` | `DatabaseService` |
| `IScannerService` | `ScannerService` (TWAIN via NTwain) |
| `IPdfService` | `PdfService` (QuestPDF) |
| `IEmailService` | `EmailService` (MailKit) |

`DatabaseService` implements **both** `ISettingsService` and `ISentItemsService` and is passed twice: `new MainViewModel(..., _db, _db)`. `SettingsService.cs` (JSON-based) is an unused alternative implementation — do not wire it up without removing `DatabaseService`'s settings logic.

### Persistence

- **SQLite database**: `%APPDATA%\SmartScanner\smartscanner.db` — stores settings (key/value), sender profiles, recipients, email profiles, and sent-item history.
- **SMTP passwords** are encrypted with Windows DPAPI (`ProtectedData`, user-scoped). The raw password is never stored; decryption only works under the same Windows account.
- **`AppSettings`** is the in-memory model used by `ISettingsService`. `SaveSettings()` / `LoadSettings()` in the VM serialize all state including `IsDarkMode`.

### Theme system

Two resource dictionaries — `Themes/Dark.xaml` and `Themes/Light.xaml` — define ~48 named brush keys prefixed `Th.*`. Theme switching works by clearing and replacing `Application.Current.Resources.MergedDictionaries` at runtime. `LoadSettings()` applies the saved theme on startup; `ToggleTheme()` applies and saves immediately. There is no deferred theme application.

### Key non-obvious constraints

- **x86 platform target** is required for TWAIN scanner support (NTwain / 32-bit TWAIN drivers).
- `IsDarkMode` has a `private set` — only `ToggleTheme()` inside the VM may change it.
- `DatabaseService` is the source of truth for all persisted data; `SettingsService.cs` is dead code.
