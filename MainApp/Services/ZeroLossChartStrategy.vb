' ═══════════════════════════════════════════════════════════════
' ZeroLossChartStrategy.vb — Zero Loss 차트 시각화 전략
' ═══════════════════════════════════════════════════════════════
'
' "What You See Is What You Trade"
'
' IStrategy 구현체로 차트에 적용하여 매수/매도 시그널을 시각화.
' ZeroLossLiveStrategy(실전매매)와 동일한 로직이지만,
' 전체 기간의 과거 캔들에 대해서도 시뮬레이션하여 화살표로 표시.
' 각 거래일마다 독립적으로 시가/누적거래대금/진입횟수를 리셋.
'
' ★ 진입 조건:
'   1) 당일 시가 대비 7%+ 상승 (OC)
'   2) 누적 거래대금 100억+ (Amt)
'   3) 최초 조건 충족 시 1회만 진입
'
' ★ 퇴출 조건:
'   1) Stop-Loss: 진입가 대비 -3%
'   2) Target: 진입가 대비 +10%
'   3) Time: 14:50 일괄 청산
'
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Generic
Imports System.Linq

Namespace Services
    Public Class ZeroLossChartStrategy
        Implements IStrategy

        ' ── 파라미터 (ZeroLossLiveStrategy와 동일) ──
        Private Const OC_THRESHOLD As Single = 7.0F
        Private Const AMT_THRESHOLD_EOK As Single = 100.0F
        Private Const STOP_LOSS_PCT As Single = -3.0F
        Private Const TARGET_PROFIT_PCT As Single = 10.0F
        Private Const MAX_POSITIONS As Integer = 5
        Private Const STRATEGY_NAME As String = "ZeroLoss"

        ' ── 청산 시각 ──
        Private Shared ReadOnly SCAN_START As New TimeSpan(9, 1, 0)
        Private Shared ReadOnly SCAN_END As New TimeSpan(14, 30, 0)
        Private Shared ReadOnly FINAL_EXIT As New TimeSpan(14, 50, 0)

        Public ReadOnly Property Name As String Implements IStrategy.Name
            Get
                Return "ZeroLoss"
            End Get
        End Property

        Public ReadOnly Property DisplayName As String Implements IStrategy.DisplayName
            Get
                Return "Zero Loss (OC7% S-3% T+10%)"
            End Get
        End Property

        Public Function RequiredIndicators() As List(Of String) Implements IStrategy.RequiredIndicators
            Return New List(Of String)()  ' OHLCV만 사용, 별도 지표 불필요
        End Function

        Public Function Evaluate(stockCode As String,
                                 candles As List(Of CandleItem),
                                 indicatorResults As Dictionary(Of String, List(Of IndicatorResult))) As List(Of StrategySignal) Implements IStrategy.Evaluate

            Dim signals As New List(Of StrategySignal)()
            If candles Is Nothing OrElse candles.Count < 2 Then Return signals

            ' ── 거래일별로 그룹핑하여 각 날짜를 독립 시뮬레이션 ──
            Dim dayGroups As New Dictionary(Of Date, Integer)()  ' Date → 시작 인덱스
            For i = 0 To candles.Count - 1
                Dim d = candles(i).Dt.Date
                If Not dayGroups.ContainsKey(d) Then
                    dayGroups(d) = i
                End If
            Next

            For Each kvp In dayGroups.OrderBy(Function(x) x.Key)
                Dim dayDate = kvp.Key
                Dim dayStartIdx = kvp.Value

                ' 해당 일의 마지막 인덱스 찾기
                Dim dayEndIdx = dayStartIdx
                For i = dayStartIdx To candles.Count - 1
                    If candles(i).Dt.Date = dayDate Then
                        dayEndIdx = i
                    Else
                        Exit For
                    End If
                Next

                ' 해당 일 시뮬레이션
                SimulateDay(stockCode, candles, dayStartIdx, dayEndIdx, signals)
            Next

            Return signals
        End Function

        Private Sub SimulateDay(stockCode As String, candles As List(Of CandleItem),
                                dayStart As Integer, dayEnd As Integer,
                                signals As List(Of StrategySignal))

            ' 당일 첫 캔들의 시가 = 당일 시가
            Dim dayOpen = candles(dayStart).Open
            If dayOpen <= 0 Then Return

            ' ── 시뮬레이션 상태 (매일 리셋) ──
            Dim entryPrice As Single = 0
            Dim inPosition As Boolean = False
            Dim todayEntryCount As Integer = 0
            Dim cumulativeAmount As Long = 0

            ' ── 각 캔들을 순회하며 진입/퇴출 평가 ──
            For i = dayStart To dayEnd
                Dim c = candles(i)
                Dim timeOfDay = c.Dt.TimeOfDay

                ' 누적 거래대금 (원)
                cumulativeAmount += c.TradeAmount
                If c.TradeAmount = 0 Then
                    cumulativeAmount += CLng(c.Close) * c.Volume
                End If

                ' ── 포지션 보유 중 → 퇴출 체크 ──
                If inPosition Then
                    ' Stop-Loss
                    If c.Low > 0 AndAlso ((c.Low / entryPrice - 1.0F) * 100.0F) <= STOP_LOSS_PCT Then
                        signals.Add(New StrategySignal With {
                            .StockCode = stockCode,
                            .StrategyName = STRATEGY_NAME,
                            .SignalType = SignalType.Sell,
                            .Price = entryPrice * (1.0F + STOP_LOSS_PCT / 100.0F),
                            .Reason = $"손절 {STOP_LOSS_PCT}%",
                            .Confidence = 1.0F,
                            .Timestamp = c.Dt
                        })
                        inPosition = False
                        Continue For
                    End If

                    ' Target Profit
                    If c.High > 0 AndAlso ((c.High / entryPrice - 1.0F) * 100.0F) >= TARGET_PROFIT_PCT Then
                        signals.Add(New StrategySignal With {
                            .StockCode = stockCode,
                            .StrategyName = STRATEGY_NAME,
                            .SignalType = SignalType.StrongSell,
                            .Price = entryPrice * (1.0F + TARGET_PROFIT_PCT / 100.0F),
                            .Reason = $"익절 +{TARGET_PROFIT_PCT}%",
                            .Confidence = 1.0F,
                            .Timestamp = c.Dt
                        })
                        inPosition = False
                        Continue For
                    End If

                    ' 14:50 일괄 청산
                    If timeOfDay >= FINAL_EXIT Then
                        signals.Add(New StrategySignal With {
                            .StockCode = stockCode,
                            .StrategyName = STRATEGY_NAME,
                            .SignalType = SignalType.Sell,
                            .Price = c.Close,
                            .Reason = "14:50 청산",
                            .Confidence = 0.8F,
                            .Timestamp = c.Dt
                        })
                        inPosition = False
                        Continue For
                    End If

                    Continue For  ' 보유 중이면 진입 로직 스킵
                End If

                ' ── 진입 조건 평가 ──
                If timeOfDay < SCAN_START OrElse timeOfDay > SCAN_END Then Continue For
                If todayEntryCount >= MAX_POSITIONS Then Continue For

                ' 조건 1: 시가 대비 상승률 >= OC%
                Dim openChange = (c.Close / dayOpen - 1.0F) * 100.0F
                If openChange < OC_THRESHOLD Then Continue For

                ' 조건 2: 누적 거래대금 >= 100억
                Dim amtEok = CSng(cumulativeAmount) / 100_000_000.0F
                If amtEok < AMT_THRESHOLD_EOK Then Continue For

                ' ★ 진입 시그널
                entryPrice = c.Close
                inPosition = True
                todayEntryCount += 1

                signals.Add(New StrategySignal With {
                    .StockCode = stockCode,
                    .StrategyName = STRATEGY_NAME,
                    .SignalType = SignalType.Buy,
                    .Price = entryPrice,
                    .Reason = $"OC={openChange:+0.0}% Amt={amtEok:N0}억",
                    .Confidence = 0.9F,
                    .Timestamp = c.Dt
                })
            Next

            ' ── 장 마감 시 보유 중이면 마지막 캔들에서 청산 표시 ──
            If inPosition Then
                Dim lastCandle = candles(dayEnd)
                signals.Add(New StrategySignal With {
                    .StockCode = stockCode,
                    .StrategyName = STRATEGY_NAME,
                    .SignalType = SignalType.Sell,
                    .Price = lastCandle.Close,
                    .Reason = "장마감 청산",
                    .Confidence = 0.8F,
                    .Timestamp = lastCandle.Dt
                })
            End If
        End Sub
    End Class
End Namespace
