# BootPivot

BootPivot is a standalone Windows CLI for staging and switching the next boot entry to a selected WIM image.

## Capabilities

- Validates host readiness (`bcdedit`, `dism`, `boot.sdi`, elevation).
- Reads WIM indexes and metadata through DISM.
- Creates isolated staging sessions with manifest + loader script.
- Builds exact BCD command plans before execution.
- Applies one-shot boot sequence changes (`/bootsequence`) for next restart.
- Cleans stale or specific staged sessions.

## Project Layout

- `src/BootPivot.Core`
  - Domain models, validation, orchestration service, templates.
- `src/BootPivot.Windows`
  - Windows driver implementation and process execution.
- `src/BootPivot.Cli`
  - `System.CommandLine` host, command handlers, DI wiring.
- `tests/BootPivot.Core.Tests`
  - Core orchestration/template unit tests.
- `tests/BootPivot.Windows.Tests`
  - Windows parsing/infrastructure unit tests.

## Commands

- `inspect`
  - Prints environment diagnostics and recommended defaults.
- `image-info --image <path>`
  - Reads available WIM indexes and image metadata.
- `stage --image <path> [--index N] [--label <text>] [--session <id>] [--work-dir <path>] [--loader-command <cmd>] [--system-partition C:] [--boot-sdi \\boot\\boot.sdi] [--winload \\Windows\\System32\\winload.efi] [--dry-run]`
  - Creates a stage session and BCD command plan.
- `pivot --session <id> [--work-dir <path>] [--apply] [--reboot]`
  - Previews or executes BCD changes for the staged session.
- `cleanup [--session <id>] [--work-dir <path>] [--older-than-days N] [--dry-run]`
  - Removes selected staged sessions.

## Build And Test

```bash
dotnet restore BootPivot.slnx
dotnet build BootPivot.slnx -c Release
dotnet test BootPivot.slnx -c Release
```

## Safety Defaults

- `pivot` is preview-only unless `--apply` is explicitly set.
- `stage` and `cleanup` support `--dry-run` previews.
