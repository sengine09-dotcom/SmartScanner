# SmartScanner

A WPF desktop application for scanning documents via TWAIN and sending them by email as PDF attachments, with a built-in sent-items history and PDF archive.

## Features

- **TWAIN scanning** — detect and scan from any installed TWAIN scanner, with selectable DPI (75–600) and color mode (Color / Grayscale / Black & White)
- **PDF import** — insert pages from existing PDF files into the current scan job
- **Page management** — reorder pages via drag-and-drop, rotate individually, delete pages from the preview
- **Email delivery** — send scanned PDFs via SMTP using saved sender profiles and recipient lists
- **Email profiles** — save preset combinations of sender, recipient, subject, and body for one-click sending
- **PDF archive** — automatically save a copy of every sent PDF to a configured local folder
- **Sent-items history** — browse, filter, and delete past send records (with associated PDF files)
- **Dark mode** — toggle between light and dark themes; preference is persisted across sessions

## Tech Stack

| Layer | Technology |
|---|---|
| UI | WPF / .NET 8 (Windows) |
| Scanner | NTwain 3.7.5 (TWAIN protocol) |
| PDF generation | QuestPDF 2026.2.4 |
| Email | MailKit 4.16.0 |
| Database | SQLite via Microsoft.Data.Sqlite 9.0.0 |
| Password storage | Windows DPAPI (user-scoped encryption) |

## Requirements

- Windows 10 (1809) or later
- .NET 8 Desktop Runtime (x86)
- A TWAIN-compatible scanner driver

## Build

```bash
# Debug build
dotnet build SmartScanner/SmartScanner.csproj

# Release build
dotnet build SmartScanner/SmartScanner.csproj -c Release

# Self-contained publish (win-x86)
dotnet publish SmartScanner/SmartScanner.csproj -c Release -r win-x86 --self-contained true -o publish
```

### Build installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```bat
build-installer.bat
```

Output is placed in `installer_output/`.

## Data storage

All settings and history are stored in:

```
%APPDATA%\SmartScanner\smartscanner.db
```

SMTP passwords are encrypted with Windows DPAPI and are only readable by the Windows account that saved them.
