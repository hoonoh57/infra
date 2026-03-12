Imports System
Imports System.Collections.Generic
Imports System.Linq
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
                .ExitSummary = BuildExitSummary(draft)
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
            Dim indicators As New List(Of StrategyIndicatorDefinition)

            AddIfMentioned(indicators, source, "volume", "Volume", defaultTimeframe)
            AddIfMentioned(indicators, source, "거래량", "Volume", defaultTimeframe)
            AddIfMentioned(indicators, source, "volume20", "VolumeMA", defaultTimeframe, ("period", 20))
            AddIfMentioned(indicators, source, "거래량20", "VolumeMA", defaultTimeframe, ("period", 20))
            AddIfMentioned(indicators, source, "기울기", "VolumeMASlope", defaultTimeframe, ("period", 20), ("slopeLookback", 3))
            AddIfMentioned(indicators, source, "macd", "MACD", defaultTimeframe, ("fast", 12), ("slow", 26), ("signal", 9))
            AddIfMentioned(indicators, source, "rsi", "RSI", defaultTimeframe, ("period", 14), ("upper", 70), ("lower", 30))
            AddIfMentioned(indicators, source, "jma", "JMA", defaultTimeframe, ("length", 14), ("phase", 50))
            AddIfMentioned(indicators, source, "supertrend", "SuperTrend", defaultTimeframe, ("atrPeriod", 10), ("multiplier", 3))

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
            If leading Is Nothing Then Return $"{frame} 기준 단일 진입"
            Return $"{frame} {leading.IndicatorType} 중심 진입, 목표수익 {draft.TargetProfitRate:P1}"
        End Function

        Private Shared Function BuildExitSummary(draft As StrategyDraft) As String
            Return $"1회 매매 후 목표수익 {draft.TargetProfitRate:P1} 또는 세션 종료 청산"
        End Function
    End Class
End Namespace
