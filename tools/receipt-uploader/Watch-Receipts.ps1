<#
.SYNOPSIS
    Watches a folder for fixed-named receipt PDFs and files them into the Costco receipts folder with a
    date stamp (taken from the receipt when possible), so each scan is kept as a uniquely named file.

.DESCRIPTION
    A phone/scanner workflow tends to save every receipt to the same name (e.g. "Gas_Station_Receipt.pdf").
    This tool watches the root OneDrive folder for those fixed names and, when one appears, reads the date
    printed on the receipt (Costco "Date: MM/DD/YY" or Amazon "Order placed ...") and MOVES the file to the
    Costco receipts folder renamed "<base>_<yyyy-MM-dd_HHmm>.pdf". Moving frees the fixed name for the next
    receipt. If no date can be read from the PDF, the file's last-write time is used.

    This is a LOCAL utility only. It is part of the repo under tools/ but is never built or deployed with
    the web app or API (nothing here is under src/). Run it on the machine where OneDrive syncs.

.PARAMETER WatchFolder
    Folder to watch (default: D:\OneDrive).

.PARAMETER DestFolder
    Where matched receipts are filed (default: "D:\OneDrive\Costo Receipts").

.PARAMETER FileNames
    The fixed file names to look for (default: In_Warehouse_Receipt.pdf, Gas_Station_Receipt.pdf).

.PARAMETER IntervalSeconds
    Poll interval in seconds (default: 30). Polling is used rather than FileSystemWatcher because OneDrive
    sync/placeholder behavior makes file-system "Created" events unreliable for cloud-synced files; a short
    poll is simpler and more dependable. See README.md.

.PARAMETER Mode
    Move (default) or Copy. Copy keeps the original (and tracks processed files by content hash so the same
    file isn't filed twice); Move removes the original after filing.

.PARAMETER RunOnce
    Scan a single time and exit (useful for testing).

.EXAMPLE
    pwsh -File .\Watch-Receipts.ps1
    pwsh -File .\Watch-Receipts.ps1 -RunOnce
    pwsh -File .\Watch-Receipts.ps1 -IntervalSeconds 15 -Mode Copy
#>
[CmdletBinding()]
param(
    [string]$WatchFolder = 'D:\OneDrive',
    [string]$DestFolder = 'D:\OneDrive\Costo Receipts',
    [string[]]$FileNames = @('In_Warehouse_Receipt.pdf', 'Gas_Station_Receipt.pdf'),
    [int]$IntervalSeconds = 30,
    [ValidateSet('Move', 'Copy')][string]$Mode = 'Move',
    [switch]$RunOnce
)

$ErrorActionPreference = 'Stop'
$Latin1 = [System.Text.Encoding]::GetEncoding('ISO-8859-1') # 1 byte = 1 char, preserves binary positions

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    Write-Host ("[{0:yyyy-MM-dd HH:mm:ss}] {1,-5} {2}" -f (Get-Date), $Level, $Message)
}

# Best-effort extraction of the text layer of a PDF: raw (for uncompressed text) plus any FlateDecode
# stream contents (inflated), with PDF string literals "(...)" pulled out of the content streams.
function Get-PdfText {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $raw = $Latin1.GetString($bytes)
    $decoded = [System.Text.StringBuilder]::new()

    $idx = 0
    while (($s = $raw.IndexOf('stream', $idx)) -ge 0) {
        if ($s -ge 3 -and $raw.Substring($s - 3, 3) -eq 'end') { $idx = $s + 6; continue } # 'endstream'
        $dataStart = $s + 6
        if ($dataStart -lt $raw.Length -and $raw[$dataStart] -eq "`r") { $dataStart++ }
        if ($dataStart -lt $raw.Length -and $raw[$dataStart] -eq "`n") { $dataStart++ }
        $e = $raw.IndexOf('endstream', $dataStart)
        if ($e -lt 0) { break }
        $chunk = $raw.Substring($dataStart, $e - $dataStart)
        $cbytes = $Latin1.GetBytes($chunk)
        try {
            if ($cbytes.Length -gt 2 -and $cbytes[0] -eq 0x78) { # zlib header -> raw DEFLATE after 2 bytes
                $in = [System.IO.MemoryStream]::new($cbytes, 2, $cbytes.Length - 2)
                $ds = [System.IO.Compression.DeflateStream]::new($in, [System.IO.Compression.CompressionMode]::Decompress)
                $sr = [System.IO.StreamReader]::new($ds, $Latin1)
                [void]$decoded.Append($sr.ReadToEnd())
                [void]$decoded.Append("`n")
                $sr.Dispose()
            }
        } catch { } # skip streams we can't inflate (images, predictors, etc.)
        $idx = $e + 9
    }

    # Pull PDF string literals out of the decoded content streams (text is drawn from "(...)" operands).
    $literals = [System.Text.StringBuilder]::new()
    foreach ($m in [regex]::Matches($decoded.ToString(), '\(((?:\\.|[^\\()])*)\)')) {
        [void]$literals.Append($m.Groups[1].Value); [void]$literals.Append(' ')
    }
    return ($literals.ToString() + ' ' + $raw)
}

# Returns the receipt's date/time as a [datetime], or $null if none could be parsed.
function Get-ReceiptDate {
    param([string]$Path)
    try { $text = Get-PdfText -Path $Path } catch { return $null }

    # Costco: "Date: 10/07/26" then "Time: 15:18". The receipt often kerns the "Time:" label into separate
    # glyphs ("T i me :"), so we don't rely on that label: we take the first H:mm token that appears AFTER
    # the date. Fall back to the first bare M/D/YY token if the "Date:" label itself is kerned.
    $m = [regex]::Match($text, 'Date:\s*(\d{1,2}/\d{1,2}/\d{2,4})')
    if (-not $m.Success) { $m = [regex]::Match($text, '(\d{1,2}/\d{1,2}/\d{2,4})') }
    if ($m.Success) {
        $datePart = $m.Groups[1].Value
        $timePart = '00:00'
        $after = $text.Substring($m.Index + $m.Length)
        $mt = [regex]::Match($after, '(\d{1,2}:\d{2})')
        if ($mt.Success) { $timePart = $mt.Groups[1].Value }
        foreach ($fmt in @('M/d/yy H:mm', 'MM/dd/yy H:mm', 'M/d/yyyy H:mm', 'MM/dd/yyyy H:mm')) {
            [datetime]$dt = [datetime]::MinValue
            if ([datetime]::TryParseExact("$datePart $timePart", $fmt, [cultureinfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$dt)) {
                return $dt
            }
        }
    }

    # Amazon: "Order placed October 12, 2026"
    $m = [regex]::Match($text, 'Order placed\s+([A-Za-z]+\.?\s+\d{1,2},?\s+\d{4})')
    if ($m.Success) {
        [datetime]$dt = [datetime]::MinValue
        if ([datetime]::TryParse(($m.Groups[1].Value -replace '\.', ''), [cultureinfo]::GetCultureInfo('en-US'), [System.Globalization.DateTimeStyles]::None, [ref]$dt)) {
            return $dt
        }
    }
    return $null
}

# Waits until a file has stopped changing and is readable (OneDrive may still be downloading/writing it).
function Wait-FileStable {
    param([string]$Path)
    for ($i = 0; $i -lt 12; $i++) {
        try {
            $len1 = (Get-Item -LiteralPath $Path).Length
            Start-Sleep -Milliseconds 800
            $len2 = (Get-Item -LiteralPath $Path).Length
            if ($len2 -gt 0 -and $len1 -eq $len2) {
                $fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'None') # exclusive open = not locked
                $fs.Close()
                return $true
            }
        } catch { Start-Sleep -Milliseconds 800 }
    }
    return $false
}

function Get-UniquePath {
    param([string]$Folder, [string]$BaseName, [string]$Extension)
    $candidate = Join-Path $Folder "$BaseName$Extension"
    $n = 1
    while (Test-Path -LiteralPath $candidate) {
        $candidate = Join-Path $Folder "${BaseName}_$n$Extension"
        $n++
    }
    return $candidate
}

# Remembers hashes of files already filed in Copy mode, so the same unchanged file isn't copied twice.
$script:ProcessedHashes = @{}

function Invoke-Scan {
    foreach ($name in $FileNames) {
        $src = Join-Path $WatchFolder $name
        if (-not (Test-Path -LiteralPath $src)) { continue }

        if (-not (Wait-FileStable -Path $src)) {
            Write-Log "Found '$name' but it isn't stable yet; will retry next scan." 'WARN'
            continue
        }

        if ($Mode -eq 'Copy') {
            $hash = (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
            if ($script:ProcessedHashes.ContainsKey($hash)) { continue } # already filed this exact file
        }

        $date = Get-ReceiptDate -Path $src
        $source = 'receipt date'
        if (-not $date) { $date = (Get-Item -LiteralPath $src).LastWriteTime; $source = 'file time' }

        $base = [System.IO.Path]::GetFileNameWithoutExtension($name)
        $stamp = $date.ToString('yyyy-MM-dd_HHmm')
        $dest = Get-UniquePath -Folder $DestFolder -BaseName "${base}_$stamp" -Extension '.pdf'

        try {
            if ($Mode -eq 'Move') {
                Move-Item -LiteralPath $src -Destination $dest
            } else {
                Copy-Item -LiteralPath $src -Destination $dest
                $script:ProcessedHashes[(Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash] = $true
            }
            Write-Log "$Mode '$name' -> '$([System.IO.Path]::GetFileName($dest))' (date from $source)."
        } catch {
            Write-Log "Failed to $Mode '$name': $($_.Exception.Message)" 'ERROR'
        }
    }
}

# --- startup validation ---
if (-not (Test-Path -LiteralPath $WatchFolder)) { throw "Watch folder not found: $WatchFolder" }
if (-not (Test-Path -LiteralPath $DestFolder)) {
    Write-Log "Destination folder not found; creating: $DestFolder" 'WARN'
    New-Item -ItemType Directory -Force -Path $DestFolder | Out-Null
}

Write-Log "Watching '$WatchFolder' for: $($FileNames -join ', ')"
Write-Log "Filing into '$DestFolder' (mode: $Mode)."

if ($RunOnce) {
    Invoke-Scan
    Write-Log 'RunOnce complete.'
    return
}

Write-Log "Scanning every $IntervalSeconds seconds. Press Ctrl+C to stop."
while ($true) {
    try { Invoke-Scan } catch { Write-Log "Scan error: $($_.Exception.Message)" 'ERROR' }
    Start-Sleep -Seconds $IntervalSeconds
}
