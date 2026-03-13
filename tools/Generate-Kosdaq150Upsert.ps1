param(
    [Parameter(Mandatory = $true)]
    [string]$ExcelPath,

    [Parameter(Mandatory = $false)]
    [string]$OutputSqlPath = "",

    [Parameter(Mandatory = $false)]
    [string]$DatabaseName = "strategy_research",

    [Parameter(Mandatory = $false)]
    [datetime]$SourceDate = (Get-Date)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Escape-SqlString {
    param([string]$Value)
    if ($null -eq $Value) { return "NULL" }
    return "'" + ($Value -replace "'", "''") + "'"
}

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

function To-Int64Value {
    param([object]$Value)
    if ($null -eq $Value) { return $null }
    $raw = $Value.ToString().Trim()
    if ($raw -eq "") { return $null }
    try {
        return [int64][Math]::Round([double]$raw, 0)
    }
    catch {
        $digits = $raw -replace "[^0-9\-]", ""
        if ([string]::IsNullOrWhiteSpace($digits)) { return $null }
        return [int64]$digits
    }
}

if (-not (Test-Path $ExcelPath)) {
    throw "Excel file not found: $ExcelPath"
}

if ([string]::IsNullOrWhiteSpace($OutputSqlPath)) {
    $stamp = $SourceDate.ToString("yyyyMMdd")
    $OutputSqlPath = Join-Path (Get-Location) "kosdaq150_upsert_$stamp.sql"
}

$excel = $null
$workbook = $null
$worksheet = $null

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false

    $workbook = $excel.Workbooks.Open($ExcelPath)
    $worksheet = $workbook.Worksheets.Item(1)
    $used = $worksheet.UsedRange

    # Expected sheet layout observed from kosdaq150.xlsx:
    # 1=code, 2=name, 6=market_cap
    $colCode = 1
    $colName = 2
    $colMarketCap = 6

    $sourceDateSql = $SourceDate.ToString("yyyy-MM-dd")
    $sourceFileSql = (Resolve-Path $ExcelPath).Path

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("USE $DatabaseName;")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("START TRANSACTION;")
    [void]$sb.AppendLine("")

    $rowCount = $used.Rows.Count
    $prepared = 0

    for ($row = 2; $row -le $rowCount; $row++) {
        $code = Normalize-Code $worksheet.Cells.Item($row, $colCode).Text
        $name = $worksheet.Cells.Item($row, $colName).Text.Trim()
        $marketCap = To-Int64Value $worksheet.Cells.Item($row, $colMarketCap).Value2

        if ([string]::IsNullOrWhiteSpace($code) -or [string]::IsNullOrWhiteSpace($name)) {
            continue
        }

        $marketCapSql = if ($null -eq $marketCap) { "NULL" } else { $marketCap.ToString() }
        $codeSql = Escape-SqlString $code
        $nameSql = Escape-SqlString $name
        $fileSql = Escape-SqlString $sourceFileSql

        [void]$sb.AppendLine("INSERT INTO universe_kosdaq150 (code, name, market, market_cap, source_date, source_file, is_active)")
        [void]$sb.AppendLine("VALUES ($codeSql, $nameSql, 'KOSDAQ', $marketCapSql, '$sourceDateSql', $fileSql, 1)")
        [void]$sb.AppendLine("ON DUPLICATE KEY UPDATE")
        [void]$sb.AppendLine("  name = VALUES(name),")
        [void]$sb.AppendLine("  market = VALUES(market),")
        [void]$sb.AppendLine("  market_cap = VALUES(market_cap),")
        [void]$sb.AppendLine("  source_file = VALUES(source_file),")
        [void]$sb.AppendLine("  is_active = VALUES(is_active),")
        [void]$sb.AppendLine("  updated_at = CURRENT_TIMESTAMP;")
        [void]$sb.AppendLine("")

        $prepared++
    }

    [void]$sb.AppendLine("COMMIT;")

    $outDir = Split-Path -Parent $OutputSqlPath
    if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir | Out-Null
    }

    [System.IO.File]::WriteAllText($OutputSqlPath, $sb.ToString(), [System.Text.Encoding]::UTF8)
    Write-Output "Generated SQL: $OutputSqlPath"
    Write-Output "Rows prepared: $prepared"
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
