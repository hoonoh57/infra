Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.RegularExpressions
Imports StrategyCore.Models

Namespace StrategyCore.Services
    Public Class StrategyPromptCompiler
        Private Shared ReadOnly IntradayFrames As String() = {"m1", "m3", "m5", "T30", "T60", "T120"}
        Private Shared ReadOnly SwingFrames As String() = {"m15", "m30", "m60"}

        Public Function CreateDraft(prompt As String, mode As TradeMode, targetProfitRate As Double) As StrategyDraft
            Dim normalized = If(prompt, "").Trim()
            Dim draft As New StrategyDraft With {
                .Name = BuildStrategyName(normalized, mode),
                .Prompt = normalized,
                .TradeMode = mode,
                .TargetProfitRate = targetProfitRate
            }

            draft.Timeframes.AddRange(ResolveTimeframes(normalized, mode))
            draft.Indicators.AddRange(ResolveIndicators(normalized, draft.Timeframes.FirstOrDefault()))
            Return draft
        End Function

        Public Function Compile(prompt As String, mode As TradeMode, targetProfitRate As Double, costModel As CostModel) As StrategyDefinition
            Dim draft = CreateDraft(prompt, mode, targetProfitRate)
            Dim strategy As New StrategyDefinition With {
                .StrategyId = draft.StrategyId,
                .Name = draft.Name,
                .Prompt = draft.Prompt,
                .TradeMode = draft.TradeMode,
                .TargetProfitRate = draft.TargetProfitRate,
                .CostModel = costModel,
                .EntrySummary = BuildEntrySummary(draft),
                .ExitSummary = BuildExitSummary(draft),
                .RequireJmaTurnUpEntry = HasJmaTurnUpEntryRule(draft.Prompt),
                .HoldBelowTargetWhileSuperTrendBullish = HasHoldBelowTargetRule(draft.Prompt),
                .ExitOnJmaTurnDownAfterTarget = HasExitOnJmaTurnDownAfterTargetRule(draft.Prompt),
                .RequireObvAboveSignal = HasObvAboveSignalRule(draft.Prompt),
                .RequireTickIntensityAboveMa5 = HasTickIntensityAboveMa5Rule(draft.Prompt),
                .MinimumTickIntensity = ResolveMinimumTickIntensity(draft.Prompt),
                .MinimumRsi = ResolveMinimumRsi(draft.Prompt),
                .RequireRelativeStrengthFilter = HasRelativeStrengthRule(draft.Prompt),
                .RelativeStrengthThreshold = ResolveRelativeStrengthThreshold(draft.Prompt),
                .RelativeStrengthBenchmark = ResolveRelativeStrengthBenchmark(draft.Prompt),
                .RequireLightOverheadResistance = HasLightOverheadResistanceRule(draft.Prompt),
                .MaxOverheadResistanceRate = ResolveMaxOverheadResistanceRate(draft.Prompt)
            }

            strategy.Timeframes.AddRange(draft.Timeframes)
            strategy.Indicators.AddRange(draft.Indicators)
            Return strategy
        End Function

        Private Shared Function BuildStrategyName(prompt As String, mode As TradeMode) As String
            Dim prefix = If(mode = TradeMode.Intraday, "LabIntraday", "LabSwing")
            If String.IsNullOrWhiteSpace(prompt) Then Return $"{prefix}_{DateTime.Now:HHmmss}"

            Dim token = prompt.Split({" "c, ","c, "."c}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            If String.IsNullOrWhiteSpace(token) Then token = "Prompt"
            token = New String(token.Where(Function(ch) Char.IsLetterOrDigit(ch)).Take(12).ToArray())
            If token = "" Then token = "Prompt"
            Return $"{prefix}_{token}_{DateTime.Now:HHmmss}"
        End Function

        Private Shared Function ResolveTimeframes(prompt As String, mode As TradeMode) As List(Of String)
            Dim source = If(prompt, "")
            Dim candidates = If(mode = TradeMode.Intraday, IntradayFrames, SwingFrames)
            Dim frames = candidates.Where(Function(tf) source.IndexOf(tf, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
            If frames.Count = 0 Then frames.Add(candidates(0))
            Return frames
        End Function

        Private Shared Function ResolveIndicators(prompt As String, defaultTimeframe As String) As List(Of StrategyIndicatorDefinition)
            Dim source = If(prompt, "").ToLowerInvariant()
            Dim indicators As New List(Of StrategyIndicatorDefinition)()

            AddIfMentioned(indicators, source, "volume", "Volume", defaultTimeframe)
            AddIfMentioned(indicators, source, "거래량", "Volume", defaultTimeframe)
            AddIfMentioned(indicators, source, "volume20", "VolumeMA", defaultTimeframe, ("period", 20))
            AddIfMentioned(indicators, source, "거래량20", "VolumeMA", defaultTimeframe, ("period", 20))
            AddIfMentioned(indicators, source, "기울기", "VolumeMASlope", defaultTimeframe, ("period", 20), ("slopeLookback", 3))
            AddIfMentioned(indicators, source, "macd", "MACD", defaultTimeframe, ("fast", 12), ("slow", 26), ("signal", 9))
            AddIfMentioned(indicators, source, "rsi", "RSI", defaultTimeframe, ("period", 14), ("upper", 70), ("lower", 30))
            AddIfMentioned(indicators, source, "jma", "JMA", defaultTimeframe, ("length", 14), ("phase", 50))
            AddIfMentioned(indicators, source, "supertrend", "SuperTrend", defaultTimeframe, ("atrPeriod", 10), ("multiplier", 3))
            AddIfMentioned(indicators, source, "obv", "OBV", defaultTimeframe, ("period", 20))
            AddIfMentioned(indicators, source, "틱강도", "TickIntensity", defaultTimeframe, ("maPeriod", 5))
            AddIfMentioned(indicators, source, "tickintensity", "TickIntensity", defaultTimeframe, ("maPeriod", 5))

            If indicators.Count = 0 Then
                indicators.Add(New StrategyIndicatorDefinition With {
                    .IndicatorType = "MACD",
                    .Timeframe = defaultTimeframe,
                    .Enabled = True,
                    .Parameters = New Dictionary(Of String, Double) From {{"fast", 12}, {"slow", 26}, {"signal", 9}}
                })
                indicators.Add(New StrategyIndicatorDefinition With {
                    .IndicatorType = "SuperTrend",
                    .Timeframe = defaultTimeframe,
                    .Enabled = True,
                    .Parameters = New Dictionary(Of String, Double) From {{"atrPeriod", 10}, {"multiplier", 3}}
                })
            End If

            Return indicators
        End Function

        Private Shared Sub AddIfMentioned(target As List(Of StrategyIndicatorDefinition),
                                          source As String,
                                          keyword As String,
                                          indicatorType As String,
                                          timeframe As String,
                                          ParamArray parameters As (String, Double)())
            If source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0 Then Return
            If target.Any(Function(existingIndicator) existingIndicator.IndicatorType.Equals(indicatorType, StringComparison.OrdinalIgnoreCase)) Then Return

            Dim item As New StrategyIndicatorDefinition With {
                .IndicatorType = indicatorType,
                .Timeframe = timeframe,
                .Enabled = True
            }

            For Each pair In parameters
                item.Parameters(pair.Item1) = pair.Item2
            Next

            target.Add(item)
        End Sub

        Private Shared Function BuildEntrySummary(draft As StrategyDraft) As String
            Dim frame = draft.Timeframes.FirstOrDefault()
            Dim leading = draft.Indicators.FirstOrDefault()
            If leading Is Nothing Then Return $"{frame} baseline entry"
            If HasJmaTurnUpEntryRule(draft.Prompt) Then
                Return $"{frame} JMA turn-up entry, target {draft.TargetProfitRate:P1}"
            End If
            Return $"{frame} {leading.IndicatorType} condition entry, target {draft.TargetProfitRate:P1}"
        End Function

        Private Shared Function BuildExitSummary(draft As StrategyDraft) As String
            If HasExitOnJmaTurnDownAfterTargetRule(draft.Prompt) Then
                Return $"exit on JMA turn-down after {draft.TargetProfitRate:P1} target"
            End If
            If HasHoldBelowTargetRule(draft.Prompt) Then
                Return $"hold below {draft.TargetProfitRate:P1} target while SuperTrend stays bullish"
            End If
            Return $"single-trade target {draft.TargetProfitRate:P1} or session-end exit"
        End Function

        Private Shared Function HasJmaTurnUpEntryRule(prompt As String) As Boolean
            Dim source = If(prompt, "").ToLowerInvariant()
            Return (source.Contains("jma 상승전환") OrElse source.Contains("jma turn up")) AndAlso
                   source.Contains("매수")
        End Function

        Private Shared Function HasHoldBelowTargetRule(prompt As String) As Boolean
            Dim source = If(prompt, "").ToLowerInvariant()
            Return source.Contains("supertrend") AndAlso
                   (source.Contains("매도자제") OrElse source.Contains("매도 자제")) AndAlso
                   source.Contains("미만")
        End Function

        Private Shared Function HasExitOnJmaTurnDownAfterTargetRule(prompt As String) As Boolean
            Dim source = If(prompt, "").ToLowerInvariant()
            Return (source.Contains("jma하락") OrElse
                    source.Contains("jma 하락") OrElse
                    source.Contains("jma하락전환") OrElse
                    source.Contains("jma 하락전환") OrElse
                    source.Contains("하락전환")) AndAlso
                   source.Contains("매도")
        End Function

        Private Shared Function HasObvAboveSignalRule(prompt As String) As Boolean
            Dim source = If(prompt, "").ToLowerInvariant()
            Return source.Contains("obv") AndAlso
                   (source.Contains("상승추세") OrElse source.Contains("obv > obvsignal") OrElse source.Contains("obv>obvsignal"))
        End Function

        Private Shared Function HasTickIntensityAboveMa5Rule(prompt As String) As Boolean
            Dim source = If(prompt, "").ToLowerInvariant()
            Return source.Contains("tickintensity> tickintensityavg5") OrElse
                   source.Contains("tickintensity>tickintensityavg5") OrElse
                   source.Contains("tickintensity > tickintensityavg5") OrElse
                   (source.Contains("틱강도") AndAlso source.Contains("5이평"))
        End Function

        Private Shared Function ResolveMinimumTickIntensity(prompt As String) As Double?
            Dim source = If(prompt, "")
            Dim lower = source.ToLowerInvariant()

            Dim englishComparator = Regex.Match(lower, "tickintensity\s*(?:>=|>)\s*(\d+(?:\.\d+)?)")
            If englishComparator.Success Then Return Double.Parse(englishComparator.Groups(1).Value)

            Dim englishNatural = Regex.Match(lower, "tickintensity\s*(\d+(?:\.\d+)?)\s*(?:이상)?")
            If englishNatural.Success Then Return Double.Parse(englishNatural.Groups(1).Value)

            Dim koreanNatural = Regex.Match(source, "틱강도\s*(\d+(?:\.\d+)?)\s*이상")
            If koreanNatural.Success Then Return Double.Parse(koreanNatural.Groups(1).Value)

            Return Nothing
        End Function

        Private Shared Function ResolveMinimumRsi(prompt As String) As Double?
            Dim source = If(prompt, "")
            Dim lower = source.ToLowerInvariant()

            Dim englishComparator = Regex.Match(lower, "rsi\s*(?:>=|>)\s*(\d+(?:\.\d+)?)")
            If englishComparator.Success Then Return Double.Parse(englishComparator.Groups(1).Value)

            Dim koreanNatural = Regex.Match(source, "rsi\s*가?\s*(\d+(?:\.\d+)?)\s*보다\s*크")
            If koreanNatural.Success Then Return Double.Parse(koreanNatural.Groups(1).Value)

            Return Nothing
        End Function

        Private Shared Function HasRelativeStrengthRule(prompt As String) As Boolean
            Dim source = If(prompt, "")
            Return source.Contains("지수대비") OrElse
                   source.Contains("지수 대비") OrElse
                   source.Contains("코스피대비") OrElse
                   source.Contains("코스피 대비") OrElse
                   source.Contains("코스닥대비") OrElse
                   source.Contains("코스닥 대비") OrElse
                   source.Contains("상대강도")
        End Function

        Private Shared Function ResolveRelativeStrengthThreshold(prompt As String) As Double?
            Dim source = If(prompt, "")
            If Not HasRelativeStrengthRule(source) Then Return Nothing

            Dim match = Regex.Match(source, "(\d+(?:\.\d+)?)\s*%")
            If match.Success Then
                Return Double.Parse(match.Groups(1).Value) / 100.0R
            End If

            Return Nothing
        End Function

        Private Shared Function ResolveRelativeStrengthBenchmark(prompt As String) As String
            Dim source = If(prompt, "")
            If source.Contains("코스닥") Then Return "U201"
            If source.Contains("코스피") Then Return "U001"
            Return "MARKET"
        End Function

        Private Shared Function HasLightOverheadResistanceRule(prompt As String) As Boolean
            Dim source = If(prompt, "")
            Return source.Contains("5일내 매물대") OrElse
                   source.Contains("5일내매물대") OrElse
                   source.Contains("상단 매물") OrElse
                   source.Contains("매물대가 없거나 얕")
        End Function

        Private Shared Function ResolveMaxOverheadResistanceRate(prompt As String) As Double?
            Dim source = If(prompt, "")
            If Not HasLightOverheadResistanceRule(source) Then Return Nothing

            Dim match = Regex.Match(source, "(\d+(?:\.\d+)?)\s*%")
            If match.Success Then
                Return Double.Parse(match.Groups(1).Value) / 100.0R
            End If

            Return 0.03R
        End Function
    End Class
End Namespace
