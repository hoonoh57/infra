Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports [Shared]
Imports StrategyCore.Models

Namespace StrategyCore.Services
    Public Class BaselineEvaluationService
        Private ReadOnly _candleProvider As ICandleDataProvider

        Public Sub New(Optional candleProvider As ICandleDataProvider = Nothing)
            _candleProvider = candleProvider
        End Sub

        Private Class EvaluatedSignal
            Public Property Index As Integer
            Public Property Score As Integer
            Public Property Reasons As New List(Of String)()
        End Class

        Public Function Evaluate(definition As StrategyDefinition,
                                 symbol As String,
                                 fromDate As DateTime,
                                 barCount As Integer) As StrategyBaselineReport
            Dim safeCount = Math.Max(60, barCount)
            Dim timeframe = ResolvePrimaryTimeframe(definition)
            If _candleProvider Is Nothing Then
                Throw New InvalidOperationException("No real candle data provider is configured for StrategyLab evaluation.")
            End If

            Dim candles = _candleProvider.GetCandles(symbol, timeframe, fromDate, safeCount).ToList()
            If candles Is Nothing OrElse candles.Count = 0 Then
                Throw New InvalidOperationException($"No real candles were loaded for {symbol} {timeframe}.")
            End If

            Dim report As New StrategyBaselineReport With {
                .Symbol = symbol,
                .SampleCount = candles.Count,
                .EvaluatedAt = DateTime.Now
            }
            report.Candles.AddRange(candles)

            Dim signals = FindEntrySignals(definition, candles)
            Dim nextAllowedIndex As Integer = 0
            For Each signal In signals
                If signal Is Nothing OrElse signal.Index < nextAllowedIndex Then Continue For

                Dim trade = BuildTrade(definition, symbol, candles, signal)
                report.Trades.Add(trade)
                nextAllowedIndex = ResolveNextAllowedIndex(candles, trade.ExitTime)
            Next

            report.TradeCount = report.Trades.Count
            report.TargetHitCount = report.Trades.Where(Function(trade) trade.HitTargetProfit).Count()
            report.MissedTargetCount = Math.Max(0, report.TradeCount - report.TargetHitCount)
            report.PrimaryMetric = If(report.TradeCount > 0, report.TargetHitCount / CDbl(report.TradeCount), 0.0R)
            report.SecondaryMetric = If(report.TradeCount > 0, report.Trades.Average(Function(trade) trade.NetReturnRate), 0.0R)
            report.AverageReturnRate = report.SecondaryMetric
            report.MaxDrawdownRate = ComputeMaxDrawdown(candles)
            report.WinRate = If(report.TradeCount > 0, report.Trades.Where(Function(trade) trade.NetReturnRate > 0).Count() / CDbl(report.TradeCount), 0.0R)

            Dim failedExample = report.Trades.
                Where(Function(trade) Not trade.HitTargetProfit).
                OrderBy(Function(trade) trade.NetReturnRate).
                ThenByDescending(Function(trade) Math.Abs(trade.MaxAdverseExcursionRate)).
                FirstOrDefault()

            report.FailedExampleSummary = BuildFailedExampleSummary(failedExample)
            report.StrengthSummary = BuildStrengthSummary(definition, report)
            report.WeaknessSummary = BuildWeaknessSummary(definition, report)
            Return report
        End Function

        Private Shared Function FindEntrySignals(definition As StrategyDefinition, candles As List(Of LabCandle)) As List(Of EvaluatedSignal)
            Dim results As New List(Of EvaluatedSignal)()
            If definition Is Nothing OrElse candles Is Nothing OrElse candles.Count < 5 Then Return results

            Dim closes = candles.Select(Function(c) c.Close).ToList()
            Dim volumes = candles.Select(Function(c) c.Volume).ToList()
            Dim ema14 = ComputeEma(closes, 14)
            Dim vwap = ComputeVwap(candles)
            Dim volumeMa20 = ComputeSimpleMovingAverage(volumes, 20)
            Dim volumeSlope = ComputeSlope(volumeMa20, 3)
            Dim macd = ComputeMacd(closes, 12, 26, 9)
            Dim rsi = ComputeRsi(closes, 14)
            Dim superTrend = ComputeSuperTrendProxy(candles, 10, 3)
            Dim requiredScore = Math.Max(1, definition.Indicators.Count)
            Dim startIndex = Math.Max(5, candles.Count \ 8)

            For i = startIndex To candles.Count - 2
                Dim reasons As New List(Of String)()
                Dim score As Integer = 0

                For Each indicator In definition.Indicators
                    If indicator Is Nothing OrElse Not indicator.Enabled Then Continue For

                    Select Case indicator.IndicatorType
                        Case "MACD"
                            If macd.Item1(i) > macd.Item2(i) AndAlso macd.Item1(i - 1) <= macd.Item2(i - 1) Then
                                score += 2
                                reasons.Add("MACD cross up")
                            ElseIf macd.Item1(i) > macd.Item2(i) Then
                                score += 1
                                reasons.Add("MACD above signal")
                            End If
                        Case "RSI"
                            If rsi(i) >= 45 AndAlso rsi(i) <= 70 Then
                                score += 1
                                reasons.Add($"RSI {rsi(i):N1}")
                            End If
                        Case "JMA"
                            If closes(i) >= ema14(i) Then
                                score += 1
                                reasons.Add("JMA trend support")
                            End If
                        Case "SuperTrend"
                            If closes(i) >= superTrend(i) Then
                                score += 1
                                reasons.Add("SuperTrend bullish")
                            End If
                        Case "VWAP"
                            If closes(i) >= vwap(i) Then
                                score += 1
                                reasons.Add("VWAP reclaim")
                            End If
                        Case "Volume"
                            If volumes(i) >= volumeMa20(i) Then
                                score += 1
                                reasons.Add("Volume above average")
                            End If
                        Case "VolumeMA"
                            If volumes(i) >= volumeMa20(i) Then
                                score += 1
                                reasons.Add("Volume20 confirmed")
                            End If
                        Case "VolumeMASlope"
                            If volumeSlope(i) > 0 Then
                                score += 1
                                reasons.Add("Volume20 slope positive")
                            End If
                    End Select
                Next

                If score >= requiredScore Then
                    results.Add(New EvaluatedSignal With {
                        .Index = i,
                        .Score = score,
                        .Reasons = reasons
                    })
                End If
            Next

            Return results
        End Function

        Private Shared Function BuildTrade(definition As StrategyDefinition,
                                           symbol As String,
                                           candles As List(Of LabCandle),
                                           entrySignal As EvaluatedSignal) As BacktestTrade
            Dim entryIdx = Math.Max(0, Math.Min(candles.Count - 2, entrySignal.Index))
            Dim entryPrice = candles(entryIdx).Close
            Dim exitIdx = DetermineExitIndex(definition, candles, entryIdx)
            Dim exitPrice = candles(exitIdx).Close
            Dim grossReturn = If(entryPrice > 0, (exitPrice - entryPrice) / entryPrice, 0)
            Dim totalCost = definition.CostModel.BuyCommissionRate + definition.CostModel.SellCommissionRate + definition.CostModel.SellTaxRate + definition.CostModel.SlippageRate
            Dim netReturn = grossReturn - totalCost
            Dim hitTarget = netReturn >= definition.TargetProfitRate
            Dim exitReason = BuildExitReason(definition, candles, entryIdx, exitIdx, hitTarget)
            Dim mfe = ComputeMaxFavorableExcursion(candles, entryIdx, exitIdx, entryPrice)
            Dim mae = ComputeMaxAdverseExcursion(candles, entryIdx, exitIdx, entryPrice)

            Return New BacktestTrade With {
                .Symbol = symbol,
                .EntryTime = candles(entryIdx).Time,
                .ExitTime = candles(exitIdx).Time,
                .EntryPrice = entryPrice,
                .ExitPrice = exitPrice,
                .NetReturnRate = netReturn,
                .HitTargetProfit = hitTarget,
                .EntryScore = entrySignal.Score,
                .EntryReasons = New List(Of String)(entrySignal.Reasons),
                .ExitReason = exitReason,
                .MaxFavorableExcursionRate = mfe,
                .MaxAdverseExcursionRate = mae,
                .Notes = $"Entry[{String.Join(" + ", entrySignal.Reasons)}] | Exit[{exitReason}] | MFE[{mfe:P2}] | MAE[{mae:P2}]"
            }
        End Function

        Private Shared Function DetermineExitIndex(definition As StrategyDefinition,
                                                   candles As List(Of LabCandle),
                                                   entryIdx As Integer) As Integer
            Dim totalCost = definition.CostModel.BuyCommissionRate + definition.CostModel.SellCommissionRate + definition.CostModel.SellTaxRate + definition.CostModel.SlippageRate
            Dim targetGross = definition.TargetProfitRate + totalCost
            Dim entryPrice = candles(entryIdx).Close
            Dim closes = candles.Select(Function(c) c.Close).ToList()
            Dim macd = ComputeMacd(closes, 12, 26, 9)
            Dim superTrend = ComputeSuperTrendProxy(candles, 10, 3)

            For i = entryIdx + 1 To candles.Count - 1
                Dim grossReturn = If(entryPrice > 0, (candles(i).Close - entryPrice) / entryPrice, 0)
                If grossReturn >= targetGross Then Return i
                If candles(i).Close < superTrend(i) Then Return i
                If macd.Item1(i) < macd.Item2(i) AndAlso macd.Item1(i - 1) >= macd.Item2(i - 1) Then Return i
            Next

            Return candles.Count - 1
        End Function

        Private Shared Function BuildExitReason(definition As StrategyDefinition,
                                                candles As List(Of LabCandle),
                                                entryIdx As Integer,
                                                exitIdx As Integer,
                                                hitTarget As Boolean) As String
            If hitTarget Then
                Return $"target {definition.TargetProfitRate:P1} reached"
            End If
            If exitIdx >= candles.Count - 1 Then
                Return "session end close"
            End If
            Return $"protective exit after signal fade ({candles(exitIdx).Time:HH:mm})"
        End Function

        Private Shared Function ResolveNextAllowedIndex(candles As List(Of LabCandle), exitTime As DateTime) As Integer
            If candles Is Nothing OrElse candles.Count = 0 Then Return 0
            For i = 0 To candles.Count - 1
                If candles(i).Time > exitTime Then Return i
            Next
            Return candles.Count
        End Function

        Private Shared Function ResolvePrimaryTimeframe(definition As StrategyDefinition) As String
            If definition Is Nothing OrElse definition.Timeframes Is Nothing OrElse definition.Timeframes.Count = 0 Then
                Return RuntimeChartSettings.DefaultCandleTimeframe
            End If
            Return definition.Timeframes(0)
        End Function

        Private Shared Function ComputeMaxDrawdown(candles As List(Of LabCandle)) As Double
            Dim peak = Double.MinValue
            Dim maxDd = 0.0R
            For Each candle In candles
                If candle.Close > peak Then peak = candle.Close
                If peak > 0 Then
                    Dim dd = (candle.Close - peak) / peak
                    If dd < maxDd Then maxDd = dd
                End If
            Next
            Return maxDd
        End Function

        Private Shared Function BuildStrengthSummary(definition As StrategyDefinition, report As StrategyBaselineReport) As String
            If report Is Nothing OrElse report.TradeCount = 0 Then
                Return "평가구간에서 진입 조건을 만족한 신호가 없어, 현재 조건 세트의 진입 가능성을 먼저 점검해야 합니다."
            End If

            Dim bestTrade = report.Trades.OrderByDescending(Function(trade) trade.NetReturnRate).FirstOrDefault()
            If report.TargetHitCount > 0 AndAlso bestTrade IsNot Nothing Then
                Return $"{definition.Name}: {report.TradeCount}건 중 {report.TargetHitCount}건이 목표수익에 도달했습니다. 최고 사례 {bestTrade.EntryTime:MM-dd HH:mm} -> {bestTrade.ExitTime:HH:mm}, 순수익 {bestTrade.NetReturnRate:P2}."
            End If

            Return $"{definition.Name}: 진입 신호는 {report.TradeCount}건 포착됐지만 목표수익 달성 사례는 없습니다. 평균 순수익 {report.AverageReturnRate:P2}."
        End Function

        Private Shared Function BuildWeaknessSummary(definition As StrategyDefinition, report As StrategyBaselineReport) As String
            If report Is Nothing OrElse report.TradeCount = 0 Then
                Return "진입 신호가 전혀 없어 조건이 과도하게 엄격하거나, 사용 지표 조합이 현재 구간과 맞지 않을 수 있습니다."
            End If

            If report.TargetHitCount = report.TradeCount Then
                Return "현재 평가구간에서는 전 신호가 목표수익에 도달했지만, 더 긴 기간과 다른 종목으로 확장 검증이 필요합니다."
            End If

            Dim exampleText = If(String.IsNullOrWhiteSpace(report.FailedExampleSummary), "", " " & report.FailedExampleSummary)
            Return $"목표수익 {definition.TargetProfitRate:P1} 미달 신호가 {report.MissedTargetCount}건입니다. 평균 순수익 {report.AverageReturnRate:P2}.{exampleText}"
        End Function

        Private Shared Function BuildFailedExampleSummary(trade As BacktestTrade) As String
            If trade Is Nothing Then Return ""
            Return $"예시 실패 구간: {trade.EntryTime:MM-dd HH:mm} 진입 후 {trade.ExitTime:HH:mm} 청산, 순수익 {trade.NetReturnRate:P2}, MFE {trade.MaxFavorableExcursionRate:P2}, MAE {trade.MaxAdverseExcursionRate:P2}, 이유 {String.Join(" + ", trade.EntryReasons)}."
        End Function

        Private Shared Function ComputeMaxFavorableExcursion(candles As List(Of LabCandle), entryIdx As Integer, exitIdx As Integer, entryPrice As Double) As Double
            If candles Is Nothing OrElse candles.Count = 0 OrElse entryPrice <= 0 Then Return 0
            Dim best As Double = Double.MinValue
            For i = entryIdx To Math.Min(exitIdx, candles.Count - 1)
                Dim excursion = (candles(i).High - entryPrice) / entryPrice
                If excursion > best Then best = excursion
            Next
            Return If(best = Double.MinValue, 0, best)
        End Function

        Private Shared Function ComputeMaxAdverseExcursion(candles As List(Of LabCandle), entryIdx As Integer, exitIdx As Integer, entryPrice As Double) As Double
            If candles Is Nothing OrElse candles.Count = 0 OrElse entryPrice <= 0 Then Return 0
            Dim worst As Double = Double.MaxValue
            For i = entryIdx To Math.Min(exitIdx, candles.Count - 1)
                Dim excursion = (candles(i).Low - entryPrice) / entryPrice
                If excursion < worst Then worst = excursion
            Next
            Return If(worst = Double.MaxValue, 0, worst)
        End Function

        Private Shared Function ComputeSimpleMovingAverage(values As List(Of Double), period As Integer) As List(Of Double)
            Dim results As New List(Of Double)
            If values Is Nothing OrElse values.Count = 0 Then Return results

            Dim safePeriod = Math.Max(1, period)
            Dim window As New Queue(Of Double)
            Dim sum As Double = 0
            For Each value In values
                window.Enqueue(value)
                sum += value
                If window.Count > safePeriod Then
                    sum -= window.Dequeue()
                End If
                results.Add(sum / window.Count)
            Next
            Return results
        End Function

        Private Shared Function ComputeSlope(values As List(Of Double), lookback As Integer) As List(Of Double)
            Dim results As New List(Of Double)
            If values Is Nothing OrElse values.Count = 0 Then Return results

            Dim safeLookback = Math.Max(1, lookback)
            For i = 0 To values.Count - 1
                Dim baseIndex = Math.Max(0, i - safeLookback)
                results.Add(values(i) - values(baseIndex))
            Next
            Return results
        End Function

        Private Shared Function ComputeEma(values As List(Of Double), period As Integer) As List(Of Double)
            Dim results As New List(Of Double)
            If values Is Nothing OrElse values.Count = 0 Then Return results

            Dim safePeriod = Math.Max(1, period)
            Dim multiplier = 2.0R / (safePeriod + 1.0R)
            Dim ema = values(0)
            For Each value In values
                ema = ((value - ema) * multiplier) + ema
                results.Add(ema)
            Next
            Return results
        End Function

        Private Shared Function ComputeMacd(values As List(Of Double), fastPeriod As Integer, slowPeriod As Integer, signalPeriod As Integer) As Tuple(Of List(Of Double), List(Of Double))
            Dim fast = ComputeEma(values, fastPeriod)
            Dim slow = ComputeEma(values, slowPeriod)
            Dim macd As New List(Of Double)
            For i = 0 To values.Count - 1
                macd.Add(fast(i) - slow(i))
            Next
            Dim signal = ComputeEma(macd, signalPeriod)
            Return Tuple.Create(macd, signal)
        End Function

        Private Shared Function ComputeRsi(values As List(Of Double), period As Integer) As List(Of Double)
            Dim results As New List(Of Double)
            If values Is Nothing OrElse values.Count = 0 Then Return results

            Dim safePeriod = Math.Max(2, period)
            Dim gains As Double = 0
            Dim losses As Double = 0
            results.Add(50)
            For i = 1 To values.Count - 1
                Dim changeValue = values(i) - values(i - 1)
                gains = ((gains * (safePeriod - 1)) + Math.Max(0, changeValue)) / safePeriod
                losses = ((losses * (safePeriod - 1)) + Math.Max(0, -changeValue)) / safePeriod
                If losses <= 0 Then
                    results.Add(100)
                Else
                    Dim rs = gains / losses
                    results.Add(100 - (100 / (1 + rs)))
                End If
            Next
            Return results
        End Function

        Private Shared Function ComputeVwap(candles As List(Of LabCandle)) As List(Of Double)
            Dim results As New List(Of Double)
            If candles Is Nothing OrElse candles.Count = 0 Then Return results

            Dim cumulativePriceVolume As Double = 0
            Dim cumulativeVolume As Double = 0
            For Each candle In candles
                Dim typicalPrice = (candle.High + candle.Low + candle.Close) / 3.0R
                cumulativePriceVolume += typicalPrice * Math.Max(1.0R, candle.Volume)
                cumulativeVolume += Math.Max(1.0R, candle.Volume)
                results.Add(cumulativePriceVolume / cumulativeVolume)
            Next
            Return results
        End Function

        Private Shared Function ComputeSuperTrendProxy(candles As List(Of LabCandle), atrPeriod As Integer, multiplier As Integer) As List(Of Double)
            Dim closes = candles.Select(Function(c) c.Close).ToList()
            Dim emaValues = ComputeEma(closes, Math.Max(2, atrPeriod))
            Dim results As New List(Of Double)
            For i = 0 To candles.Count - 1
                Dim bandOffset = Math.Max(1.0R, (candles(i).High - candles(i).Low) * Math.Max(1, multiplier) * 0.25R)
                results.Add(emaValues(i) - bandOffset)
            Next
            Return results
        End Function
    End Class
End Namespace
