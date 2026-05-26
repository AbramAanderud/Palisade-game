param(
    [string]$SourceRoot = (Join-Path $env:USERPROFILE ".claude\projects"),
    [string]$OutputRoot = ".\AI Chats\Claude Code",
    [string]$TranscriptPath = "",
    [string]$ProjectFilter = "",
    [int]$MaxEntryChars = 5000,
    [switch]$IncludeToolResults,
    [switch]$IncludeThinking,
    [switch]$Force,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function ConvertTo-SafeFileName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return "unknown"
    }

    $invalid = [System.IO.Path]::GetInvalidFileNameChars()
    $chars = $Name.ToCharArray() | ForEach-Object {
        if ($invalid -contains $_) { "-" } else { $_ }
    }
    $safe = (-join $chars).Trim(" .")
    $safe = $safe -replace "\s+", "-"
    $safe = $safe -replace "-{2,}", "-"
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return "unknown"
    }
    return $safe
}

function ConvertTo-ReadableProjectName {
    param([string]$EncodedName)

    $tokens = $EncodedName -split "-" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $markers = @("Documents", "Desktop", "Downloads", "source", "repos", "dev")
    for ($index = 0; $index -lt $tokens.Count; $index++) {
        if ($markers -contains $tokens[$index] -and $index -lt ($tokens.Count - 1)) {
            return (($tokens[($index + 1)..($tokens.Count - 1)]) -join "-")
        }
    }

    if ($tokens.Count -gt 0) {
        return $tokens[$tokens.Count - 1]
    }

    return $EncodedName
}

function Get-ProjectDirectoryName {
    param(
        [string]$SourceRoot,
        [System.IO.FileInfo]$Transcript
    )

    $current = $Transcript.Directory
    $source = (Resolve-Path -LiteralPath $SourceRoot).Path.TrimEnd("\")
    while ($null -ne $current.Parent -and $current.Parent.FullName.TrimEnd("\") -ne $source) {
        $current = $current.Parent
    }
    return $current.Name
}

function Convert-ContentNodeToMarkdown {
    param($Node)

    if ($null -eq $Node) {
        return ""
    }

    if ($Node -is [string]) {
        return $Node
    }

    if ($Node -is [System.Collections.IEnumerable] -and -not ($Node -is [string])) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($item in $Node) {
            $text = Convert-ContentNodeToMarkdown $item
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                $parts.Add($text)
            }
        }
        return ($parts -join "`n`n")
    }

    $type = ""
    if ($Node.PSObject.Properties.Name -contains "type") {
        $type = [string]$Node.type
    }

    switch ($type) {
        "text" {
            if ($Node.PSObject.Properties.Name -contains "text") {
                return [string]$Node.text
            }
        }
        "thinking" {
            if ($Node.PSObject.Properties.Name -contains "thinking") {
                return "[thinking]`n`n$($Node.thinking)"
            }
        }
        "tool_use" {
            $name = "tool"
            if ($Node.PSObject.Properties.Name -contains "name") {
                $name = [string]$Node.name
            }
            $inputJson = ""
            if ($Node.PSObject.Properties.Name -contains "input") {
                $inputJson = ($Node.input | ConvertTo-Json -Depth 20)
            }
            if ([string]::IsNullOrWhiteSpace($inputJson)) {
                return "[tool use: $name]"
            }
            return "[tool use: $name]`n`n``````json`n$inputJson`n``````"
        }
        "tool_result" {
            if ($Node.PSObject.Properties.Name -contains "content") {
                $result = Convert-ContentNodeToMarkdown $Node.content
                return "[tool result]`n`n$result"
            }
            return "[tool result]"
        }
        default {
            if ($Node.PSObject.Properties.Name -contains "text") {
                return [string]$Node.text
            }
            if ($Node.PSObject.Properties.Name -contains "content") {
                return Convert-ContentNodeToMarkdown $Node.content
            }
            return ($Node | ConvertTo-Json -Depth 20)
        }
    }
}

function Get-EntryRole {
    param($Entry)

    if ($Entry.PSObject.Properties.Name -contains "message") {
        $message = $Entry.message
        if ($null -ne $message -and $message.PSObject.Properties.Name -contains "role") {
            return [string]$message.role
        }
    }

    if ($Entry.PSObject.Properties.Name -contains "type") {
        $type = [string]$Entry.type
        if ($type -eq "user" -or $type -eq "assistant" -or $type -eq "system") {
            return $type
        }
        return $type
    }

    return "entry"
}

function Get-EntryContent {
    param($Entry)

    if ($Entry.PSObject.Properties.Name -contains "message") {
        $message = $Entry.message
        if ($null -ne $message -and $message.PSObject.Properties.Name -contains "content") {
            return Convert-ContentNodeToMarkdown $message.content
        }
    }

    if ($Entry.PSObject.Properties.Name -contains "content") {
        return Convert-ContentNodeToMarkdown $Entry.content
    }

    if ($Entry.PSObject.Properties.Name -contains "summary") {
        return [string]$Entry.summary
    }

    return ($Entry | ConvertTo-Json -Depth 20)
}

function Limit-EntryContent {
    param(
        [string]$Content,
        [int]$MaxChars
    )

    if ($MaxChars -le 0 -or [string]::IsNullOrEmpty($Content) -or $Content.Length -le $MaxChars) {
        return $Content
    }

    return $Content.Substring(0, $MaxChars) + "`n`n[truncated: entry was $($Content.Length) characters; rerun with -MaxEntryChars 0 for full raw export]"
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    $baseUri = [System.Uri]((Resolve-Path $BasePath).Path.TrimEnd("\") + "\")
    $pathUri = [System.Uri]((Resolve-Path $Path).Path)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace("/", "\")
}

if (-not [string]::IsNullOrWhiteSpace($TranscriptPath)) {
    if (-not (Test-Path -LiteralPath $TranscriptPath)) {
        throw "TranscriptPath does not exist: $TranscriptPath"
    }
    $current = (Get-Item -LiteralPath $TranscriptPath).Directory
    while ($null -ne $current -and $current.Name -ne "projects") {
        $current = $current.Parent
    }
    if ($null -ne $current) {
        $SourceRoot = $current.FullName
    } else {
        $SourceRoot = Split-Path -Parent (Split-Path -Parent (Resolve-Path -LiteralPath $TranscriptPath).Path)
    }
}

if (-not (Test-Path -LiteralPath $SourceRoot)) {
    throw "SourceRoot does not exist: $SourceRoot"
}

$resolvedSourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$resolvedOutputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)
$manifestPath = Join-Path $resolvedOutputRoot ".export-manifest.json"

$manifest = @{}
if (Test-Path -LiteralPath $manifestPath) {
    $loaded = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($property in $loaded.PSObject.Properties) {
        $manifest[$property.Name] = $property.Value
    }
}

if ([string]::IsNullOrWhiteSpace($TranscriptPath)) {
    $transcripts = Get-ChildItem -LiteralPath $resolvedSourceRoot -Recurse -File -Filter "*.jsonl"
} else {
    $transcripts = @((Get-Item -LiteralPath $TranscriptPath))
}
if (-not [string]::IsNullOrWhiteSpace($ProjectFilter)) {
    $transcripts = $transcripts | Where-Object {
        $_.FullName -like "*$ProjectFilter*" -or $_.Directory.Name -like "*$ProjectFilter*"
    }
}

$exported = 0
$skipped = 0

foreach ($transcript in $transcripts) {
    $sourceKey = $transcript.FullName
    $signature = "$($transcript.LastWriteTimeUtc.Ticks):$($transcript.Length)"
    $previous = $manifest[$sourceKey]

    if (-not $Force -and $null -ne $previous -and $previous.signature -eq $signature) {
        $skipped++
        continue
    }

    $projectFolder = Get-ProjectDirectoryName -SourceRoot $resolvedSourceRoot -Transcript $transcript
    $projectName = ConvertTo-ReadableProjectName $projectFolder
    $safeProjectName = ConvertTo-SafeFileName $projectName
    $sessionId = [System.IO.Path]::GetFileNameWithoutExtension($transcript.Name)
    $safeSessionId = ConvertTo-SafeFileName $sessionId
    $projectOutputRoot = Join-Path $resolvedOutputRoot $safeProjectName
    $outputPath = Join-Path $projectOutputRoot "$safeSessionId.md"

    $lines = Get-Content -LiteralPath $transcript.FullName
    $entries = New-Object System.Collections.Generic.List[object]
    $parseErrors = New-Object System.Collections.Generic.List[string]

    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $entry = $line | ConvertFrom-Json
            $entries.Add($entry)
        } catch {
            $parseErrors.Add($_.Exception.Message)
        }
    }

    $detectedSessionId = $sessionId
    foreach ($entry in $entries) {
        if ($entry.PSObject.Properties.Name -contains "sessionId" -and -not [string]::IsNullOrWhiteSpace([string]$entry.sessionId)) {
            $detectedSessionId = [string]$entry.sessionId
            break
        }
    }

    $body = New-Object System.Collections.Generic.List[string]
    $body.Add("---")
    $body.Add("source_transcript: `"$($transcript.FullName.Replace("\", "\\"))`"")
    $body.Add("session_id: `"$detectedSessionId`"")
    $body.Add("project: `"$projectName`"")
    $body.Add("source_last_write: `"$($transcript.LastWriteTime.ToString("o"))`"")
    $body.Add("exported_at: `"$((Get-Date).ToString("o"))`"")
    $body.Add("---")
    $body.Add("")
    $body.Add("# Claude Code Chat - $projectName")
    $body.Add("")
    $body.Add("- Source transcript: ``$($transcript.FullName)``")
    $body.Add("- Session: ``$detectedSessionId``")
    $body.Add("- Source last write: $($transcript.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))")
    $body.Add("")

    if ($parseErrors.Count -gt 0) {
        $body.Add("> Parse warnings: $($parseErrors.Count) line(s) could not be decoded as JSON.")
        $body.Add("")
    }

    foreach ($entry in $entries) {
        $role = Get-EntryRole $entry
        if ($role -eq "queue-operation" -or $role -eq "attachment") {
            continue
        }

        $content = Get-EntryContent $entry
        if ([string]::IsNullOrWhiteSpace($content)) {
            continue
        }

        if (-not $IncludeToolResults -and $content.TrimStart().StartsWith("[tool result]")) {
            continue
        }

        if (-not $IncludeToolResults -and $content.TrimStart().StartsWith("[tool use:")) {
            continue
        }

        if (-not $IncludeThinking -and $content.TrimStart().StartsWith("[thinking]")) {
            continue
        }

        $content = Limit-EntryContent -Content $content.Trim() -MaxChars $MaxEntryChars

        $timestamp = ""
        if ($entry.PSObject.Properties.Name -contains "timestamp") {
            $timestamp = " - $($entry.timestamp)"
        }

        $body.Add("## $role$timestamp")
        $body.Add("")
        $body.Add($content)
        $body.Add("")
    }

    if ($DryRun) {
        Write-Host "Would export: $($transcript.FullName) -> $outputPath"
    } else {
        New-Item -ItemType Directory -Force -Path $projectOutputRoot | Out-Null
        $body -join "`n" | Set-Content -LiteralPath $outputPath -Encoding utf8
        $manifest[$sourceKey] = @{
            signature = $signature
            outputPath = $outputPath
            exportedAt = (Get-Date).ToString("o")
        }
    }

    $exported++
}

if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path $resolvedOutputRoot | Out-Null
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

Write-Host "Claude chat export complete. Exported: $exported. Skipped unchanged: $skipped. Output: $resolvedOutputRoot"
