' ═══════════════════════════════════════════════════════════════
' ZeroLossExperimentSweepService.vb — ZeroLoss 파라미터 그리드 서치
' ═══════════════════════════════════════════════════════════════
'
' ZeroLossBatchAnalyzer의 시뮬레이션 로직을 파라미터화하여 복제.
' 캔들을 1회 로드 후 메모리 캐시 → 조합별 in-memory 시뮬레이션.
' 원본 ZeroLossChartStrategy, ZeroLossBatchAnalyzer는 수정하지 않음.
'
' ═══════════════════════════════════════════════════════════════

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Newtonsoft.Json
Imports [Shared]

Namespace Services

    ' ── 파라미터 세트 ──
    Public Class ZeroLossExperimentParams
        Public OcThreshold As Single     ' 시가 대비 상승률 %
        Public AmtThresholdEok As Single ' 누적 거래대금 (억)
        Public StopLossPct As Single     ' 손절 % (음수)
        Public TargetProfitPct As Single ' 익절 %
        Public MaxEntries As Integer = 1 ' 하루 최대 진입 횟수
        Public ScanEndMinute As Integer = 870   ' 스캔 종료 (분, 870=14:30)
        Public FinalExitMinute As Integer = 890 ' 일괄 청산 (분, 890=14:50)

        ' ── VI (Volatility Interruption) 파라미터 ──
        Public ViCooldownBars As Integer = 3    ' VI 감지 후 진입 금지 캔들 수 (0=VI 무시)
        Public ViSlippagePct As Single = 0.5F   ' VI 후 진입 슬리피지 % (0=없음)
        Public ViThresholdPct As Single = 6.0F  ' 동적 VI 감지 임계값 (코스닥 ±6%)

        Public ReadOnly Property VersionName As String
            Get
                Dim baseName = $"OC{OcThreshold:0}_{If(AmtThresholdEok >= 1000, $"Amt{CInt(AmtThresholdEok / 10)}h", $"Amt{CInt(AmtThresholdEok)}")}_S{CInt(Math.Abs(StopLossPct))}_T{CInt(TargetProfitPct)}"
                If MaxEntries <> 1 Then baseName &= $"_E{MaxEntries}"
                If ScanEndMinute <> 870 Then baseName &= $"_SE{ScanEndMinute \ 60}:{ScanEndMinute Mod 60:00}"
                If FinalExitMinute <> 890 Then baseName &= $"_FE{FinalExitMinute \ 60}:{FinalExitMinute Mod 60:00}"
                If ViCooldownBars <> 3 OrElse Math.Abs(ViSlippagePct - 0.5F) > 0.01F Then
                    baseName &= $"_VI{ViCooldownBars}s{ViSlippagePct:0.#}"
                End If
                Return baseName
            End Get
        End Property

        Public ReadOnly Property DisplayText As String
            Get
                Dim txt = $"OC≥{OcThreshold}% Amt≥{AmtThresholdEok}억 S={StopLossPct}% T=+{TargetProfitPct}%"
                If MaxEntries <> 1 Then txt &= $" 재진입={MaxEntries}회"
                If ScanEndMinute <> 870 Then txt &= $" 스캔종료={ScanEndMinute \ 60}:{ScanEndMinute Mod 60:00}"
                If FinalExitMinute <> 890 Then txt &= $" 청산={FinalExitMinute \ 60}:{FinalExitMinute Mod 60:00}"
                txt &= $" VI쿨다운={ViCooldownBars}분 슬리피지={ViSlippagePct}%"
                Return txt
            End Get
        End Property

        Public ReadOnly Property ScanEndTimeSpan As TimeSpan
            Get
                Return New TimeSpan(ScanEndMinute \ 60, ScanEndMinute Mod 60, 0)
            End Get
        End Property

        Public ReadOnly Property FinalExitTimeSpan As TimeSpan
            Get
                Return New TimeSpan(FinalExitMinute \ 60, FinalExitMinute Mod 60, 0)
            End Get
        End Property
    End Class

    ' ── 실험 결과 ──
    Public Class ZeroLossExperimentResult
        Public Params As ZeroLossExperimentParams
        Public TotalTrades As Integer
        Public Wins As Integer
        Public Losses As Integer
        Public WinRate As Single
        Public AvgPnl As Single
        Public TotalPnl As Single
        Public BestTrade As Single
        Public WorstTrade As Single
        Public TargetExits As Integer
        Public StopExits As Integer
        Public TimeExits As Integer
        Public EodExits As Integer
        Public ViSkippedEntries As Integer  ' VI로 인해 진입하지 못한 횟수
        Public CompositeScore As Single  ' AvgPnl × (WinRate / 100)

        Public ReadOnly Property TargetRate As Single
            Get
                Return If(TotalTrades > 0, CSng(TargetExits) / TotalTrades * 100.0F, 0)
            End Get
        End Property

        Public ReadOnly Property StopRate As Single
            Get
                Return If(TotalTrades > 0, CSng(StopExits) / TotalTrades * 100.0F, 0)
            End Get
        End Property

        Public ReadOnly Property TimeRate As Single
            Get
                Return If(TotalTrades > 0, CSng(TimeExits + EodExits) / TotalTrades * 100.0F, 0)
            End Get
        End Property
    End Class

    ' ── 정렬 기준 ──
    Public Enum SweepSortMode
        Composite   ' AvgPnl × (WinRate/100)
        TotalPnl    ' 총수익 합계
        AvgPnl      ' 건당 평균 수익
        WinRate     ' 승률
    End Enum

    ' ── 진행 이벤트 ──
    Public Class SweepProgressEventArgs
        Inherits EventArgs
        Public Current As Integer
        Public Total As Integer
        Public CurrentVersion As String = ""
    End Class

    ' ══════════════════════════════════════════
    ' 스위프 서비스
    ' ══════════════════════════════════════════

    Public Class ZeroLossExperimentSweepService

        Private Shared ReadOnly SCAN_START As New TimeSpan(9, 1, 0)

        Private ReadOnly _config As ResearchDbMySqlConfig
        Private _lastViSkipCount As Integer = 0

        Public Event Progress As EventHandler(Of SweepProgressEventArgs)

        ''' <summary>취소 플래그</summary>
        Public Property CancelRequested As Boolean = False

        Public Sub New()
            _config = LoadDbConfig()
        End Sub

        ''' <summary>그리드 서치용 파라미터 조합 생성</summary>
        Public Shared Function GenerateGridSearch(ocValues As Single(),
                                                   amtValues As Single(),
                                                   stopValues As Single(),
                                                   targetValues As Single(),
                                                   Optional maxEntriesValues As Integer() = Nothing,
                                                   Optional scanEndValues As Integer() = Nothing,
                                                   Optional finalExitValues As Integer() = Nothing,
                                                   Optional viCooldownValues As Integer() = Nothing,
                                                   Optional viSlippageValues As Single() = Nothing) As List(Of ZeroLossExperimentParams)
            If maxEntriesValues Is Nothing OrElse maxEntriesValues.Length = 0 Then maxEntriesValues = {1}
            If scanEndValues Is Nothing OrElse scanEndValues.Length = 0 Then scanEndValues = {870}
            If finalExitValues Is Nothing OrElse finalExitValues.Length = 0 Then finalExitValues = {890}
            If viCooldownValues Is Nothing OrElse viCooldownValues.Length = 0 Then viCooldownValues = {3}
            If viSlippageValues Is Nothing OrElse viSlippageValues.Length = 0 Then viSlippageValues = {0.5F}

            Dim result As New List(Of ZeroLossExperimentParams)()
            For Each oc In ocValues
                For Each amt In amtValues
                    For Each s In stopValues
                        For Each t In targetValues
                            For Each me_ In maxEntriesValues
                                For Each se In scanEndValues
                                    For Each fe In finalExitValues
                                        For Each viCd In viCooldownValues
                                            For Each viSl In viSlippageValues
                                                result.Add(New ZeroLossExperimentParams With {
                                                    .OcThreshold = oc,
                                                    .AmtThresholdEok = amt,
                                                    .StopLossPct = s,
                                                    .TargetProfitPct = t,
                                                    .MaxEntries = me_,
                                                    .ScanEndMinute = se,
                                                    .FinalExitMinute = fe,
                                                    .ViCooldownBars = viCd,
                                                    .ViSlippagePct = viSl
                                                })
                                            Next
                                        Next
                                    Next
                                Next
                            Next
                        Next
                    Next
                Next
            Next
            Return result
        End Function

        ''' <summary>스위프 실행: 캔들 1회 로드 → 조합별 시뮬레이션</summary>
        Public Function RunSweep(fromDate As DateTime, toDate As DateTime,
                                  paramSets As List(Of ZeroLossExperimentParams),
                                  Optional sortMode As SweepSortMode = SweepSortMode.Composite) As List(Of ZeroLossExperimentResult)

            If _config Is Nothing OrElse Not _config.Enabled Then
                Throw New InvalidOperationException("db.config not found or disabled.")
            End If

            ' 1) 유니버스 로드
            Dim codes = LoadUniverse(fromDate)
            If codes.Count = 0 Then
                Throw New InvalidOperationException("No universe codes found.")
            End If

            ' 2) 전 종목 캔들 1회 로드 (캐시)
            Dim allCandles As New Dictionary(Of String, List(Of MinuteBar))()
            For Each code In codes
                If CancelRequested Then Return New List(Of ZeroLossExperimentResult)()
                Dim candles = LoadMinuteCandles(code, fromDate, toDate)
                If candles.Count >= 10 Then
                    allCandles(code) = candles
                End If
            Next

            ' 3) 각 파라미터 조합별 시뮬레이션
            Dim results As New List(Of ZeroLossExperimentResult)()
            For idx = 0 To paramSets.Count - 1
                If CancelRequested Then Exit For

                Dim p = paramSets(idx)
                RaiseEvent Progress(Me, New SweepProgressEventArgs With {
                    .Current = idx + 1,
                    .Total = paramSets.Count,
                    .CurrentVersion = p.VersionName
                })

                _lastViSkipCount = 0
                Dim trades As New List(Of TradeRecord)()
                For Each kvp In allCandles
                    SimulateAllDays(kvp.Key, kvp.Value, p, trades)
                Next

                Dim result = BuildResult(p, trades)
                result.ViSkippedEntries = _lastViSkipCount
                results.Add(result)
            Next

            ' 4) 정렬
            SortResults(results, sortMode)
            Return results
        End Function

        ''' <summary>결과 재정렬 (폼에서 정렬 전환 시 호출 가능)</summary>
        Public Shared Sub SortResults(results As List(Of ZeroLossExperimentResult), sortMode As SweepSortMode)
            Select Case sortMode
                Case SweepSortMode.TotalPnl
                    results.Sort(Function(a, b) b.TotalPnl.CompareTo(a.TotalPnl))
                Case SweepSortMode.AvgPnl
                    results.Sort(Function(a, b) b.AvgPnl.CompareTo(a.AvgPnl))
                Case SweepSortMode.WinRate
                    results.Sort(Function(a, b) b.WinRate.CompareTo(a.WinRate))
                Case Else
                    results.Sort(Function(a, b) b.CompositeScore.CompareTo(a.CompositeScore))
            End Select
        End Sub

        ' ══════════════════════════════════════════
        ' 시뮬레이션 (파라미터화 — ZeroLossBatchAnalyzer 동일 로직)
        ' ══════════════════════════════════════════

        Private Sub SimulateAllDays(code As String, candles As List(Of MinuteBar),
                                     p As ZeroLossExperimentParams, trades As List(Of TradeRecord))
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
                SimulateDay(code, candles, dayStart, dayEnd, p, trades)
            Next
        End Sub

        Private Sub SimulateDay(code As String, candles As List(Of MinuteBar),
                                 dayStart As Integer, dayEnd As Integer,
                                 p As ZeroLossExperimentParams, trades As List(Of TradeRecord))

            Dim dayOpen = candles(dayStart).Open
            If dayOpen <= 0 Then Return

            Dim entryPrice As Single = 0
            Dim entryTime As DateTime = DateTime.MinValue
            Dim inPosition As Boolean = False
            Dim todayEntryCount As Integer = 0
            Dim cumulativeAmount As Long = 0

            ' ── VI (Volatility Interruption) 상태 ──
            ' 동적 VI: 직전 체결가 대비 ±ViThresholdPct% 이상 급변 시 발동
            ' 발동 시 2분(2캔들) 거래 정지 → ViCooldownBars 캔들 동안 진입 금지
            Dim viCooldownRemaining As Integer = 0  ' 남은 쿨다운 캔들 수
            Dim viSkipCount As Integer = 0          ' VI로 스킵된 진입 시도 횟수

            For i = dayStart To dayEnd
                Dim c = candles(i)
                Dim timeOfDay = c.Dt.TimeOfDay

                cumulativeAmount += c.TradeAmount
                If c.TradeAmount = 0 Then
                    cumulativeAmount += CLng(c.Close) * c.Volume
                End If

                ' ── VI 감지: 직전 캔들 종가 대비 급변 ──
                If p.ViCooldownBars > 0 AndAlso i > dayStart Then
                    Dim prevClose = candles(i - 1).Close
                    If prevClose > 0 Then
                        Dim jumpPct = Math.Abs((c.Close / prevClose - 1.0F) * 100.0F)
                        If jumpPct >= p.ViThresholdPct Then
                            viCooldownRemaining = p.ViCooldownBars  ' VI 발동 → 쿨다운 시작
                        End If
                    End If
                End If

                ' 쿨다운 카운트다운
                If viCooldownRemaining > 0 Then
                    viCooldownRemaining -= 1
                End If

                If inPosition Then
                    ' Stop-Loss
                    If c.Low > 0 AndAlso ((c.Low / entryPrice - 1.0F) * 100.0F) <= p.StopLossPct Then
                        Dim exitPrice = entryPrice * (1.0F + p.StopLossPct / 100.0F)
                        trades.Add(New TradeRecord With {
                            .Code = code, .EntryTime = entryTime, .EntryPrice = entryPrice,
                            .ExitTime = c.Dt, .ExitPrice = exitPrice,
                            .ExitReason = "StopLoss",
                            .PnlPct = (exitPrice / entryPrice - 1.0F) * 100.0F
                        })
                        inPosition = False
                        ' 손절 후 재진입 가능 → Continue 대신 아래 진입 로직으로 fall-through
                    End If

                    ' Target Profit
                    If inPosition AndAlso c.High > 0 AndAlso ((c.High / entryPrice - 1.0F) * 100.0F) >= p.TargetProfitPct Then
                        Dim exitPrice = entryPrice * (1.0F + p.TargetProfitPct / 100.0F)
                        trades.Add(New TradeRecord With {
                            .Code = code, .EntryTime = entryTime, .EntryPrice = entryPrice,
                            .ExitTime = c.Dt, .ExitPrice = exitPrice,
                            .ExitReason = "Target",
                            .PnlPct = (exitPrice / entryPrice - 1.0F) * 100.0F
                        })
                        inPosition = False
                        Continue For  ' 익절 후 같은 캔들에서 재진입 안함
                    End If

                    ' 청산 시각
                    If inPosition AndAlso timeOfDay >= p.FinalExitTimeSpan Then
                        trades.Add(New TradeRecord With {
                            .Code = code, .EntryTime = entryTime, .EntryPrice = entryPrice,
                            .ExitTime = c.Dt, .ExitPrice = c.Close,
                            .ExitReason = "TimeExit",
                            .PnlPct = (c.Close / entryPrice - 1.0F) * 100.0F
                        })
                        inPosition = False
                        Continue For
                    End If

                    If inPosition Then Continue For
                End If

                ' 진입 조건
                If timeOfDay < SCAN_START OrElse timeOfDay > p.ScanEndTimeSpan Then Continue For
                If todayEntryCount >= p.MaxEntries Then Continue For

                Dim openChange = (c.Close / dayOpen - 1.0F) * 100.0F
                If openChange < p.OcThreshold Then Continue For

                Dim amtEok = CSng(cumulativeAmount) / 100_000_000.0F
                If amtEok < p.AmtThresholdEok Then Continue For

                ' ── VI 쿨다운 중이면 진입 불가 ──
                If p.ViCooldownBars > 0 AndAlso viCooldownRemaining > 0 Then
                    viSkipCount += 1
                    Continue For
                End If

                ' ── 진입 (VI 슬리피지 적용) ──
                ' VI 직후(쿨다운 끝난 직후)에는 단일가 매매로 슬리피지 발생
                Dim actualEntryPrice = c.Close
                If p.ViSlippagePct > 0 Then
                    ' 직전 캔들에서 VI급 점프가 있었으면 슬리피지 적용
                    If i > dayStart Then
                        Dim prevClose = candles(i - 1).Close
                        If prevClose > 0 Then
                            Dim recentJump = Math.Abs((c.Close / prevClose - 1.0F) * 100.0F)
                            If recentJump >= p.ViThresholdPct * 0.5F Then
                                ' VI 영향권 → 슬리피지 가산 (더 비싸게 매수)
                                actualEntryPrice = c.Close * (1.0F + p.ViSlippagePct / 100.0F)
                            End If
                        End If
                    End If
                End If

                entryPrice = actualEntryPrice
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

            ' VI 스킵 카운트를 thread-safe하게 누적 (단일 스레드이므로 직접 접근)
            _lastViSkipCount += viSkipCount
        End Sub

        ' ══════════════════════════════════════════
        ' 결과 집계
        ' ══════════════════════════════════════════

        Private Shared Function BuildResult(p As ZeroLossExperimentParams, trades As List(Of TradeRecord)) As ZeroLossExperimentResult
            Dim r As New ZeroLossExperimentResult With {.Params = p}

            r.TotalTrades = trades.Count
            If trades.Count = 0 Then Return r

            Dim wins = trades.Where(Function(t) t.PnlPct > 0).ToList()
            r.Wins = wins.Count
            r.Losses = trades.Count - wins.Count
            r.WinRate = CSng(wins.Count) / trades.Count * 100.0F
            r.AvgPnl = trades.Average(Function(t) t.PnlPct)
            r.TotalPnl = trades.Sum(Function(t) t.PnlPct)
            r.BestTrade = trades.Max(Function(t) t.PnlPct)
            r.WorstTrade = trades.Min(Function(t) t.PnlPct)

            r.TargetExits = trades.Where(Function(t) t.ExitReason = "Target").Count()
            r.StopExits = trades.Where(Function(t) t.ExitReason = "StopLoss").Count()
            r.TimeExits = trades.Where(Function(t) t.ExitReason = "TimeExit").Count()
            r.EodExits = trades.Where(Function(t) t.ExitReason = "EOD").Count()

            r.CompositeScore = r.AvgPnl * (r.WinRate / 100.0F)
            Return r
        End Function

        ' ══════════════════════════════════════════
        ' Research DB Access (mysql CLI) — ZeroLossBatchAnalyzer 동일 패턴
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

        ' ── 내부 모델 ──
        Private Class MinuteBar
            Public Dt As DateTime
            Public Open As Single
            Public High As Single
            Public Low As Single
            Public Close As Single
            Public Volume As Long
            Public TradeAmount As Long
        End Class

        Private Class TradeRecord
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
