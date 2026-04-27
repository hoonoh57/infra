Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic

''' <summary>
''' 차트 메뉴, 전략관리자, 백테스트, 모의/실거래 런타임에서 공통으로 사용하는 내장 전략 생성기.
''' 전략 생성 경로를 한 곳으로 모아, 같은 전략이 서로 다른 화면에서 다르게 생성되는 문제를 방지한다.
''' </summary>
Public NotInheritable Class BuiltInStrategyFactory

    Public Const TRUE_LEADER_EARLY_TREND_TOP3 As String = "TrueLeaderEarlyTrendTop3"

    Private Sub New()
    End Sub

    Public Shared Function CreateStrategy(strategyName As String) As IStrategy
        If String.IsNullOrWhiteSpace(strategyName) Then Return Nothing

        Dim normalizedName As String = strategyName.Trim()
        If String.Equals(normalizedName, TRUE_LEADER_EARLY_TREND_TOP3, StringComparison.OrdinalIgnoreCase) Then
            Return New TrueLeaderEarlyTrendStrategy()
        End If

        If String.Equals(normalizedName, "진성대장주 초입 Top3 전략", StringComparison.OrdinalIgnoreCase) Then
            Return New TrueLeaderEarlyTrendStrategy()
        End If

        Return Nothing
    End Function

    Public Shared Function GetAllStrategies() As List(Of IStrategy)
        Dim items As New List(Of IStrategy)()
        items.Add(New TrueLeaderEarlyTrendStrategy())
        Return items
    End Function

    Public Shared Function GetDisplayItems() As List(Of BuiltInStrategyDisplayItem)
        Dim result As New List(Of BuiltInStrategyDisplayItem)()

        Dim strategy As IStrategy = New TrueLeaderEarlyTrendStrategy()
        Dim item As New BuiltInStrategyDisplayItem()
        item.Name = strategy.Name
        item.DisplayName = strategy.DisplayName
        item.Description = "조건검색 후보 중 TickIntensity 파생강도, SuperTrend, JMA, OBV, 진입안전성을 결합하여 진성대장주 초입/재반등 구간만 매수 후보로 판단합니다."
        result.Add(item)

        Return result
    End Function

End Class

Public Class BuiltInStrategyDisplayItem
    Public Property Name As String = ""
    Public Property DisplayName As String = ""
    Public Property Description As String = ""

    Public Overrides Function ToString() As String
        If String.IsNullOrWhiteSpace(DisplayName) Then Return Name
        Return DisplayName
    End Function
End Class
