Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports [Shared]
Imports StrategyCore.Models

Namespace StrategyCore.Services
    Public Class BaselineEvaluationService
        Private ReadOnly _candleProvider As ICandleDataProvider
        Private ReadOnly _auxDataProvider As IStrategyIndicatorAuxDataProvider

        Public Sub New(Optional candleProvider As ICandleDataProvider = Nothing,
                       Optional auxDataProvider As IStrategyIndicatorAuxDataProvider = Nothing)
            _candleProvider = candleProvider
            _auxDataProvider = auxDataProvider
        End Sub

        Private Class EvaluatedSignal
            Public Property Index As Integer
            Public Property Score As Integer
            Public Property Reasons As New List(Of String)()
        End Class

        Private Class EvaluationContext
            Public Property Closes As List(Of Double)
            Public Property Volumes As List(Of Double)
            Public Property Jma As List(Of Double)
            Public Property Vwap As List(Of Double)
            Public Property VolumeMa20 As List(Of Double)
            Public Property VolumeSlope As List(Of Double)
            Public Property MacdLine As List(Of Double)
            Public Property MacdSignal As List(Of Double)
            Public Property Rsi As List(Of Double)
            Public Property SuperTrend As List(Of Double)
            Public Property Obv As List(Of Double)
            Public Property ObvSignal As List(Of Double)
            Public Property TickIntensity As List(Of Double)
            Public Property TickIntensityMa5 As List(Of Double)
            Public Property RelativeStrength As List(Of Double)
            Public Property RelativeStrengthThreshold As List(Of Double)
            Public Property OverheadResistanceRate As List(Of Double)
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

            Dim context = BuildEvaluationContext(definition, symbol, candles, timeframe, fromDate, safeCount)
            Dim signals = FindEntrySignals(definition, candles, context)
            Dim nextAllowedIndex As Integer = 0
            For Each signal In signals
                If signal Is Nothing OrElse signal.Index < nextAllowedIndex Then Continue For

                Dim trade = BuildTrade(definition, symbol, candles, signal, context)
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

            report.FailedExampleSummary = BuildFailedExampleSummaryV2(failedExample)
            report.ToxicTradeSummary = BuildToxicTradeSummaryV2(report.Trades)
            report.StrengthSummary = BuildStrengthSummary(definition, report)
            report.WeaknessSummary = BuildWeaknessSummaryV2(definition, report)
            Return report
        End Function

        Private Shared Function FindEntrySignals(definition As StrategyDefinition,
                                                 candles As List(Of LabCandle),
                                                 context As EvaluationContext) As List(Of EvaluatedSignal)
            Dim results As New List(Of EvaluatedSignal)()
            If definition Is Nothing OrElse candles Is Nothing OrElse candles.Count < 5 Then Return results

            Dim startIndex = Math.Max(2, ResolveEntryWarmup(definition))

            For i = startIndex To candles.Count - 2
                Dim reasons As New List(Of String)()
                Dim score As Integer = 0
                Dim qualified As Boolean = True

                If definition.RequireJmaTurnUpEntry Then
                    If IsJmaTurnUp(context.Jma, i) Then
                        score += 2
                        reasons.Add("JMA turn up")
                    Else
                        Continue For
                    End If
                End If

                If definition.RequireRelativeStrengthFilter Then
                    Dim rsThreshold = ResolveRelativeStrengthThreshold(definition, context, i)
                    Dim previousRs = If(context.RelativeStrength IsNot Nothing AndAlso i > 0 AndAlso context.RelativeStrength.Count > i - 1, context.RelativeStrength(i - 1), Double.MinValue)
                    If context.RelativeStrength Is Nothing OrElse context.RelativeStrength.Count <= i OrElse
                       context.RelativeStrength(i) < rsThreshold OrElse
                       previousRs < rsThreshold OrElse
                       context.RelativeStrength(i) < previousRs Then
                        Continue For
                    End If
                    score += 1
                    reasons.Add($"RelativeStrength hold {context.RelativeStrength(i):P2} >= {rsThreshold:P2}")
                End If

                If definition.RequireLightOverheadResistance Then
                    Dim maxResistance = If(definition.MaxOverheadResistanceRate.HasValue, definition.MaxOverheadResistanceRate.Value, 0.03R)
                    If context.OverheadResistanceRate Is Nothing OrElse context.OverheadResistanceRate.Count <= i OrElse context.OverheadResistanceRate(i) > maxResistance Then
                        Continue For
                    End If
                    score += 1
                    reasons.Add($"OverheadResistance {context.OverheadResistanceRate(i):P2}")
                End If

                For Each indicator In definition.Indicators
                    If indicator Is Nothing OrElse Not indicator.Enabled Then Continue For
                    Dim matched As Boolean = False

                    Select Case indicator.IndicatorType
                        Case "MACD"
                            If context.MacdLine(i) > context.MacdSignal(i) AndAlso context.MacdLine(i - 1) <= context.MacdSignal(i - 1) Then
                                score += 2
                                reasons.Add("MACD cross up")
                                matched = True
                            ElseIf context.MacdLine(i) > context.MacdSignal(i) Then
                                score += 1
                                reasons.Add("MACD above signal")
                                matched = True
                            End If
                        Case "RSI"
                            Dim minimumRsi = If(definition.MinimumRsi.HasValue, definition.MinimumRsi.Value, 45.0R)
                            If context.Rsi(i) >= minimumRsi AndAlso context.Rsi(i) <= 80 Then
                                score += 1
                                reasons.Add($"RSI {context.Rsi(i):N1} >= {minimumRsi:N1}")
                                matched = True
                            End If
                        Case "JMA"
                            If candles(i).Close >= context.Jma(i) Then
                                score += 1
                                reasons.Add("JMA trend support")
                                matched = True
                            End If
                        Case "SuperTrend"
                            If candles(i).Close >= context.SuperTrend(i) Then
                                score += 1
                                reasons.Add("SuperTrend bullish")
                                matched = True
                            End If
                        Case "VWAP"
                            If candles(i).Close >= context.Vwap(i) Then
                                score += 1
                                reasons.Add("VWAP reclaim")
                                matched = True
                            End If
                        Case "Volume"
                            If context.Volumes(i) >= context.VolumeMa20(i) Then
                                score += 1
                                reasons.Add("Volume above average")
                                matched = True
                            End If
                        Case "VolumeMA"
                            If context.Volumes(i) >= context.VolumeMa20(i) Then
                                score += 1
                                reasons.Add("Volume20 confirmed")
                                matched = True
                            End If
                        Case "VolumeMASlope"
                            If context.VolumeSlope(i) > 0 Then
                                score += 1
                                reasons.Add("Volume20 slope positive")
                                matched = True
                            End If
                        Case "OBV"
                            If context.Obv(i) >= context.ObvSignal(i) AndAlso context.Obv(i) > context.Obv(Math.Max(0, i - 1)) Then
                                score += 1
                                reasons.Add($"OBV {context.Obv(i):N0} >= Signal {context.ObvSignal(i):N0}")
                                matched = True
                            End If
                        Case "TickIntensity"
                            Dim minimumTickIntensity = If(definition.MinimumTickIntensity.HasValue, definition.MinimumTickIntensity.Value, 0.0R)
                            If context.TickIntensity IsNot Nothing AndAlso context.TickIntensity.Count > i AndAlso
                               context.TickIntensityMa5 IsNot Nothing AndAlso context.TickIntensityMa5.Count > i AndAlso
                               context.TickIntensity(i) >= minimumTickIntensity AndAlso
                               context.TickIntensity(i) > context.TickIntensityMa5(i) Then
                                score += 1
                                reasons.Add($"TickIntensity {context.TickIntensity(i):N2} >= {minimumTickIntensity:N2} and > Avg5")
                                matched = True
                            End If
                    End Select

                    If Not matched Then
                        qualified = False
                        Exit For
                    End If
                Next

                If qualified AndAlso score > 0 Then
                    results.Add(New EvaluatedSignal With {
                        .Index = i,
                        .Score = score,
                        .Reasons = reasons
                    })
                End If
            Next

            Return results
        End Function

        Private Shared Function ResolveEntryWarmup(definition As StrategyDefinition) As Integer
            Dim warmup As Integer = 2
            If definition Is Nothing OrElse definition.Indicators Is Nothing Then
                Return warmup
            End If

            For Each indicator In definition.Indicators
                If indicator Is Nothing OrElse Not indicator.Enabled Then Continue For

                Select Case indicator.IndicatorType
                    Case "MACD"
                        warmup = Math.Max(warmup, 35)
                    Case "RSI", "JMA"
                        warmup = Math.Max(warmup, 14)
                    Case "Volume", "VolumeMA", "VolumeMASlope", "OBV"
                        warmup = Math.Max(warmup, 20)
                    Case "TickIntensity"
                        warmup = Math.Max(warmup, 5)
                    Case "SuperTrend", "VWAP"
                        warmup = Math.Max(warmup, 10)
                End Select
            Next

            If definition.RequireLightOverheadResistance Then
                warmup = Math.Max(warmup, 5)
            End If

            Return warmup
        End Function

        Private Shared Function BuildTrade(definition As StrategyDefinition,
                                           symbol As String,
                                           candles As List(Of LabCandle),
                                           entrySignal As EvaluatedSignal,
                                           context As EvaluationContext) As BacktestTrade
            Dim entryIdx = Math.Max(0, Math.Min(candles.Count - 2, entrySignal.Index))
            Dim entryPrice = candles(entryIdx).Close
            Dim exitIdx = DetermineExitIndex(definition, candles, entryIdx, context)
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
                .EntryRsi = SafeGet(context.Rsi, entryIdx),
                .EntryTickIntensity = SafeGet(context.TickIntensity, entryIdx),
                .EntryTickIntensityMa5 = SafeGet(context.TickIntensityMa5, entryIdx),
                .EntryRelativeStrength = SafeGet(context.RelativeStrength, entryIdx),
                .EntryObv = SafeGet(context.Obv, entryIdx),
                .EntryObvSignal = SafeGet(context.ObvSignal, entryIdx),
                .ExitReason = exitReason,
                .MaxFavorableExcursionRate = mfe,
                .MaxAdverseExcursionRate = mae,
                .ToxicClass = ResolveToxicClass(candles(entryIdx).Time, netReturn, mfe, mae, entrySignal.Reasons),
                .ToxicReason = ResolveToxicReason(candles(entryIdx).Time, netReturn, mfe, mae, entrySignal.Reasons),
                .Notes = BuildTradeNotes(entrySignal.Reasons, context, entryIdx, exitReason, mfe, mae)
            }
        End Function

        Private Shared Function DetermineExitIndex(definition As StrategyDefinition,
                                                   candles As List(Of LabCandle),
                                                   entryIdx As Integer,
                                                   context As EvaluationContext) As Integer
            Dim totalCost = definition.CostModel.BuyCommissionRate + definition.CostModel.SellCommissionRate + definition.CostModel.SellTaxRate + definition.CostModel.SlippageRate
            Dim targetGross = definition.TargetProfitRate + totalCost
            Dim entryPrice = candles(entryIdx).Close

            For i = entryIdx + 1 To candles.Count - 1
                Dim grossReturn = If(entryPrice > 0, (candles(i).Close - entryPrice) / entryPrice, 0)
                Dim superTrendBullish = candles(i).Close >= context.SuperTrend(i)
                Dim jmaTurnDown = IsJmaTurnDown(context.Jma, i)

                If definition.ExitOnJmaTurnDownAfterTarget Then
                    If grossReturn >= targetGross AndAlso jmaTurnDown Then Return i
                    If definition.HoldBelowTargetWhileSuperTrendBullish AndAlso grossReturn < targetGross AndAlso superTrendBullish Then
                        Continue For
                    End If
                    If jmaTurnDown AndAlso Not superTrendBullish Then Return i
                Else
                    If grossReturn >= targetGross Then Return i
                End If

                If candles(i).Close < context.SuperTrend(i) Then Return i
                If context.MacdLine(i) < context.MacdSignal(i) AndAlso context.MacdLine(i - 1) >= context.MacdSignal(i - 1) Then Return i
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

        Private Function BuildEvaluationContext(definition As StrategyDefinition,
                                                symbol As String,
                                                candles As List(Of LabCandle),
                                                timeframe As String,
                                                fromDate As DateTime,
                                                barCount As Integer) As EvaluationContext
            Dim closes = candles.Select(Function(c) c.Close).ToList()
            Dim volumes = candles.Select(Function(c) c.Volume).ToList()
            Dim macd = ComputeMacd(closes, 12, 26, 9)
            Dim obv = ComputeObv(candles)
            Dim tickIntensity As List(Of Double) = Enumerable.Repeat(0.0R, candles.Count).ToList()
            Dim tickIntensityMa5 As List(Of Double) = Enumerable.Repeat(0.0R, candles.Count).ToList()
            Dim relativeStrength As List(Of Double) = Enumerable.Repeat(0.0R, candles.Count).ToList()
            Dim relativeStrengthThreshold As List(Of Double) = Enumerable.Repeat(0.0R, candles.Count).ToList()
            Dim overheadResistanceRate As List(Of Double) = Enumerable.Repeat(0.0R, candles.Count).ToList()

            If definition IsNot Nothing AndAlso
               definition.Indicators.Any(Function(ind) ind IsNot Nothing AndAlso String.Equals(ind.IndicatorType, "TickIntensity", StringComparison.OrdinalIgnoreCase)) AndAlso
               _auxDataProvider IsNot Nothing Then
                Dim tickTimestamps = _auxDataProvider.GetTickTimestamps(symbol, timeframe, fromDate, barCount)
                tickIntensity = ComputeTickIntensity(candles, tickTimestamps, timeframe)
                tickIntensityMa5 = ComputeSimpleMovingAverage(tickIntensity, 5)
            End If

            If definition IsNot Nothing AndAlso definition.RequireRelativeStrengthFilter AndAlso _candleProvider IsNot Nothing Then
                Dim kospiCandles = _candleProvider.GetCandles("U001", timeframe, fromDate, barCount).ToList()
                Dim kosdaqCandles = _candleProvider.GetCandles("U201", timeframe, fromDate, barCount).ToList()
                relativeStrength = ComputeRelativeStrength(candles, kospiCandles, kosdaqCandles, definition.RelativeStrengthBenchmark)
                relativeStrengthThreshold = ComputeRelativeStrengthThresholds(candles, kospiCandles, kosdaqCandles, definition.RelativeStrengthThreshold, definition.RelativeStrengthBenchmark)
            End If

            If definition IsNot Nothing AndAlso definition.RequireLightOverheadResistance AndAlso _candleProvider IsNot Nothing Then
                Dim dailyFrom = fromDate.Date.AddDays(-20)
                Dim dailyCandles = _candleProvider.GetCandles(symbol, "d", dailyFrom, Math.Max(30, definition.OverheadResistanceLookbackDays + 10)).ToList()
                overheadResistanceRate = ComputeOverheadResistanceRates(candles, dailyCandles, Math.Max(1, definition.OverheadResistanceLookbackDays))
            End If

            Return New EvaluationContext With {
                .Closes = closes,
                .Volumes = volumes,
                .Jma = ComputeEma(closes, 14),
                .Vwap = ComputeVwap(candles),
                .VolumeMa20 = ComputeSimpleMovingAverage(volumes, 20),
                .VolumeSlope = ComputeSlope(ComputeSimpleMovingAverage(volumes, 20), 3),
                .MacdLine = macd.Item1,
                .MacdSignal = macd.Item2,
                .Rsi = ComputeRsi(closes, 14),
                .SuperTrend = ComputeSuperTrendProxy(candles, 10, 3),
                .Obv = obv,
                .ObvSignal = ComputeSimpleMovingAverage(obv, 20),
                .TickIntensity = tickIntensity,
                .TickIntensityMa5 = tickIntensityMa5,
                .RelativeStrength = relativeStrength,
                .RelativeStrengthThreshold = relativeStrengthThreshold,
                .OverheadResistanceRate = overheadResistanceRate
            }
        End Function

        Private Shared Function IsJmaTurnUp(values As List(Of Double), index As Integer) As Boolean
            If values Is Nothing OrElse index < 2 OrElse index >= values.Count Then Return False
            Return values(index) > values(index - 1) AndAlso values(index - 1) <= values(index - 2)
        End Function

        Private Shared Function IsJmaTurnDown(values As List(Of Double), index As Integer) As Boolean
            If values Is Nothing OrElse index < 2 OrElse index >= values.Count Then Return False
            Return values(index) < values(index - 1) AndAlso values(index - 1) >= values(index - 2)
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

        Private Shared Function ResolveRelativeStrengthThreshold(definition As StrategyDefinition,
                                                                 context As EvaluationContext,
                                                                 index As Integer) As Double
            If definition IsNot Nothing AndAlso definition.RelativeStrengthThreshold.HasValue Then
                Return definition.RelativeStrengthThreshold.Value
            End If

            If context Is Nothing OrElse context.RelativeStrengthThreshold Is Nothing OrElse context.RelativeStrengthThreshold.Count <= index Then
                Return 0.03R
            End If

            Return context.RelativeStrengthThreshold(index)
        End Function

        Private Shared Function ComputeRelativeStrength(stockCandles As List(Of LabCandle),
                                                        kospiCandles As List(Of LabCandle),
                                                        kosdaqCandles As List(Of LabCandle),
                                                        benchmark As String) As List(Of Double)
            Dim stockReturns = ComputeReturnsSinceCapture(stockCandles)
            Dim kospiReturns = ComputeAlignedReturns(stockCandles, kospiCandles)
            Dim kosdaqReturns = ComputeAlignedReturns(stockCandles, kosdaqCandles)
            Dim results As New List(Of Double)(stockCandles.Count)

            For i = 0 To stockCandles.Count - 1
                Dim benchmarkReturn = ResolveBenchmarkReturn(benchmark, kospiReturns, kosdaqReturns, i)
                results.Add(stockReturns(i) - benchmarkReturn)
            Next

            Return results
        End Function

        Private Shared Function ComputeRelativeStrengthThresholds(stockCandles As List(Of LabCandle),
                                                                  kospiCandles As List(Of LabCandle),
                                                                  kosdaqCandles As List(Of LabCandle),
                                                                  fixedThreshold As Double?,
                                                                  benchmark As String) As List(Of Double)
            If fixedThreshold.HasValue Then
                Return Enumerable.Repeat(fixedThreshold.Value, stockCandles.Count).ToList()
            End If

            Dim kospiReturns = ComputeAlignedReturns(stockCandles, kospiCandles)
            Dim kosdaqReturns = ComputeAlignedReturns(stockCandles, kosdaqCandles)
            Dim results As New List(Of Double)(stockCandles.Count)

            For i = 0 To stockCandles.Count - 1
                Dim benchmarkReturn = ResolveBenchmarkReturn(benchmark, kospiReturns, kosdaqReturns, i)
                If benchmarkReturn >= 0.015R Then
                    results.Add(0.05R)
                ElseIf benchmarkReturn >= 0.005R Then
                    results.Add(0.04R)
                Else
                    results.Add(0.03R)
                End If
            Next

            Return results
        End Function

        Private Shared Function ComputeOverheadResistanceRates(stockCandles As List(Of LabCandle),
                                                               dailyCandles As List(Of LabCandle),
                                                               lookbackDays As Integer) As List(Of Double)
            Dim orderedDaily = If(dailyCandles, New List(Of LabCandle)()).
                OrderBy(Function(c) c.Time).
                ToList()
            Dim results As New List(Of Double)(stockCandles.Count)

            For Each stockCandle In stockCandles
                Dim priorDaily = orderedDaily.
                    Where(Function(c) c.Time.Date < stockCandle.Time.Date).
                    Reverse().
                    Take(lookbackDays).
                    ToList()

                If priorDaily.Count = 0 OrElse stockCandle.Close <= 0 Then
                    results.Add(0.0R)
                    Continue For
                End If

                Dim maxHigh = priorDaily.Max(Function(c) c.High)
                Dim overhead = Math.Max(0.0R, (maxHigh - stockCandle.Close) / stockCandle.Close)
                results.Add(overhead)
            Next

            Return results
        End Function

        Private Shared Function SafeGet(values As List(Of Double), index As Integer) As Double?
            If values Is Nothing OrElse index < 0 OrElse index >= values.Count Then Return Nothing
            Return values(index)
        End Function

        Private Shared Function ComputeReturnsSinceCapture(candles As List(Of LabCandle)) As List(Of Double)
            Dim results As New List(Of Double)(candles.Count)
            If candles Is Nothing OrElse candles.Count = 0 Then Return results

            Dim baseClose = Math.Max(0.0001R, candles(0).Close)
            For Each candle In candles
                results.Add((candle.Close - baseClose) / baseClose)
            Next

            Return results
        End Function

        Private Shared Function ComputeAlignedReturns(stockCandles As List(Of LabCandle),
                                                      benchmarkCandles As List(Of LabCandle)) As List(Of Double)
            Dim results As New List(Of Double)(stockCandles.Count)
            If stockCandles Is Nothing OrElse stockCandles.Count = 0 Then Return results
            If benchmarkCandles Is Nothing OrElse benchmarkCandles.Count = 0 Then
                Return Enumerable.Repeat(0.0R, stockCandles.Count).ToList()
            End If

            Dim orderedBenchmark = benchmarkCandles.OrderBy(Function(c) c.Time).ToList()
            Dim baseClose = Math.Max(0.0001R, orderedBenchmark(0).Close)
            Dim benchmarkIndex = 0

            For Each stockCandle In stockCandles
                While benchmarkIndex < orderedBenchmark.Count - 1 AndAlso orderedBenchmark(benchmarkIndex + 1).Time <= stockCandle.Time
                    benchmarkIndex += 1
                End While

                Dim benchmarkClose = orderedBenchmark(benchmarkIndex).Close
                results.Add((benchmarkClose - baseClose) / baseClose)
            Next

            Return results
        End Function

        Private Shared Function ResolveBenchmarkReturn(benchmark As String,
                                                       kospiReturns As List(Of Double),
                                                       kosdaqReturns As List(Of Double),
                                                       index As Integer) As Double
            Dim safeKospi = If(kospiReturns IsNot Nothing AndAlso kospiReturns.Count > index, kospiReturns(index), 0.0R)
            Dim safeKosdaq = If(kosdaqReturns IsNot Nothing AndAlso kosdaqReturns.Count > index, kosdaqReturns(index), 0.0R)

            Select Case If(benchmark, "").ToUpperInvariant()
                Case "U001"
                    Return safeKospi
                Case "U201"
                    Return safeKosdaq
                Case Else
                    Return Math.Max(safeKospi, safeKosdaq)
            End Select
        End Function

        Private Shared Function ResolveToxicClass(entryTime As DateTime,
                                                  netReturn As Double,
                                                  mfe As Double,
                                                  mae As Double,
                                                  reasons As List(Of String)) As String
            If netReturn >= 0 Then Return ""

            Dim hourMinute = entryTime.Hour * 60 + entryTime.Minute
            Dim isOpenWindow = hourMinute <= (9 * 60 + 30)
            Dim hasRelativeStrength = reasons IsNot Nothing AndAlso reasons.Any(Function(reason) reason.IndexOf("RelativeStrength", StringComparison.OrdinalIgnoreCase) >= 0)

            If isOpenWindow AndAlso mfe < 0.01R AndAlso mae <= -0.02R Then Return "OpenWhipsaw"
            If hasRelativeStrength AndAlso mfe < 0.01R Then Return "RelativeStrengthFade"
            If mfe >= 0.01R AndAlso mae <= -0.03R Then Return "BreakoutFailure"
            If mfe < 0.005R AndAlso mae <= -0.015R Then Return "WeakFollowThrough"
            Return ""
        End Function

        Private Shared Function ResolveToxicReason(entryTime As DateTime,
                                                   netReturn As Double,
                                                   mfe As Double,
                                                   mae As Double,
                                                   reasons As List(Of String)) As String
            Dim toxicClass = ResolveToxicClass(entryTime, netReturn, mfe, mae, reasons)
            Select Case toxicClass
                Case "OpenWhipsaw"
                    Return "open-session entry failed without follow-through"
                Case "RelativeStrengthFade"
                    Return "relative strength was high but faded before extension"
                Case "BreakoutFailure"
                    Return "breakout excursion was reversed into deep adverse move"
                Case "WeakFollowThrough"
                    Return "entry never gained enough follow-through after trigger"
                Case Else
                    Return ""
            End Select
        End Function

        Private Shared Function BuildTradeNotes(entryReasons As List(Of String),
                                                context As EvaluationContext,
                                                entryIndex As Integer,
                                                exitReason As String,
                                                mfe As Double,
                                                mae As Double) As String
            Dim entryText = If(entryReasons Is Nothing OrElse entryReasons.Count = 0,
                               "Entry[none]",
                               $"Entry[{String.Join(" + ", entryReasons)}]")
            Dim metrics As New List(Of String)()

            Dim rs = SafeGet(context.RelativeStrength, entryIndex)
            If rs.HasValue Then metrics.Add($"RS={rs.Value:P2}")

            Dim rsi = SafeGet(context.Rsi, entryIndex)
            If rsi.HasValue Then metrics.Add($"RSI={rsi.Value:N1}")

            Dim tick = SafeGet(context.TickIntensity, entryIndex)
            If tick.HasValue Then metrics.Add($"Tick={tick.Value:N2}")

            Dim tickAvg5 = SafeGet(context.TickIntensityMa5, entryIndex)
            If tickAvg5.HasValue Then metrics.Add($"TickAvg5={tickAvg5.Value:N2}")

            Dim obv = SafeGet(context.Obv, entryIndex)
            Dim obvSignal = SafeGet(context.ObvSignal, entryIndex)
            If obv.HasValue AndAlso obvSignal.HasValue Then
                metrics.Add($"OBV={obv.Value:N0}")
                metrics.Add($"OBVSignal={obvSignal.Value:N0}")
            End If

            Dim metricText = If(metrics.Count = 0, "", $" | Metrics[{String.Join(", ", metrics)}]")
            Return $"{entryText}{metricText} | Exit[{exitReason}] | MFE[{mfe:P2}] | MAE[{mae:P2}]"
        End Function

        Private Shared Function BuildWeaknessSummaryV2(definition As StrategyDefinition, report As StrategyBaselineReport) As String
            If report Is Nothing OrElse report.TradeCount = 0 Then
                Return "진입 신호가 없어 조건이 과도하게 엄격하거나 현재 구간과 맞지 않을 수 있습니다."
            End If

            If report.TargetHitCount = report.TradeCount Then
                Return "현재 평가구간에서는 모든 신호가 목표수익에 도달했지만, 다른 종목과 기간으로 확장 검증이 필요합니다."
            End If

            Dim exampleText = If(String.IsNullOrWhiteSpace(report.FailedExampleSummary), "", " " & report.FailedExampleSummary)
            Dim toxicText = If(String.IsNullOrWhiteSpace(report.ToxicTradeSummary), "", " " & report.ToxicTradeSummary)
            Return $"목표수익 {definition.TargetProfitRate:P1} 미달 신호가 {report.MissedTargetCount}건입니다. 평균 순수익 {report.AverageReturnRate:P2}.{exampleText}{toxicText}"
        End Function

        Private Shared Function BuildFailedExampleSummaryV2(trade As BacktestTrade) As String
            If trade Is Nothing Then Return ""

            Dim toxicSuffix = If(String.IsNullOrWhiteSpace(trade.ToxicClass), "", $", 독성유형 {trade.ToxicClass} ({trade.ToxicReason})")
            Return $"예시 실패 구간: {trade.EntryTime:MM-dd HH:mm} 진입 후 {trade.ExitTime:HH:mm} 청산, 순수익 {trade.NetReturnRate:P2}, MFE {trade.MaxFavorableExcursionRate:P2}, MAE {trade.MaxAdverseExcursionRate:P2}, 이유 {String.Join(" + ", trade.EntryReasons)}{toxicSuffix}."
        End Function

        Private Shared Function BuildToxicTradeSummaryV2(trades As List(Of BacktestTrade)) As String
            If trades Is Nothing OrElse trades.Count = 0 Then Return ""

            Dim toxicTrades = trades.
                Where(Function(trade) Not String.IsNullOrWhiteSpace(trade.ToxicClass)).
                GroupBy(Function(trade) trade.ToxicClass).
                OrderByDescending(Function(group) group.Count()).
                ToList()

            If toxicTrades.Count = 0 Then Return ""

            Dim topGroup = toxicTrades(0)
            Return $"주요 독성매매 유형은 {topGroup.Key} {topGroup.Count()}건입니다."
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

        Private Shared Function ComputeObv(candles As List(Of LabCandle)) As List(Of Double)
            Dim results As New List(Of Double)
            If candles Is Nothing OrElse candles.Count = 0 Then Return results

            Dim current As Double = Math.Max(0.0R, candles(0).Volume)
            results.Add(current)
            For i = 1 To candles.Count - 1
                If candles(i).Close > candles(i - 1).Close Then
                    current += candles(i).Volume
                ElseIf candles(i).Close < candles(i - 1).Close Then
                    current -= candles(i).Volume
                End If
                results.Add(current)
            Next
            Return results
        End Function

        Private Shared Function ComputeTickIntensity(candles As List(Of LabCandle),
                                                     tickTimestamps As IReadOnlyList(Of DateTime),
                                                     timeframe As String) As List(Of Double)
            Dim results As New List(Of Double)
            If candles Is Nothing OrElse candles.Count = 0 Then Return results

            Dim minuteUnit = ResolveMinuteUnit(timeframe)
            If minuteUnit <= 0 Then
                For i = 0 To candles.Count - 1
                    results.Add(0.0R)
                Next
                Return results
            End If

            Dim rawTicks = If(tickTimestamps, Array.Empty(Of DateTime)()).
                Where(Function(ts) ts <> DateTime.MinValue).
                OrderBy(Function(ts) ts).
                ToList()
            Dim tickIndex As Integer = 0

            For Each candle In candles
                Dim startTime = candle.Time
                Dim endTime = startTime.AddMinutes(minuteUnit)
                Dim count As Integer = 0

                While tickIndex < rawTicks.Count AndAlso rawTicks(tickIndex) < startTime
                    tickIndex += 1
                End While

                Dim scanIndex = tickIndex
                While scanIndex < rawTicks.Count AndAlso rawTicks(scanIndex) < endTime
                    count += 1
                    scanIndex += 1
                End While

                results.Add(count)
            Next

            Return results
        End Function

        Private Shared Function ResolveMinuteUnit(timeframe As String) As Integer
            Dim normalized = If(timeframe, "").Trim().ToLowerInvariant()
            If normalized.StartsWith("m", StringComparison.OrdinalIgnoreCase) Then
                Dim value As Integer = 1
                If normalized.Length > 1 Then Integer.TryParse(normalized.Substring(1), value)
                Return Math.Max(1, value)
            End If
            Return 0
        End Function
    End Class
End Namespace
