Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports StrategyCore.Models

Namespace StrategyCore.Services
    Public Class StrategyImprovementSuggestionService
        Public Function BuildPlan(definition As StrategyDefinition,
                                  report As StrategyBaselineReport,
                                  diagnosis As StrategyDiagnosisReport) As StrategyImprovementPlan
            Dim plan As New StrategyImprovementPlan()

            If diagnosis Is Nothing Then
                plan.Summary = "No diagnosis available."
                Return plan
            End If

            Dim suggestions As New List(Of StrategyImprovementSuggestion)()

            For Each item In diagnosis.Items
                suggestions.Add(CreateSuggestion(item))
            Next

            If suggestions.Count = 0 AndAlso report IsNot Nothing AndAlso report.SecondaryMetric <= 0 Then
                suggestions.Add(New StrategyImprovementSuggestion With {
                    .Category = "BaselineRecovery",
                    .Priority = "High",
                    .PriorityOrder = 1,
                    .Title = "Protect net return after costs",
                    .Action = "Tighten entry timing and add one exclusion rule for weak open conditions.",
                    .TemplateName = "RecoveryGuard",
                    .ExpectedEffect = "Reduce weak setups and lift net return after cost.",
                    .PromptHint = "add m5 volume20 supertrend confirmation to reduce weak open signals and improve net return after costs"
                })
            End If

            If definition IsNot Nothing AndAlso definition.Timeframes.Count = 1 Then
                suggestions.Add(New StrategyImprovementSuggestion With {
                    .Category = "TimeframeExpansion",
                    .Priority = "Medium",
                    .PriorityOrder = 2,
                    .Title = "Add one higher timeframe confirmation",
                    .Action = "Keep the current trigger timeframe and add one slower confirmation timeframe.",
                    .TemplateName = "HigherFrameConfirm",
                    .ExpectedEffect = "Reduce false positives from single timeframe entry.",
                    .PromptHint = $"keep {definition.Timeframes(0)} trigger and add {ResolveHigherFrame(definition)} rsi confirmation"
                })
            End If

            AddCompositeSuggestions(suggestions, diagnosis)

            plan.Suggestions = suggestions _
                .GroupBy(Function(item) item.Category, StringComparer.OrdinalIgnoreCase) _
                .Select(Function(group) group.OrderBy(Function(item) item.PriorityOrder).First()) _
                .OrderBy(Function(item) item.PriorityOrder) _
                .ThenBy(Function(item) item.Title, StringComparer.OrdinalIgnoreCase) _
                .ToList()

            plan.PrimaryCategory = If(plan.Suggestions.Count > 0, plan.Suggestions(0).Category, "")
            plan.Summary = $"Generated {plan.Suggestions.Count} prioritized suggestions. Primary focus: {If(plan.PrimaryCategory = "", "None", plan.PrimaryCategory)}."
            Return plan
        End Function

        Private Shared Function CreateSuggestion(item As StrategyDiagnosisItem) As StrategyImprovementSuggestion
            Dim suggestion As New StrategyImprovementSuggestion With {
                .Category = item.Category,
                .Priority = item.Severity,
                .PriorityOrder = GetPriorityOrder(item.Severity),
                .Title = item.Category,
                .Action = item.Recommendation,
                .TemplateName = "GenericImprove",
                .ExpectedEffect = "Refine the current prompt around the diagnosed weakness.",
                .PromptHint = item.Recommendation
            }

            Select Case item.Category
                Case "TargetProfitMiss"
                    suggestion.PriorityOrder = 1
                    suggestion.Title = "Improve hit rate for target profit"
                    suggestion.TemplateName = "TargetHitBoost"
                    suggestion.ExpectedEffect = "Raise post-cost target hit rate with extra confirmation."
                    suggestion.PromptHint = "add m5 rsi and volume20 confirmation to improve target profit hit rate"
                Case "FailedExample"
                    suggestion.PriorityOrder = 1
                    suggestion.Title = "Fix one concrete failed segment"
                    suggestion.TemplateName = "FailedExampleRepair"
                    suggestion.ExpectedEffect = "Turn the observed failed entry pattern into an explicit exclusion or confirmation rule."
                    suggestion.PromptHint = "keep current trigger but add m5 confirmation volume20 slope positive and supertrend bullish filter to avoid the failed example pattern"
                Case "NetReturnNegative"
                    suggestion.PriorityOrder = 1
                    suggestion.Title = "Protect returns after cost"
                    suggestion.TemplateName = "CostGuard"
                    suggestion.ExpectedEffect = "Reduce low-quality entries that fail after cost deduction."
                    suggestion.PromptHint = "add supertrend and volume20 slope filter to avoid negative net-return setups"
                Case "NoTrade"
                    suggestion.PriorityOrder = 1
                    suggestion.Title = "Loosen over-strict entry"
                    suggestion.TemplateName = "SignalRecovery"
                    suggestion.ExpectedEffect = "Recover signal frequency before optimizing target hit rate."
                    suggestion.PromptHint = "keep core indicators but remove one confirmation condition or widen rsi band to recover signal frequency"
                Case "Drawdown"
                    suggestion.PriorityOrder = 2
                    suggestion.Title = "Reduce drawdown on unstable moves"
                    suggestion.TemplateName = "DrawdownGuard"
                    suggestion.ExpectedEffect = "Cut unstable entries and reduce downside excursion."
                    suggestion.PromptHint = "add m5 supertrend guard and rsi filter to reduce drawdown on unstable moves"
                Case "EarlySessionSensitivity"
                    suggestion.PriorityOrder = 2
                    suggestion.Title = "Stabilize early-session entry"
                    suggestion.TemplateName = "OpenStability"
                    suggestion.ExpectedEffect = "Reduce early whipsaw sensitivity near the open."
                    suggestion.PromptHint = "keep current setup but add m5 confirmation and volume20 slope filter for early-session entry"
                Case "ThinIndicatorSet"
                    suggestion.PriorityOrder = 3
                    suggestion.Title = "Broaden confirmation inputs"
                    suggestion.TemplateName = "IndicatorBalance"
                    suggestion.ExpectedEffect = "Balance trend, momentum, and volume confirmation."
                    suggestion.PromptHint = "add volume20 volume20 slope and rsi as complementary confirmation inputs"
            End Select

            Return suggestion
        End Function

        Private Shared Sub AddCompositeSuggestions(target As List(Of StrategyImprovementSuggestion),
                                                   diagnosis As StrategyDiagnosisReport)
            Dim categories = New HashSet(Of String)(diagnosis.Items.Select(Function(item) item.Category), StringComparer.OrdinalIgnoreCase)

            If categories.Contains("NetReturnNegative") AndAlso categories.Contains("EarlySessionSensitivity") Then
                target.Add(New StrategyImprovementSuggestion With {
                    .Category = "OpenCostGuard",
                    .Priority = "High",
                    .PriorityOrder = 1,
                    .Title = "Combine open filter with cost protection",
                    .Action = "Add an opening stabilization filter and a stricter confirmation block together.",
                    .TemplateName = "OpenCostGuard",
                    .ExpectedEffect = "Reduce open-session false entries that become net negative after costs.",
                    .PromptHint = "keep current setup but add m5 confirmation volume20 slope and supertrend filter for open-session cost protection"
                })
            End If

            If categories.Contains("Drawdown") AndAlso categories.Contains("TargetProfitMiss") Then
                target.Add(New StrategyImprovementSuggestion With {
                    .Category = "RiskAdjustedTarget",
                    .Priority = "High",
                    .PriorityOrder = 1,
                    .Title = "Trade less but target cleaner moves",
                    .Action = "Tighten entry and only keep setups with stronger trend and momentum alignment.",
                    .TemplateName = "RiskAdjustedTarget",
                    .ExpectedEffect = "Improve hit rate while cutting unstable drawdown-heavy entries.",
                    .PromptHint = "add m5 supertrend rsi and volume20 confirmation to target cleaner moves with lower drawdown"
                })
            End If
        End Sub

        Private Shared Function GetPriorityOrder(priority As String) As Integer
            Select Case If(priority, "").Trim().ToLowerInvariant()
                Case "high"
                    Return 1
                Case "medium"
                    Return 2
                Case Else
                    Return 3
            End Select
        End Function

        Private Shared Function ResolveHigherFrame(definition As StrategyDefinition) As String
            If definition Is Nothing OrElse definition.Timeframes.Count = 0 Then Return "m5"

            Select Case definition.Timeframes(0).ToLowerInvariant()
                Case "m1"
                    Return "m3"
                Case "m3"
                    Return "m5"
                Case "m5"
                    Return "t30"
                Case "m15"
                    Return "m30"
                Case "m30"
                    Return "m60"
                Case Else
                    Return definition.Timeframes(0)
            End Select
        End Function
    End Class
End Namespace
