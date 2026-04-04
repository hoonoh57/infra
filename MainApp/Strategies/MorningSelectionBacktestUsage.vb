Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic

Public NotInheritable Class MorningSelectionBacktestUsage

    Private Sub New()
    End Sub

    Public Shared Sub EvaluateUniverse(items As IList(Of StockInfoItem), ByRef top10 As List(Of StockInfoItem), ByRef picks As List(Of StockInfoItem), ByRef summary As MorningSelectionBacktestSummary)
        top10 = New List(Of StockInfoItem)()
        picks = New List(Of StockInfoItem)()
        summary = New MorningSelectionBacktestSummary()

        If items Is Nothing Then Return

        MorningSelectionEngine.UpdateBasicScore(items)
        top10 = MorningSelectionEngine.GetTop10(items)
        picks = MorningSelectionEngine.PickEntries(top10, 3)
        summary = MorningSelectionEngine.Evaluate(items)
    End Sub

End Class
