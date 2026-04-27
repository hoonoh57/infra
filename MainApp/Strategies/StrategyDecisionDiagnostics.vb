Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports System.Data

Public Interface IStrategyDecisionDiagnostics
    ReadOnly Property DecisionLogs As List(Of StrategyDecisionLog)
    Sub ClearDecisionLogs()
End Interface

Public Class StrategyDecisionLog
    Public Property TimeStamp As DateTime = DateTime.MinValue
    Public Property ClosePrice As Double = 0.0R
    Public Property OpenRiseRate As Double = 0.0R
    Public Property State As String = ""
    Public Property Reason As String = ""
    Public Property LeaderScore As Double = 0.0R
    Public Property TrendStartScore As Double = 0.0R
    Public Property EntrySafetyScore As Double = 0.0R
    Public Property TradePriorityScore As Double = 0.0R
    Public Property TickValue As Double = 0.0R
    Public Property TickMa5 As Double = 0.0R
    Public Property TickMa20 As Double = 0.0R
    Public Property RecentTickCrossBarsAgo As Integer = -1
    Public Property TickNotCollapsed As Boolean = False
    Public Property SuperTrendBullish As Boolean = False
    Public Property JmaBullish As Boolean = False
    Public Property JmaTurnUp As Boolean = False
    Public Property ObvBullish As Boolean = False
    Public Property IsSecondViLeaderEntry As Boolean = False

    Public Function ToDataRow(table As DataTable) As DataRow
        Dim row As DataRow = table.NewRow()
        row("시간") = If(TimeStamp = DateTime.MinValue, "", TimeStamp.ToString("yyyy-MM-dd HH:mm:ss"))
        row("종가") = ClosePrice
        row("시가대비%") = OpenRiseRate
        row("상태") = State
        row("차단/신호사유") = Reason
        row("LeaderScore") = LeaderScore
        row("TrendStartScore") = TrendStartScore
        row("EntrySafetyScore") = EntrySafetyScore
        row("TradePriorityScore") = TradePriorityScore
        row("Tick") = TickValue
        row("TickMA5") = TickMa5
        row("TickMA20") = TickMa20
        row("RecentCross") = RecentTickCrossBarsAgo
        row("Tick유지") = TickNotCollapsed
        row("ST상승") = SuperTrendBullish
        row("JMA상승") = JmaBullish
        row("JMA상승전환") = JmaTurnUp
        row("OBV상승") = ObvBullish
        row("2차VI후보") = IsSecondViLeaderEntry
        Return row
    End Function
End Class

Public NotInheritable Class StrategyDecisionLogTableBuilder
    Private Sub New()
    End Sub

    Public Shared Function CreateTable() As DataTable
        Dim table As New DataTable("DecisionLogs")
        table.Columns.Add("시간", GetType(String))
        table.Columns.Add("종가", GetType(Double))
        table.Columns.Add("시가대비%", GetType(Double))
        table.Columns.Add("상태", GetType(String))
        table.Columns.Add("차단/신호사유", GetType(String))
        table.Columns.Add("LeaderScore", GetType(Double))
        table.Columns.Add("TrendStartScore", GetType(Double))
        table.Columns.Add("EntrySafetyScore", GetType(Double))
        table.Columns.Add("TradePriorityScore", GetType(Double))
        table.Columns.Add("Tick", GetType(Double))
        table.Columns.Add("TickMA5", GetType(Double))
        table.Columns.Add("TickMA20", GetType(Double))
        table.Columns.Add("RecentCross", GetType(Integer))
        table.Columns.Add("Tick유지", GetType(Boolean))
        table.Columns.Add("ST상승", GetType(Boolean))
        table.Columns.Add("JMA상승", GetType(Boolean))
        table.Columns.Add("JMA상승전환", GetType(Boolean))
        table.Columns.Add("OBV상승", GetType(Boolean))
        table.Columns.Add("2차VI후보", GetType(Boolean))
        Return table
    End Function
End Class
