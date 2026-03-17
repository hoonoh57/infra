' ═══════════════════════════════════════════════════════════════
' ZeroLossBatchAnalyzer.vb — Research DB 배치 분석 리포트
' ═══════════════════════════════════════════════════════════════
'
' Research DB (minute_candles_k150)에서 분봉을 읽어
' ZeroLoss 전략을 전 종목 × 전 거래일에 대해 시뮬레이션하고
' 리포트를 생성한다.
'
' ZeroLossChartStrategy와 동일한 파라미터:
'   OC=7%, Amt=100억, S=-3%, T=+10%, Time=14:50
'
' ═══════════════════════════════════════════════════════════════

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports [Shared]

Namespace Services
    Public Class ZeroLossBatchAnalyzer

        ' ── ZeroLoss 파라미터 (ZeroLossChartStrategy와 동일) ──
        Private Const OC_THRESHOLD As Single = 7.0F
        Private Const AMT_THRESHOLD_EOK As Single = 100.0F
        Private Const STOP_LOSS_PCT As Single = -3.0F
        Private Const TARGET_PROFIT_PCT As Single = 10.0F

        Private Shared ReadOnly SCAN_START As New TimeSpan(9, 1, 0)
        Private Shared ReadOnly SCAN_END As New TimeSpan(14, 30, 0)
        Private Shared ReadOnly FINAL_EXIT As New TimeSpan(14, 50, 0)

        Private ReadOnly _config As ResearchDbMySqlConfig

        Public Sub New()
            _config = LoadDbConfig()
        End Sub

        ''' <summary>배치 분석 실행 → 리포트 텍스트 반환</summary>
        Public Function RunBatchAnalysis(fromDate As DateTime, toDate As DateTime) As String
            If _config Is Nothing OrElse Not _config.Enabled Then
                Return "ERROR: db.config not found or disabled."
            End If

            AppLogger.I.Info($"[ZeroLoss Batch] 분석 시작: {fromDate:yyyy-MM-dd} ~ {toDate:yyyy-MM-dd}", "Batch")

            ' 1) 유니버스 로드
            Dim codes = LoadUniverse(fromDate)
            If codes.Count = 0 Then Return "ERROR: No universe codes found."
            AppLogger.I.Info($"[ZeroLoss Batch] 유니버스: {codes.Count}종목", "Batch")

            ' 2) 전 종목 시뮬레이션
            Dim allTrades As New List(Of TradeRecord)()
            For Each code In codes
                Dim candles = LoadMinuteCandles(code, fromDate, toDate)
                If candles.Count < 10 Then Continue For

                Dim trades = SimulateAllDays(code, candles)
                allTrades.AddRange(trades)
            Next

            AppLogger.I.Info($"[ZeroLoss Batch] 완료: {allTrades.Count}건 매매", "Batch")

            ' 3) 리포트 생성
            Return BuildReport(allTrades, fromDate, toDate, codes.Count)
        End Function

        ' ══════════════════════════════════════════
        ' 시뮬레이션 (ZeroLossChartStrategy 동일 로직)
        ' ══════════════════════════════════════════

        Private Function SimulateAllDays(code As String, candles As List(Of MinuteBar)) As List(Of TradeRecord)
            Dim trades As New List(Of TradeRecord)()

            ' 거래일별 그룹핑
            Dim dayGroups As New Dictionary(Of Date, Integer)()
            For i = 0 To candles.Count - 1
                Dim d = candles(i).Dt.Date
                If Not dayGroups.ContainsKey(d) Then dayGroups(d) = i
            Next

            For Each kvp In dayGroups.OrderBy(Function(x) x.Key)
                Dim dayDate = kvp.Key
                Dim dayStart = kvp.Value
                Dim dayEnd = dayStart
                For i = dayStart To candles.Count - 1
                    If candles(i).Dt.Date = dayDate Then dayEnd = i Else Exit For
                Next

                SimulateDay(code, candles, dayStart, dayEnd, trades)
            Next

            Return trades
        End Function

        Private Sub SimulateDay(code As String, candles As List(Of MinuteBar),
                                dayStart As Integer, dayEnd As Integer,
                                trades As List(Of TradeRecord))

            Dim dayOpen = candles(dayStart).Open
            If dayOpen <= 0 Then Return

            Dim entryPrice As Single = 0
            Dim entryTime As DateTime = DateTime.MinValue
            Dim inPosition As Boolean = False
            Dim todayEntryCount As Integer = 0
            Dim cumulativeAmount As Long = 0

            For i = dayStart To dayEnd
                Dim c = candles(i)
                Dim timeOfDay = c.Dt.TimeOfDay

                cumulativeAmount += c.TradeAmount
                If c.TradeAmount = 0 Then
                    cumulativeAmount += CLng(c.Close) * c.Volume
                End If

                If inPosition Then
                    ' Stop-Loss
                    If c.Low > 0 AndAlso ((c.Low / entryPrice - 1.0F) * 100.0F) <= STOP_LOSS_PCT Then
                        Dim exitPrice = entryPrice * (1.0F + STOP_LOSS_PCT / 100.0F)
                        trades.Add(New TradeRecord With {
                            .Code = code, .EntryTime = entryTime, .EntryPrice = entryPrice,
                            .ExitTime = c.Dt, .ExitPrice = exitPrice,
                            .ExitReason = "StopLoss",
                            .PnlPct = (exitPrice / entryPrice - 1.0F) * 100.0F
                        })
                        inPosition = False
                        Continue For
                    End If

                    ' Target Profit
                    If c.High > 0 AndAlso ((c.High / entryPrice - 1.0F) * 100.0F) >= TARGET_PROFIT_PCT Then
                        Dim exitPrice = entryPrice * (1.0F + TARGET_PROFIT_PCT / 100.0F)
                        trades.Add(New TradeRecord With {
                            .Code = code, .EntryTime = entryTime, .EntryPrice = entryPrice,
                            .ExitTime = c.Dt, .ExitPrice = exitPrice,
                            .ExitReason = "Target",
                            .PnlPct = (exitPrice / entryPrice - 1.0F) * 100.0F
                        })
                        inPosition = False
                        Continue For
                    End If

                    ' 14:50 청산
                    If timeOfDay >= FINAL_EXIT Then
                        trades.Add(New TradeRecord With {
                            .Code = code, .EntryTime = entryTime, .EntryPrice = entryPrice,
                            .ExitTime = c.Dt, .ExitPrice = c.Close,
                            .ExitReason = "TimeExit",
                            .PnlPct = (c.Close / entryPrice - 1.0F) * 100.0F
                        })
                        inPosition = False
                        Continue For
                    End If

                    Continue For
                End If

                ' 진입 조건
                If timeOfDay < SCAN_START OrElse timeOfDay > SCAN_END Then Continue For
                If todayEntryCount >= 1 Then Continue For  ' 하루 1회 진입

                Dim openChange = (c.Close / dayOpen - 1.0F) * 100.0F
                If openChange < OC_THRESHOLD Then Continue For

                Dim amtEok = CSng(cumulativeAmount) / 100_000_000.0F
                If amtEok < AMT_THRESHOLD_EOK Then Continue For

                entryPrice = c.Close
                entryTime = c.Dt
                inPosition = True
                todayEntryCount += 1
            Next

            ' 장 마감 미청산
            If inPosition Then
                Dim lastCandle = candles(dayEnd)
                trades.Add(New TradeRecord With {
                    .Code = code, .EntryTime = entryTime, .EntryPrice = entryPrice,
                    .ExitTime = lastCandle.Dt, .ExitPrice = lastCandle.Close,
                    .ExitReason = "EOD",
                    .PnlPct = (lastCandle.Close / entryPrice - 1.0F) * 100.0F
                })
            End If
        End Sub

        ' ══════════════════════════════════════════
        ' 리포트 생성
        ' ══════════════════════════════════════════

        Private Function BuildReport(trades As List(Of TradeRecord),
                                      fromDate As DateTime, toDate As DateTime,
                                      universeCount As Integer) As String
            Dim sb As New StringBuilder()
            sb.AppendLine("═══════════════════════════════════════════════════════")
            sb.AppendLine("  ZeroLoss Batch Analysis Report")
            sb.AppendLine($"  Period: {fromDate:yyyy-MM-dd} ~ {toDate:yyyy-MM-dd}")
            sb.AppendLine($"  Universe: KOSDAQ150 ({universeCount} stocks)")
            sb.AppendLine($"  Parameters: OC>={OC_THRESHOLD}% Amt>={AMT_THRESHOLD_EOK}억 S={STOP_LOSS_PCT}% T=+{TARGET_PROFIT_PCT}%")
            sb.AppendLine("═══════════════════════════════════════════════════════")
            sb.AppendLine()

            If trades.Count = 0 Then
                sb.AppendLine("  No trades found in this period.")
                Return sb.ToString()
            End If

            ' ── 전체 요약 ──
            Dim wins = trades.Where(Function(t) t.PnlPct > 0).ToList()
            Dim losses = trades.Where(Function(t) t.PnlPct <= 0).ToList()
            Dim avgPnl = trades.Average(Function(t) t.PnlPct)
            Dim totalPnl = trades.Sum(Function(t) t.PnlPct)

            sb.AppendLine("── Summary ──")
            sb.AppendLine($"  Total Trades:     {trades.Count}")
            sb.AppendLine($"  Win / Loss:       {wins.Count} / {losses.Count}  ({wins.Count * 100.0 / trades.Count:F1}% win rate)")
            sb.AppendLine($"  Avg PnL:          {avgPnl:+0.00;-0.00}%")
            sb.AppendLine($"  Total PnL:        {totalPnl:+0.00;-0.00}%")
            sb.AppendLine($"  Best Trade:       {trades.Max(Function(t) t.PnlPct):+0.00;-0.00}%")
            sb.AppendLine($"  Worst Trade:      {trades.Min(Function(t) t.PnlPct):+0.00;-0.00}%")
            If wins.Count > 0 Then sb.AppendLine($"  Avg Win:          {wins.Average(Function(t) t.PnlPct):+0.00;-0.00}%")
            If losses.Count > 0 Then sb.AppendLine($"  Avg Loss:         {losses.Average(Function(t) t.PnlPct):+0.00;-0.00}%")
            sb.AppendLine()

            ' ── Exit Reason 분포 ──
            sb.AppendLine("── Exit Reason Distribution ──")
            For Each grp In trades.GroupBy(Function(t) t.ExitReason).OrderByDescending(Function(g) g.Count())
                Dim cnt = grp.Count()
                Dim avg = grp.Average(Function(t) t.PnlPct)
                sb.AppendLine($"  {grp.Key,-12} {cnt,4}건  avg={avg:+0.00;-0.00}%")
            Next
            sb.AppendLine()

            ' ── 일별 요약 ──
            sb.AppendLine("── Daily Summary ──")
            sb.AppendLine($"  {"Date",-12} {"Trades",7} {"Win",5} {"Loss",5} {"AvgPnL",9} {"TotalPnL",10}")
            sb.AppendLine($"  {New String("-"c, 60)}")
            For Each dayGrp In trades.GroupBy(Function(t) t.EntryTime.Date).OrderBy(Function(g) g.Key)
                Dim d = dayGrp.Key
                Dim dTrades = dayGrp.ToList()
                Dim dWins = dTrades.Where(Function(t) t.PnlPct > 0).Count()
                Dim dLosses = dTrades.Count - dWins
                Dim dAvg = dTrades.Average(Function(t) t.PnlPct)
                Dim dTotal = dTrades.Sum(Function(t) t.PnlPct)
                sb.AppendLine($"  {d:yyyy-MM-dd}   {dTrades.Count,5}   {dWins,4}  {dLosses,4}   {dAvg:+0.00;-0.00}%   {dTotal:+0.00;-0.00}%")
            Next
            sb.AppendLine()

            ' ── 개별 매매 상세 (최근 50건) ──
            sb.AppendLine("── Recent Trades (last 50) ──")
            sb.AppendLine($"  {"Code",-8} {"Entry Time",-18} {"Entry",8} {"Exit Time",-18} {"Exit",8} {"PnL",8} {"Reason",-10}")
            sb.AppendLine($"  {New String("-"c, 90)}")
            For Each t In trades.OrderByDescending(Function(x) x.EntryTime).Take(50)
                sb.AppendLine($"  {t.Code,-8} {t.EntryTime:MM-dd HH:mm}       {t.EntryPrice,8:N0} {t.ExitTime:MM-dd HH:mm}       {t.ExitPrice,8:N0} {t.PnlPct,7:+0.00;-0.00}% {t.ExitReason,-10}")
            Next

            Return sb.ToString()
        End Function

        ' ══════════════════════════════════════════
        ' Research DB Access (mysql CLI)
        ' ══════════════════════════════════════════

        Private Function LoadUniverse(asOfDate As DateTime) As List(Of String)
            Dim sql = "SELECT DISTINCT code FROM universe_kosdaq150 " &
                      $"WHERE source_date = COALESCE((SELECT MAX(source_date) FROM universe_kosdaq150 WHERE source_date <= '{asOfDate:yyyy-MM-dd}'), " &
                      "(SELECT MAX(source_date) FROM universe_kosdaq150)) AND is_active = 1 ORDER BY code;"
            Dim lines = ExecuteQuery(sql)
            Return lines.Where(Function(l) l.Trim().Length = 6).Select(Function(l) l.Trim()).ToList()
        End Function

        Private Function LoadMinuteCandles(code As String, fromDate As DateTime, toDate As DateTime) As List(Of MinuteBar)
            Dim sql = "SELECT DATE_FORMAT(candle_dt, '%Y-%m-%d %H:%i:%s'), open, high, low, close, volume, COALESCE(tr_amount, 0) " &
                      "FROM minute_candles_k150 " &
                      $"WHERE code = '{code}' AND timeframe_min = 1 " &
                      $"AND candle_dt >= '{fromDate:yyyy-MM-dd} 00:00:00' " &
                      $"AND candle_dt <= '{toDate:yyyy-MM-dd} 23:59:59' " &
                      "ORDER BY candle_dt ASC;"
            Dim lines = ExecuteQuery(sql)
            Dim result As New List(Of MinuteBar)()
            For Each line In lines
                Dim cols = line.Split(ControlChars.Tab)
                If cols.Length < 7 Then Continue For
                Dim dt As DateTime
                If Not DateTime.TryParseExact(cols(0).Trim(), "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Continue For
                Dim bar As New MinuteBar()
                bar.Dt = dt
                Single.TryParse(cols(1), NumberStyles.Any, CultureInfo.InvariantCulture, bar.Open)
                Single.TryParse(cols(2), NumberStyles.Any, CultureInfo.InvariantCulture, bar.High)
                Single.TryParse(cols(3), NumberStyles.Any, CultureInfo.InvariantCulture, bar.Low)
                Single.TryParse(cols(4), NumberStyles.Any, CultureInfo.InvariantCulture, bar.Close)
                Long.TryParse(cols(5), NumberStyles.Any, CultureInfo.InvariantCulture, bar.Volume)
                Long.TryParse(cols(6), NumberStyles.Any, CultureInfo.InvariantCulture, bar.TradeAmount)
                result.Add(bar)
            Next
            Return result
        End Function

        Private Function ExecuteQuery(sql As String) As List(Of String)
            Dim psi As New ProcessStartInfo With {
                .FileName = _config.MySqlCliPath,
                .Arguments = BuildArgs(sql),
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8
            }
            Using proc = Process.Start(psi)
                If proc Is Nothing Then Return New List(Of String)()
                Dim output = proc.StandardOutput.ReadToEnd()
                proc.WaitForExit()
                Return output.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries).ToList()
            End Using
        End Function

        Private Function BuildArgs(sql As String) As String
            Dim q = Function(v As String) """" & v.Replace("""", "\""") & """"
            Dim args As New List(Of String) From {
                "--batch", "--raw", "--skip-column-names",
                $"--host={q(_config.Host)}",
                $"--port={_config.Port}",
                $"--user={q(_config.UserName)}",
                $"--default-character-set={q(_config.Charset)}"
            }
            If Not String.IsNullOrWhiteSpace(_config.Password) Then
                args.Add($"--password={q(_config.Password)}")
            End If
            args.Add(q(_config.DatabaseName))
            args.Add("-e")
            args.Add(q(sql))
            Return String.Join(" ", args)
        End Function

        Private Shared Function LoadDbConfig() As ResearchDbMySqlConfig
            Dim current = New DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
            For i = 0 To 5
                If current Is Nothing Then Exit For
                Dim path = IO.Path.Combine(current.FullName, "db.config")
                If File.Exists(path) Then
                    Dim json = File.ReadAllText(path, Encoding.UTF8)
                    Dim cfg = JsonConvert.DeserializeObject(Of ResearchDbMySqlConfig)(json)
                    If cfg IsNot Nothing Then
                        If String.IsNullOrWhiteSpace(cfg.MySqlCliPath) Then cfg.MySqlCliPath = "mysql"
                        If String.IsNullOrWhiteSpace(cfg.Host) Then cfg.Host = "127.0.0.1"
                        If cfg.Port <= 0 Then cfg.Port = 3306
                        If String.IsNullOrWhiteSpace(cfg.DatabaseName) Then cfg.DatabaseName = "strategy_research"
                        If String.IsNullOrWhiteSpace(cfg.UserName) Then cfg.UserName = "root"
                        If String.IsNullOrWhiteSpace(cfg.Charset) Then cfg.Charset = "utf8mb4"
                        Return cfg
                    End If
                End If
                current = current.Parent
            Next
            Return Nothing
        End Function

        ' ══════════════════════════════════════════
        ' 내부 모델
        ' ══════════════════════════════════════════

        Private Class MinuteBar
            Public Dt As DateTime
            Public Open As Single
            Public High As Single
            Public Low As Single
            Public Close As Single
            Public Volume As Long
            Public TradeAmount As Long
        End Class

        Public Class TradeRecord
            Public Code As String = ""
            Public EntryTime As DateTime
            Public EntryPrice As Single
            Public ExitTime As DateTime
            Public ExitPrice As Single
            Public ExitReason As String = ""
            Public PnlPct As Single
        End Class
    End Class
End Namespace
