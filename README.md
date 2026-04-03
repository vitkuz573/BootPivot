# BootPivot

BootPivot is a Windows-focused CLI for staging and switching the next boot entry to a selected WIM image.

## What It Does

- Validates host readiness for boot entry operations.
- Creates isolated staging sessions for WIM boot workflows.
- Builds and previews BCD command plans before applying changes.
- Applies boot sequence changes for the next restart.
- Cleans up stale or specific staging sessions.

## Project Layout

- `src/BootPivot.Core`
  - Domain models, contracts, validation, session orchestration.
- `src/BootPivot.Windows`
  - Windows driver implementation (`bcdedit`, elevation checks, process execution).
- `src/BootPivot.Cli`
  - Command-line host on `System.CommandLine`, DI wiring, exit code mapping.
- `tests/BootPivot.Core.Tests`
  - Unit tests for template rendering and orchestration rules.

## Commands

- `inspect`
  - Checks platform support, elevation state, and required tooling.
- `stage`
  - Creates a session and renders loader script/template with placeholders.
  - Supports `--dry-run` for preview-only output.
- `pivot`
  - Previews or applies BCD changes for a staged session.
  - Use `--apply` to execute and optional `--reboot` for immediate restart.
- `cleanup`
  - Removes a specific session or sessions older than a threshold.

## Build And Test

```bash
dotnet restore BootPivot.slnx
dotnet build BootPivot.slnx -c Release
dotnet test BootPivot.slnx -c Release
```

## Safety Defaults

- `pivot` runs in preview mode unless `--apply` is explicitly provided.
- Loader template keeps `<some_var>` placeholder unless a command override is supplied.
