' ═══════════════════════════════════════════════════════════════
' StrategyModels.vb — 전략 데이터 모델 (C# 호환)
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Generic

Namespace Models
    Public Enum LogicalOperator
        [AND]
        [OR]
        [XOR]
    End Enum

    Public Enum ComparisonOperator
        Equal
        NotEqual
        Greater
        Less
        GreaterEqual
        LessEqual
        CrossUp
        CrossDown
    End Enum

    Public Class ConditionCell
        Public Property Id As String
        Public Property Description As String
        Public Property IndicatorA As String ' LeftSource
        Public Property [Operator] As ComparisonOperator
        Public Property IndicatorB As String ' RightSource
        Public Property ConstantValue As Double?
        Public Property IsActive As Boolean = True
        Public Property Offset As Integer = 0
        Public Property Lookback As Integer = 1
        Public Property IsInverted As Boolean = False

        Public Sub New()
        End Sub

        Public Sub New(id As String, desc As String, indA As String, opt As ComparisonOperator, indB As String, Optional val As Double? = Nothing)
            Me.Id = id
            Me.Description = desc
            Me.IndicatorA = indA
            Me.Operator = opt
            Me.IndicatorB = indB
            Me.ConstantValue = val
        End Sub
    End Class

    Public Class LogicGate
        Public Property Name As String
        Public Property [Operator] As LogicalOperator = LogicalOperator.AND
        Public Property Conditions As New List(Of ConditionCell)
        Public Property IsActive As Boolean = True

        Public Sub New()
        End Sub

        Public Sub New(name As String, opt As LogicalOperator, conds As List(Of ConditionCell))
            Me.Name = name
            Me.Operator = opt
            If conds IsNot Nothing Then Me.Conditions = conds
        End Sub
    End Class

    Public Class StrategyDefinition
        Public Property StrategyId As String = Guid.NewGuid().ToString("N")
        Public Property GroupId As String = ""
        Public Property BaseStrategyId As String = ""
        Public Property Version As Integer = 1
        Public Property DisplayOrder As Integer = 0
        Public Property Name As String
        Public Property Description As String
        Public Property NaturalLanguagePrompt As String
        Public Property BuyRules As New List(Of LogicGate)
        Public Property SellRules As New List(Of LogicGate)
        Public Property RequiredDataDays As Integer = 0
        Public Property IsActive As Boolean = True
        Public Property Mode As String = "Test" ' "Test" or "Live"

        Public Sub New()
        End Sub

        Public Sub New(name As String, desc As String, buy As List(Of LogicGate), sell As List(Of LogicGate), Optional nlPrompt As String = "")
            Me.Name = name
            Me.Description = desc
            Me.BuyRules = buy
            Me.SellRules = sell
            Me.NaturalLanguagePrompt = nlPrompt
        End Sub
    End Class

    Public Class StrategyGroup
        Public Property GroupId As String = Guid.NewGuid().ToString("N")
        Public Property GroupName As String = "Default"
        Public Property Description As String = ""
        Public Property DisplayOrder As Integer = 0
        Public Property IsActive As Boolean = True
    End Class

    Public Class StrategyStore
        Public Property Groups As New List(Of StrategyGroup)
        Public Property Strategies As New List(Of StrategyDefinition)
    End Class

    Public Class EvaluationResult
        Public Property Timestamp As DateTime
        Public Property StrategyName As String
        Public Property IsBuySignal As Boolean
        Public Property IsSellSignal As Boolean
        Public Property AgentScore As Double
        Public Property ConditionStates As New Dictionary(Of String, Boolean)

        Public Sub New()
        End Sub

        Public Sub New(time As DateTime, name As String, buy As Boolean, sell As Boolean, states As Dictionary(Of String, Boolean))
            Me.Timestamp = time
            Me.StrategyName = name
            Me.IsBuySignal = buy
            Me.IsSellSignal = sell
            Me.ConditionStates = states
        End Sub
    End Class

    Public Class MarketSnapshot
        Public Property Time As DateTime
        Public Property Code As String
        Public Property Open As Double
        Public Property High As Double
        Public Property Low As Double
        Public Property Close As Double
        Public Property Indicators As New Dictionary(Of String, Double)

        Public Sub New()
        End Sub

        Public Function GetValue(key As String) As Double
            Dim normalizedKey As String = If(key, "").Trim()
            If normalizedKey.Length = 0 Then Return Double.NaN

            If String.Equals(normalizedKey, "Price", System.StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(normalizedKey, "Close", System.StringComparison.OrdinalIgnoreCase) Then Return Close
            If String.Equals(normalizedKey, "Open", System.StringComparison.OrdinalIgnoreCase) Then Return Open
            If String.Equals(normalizedKey, "High", System.StringComparison.OrdinalIgnoreCase) Then Return High
            If String.Equals(normalizedKey, "Low", System.StringComparison.OrdinalIgnoreCase) Then Return Low
            
            Dim val As Double = 0
            If Indicators.TryGetValue(normalizedKey, val) Then Return val
            For Each kv In Indicators
                If String.Equals(kv.Key, normalizedKey, System.StringComparison.OrdinalIgnoreCase) Then
                    Return kv.Value
                End If
            Next

            ' 지표명.값 형태(ST_10_3.0.Value 등)까지 fallback 검색
            Dim suffix As String = "." & normalizedKey
            For Each kv In Indicators
                If kv.Key Is Nothing Then Continue For
                If kv.Key.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase) Then
                    Return kv.Value
                End If
            Next

            ' SuperTrend 별칭 fallback
            If String.Equals(normalizedKey, "SuperTrend", System.StringComparison.OrdinalIgnoreCase) Then
                For Each kv In Indicators
                    Dim k = If(kv.Key, "")
                    If k.IndexOf("SUPERTREND", System.StringComparison.OrdinalIgnoreCase) >= 0 Then
                        Return kv.Value
                    End If
                    If k.StartsWith("ST_", System.StringComparison.OrdinalIgnoreCase) AndAlso
                       k.EndsWith(".Value", System.StringComparison.OrdinalIgnoreCase) Then
                        Return kv.Value
                    End If
                Next
            End If

            Return Double.NaN
        End Function

        Public Sub SetIndicator(key As String, val As Double)
            Indicators(key) = val
        End Sub
    End Class
End Namespace
