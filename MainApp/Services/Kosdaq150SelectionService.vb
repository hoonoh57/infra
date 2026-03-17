Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Newtonsoft.Json

Namespace Services
    Public Class Kosdaq150CandidateRow
        Public Property Code As String = ""
        Public Property Name As String = ""
        Public Property PrevClose As Integer
        Public Property CutoffClose As Integer
        Public Property RisePct As Double
        Public Property TradeAmountEok As Double
    End Class

    Public Class Kosdaq150SelectionService
        Private Const DbConfigFileName As String = "db.config"

        Private ReadOnly _config As ResearchDbMySqlConfig

        Public Sub New()
            _config = LoadDbConfig()
            If _config Is Nothing OrElse Not _config.Enabled Then
                Throw New InvalidOperationException("KOSDAQ150 selection requires an enabled db.config.")
            End If
        End Sub

        ''' <summary>
        ''' KOSDAQ150 유니버스 전체를 전일 종가와 함께 로드.
        ''' 장 시작 전 준비용: 당일 분봉 데이터 없이도 동작.
        ''' </summary>
        Public Function LoadUniverse(tradingDate As DateTime) As IReadOnlyList(Of Kosdaq150CandidateRow)
            Dim tradingDateText As String = tradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

            ' 직전 거래일 찾기: tradingDate 이전의 가장 최근 daily_candles_k150 날짜
            Dim sqlText As String =
                "SELECT u.code, u.name, " &
                "IFNULL(d.close, 0) AS prev_close, " &
                "0 AS cutoff_close, " &
                "0 AS rise_pct, " &
                "0 AS trade_amount_eok " &
                "FROM universe_kosdaq150 u " &
                "LEFT JOIN daily_candles_k150 d ON d.code = u.code AND d.candle_date = (" &
                    "SELECT MAX(d2.candle_date) FROM daily_candles_k150 d2 " &
                    "WHERE d2.code = u.code AND d2.candle_date < " & Sql(tradingDateText) &
                ") " &
                "WHERE u.source_date = (" &
                    "SELECT COALESCE(MAX(CASE WHEN source_date <= " & Sql(tradingDateText) & " THEN source_date END), MAX(source_date)) " &
                    "FROM universe_kosdaq150" &
                ") " &
                "ORDER BY u.code;"

            Dim rows As List(Of String()) = ExecuteQuery(sqlText)
            Dim result As New List(Of Kosdaq150CandidateRow)()
            For Each cols In rows
                If cols.Length < 6 Then Continue For
                result.Add(New Kosdaq150CandidateRow() With {
                    .Code = NormalizeCode(cols(0)),
                    .Name = cols(1).Trim(),
                    .PrevClose = CInt(Math.Truncate(SafeDouble(cols(2)))),
                    .CutoffClose = 0,
                    .RisePct = 0,
                    .TradeAmountEok = 0
                })
            Next
            Return result
        End Function

        Public Function LoadCandidates(tradingDate As DateTime,
                                       cutoffTime As TimeSpan,
                                       minRisePct As Double,
                                       minTradeAmountEok As Double) As IReadOnlyList(Of Kosdaq150CandidateRow)
            Dim tradingDateText As String = tradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Dim prevDateText As String = tradingDate.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Dim cutoffDateTimeText As String = tradingDate.Date.Add(cutoffTime).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            Dim startDateTimeText As String = tradingDate.Date.ToString("yyyy-MM-dd 00:00:00", CultureInfo.InvariantCulture)
            Dim minAmount As Double = minTradeAmountEok * 100000000.0R

            Dim sqlText As String =
                "SELECT u.code, u.name, " &
                "IFNULL(d.close, 0) AS prev_close, " &
                "IFNULL(m.close, 0) AS cutoff_close, " &
                "ROUND(((IFNULL(m.close, 0) - IFNULL(d.close, 0)) / NULLIF(IFNULL(d.close, 0), 0)) * 100, 4) AS rise_pct, " &
                "ROUND(IFNULL(a.trade_amount, 0) / 100000000, 4) AS trade_amount_eok " &
                "FROM universe_kosdaq150 u " &
                "LEFT JOIN daily_candles_k150 d ON d.code = u.code AND d.candle_date = " & Sql(prevDateText) & " " &
                "LEFT JOIN minute_candles_k150 m ON m.code = u.code AND m.timeframe_min = 1 AND m.candle_dt = (" &
                    "SELECT MAX(m2.candle_dt) FROM minute_candles_k150 m2 " &
                    "WHERE m2.code = u.code AND m2.timeframe_min = 1 " &
                    "AND m2.candle_dt >= " & Sql(startDateTimeText) & " " &
                    "AND m2.candle_dt <= " & Sql(cutoffDateTimeText) & ") " &
                "LEFT JOIN (" &
                    "SELECT code, SUM(CASE WHEN IFNULL(tr_amount, 0) > 0 THEN tr_amount ELSE close * volume END) AS trade_amount " &
                    "FROM minute_candles_k150 " &
                    "WHERE timeframe_min = 1 " &
                    "AND candle_dt >= " & Sql(startDateTimeText) & " " &
                    "AND candle_dt <= " & Sql(cutoffDateTimeText) & " " &
                    "GROUP BY code" &
                ") a ON a.code = u.code " &
                "WHERE u.source_date = (" &
                    "SELECT COALESCE(MAX(CASE WHEN source_date <= " & Sql(tradingDateText) & " THEN source_date END), MAX(source_date)) " &
                    "FROM universe_kosdaq150" &
                ") " &
                "AND IFNULL(d.close, 0) > 0 " &
                "AND IFNULL(m.close, 0) > 0 " &
                "AND (((IFNULL(m.close, 0) - IFNULL(d.close, 0)) / NULLIF(IFNULL(d.close, 0), 0)) * 100) >= " & minRisePct.ToString(CultureInfo.InvariantCulture) & " " &
                "AND IFNULL(a.trade_amount, 0) >= " & minAmount.ToString(CultureInfo.InvariantCulture) & " " &
                "ORDER BY rise_pct DESC, trade_amount_eok DESC;"

            Dim rows As List(Of String()) = ExecuteQuery(sqlText)
            Dim result As New List(Of Kosdaq150CandidateRow)()
            For Each cols In rows
                If cols.Length < 6 Then Continue For
                result.Add(New Kosdaq150CandidateRow() With {
                    .Code = NormalizeCode(cols(0)),
                    .Name = cols(1).Trim(),
                    .PrevClose = CInt(Math.Truncate(SafeDouble(cols(2)))),
                    .CutoffClose = CInt(Math.Truncate(SafeDouble(cols(3)))),
                    .RisePct = SafeDouble(cols(4)),
                    .TradeAmountEok = SafeDouble(cols(5))
                })
            Next

            Return result
        End Function

        Private Function ExecuteQuery(sqlText As String) As List(Of String())
            Dim psi As New ProcessStartInfo() With {
                .FileName = _config.MySqlCliPath,
                .Arguments = BuildMysqlQueryArguments(_config, sqlText),
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8
            }

            Using proc As Process = Process.Start(psi)
                If proc Is Nothing Then Throw New InvalidOperationException("mysql.exe could not be started for KOSDAQ150 query.")

                Dim stdout As String = proc.StandardOutput.ReadToEnd()
                Dim stderr As String = proc.StandardError.ReadToEnd()
                proc.WaitForExit()

                If proc.ExitCode <> 0 Then
                    Throw New InvalidOperationException($"KOSDAQ150 query failed: {stderr}")
                End If

                Dim result As New List(Of String())()
                For Each rawLine In stdout.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
                    Dim line As String = rawLine.Trim()
                    If line.Length = 0 Then Continue For
                    result.Add(line.Split(ControlChars.Tab))
                Next
                Return result
            End Using
        End Function

        Private Shared Function BuildMysqlQueryArguments(config As ResearchDbMySqlConfig, sqlText As String) As String
            Dim parts As New List(Of String) From {
                "--batch",
                "--raw",
                "--skip-column-names",
                "-h", QuoteCli(config.Host),
                "-P", config.Port.ToString(CultureInfo.InvariantCulture),
                "-u", QuoteCli(config.UserName)
            }

            If Not String.IsNullOrWhiteSpace(config.Password) Then
                parts.Add("-p" & QuoteCli(config.Password))
            End If

            If Not String.IsNullOrWhiteSpace(config.Charset) Then
                parts.Add("--default-character-set=" & QuoteCli(config.Charset))
            End If

            parts.Add(QuoteCli(config.DatabaseName))
            parts.Add("-e")
            parts.Add(QuoteCli(sqlText))
            Return String.Join(" ", parts)
        End Function

        Private Shared Function LoadDbConfig() As ResearchDbMySqlConfig
            Dim path As String = ResolveDbConfigPath()
            If Not File.Exists(path) Then Throw New FileNotFoundException("db.config was not found.", path)

            Dim jsonText As String = File.ReadAllText(path)
            Dim config As ResearchDbMySqlConfig = JsonConvert.DeserializeObject(Of ResearchDbMySqlConfig)(jsonText)
            If config Is Nothing Then Throw New InvalidOperationException("db.config could not be parsed.")

            If String.IsNullOrWhiteSpace(config.MySqlCliPath) Then config.MySqlCliPath = "mysql"
            If String.IsNullOrWhiteSpace(config.Host) Then config.Host = "127.0.0.1"
            If config.Port <= 0 Then config.Port = 3306
            If String.IsNullOrWhiteSpace(config.DatabaseName) Then config.DatabaseName = "strategy_research"
            If String.IsNullOrWhiteSpace(config.Charset) Then config.Charset = "utf8mb4"
            Return config
        End Function

        Private Shared Function ResolveDbConfigPath() As String
            Dim baseDir As String = AppDomain.CurrentDomain.BaseDirectory
            Dim current As DirectoryInfo = New DirectoryInfo(baseDir)

            Do While current IsNot Nothing
                Dim candidate As String = Path.Combine(current.FullName, DbConfigFileName)
                If File.Exists(candidate) Then Return candidate
                current = current.Parent
            Loop

            Return Path.Combine(baseDir, DbConfigFileName)
        End Function

        Private Shared Function NormalizeCode(value As String) As String
            Dim digits As String = New String((If(value, String.Empty)).Where(AddressOf Char.IsDigit).ToArray())
            If digits.Length >= 6 Then Return digits.Substring(digits.Length - 6)
            Return digits.PadLeft(6, "0"c)
        End Function

        Private Shared Function SafeDouble(value As String) As Double
            Dim parsed As Double
            If Double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, parsed) Then Return parsed
            If Double.TryParse(value, NumberStyles.Any, CultureInfo.GetCultureInfo("ko-KR"), parsed) Then Return parsed
            Return 0R
        End Function

        Private Shared Function Sql(value As String) As String
            Return "'" & value.Replace("'", "''") & "'"
        End Function

        Private Shared Function QuoteCli(value As String) As String
            Return """" & (If(value, String.Empty)).Replace("""", """""") & """"
        End Function
    End Class
End Namespace
