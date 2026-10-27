# Development guide

## Prerequisites

- .NET SDK 8.0.x (`dotnet --version`). Nothing else is required; the game itself is not needed to build.

## Build and test

```
dotnet restore Slrr.sln
dotnet build Slrr.sln -warnaserror      # warnings are errors (Directory.Build.props)
dotnet test Slrr.sln
```

CI (`.github/workflows/dotnet.yml`) runs exactly this on windows-latest and ubuntu-latest and uploads `.trx` results.

## CLI

The command-line tool lives in `tools/Slrr.Cli`:

```
dotnet run --project tools/Slrr.Cli -- <command> [args]
```

Run it without arguments to print the available commands.

## Corpus tests and the game root

Some tests read the real game content (all shipped `.rpk` archives, cfg and binary files). They find it via
`tests/Slrr.Formats.Tests/GameRoot.cs`:

1. `SLRR_GAME_ROOT` environment variable, if it points to a directory containing `sl.rpk`;
2. otherwise the first ancestor of the test binary containing both `Slrr.sln` and `sl.rpk` (this repository).

If neither is found, corpus tests return early and pass vacuously, so the suite still works on machines without game data.
To force the tests to use a specific install:

```
SLRR_GAME_ROOT=/path/to/game dotnet test Slrr.sln            # bash
$env:SLRR_GAME_ROOT = "D:\Games\SLRR"; dotnet test Slrr.sln  # PowerShell
```

`.github/workflows/corpus.yml` (manual, weekly and on code changes to master) sets `SLRR_GAME_ROOT` to the checkout
and fails if `sl.rpk` is missing, so it cannot pass vacuously.

## Formatting

`.editorconfig` encodes the C# style (4-space indent, file-scoped namespaces, `_camelCase` private fields).
`dotnet format Slrr.sln --verify-no-changes` is not enforced in CI yet: it currently reports whitespace issues in
`src/Slrr.Formats/Scx/ScxFile.cs` and `src/Slrr.Formats/Tufa/TufaFile.cs`. Run `dotnet format` and add it to CI once fixed.

## Repository layout

| Path | Contents |
| --- | --- |
| `Slrr.sln`, `Directory.Build.props` | Solution and shared build settings (net8.0, nullable, warnings as errors) |
| `src/Slrr.Core` | Byte reader/writer, resource ids, game file system, diagnostics |
| `src/Slrr.Formats` | Parsers/writers for game file formats (rpk, cfg, binary formats) |
| `tests/Slrr.Formats.Tests` | xUnit tests, including corpus tests |
| `tools/Slrr.Cli` | Command-line tool |
| `docs/` | `CSHARP_ENGINE_REWRITE_PLAN.md`, `formats/` format notes |
| `src/*.java` | Legacy Java sources |
| everything else | Original game content (never modify) |
