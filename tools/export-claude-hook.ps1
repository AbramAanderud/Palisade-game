param(
    [string]$OutputRoot = "C:\Users\Abe\Documents\palisade-godot\AI Chats\Claude Code"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$inputJson = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($inputJson)) {
    exit 0
}

$hookInput = $inputJson | ConvertFrom-Json
if (-not ($hookInput.PSObject.Properties.Name -contains "transcript_path")) {
    exit 0
}

$transcriptPath = [string]$hookInput.transcript_path
if ([string]::IsNullOrWhiteSpace($transcriptPath) -or -not (Test-Path -LiteralPath $transcriptPath)) {
    exit 0
}

$exporter = Join-Path $PSScriptRoot "export-claude-chats.ps1"
& $exporter -TranscriptPath $transcriptPath -OutputRoot $OutputRoot | Out-Null
