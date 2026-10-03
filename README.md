# TradeCopilot

TradeCopilot is a .NET 8 solution for trade automation, consisting of a Windows Worker Service, a WPF desktop UI, a shared core library, and an xUnit test project.

## Requirements

- .NET SDK 8.0 (targets `net8.0-windows10.0.19041.0` for UI, Core, Service, and Tests — Windows 10 2004 / build 19041 or later)

## Folder structure

```
TradeCopilot/
├── TradeCopilot.sln              # Solution file referencing all four projects
├── README.md
├── .gitignore                    # Standard .NET gitignore (from `dotnet new gitignore`)
│
├── src/
│   ├── TradeCopilot.Service/     # Worker Service (Microsoft.Extensions.Hosting.WindowsServices)
│   │                             #   Runs headless in the background, installable as a Windows service.
│   ├── TradeCopilot.UI/          # WPF desktop application (net8.0-windows10.0.19041.0)
│   │                             #   User interface for controlling and monitoring TradeCopilot.
│   ├── TradeCopilot.Core/        # Shared class library (net8.0-windows10.0.19041.0)
│   │                             #   Domain models, business logic, and shared services
│   │                             #   referenced by Service, UI, and Tests.
│   └── TradeCopilot.Tests/       # xUnit test project
│                                 #   Unit tests for TradeCopilot.Core.
│
├── models/                       # (gitignored) ML model files — large binaries kept out of git
├── data/                         # (gitignored) datasets, logs, and runtime data
└── tools/                        # (gitignored) auxiliary scripts and third-party utilities
```

## Project references

```
TradeCopilot.Service ──► TradeCopilot.Core
TradeCopilot.UI      ──► TradeCopilot.Core
TradeCopilot.Tests   ──► TradeCopilot.Core
```

`TradeCopilot.Core` contains the shared code; it has no dependencies on the other projects.

## Building

```powershell
dotnet build            # builds the entire solution
```

## Running tests

```powershell
dotnet test
```

## Notes

- `models/`, `data/`, and `tools/` are intentionally empty and gitignored: they hold large or machine-local artifacts (ML models, datasets, helper tools) that should not be committed. The folder structure is preserved via the `.gitignore` rules; Git itself does not track empty directories, so add a `.gitkeep` inside them if you need the folders to exist in a fresh clone.
- The Service registers itself with the name `TradeCopilot` when hosted as a Windows service (`AddWindowsService`), and can also run as a normal console app for development.
