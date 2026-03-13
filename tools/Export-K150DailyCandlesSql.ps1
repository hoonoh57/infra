param(
    [Parameter(Mandatory = $true)]
    [string]$ExcelPath,

    [Parameter(Mandatory = $false)]
    [string]$BaseUrl = "http://localhost:8082",

    [Parameter(Mandatory = $false)]
    [datetime]$AsOfDate = (Get-Date),

    [Parameter(Mandatory = $false)]
    [datetime]$StopDate = (Get-Date).AddYears(-3),

    [Parameter(Mandatory = $false)]
    [string]$OutputSqlPath = "",

    [Parameter(Mandatory = $false)]
    [int]$Limit = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Normalize-Code {
    param([object]$Value)
    $digits = ($Value.ToString() -replace "[^0-9]", "")
    if ([string]::IsNullOrWhiteSpace($digits)) { return "" }
    $padded = $digits.PadLeft(6, "0")
    if ($padded.Length -gt 6) {
        return $padded.Substring($padded.Length - 6)
    }
    return $padded
}

function Escape-SqlString {
    param([string]$Value)
    if ($null -eq $Value) { return "NULL" }
    return "'" + ($Value -replace "'", "''") + "'"
}

function Read-Kosdaq150Codes {
    param([string]$Path)

    $excel = $null
    $workbook = $null
    $worksheet = $null
    $codes = New-Object System.Collections.Generic.List[string]

    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = $false
        $excel.DisplayAlerts = $false

        $workbook = $excel.Workbooks.Open($Path)
        $worksheet = $workbook.Worksheets.Item(1)
        $used = $worksheet.UsedRange
        $rowCount = $used.Rows.Count

        for ($row = 2; $row -le $rowCount; $row++) {
            $code = Normalize-Code $worksheet.Cells.Item($row, 1).Text
            if (-not [string]::IsNullOrWhiteSpace($code)) {
                $codes.Add($code)
            }
        }
    }
    finally {
        if ($worksheet -ne $null) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($worksheet) }
        if ($workbook -ne $null) {
            $workbook.Close($false)
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($workbook)
        }
        if ($excel -ne $null) {
            $excel.Quit()
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel)
        }
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
    }

    return $codes | Select-Object -Unique
}

function Get-RowValue {
    param(
        [object]$Row,
        [string[]]$Keys
    )

    foreach ($key in $Keys) {
        if ($Row.PSObject.Properties.Name -contains $key) {
            $v = $Row.$key
            if ($null -ne $v -and $v.ToString().Trim() -ne "") {
                return $v.ToString().Trim()
            }
        }
    }

    return ""
}

function To-IntValue {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $digits = $Value -replace "[^0-9\-]", ""
    if ([string]::IsNullOrWhiteSpace($digits)) { return $null }
    return [int]$digits
}

function To-Int64Value {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $digits = $Value -replace "[^0-9\-]", ""
    if ([string]::IsNullOrWhiteSpace($digits)) { return $null }
    return [int64]$digits
}

if (-not (Test-Path $ExcelPath)) {
    throw "Excel file not found: $ExcelPath"
}

if ([string]::IsNullOrWhiteSpace($OutputSqlPath)) {
    $stamp = $AsOfDate.ToString("yyyyMMdd")
    $OutputSqlPath = Join-Path (Get-Location) "daily_candles_k150_upsert_$stamp.sql"
}

$codes = Read-Kosdaq150Codes -Path $ExcelPath
if ($Limit -gt 0) {
    $codes = $codes | Select-Object -First $Limit
}

$dateText = $AsOfDate.ToString("yyyyMMdd")
$stopText = $StopDate.ToString("yyyyMMdd")
$base = $BaseUrl.TrimEnd("/")

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("USE strategy_research;")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("START TRANSACTION;")
[void]$sb.AppendLine("")

$totalRows = 0

foreach ($code in $codes) {
    $url = "$base/api/market/candles/daily?code=$code&date=$dateText&stopDate=$stopText"
    Write-Host "Fetching $code ..." -ForegroundColor Cyan
    $resp = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 60
    if (-not $resp.Success -or $null -eq $resp.Data) {
        Write-Warning "Failed to fetch $code : $($resp.Message)"
        continue
    }

    foreach ($row in $resp.Data) {
        $candleDate = Get-RowValue -Row $row -Keys @("date", "일자")
        $open = To-IntValue (Get-RowValue -Row $row -Keys @("open", "시가"))
        $high = To-IntValue (Get-RowValue -Row $row -Keys @("high", "고가"))
        $low = To-IntValue (Get-RowValue -Row $row -Keys @("low", "저가"))
        $close = To-IntValue (Get-RowValue -Row $row -Keys @("close", "현재가", "종가"))
        $volume = To-Int64Value (Get-RowValue -Row $row -Keys @("volume", "거래량"))

        if ([string]::IsNullOrWhiteSpace($candleDate) -or $null -eq $open -or $null -eq $high -or $null -eq $low -or $null -eq $close -or $null -eq $volume) {
            continue
        }

        if ($candleDate.Length -eq 8) {
            $candleDate = [datetime]::ParseExact($candleDate, "yyyyMMdd", $null).ToString("yyyy-MM-dd")
        }

        [void]$sb.AppendLine("INSERT INTO daily_candles_k150 (code, candle_date, open, high, low, close, volume, source)")
        [void]$sb.AppendLine("VALUES ('" + $code + "', '" + $candleDate + "', " + $open + ", " + $high + ", " + $low + ", " + $close + ", " + $volume + ", 'cybos')")
        [void]$sb.AppendLine("ON DUPLICATE KEY UPDATE")
        [void]$sb.AppendLine("  open = VALUES(open),")
        [void]$sb.AppendLine("  high = VALUES(high),")
        [void]$sb.AppendLine("  low = VALUES(low),")
        [void]$sb.AppendLine("  close = VALUES(close),")
        [void]$sb.AppendLine("  volume = VALUES(volume),")
        [void]$sb.AppendLine("  source = VALUES(source);")
        [void]$sb.AppendLine("")

        $totalRows++
    }
}

[void]$sb.AppendLine("COMMIT;")

$outDir = Split-Path -Parent $OutputSqlPath
if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

[System.IO.File]::WriteAllText($OutputSqlPath, $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Output "Generated SQL: $OutputSqlPath"
Write-Output "Rows prepared: $totalRows"
