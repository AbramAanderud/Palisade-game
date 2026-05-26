# BUILD.md

# Palisade Build and Run Notes

## Requirements

- Godot 4.x with C#/.NET support
- .NET SDK
- Windows development environment
- VS Code or Godot editor

## Project Root

All commands should be run from the project root.

Do not `cd` inside scripts or subfolders when using `/godogen`.

Project root:

```txt
C:\Users\Abe\Documents\palisade-godot
```

## Claude Chat Export to Obsidian

This repo is also an Obsidian vault. Claude Code transcript archives can be exported locally with no API calls or LLM token cost:

```powershell
.\tools\export-claude-chats.ps1
```

If PowerShell script execution is disabled on the machine, use the per-command bypass form:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\export-claude-chats.ps1
```

By default the script reads transcript JSONL files from:

```txt
C:\Users\Abe\.claude\projects
```

and writes Markdown files to:

```txt
.\AI Chats\Claude Code
```

Useful options:

```powershell
.\tools\export-claude-chats.ps1 -DryRun
.\tools\export-claude-chats.ps1 -ProjectFilter palisade
.\tools\export-claude-chats.ps1 -Force
.\tools\export-claude-chats.ps1 -OutputRoot "D:\Obsidian\AI Chats\Claude Code"
```

The exporter keeps `.export-manifest.json` in the output folder and skips unchanged transcripts unless `-Force` is passed.

By default the exporter is Obsidian-friendly: it skips Claude thinking blocks, tool-use JSON, and tool results, and truncates very large individual messages. This prevents the vault from freezing on huge raw transcript dumps. For a full raw archive, export outside an Obsidian vault and pass:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\export-claude-chats.ps1 -IncludeThinking -IncludeToolResults -MaxEntryChars 0 -OutputRoot "C:\Users\Abe\Documents\Claude Raw Archive"
```

For Claude Code hook automation, use `tools/export-claude-hook.ps1`. It reads Claude hook JSON from stdin, extracts `transcript_path`, and exports only that session:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\export-claude-hook.ps1
```

This machine has a global Claude `SessionEnd` hook installed in:

```txt
C:\Users\Abe\.claude\settings.json
```

The hook runs the global exporter script from:

```txt
C:\Users\Abe\.claude\tools\export-claude-hook.ps1
```

It writes all project exports into the main personal Obsidian vault:

```txt
C:\Users\Abe\Documents\NoBrainer\AI Chats\Claude Code
```

Palisade can still keep project-local exports in `.\AI Chats\Claude Code` when running the exporter manually with `-OutputRoot`.
