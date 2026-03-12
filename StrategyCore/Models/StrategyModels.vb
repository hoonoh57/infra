Imports System
Imports System.Collections.Generic

Namespace StrategyCore.Models
    Public Enum StrategyVersionType
        Base
        Derived
        AlphaCandidate
    End Enum

    Public Enum TradeMode
        Intraday
        Swing
    End Enum

    Public Class CostModel
        Public Property BuyCommissionRate As Double = 0.00015
        Public Property SellCommissionRate As Double = 0.00015
        Public Property SellTaxRate As Double = 0.0018
        Public Property SlippageRate As Double = 0
    End Class

    Public Class ExecutionConstraints
        Public Property SingleTradeOnly As Boolean = True
        Public Property MaxEntriesPerSymbol As Integer = 1
        Public Property AllowReentry As Boolean = False
        Public Property ForceFlatAtSessionEnd As Boolean = True
    End Class

    Public Class StrategyIndicatorDefinition
        Public Property IndicatorType As String = ""
        Public Property Timeframe As String = ""
        Public Property Enabled As Boolean = True
        Public Property Parameters As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)
    End Class

    Public Class StrategyDraft
        Public Property StrategyId As String = Guid.NewGuid().ToString("N")
        Public Property Name As String = ""
        Public Property Prompt As String = ""
        Public Property TradeMode As TradeMode = TradeMode.Intraday
        Public Property TargetProfitRate As Double = 0.02
        Public Property Timeframes As New List(Of String)()
        Public Property Indicators As New List(Of StrategyIndicatorDefinition)()
    End Class

    Public Class StrategyDefinition
        Public Property StrategyLineId As String = Guid.NewGuid().ToString("N")
        Public Property StrategyVersionId As String = Guid.NewGuid().ToString("N")
        Public Property ParentVersionId As String = ""
        Public Property VersionTag As String = "V1"
        Public Property VersionType As StrategyVersionType = StrategyVersionType.Base
        Public Property ChangeSummary As String = ""
        Public Property ImmutableHash As String = ""
        Public Property StrategyId As String = Guid.NewGuid().ToString("N")
        Public Property Name As String = ""
        Public Property Version As Integer = 1
        Public Property Prompt As String = ""
        Public Property TradeMode As TradeMode = TradeMode.Intraday
        Public Property TargetProfitRate As Double = 0.02
        Public Property EntrySummary As String = ""
        Public Property ExitSummary As String = ""
        Public Property Timeframes As New List(Of String)()
        Public Property Indicators As New List(Of StrategyIndicatorDefinition)()
        Public Property CostModel As New CostModel()
        Public Property Constraints As New ExecutionConstraints()
    End Class

    Public Class LabCandle
        Public Property [Time] As DateTime
        Public Property Open As Double
        Public Property High As Double
        Public Property Low As Double
        Public Property Close As Double
        Public Property Volume As Double
    End Class

    Public Class BacktestTrade
        Public Property Symbol As String = ""
        Public Property EntryTime As DateTime
        Public Property ExitTime As DateTime
        Public Property EntryPrice As Double
        Public Property ExitPrice As Double
        Public Property NetReturnRate As Double
        Public Property HitTargetProfit As Boolean
        Public Property EntryScore As Integer
        Public Property EntryReasons As New List(Of String)()
        Public Property ExitReason As String = ""
        Public Property MaxFavorableExcursionRate As Double
        Public Property MaxAdverseExcursionRate As Double
        Public Property Notes As String = ""
    End Class

    Public Class StrategyBaselineReport
        Public Property EvaluatedAt As DateTime = DateTime.Now
        Public Property Symbol As String = ""
        Public Property PrimaryMetric As Double
        Public Property SecondaryMetric As Double
        Public Property AverageReturnRate As Double
        Public Property MaxDrawdownRate As Double
        Public Property WinRate As Double
        Public Property SampleCount As Integer
        Public Property TradeCount As Integer
        Public Property TargetHitCount As Integer
        Public Property MissedTargetCount As Integer
        Public Property FailedExampleSummary As String = ""
        Public Property StrengthSummary As String = ""
        Public Property WeaknessSummary As String = ""
        Public Property Candles As New List(Of LabCandle)()
        Public Property Trades As New List(Of BacktestTrade)()
    End Class

    Public Class StrategyValidationSummary
        Public Property AverageReturnRate As Double
        Public Property PrimaryMetricName As String = "TargetProfitHitRateAfterCost"
        Public Property PrimaryMetricValue As Double
        Public Property SecondaryMetricName As String = "NetReturnAfterCost"
        Public Property SecondaryMetricValue As Double
        Public Property SampleCount As Integer
        Public Property ValidatedFrom As DateTime
        Public Property ValidatedTo As DateTime
    End Class

    Public Class StrategyDiagnosisItem
        Public Property Category As String = ""
        Public Property Severity As String = ""
        Public Property Observation As String = ""
        Public Property Recommendation As String = ""
    End Class

    Public Class StrategyDiagnosisReport
        Public Property Summary As String = ""
        Public Property Strengths As New List(Of String)()
        Public Property Weaknesses As New List(Of String)()
        Public Property Items As New List(Of StrategyDiagnosisItem)()
    End Class

    Public Class StrategyImprovementSuggestion
        Public Property Category As String = ""
        Public Property Priority As String = ""
        Public Property PriorityOrder As Integer
        Public Property Title As String = ""
        Public Property Action As String = ""
        Public Property TemplateName As String = ""
        Public Property ExpectedEffect As String = ""
        Public Property PromptHint As String = ""
    End Class

    Public Class StrategyImprovementPlan
        Public Property Summary As String = ""
        Public Property PrimaryCategory As String = ""
        Public Property Suggestions As New List(Of StrategyImprovementSuggestion)()
    End Class

    Public Class StrategyPackage
        Public Property PackageVersion As String = "1.0"
        Public Property StrategyId As String = ""
        Public Property Name As String = ""
        Public Property Version As Integer = 1
        Public Property Status As String = "Validated"
        Public Property StrategyType As String = ""
        Public Property CreatedAt As DateTime
        Public Property PromotedAt As DateTime
        Public Property PromotedBy As String = ""
        Public Property Prompt As String = ""
        Public Property EntrySummary As String = ""
        Public Property ExitSummary As String = ""
        Public Property Timeframes As New List(Of String)()
        Public Property IndicatorSet As New List(Of StrategyIndicatorDefinition)()
        Public Property CostModel As New CostModel()
        Public Property ExecutionConstraints As New ExecutionConstraints()
        Public Property ValidationSummary As New StrategyValidationSummary()
        Public Property HashAlgorithm As String = "SHA256"
        Public Property Hash As String = ""
    End Class

    Public Class PromotionManifest
        Public Property StrategyId As String = ""
        Public Property Version As Integer
        Public Property ApprovedBy As String = ""
        Public Property ApprovedAt As DateTime
        Public Property PrimaryMetric As Double
        Public Property SecondaryMetric As Double
        Public Property Notes As String = ""
        Public Property PackageHash As String = ""
    End Class

    Public Class StrategyLabResult
        Public Property Draft As StrategyDraft
        Public Property Definition As StrategyDefinition
        Public Property Report As StrategyBaselineReport
        Public Property Diagnosis As StrategyDiagnosisReport
        Public Property ImprovementPlan As StrategyImprovementPlan
    End Class
End Namespace
