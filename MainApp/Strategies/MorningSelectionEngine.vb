Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports System.Linq

Public Class MorningSelectionWeights
    Public Property StAbove As Integer = 30
    Public Property JmaUp As Integer = 25
    Public Property Tick520 As Integer = 25
    Public Property PriceAccelUp As Integer = 20
    Public Property VwapAbove As Integer = 10
    Public Property UpperWickPenalty As Integer = 10
    Public Property ReentryPenalty As Integer = 50
End Class

Public Class MorningSelectionBacktestSummary
    Public Property EvaluatedAt As DateTime = DateTime.Now
    Public Property UniverseCount As Integer = 0
    Public Property BasePassedCount As Integer = 0
    Public Property CorrelationScoreVsChange As Double = 0
    Public Property CorrelationScoreVsHigh As Double = 0
    Public Property Top3Hit10Pct As Integer = 0
    Public Property Top5Hit10Pct As Integer = 0
    Public Property Top3HitHighRank As Integer = 0
    Public Property Top5HitHighRank As Integer = 0
End Class

Public NotInheritable Class MorningSelectionEngine

    Private Sub New()
    End Sub

    Public Shared Sub UpdateBasicScore(items As IList(Of StockInfoItem), Optional weights As MorningSelectionWeights = Nothing)
        If items Is Nothing Then Return
        If weights Is Nothing Then weights = New MorningSelectionWeights()

        Dim i As Integer = 0
        For i = 0 To items.Count - 1
            Dim item As StockInfoItem = items(i)
            If item Is Nothing Then Continue For

            item.BaseInvariantPassed = (item.CapturePrice > 0 AndAlso item.Price > 0 AndAlso item.Price >= CInt(Math.Truncate(item.Ma120)))
            item.IsTop10 = False
            item.IsEntryCandidate = False

            If Not item.BaseInvariantPassed Then
                item.CandidateScore = 0
                item.EntryFitScore = 0
                item.FinalScore = 0
                item.SelectionState = SelectionState.BaseRejected
                Continue For
            End If

            Dim score As Integer = 0
            If item.SuperTrend > 0 AndAlso item.Price >= CInt(Math.Truncate(item.SuperTrend)) Then score += weights.StAbove
            If item.JmaSlope > 0 Then score += weights.JmaUp
            If item.Tick5 > item.Tick20 AndAlso item.Tick5 > 0 Then score += weights.Tick520
            If item.PriceAccel > 0 Then score += weights.PriceAccelUp
            If item.Vwap > 0 AndAlso item.Price >= CInt(Math.Truncate(item.Vwap)) Then score += weights.VwapAbove
            If HasLargeUpperWick(item) Then score -= weights.UpperWickPenalty
            If item.IsReEntryBlocked Then score -= weights.ReentryPenalty

            item.CandidateScore = Math.Max(0, score)
            item.EntryFitScore = ComputeEntryFitScore(item)
            item.FinalScore = item.CandidateScore
            item.SelectionState = SelectionState.Candidate
            item.LastScoreUpdateTimeStamp = DateTime.Now
        Next

        ApplyRanks(items)
    End Sub

    Public Shared Function GetTop10(items As IList(Of StockInfoItem)) As List(Of StockInfoItem)
        Dim ordered As List(Of StockInfoItem) = SortByFinalScore(items)
        Dim result As New List(Of StockInfoItem)()
        Dim i As Integer = 0
        For i = 0 To ordered.Count - 1
            If i < 10 Then
                ordered(i).IsTop10 = True
                ordered(i).SelectionState = SelectionState.Top10
                result.Add(ordered(i))
            Else
                Exit For
            End If
        Next
        Return result
    End Function

    Public Shared Function PickEntries(top10 As IList(Of StockInfoItem), maxCount As Integer) As List(Of StockInfoItem)
        Dim candidates As New List(Of StockInfoItem)()
        If top10 Is Nothing Then Return candidates

        Dim ordered As New List(Of StockInfoItem)(top10)
        ordered.Sort(AddressOf CompareByEntryFitThenFinal)

        Dim i As Integer = 0
        For i = 0 To ordered.Count - 1
            If candidates.Count >= maxCount Then Exit For
            Dim item As StockInfoItem = ordered(i)
            If item Is Nothing Then Continue For
            If item.IsReEntryBlocked Then Continue For
            If item.EntryFitScore <= 0 Then Continue For
            item.IsEntryCandidate = True
            item.SelectionState = SelectionState.EntryCandidate
            candidates.Add(item)
        Next

        Return candidates
    End Function

    Public Shared Function Evaluate(items As IList(Of StockInfoItem)) As MorningSelectionBacktestSummary
        Dim summary As New MorningSelectionBacktestSummary()
        If items Is Nothing OrElse items.Count = 0 Then Return summary

        summary.UniverseCount = items.Count

        Dim scoreList As New List(Of Double)()
        Dim changeList As New List(Of Double)()
        Dim highList As New List(Of Double)()

        Dim i As Integer = 0
        For i = 0 To items.Count - 1
            Dim item As StockInfoItem = items(i)
            If item Is Nothing Then Continue For
            If item.BaseInvariantPassed Then summary.BasePassedCount += 1
            scoreList.Add(item.FinalScore)
            changeList.Add(item.ChangeRate)
            highList.Add(item.HighestRisePct)
        Next

        summary.CorrelationScoreVsChange = ComputePearson(scoreList, changeList)
        summary.CorrelationScoreVsHigh = ComputePearson(scoreList, highList)

        Dim byScore As List(Of StockInfoItem) = SortByFinalScore(items)
        Dim byHigh As List(Of StockInfoItem) = SortByHighestRise(items)

        summary.Top3Hit10Pct = CountHitTarget(byScore, 3, 10.0R)
        summary.Top5Hit10Pct = CountHitTarget(byScore, 5, 10.0R)
        summary.Top3HitHighRank = CountOverlap(byScore, byHigh, 3)
        summary.Top5HitHighRank = CountOverlap(byScore, byHigh, 5)

        Return summary
    End Function

    Public Shared Sub ApplyRanks(items As IList(Of StockInfoItem))
        If items Is Nothing Then Return

        Dim byScore As List(Of StockInfoItem) = SortByFinalScore(items)
        Dim byChange As List(Of StockInfoItem) = SortByChangeRate(items)
        Dim byHigh As List(Of StockInfoItem) = SortByHighestRise(items)

        Dim i As Integer = 0
        For i = 0 To byScore.Count - 1
            byScore(i).ScoreRank = i + 1
        Next
        For i = 0 To byChange.Count - 1
            byChange(i).ChangeRateRank = i + 1
        Next
        For i = 0 To byHigh.Count - 1
            byHigh(i).HighestRiseRank = i + 1
        Next
        For i = 0 To items.Count - 1
            Dim item As StockInfoItem = items(i)
            If item Is Nothing Then Continue For
            item.ScoreVsChangeRankGap = item.ScoreRank - item.ChangeRateRank
            item.ScoreVsHighRankGap = item.ScoreRank - item.HighestRiseRank
        Next
    End Sub

    Public Shared Function BuildBasicStrategyDefinition() As Models.StrategyDefinition
        Dim buy As New List(Of Models.LogicGate)()
        Dim sell As New List(Of Models.LogicGate)()

        Dim buyGate As New Models.LogicGate()
        buyGate.Name = "기본 아침 선별"
        buyGate.Operator = Models.LogicalOperator.AND
        buyGate.Conditions.Add(New Models.ConditionCell("BASE_MA120", "종가>=MA120", "Close", Models.ComparisonOperator.GreaterEqual, "MA120"))
        buyGate.Conditions.Add(New Models.ConditionCell("BASE_ST", "종가>=SuperTrend", "Close", Models.ComparisonOperator.GreaterEqual, "SuperTrend"))
        buyGate.Conditions.Add(New Models.ConditionCell("BASE_JMA", "JMA 상승", "JMA_SLOPE", Models.ComparisonOperator.Greater, "", 0))
        buyGate.Conditions.Add(New Models.ConditionCell("BASE_TICK", "Tick5>Tick20", "Tick5", Models.ComparisonOperator.Greater, "Tick20"))
        buy.Add(buyGate)

        Dim sellGate As New Models.LogicGate()
        sellGate.Name = "기본 청산"
        sellGate.Operator = Models.LogicalOperator.OR
        sellGate.Conditions.Add(New Models.ConditionCell("SELL_ST", "종가<SuperTrend", "Close", Models.ComparisonOperator.Less, "SuperTrend"))
        sellGate.Conditions.Add(New Models.ConditionCell("SELL_JMA", "목표후 JMA 하락", "JMA_SLOPE", Models.ComparisonOperator.LessEqual, "", 0))
        sell.Add(sellGate)

        Dim definition As New Models.StrategyDefinition()
        definition.Name = "MorningSelectionBasic"
        definition.Description = "MA120 위 포착종목을 ST/JMA/Tick/PriceAccel로 점수화하고 상위랭커 적중도를 검증하는 기본 전략"
        definition.BuyRules = buy
        definition.SellRules = sell
        definition.Mode = "Test"
        definition.IsActive = True
        Return definition
    End Function

    Private Shared Function ComputeEntryFitScore(item As StockInfoItem) As Integer
        Dim score As Integer = 0

        If item.SuperTrend > 0 Then
            Dim dist As Double = (item.Price - item.SuperTrend) / Math.Max(1.0R, item.SuperTrend) * 100.0R
            If dist >= 0 AndAlso dist <= 2.0R Then
                score += 35
            ElseIf dist > 2.0R AndAlso dist <= 4.0R Then
                score += 20
            End If
        End If

        If item.Vwap > 0 Then
            Dim dv As Double = Math.Abs(item.Price - item.Vwap) / Math.Max(1.0R, item.Vwap) * 100.0R
            If dv <= 1.2R Then
                score += 20
            ElseIf dv <= 2.0R Then
                score += 10
            End If
        End If

        If item.JmaSlope > 0 Then score += 20
        If item.Tick5 > item.Tick20 AndAlso item.TickAccel >= 1.05R Then score += 15
        If item.Price > item.Open Then score += 10

        If HasLargeUpperWick(item) Then score -= 20
        If item.PriceAccel < 0 Then score -= 15

        Return Math.Max(0, score)
    End Function

    Private Shared Function HasLargeUpperWick(item As StockInfoItem) As Boolean
        If item Is Nothing Then Return False
        If item.High <= 0 OrElse item.Price <= 0 OrElse item.Open <= 0 Then Return False

        Dim body As Integer = Math.Abs(item.Price - item.Open)
        Dim upper As Integer = item.High - Math.Max(item.Price, item.Open)
        If upper <= 0 Then Return False
        If body <= 0 Then Return upper > 0
        Return upper >= body
    End Function

    Private Shared Function SortByFinalScore(items As IList(Of StockInfoItem)) As List(Of StockInfoItem)
        Dim result As New List(Of StockInfoItem)()
        If items Is Nothing Then Return result
        Dim i As Integer = 0
        For i = 0 To items.Count - 1
            If items(i) IsNot Nothing Then result.Add(items(i))
        Next
        result.Sort(AddressOf CompareByFinalScore)
        Return result
    End Function

    Private Shared Function SortByChangeRate(items As IList(Of StockInfoItem)) As List(Of StockInfoItem)
        Dim result As New List(Of StockInfoItem)()
        If items Is Nothing Then Return result
        Dim i As Integer = 0
        For i = 0 To items.Count - 1
            If items(i) IsNot Nothing Then result.Add(items(i))
        Next
        result.Sort(Function(a As StockInfoItem, b As StockInfoItem) b.ChangeRate.CompareTo(a.ChangeRate))
        Return result
    End Function

    Private Shared Function SortByHighestRise(items As IList(Of StockInfoItem)) As List(Of StockInfoItem)
        Dim result As New List(Of StockInfoItem)()
        If items Is Nothing Then Return result
        Dim i As Integer = 0
        For i = 0 To items.Count - 1
            If items(i) IsNot Nothing Then result.Add(items(i))
        Next
        result.Sort(Function(a As StockInfoItem, b As StockInfoItem) b.HighestRisePct.CompareTo(a.HighestRisePct))
        Return result
    End Function

    Private Shared Function CompareByFinalScore(x As StockInfoItem, y As StockInfoItem) As Integer
        If x Is Nothing AndAlso y Is Nothing Then Return 0
        If x Is Nothing Then Return 1
        If y Is Nothing Then Return -1

        Dim c As Integer = y.FinalScore.CompareTo(x.FinalScore)
        If c <> 0 Then Return c
        c = y.ChangeRate.CompareTo(x.ChangeRate)
        If c <> 0 Then Return c
        c = y.HighestRisePct.CompareTo(x.HighestRisePct)
        If c <> 0 Then Return c
        Return String.Compare(x.Code, y.Code, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function CompareByEntryFitThenFinal(x As StockInfoItem, y As StockInfoItem) As Integer
        If x Is Nothing AndAlso y Is Nothing Then Return 0
        If x Is Nothing Then Return 1
        If y Is Nothing Then Return -1

        Dim c As Integer = y.EntryFitScore.CompareTo(x.EntryFitScore)
        If c <> 0 Then Return c
        Return CompareByFinalScore(x, y)
    End Function

    Private Shared Function CountHitTarget(items As IList(Of StockInfoItem), takeCount As Integer, targetPct As Double) As Integer
        Dim count As Integer = 0
        Dim i As Integer = 0
        For i = 0 To Math.Min(takeCount, items.Count) - 1
            If items(i).HighestRisePct >= targetPct Then count += 1
        Next
        Return count
    End Function

    Private Shared Function CountOverlap(a As IList(Of StockInfoItem), b As IList(Of StockInfoItem), takeCount As Integer) As Integer
        Dim setA As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim i As Integer = 0
        For i = 0 To Math.Min(takeCount, a.Count) - 1
            setA.Add(a(i).Code)
        Next

        Dim count As Integer = 0
        For i = 0 To Math.Min(takeCount, b.Count) - 1
            If setA.Contains(b(i).Code) Then count += 1
        Next
        Return count
    End Function

    Private Shared Function ComputePearson(x As IList(Of Double), y As IList(Of Double)) As Double
        If x Is Nothing OrElse y Is Nothing Then Return 0
        Dim n As Integer = Math.Min(x.Count, y.Count)
        If n <= 1 Then Return 0

        Dim sumX As Double = 0
        Dim sumY As Double = 0
        Dim i As Integer = 0
        For i = 0 To n - 1
            sumX += x(i)
            sumY += y(i)
        Next

        Dim meanX As Double = sumX / n
        Dim meanY As Double = sumY / n
        Dim num As Double = 0
        Dim denX As Double = 0
        Dim denY As Double = 0

        For i = 0 To n - 1
            Dim dx As Double = x(i) - meanX
            Dim dy As Double = y(i) - meanY
            num += dx * dy
            denX += dx * dx
            denY += dy * dy
        Next

        If denX <= 0 OrElse denY <= 0 Then Return 0
        Return num / Math.Sqrt(denX * denY)
    End Function

End Class
