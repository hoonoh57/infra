Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports [Shared]

Public Class SafeChartIndicatorBridge
    Private ReadOnly _engine As New IndicatorEngine()

    Public Sub New()
        RegisterDefaultIndicators()
    End Sub

    Public ReadOnly Property Engine As IndicatorEngine
        Get
            Return _engine
        End Get
    End Property

    Private Sub RegisterDefaultIndicators()
        Try
            _engine.Register(New TickIntensity_Indicator(1))
        Catch
        End Try
    End Sub

    Public Sub SetTickBars(tickBars As List(Of DateTime))
        If tickBars Is Nothing Then Return

        For Each ind As IIndicator In _engine.GetAll()
            Dim ti As TickIntensity_Indicator = TryCast(ind, TickIntensity_Indicator)
            If ti IsNot Nothing Then
                ti.SetTickBars(tickBars)
            End If
        Next
    End Sub

    Public Sub AddRealtimeTick(tickTime As DateTime)
        For Each ind As IIndicator In _engine.GetAll()
            Dim ti As TickIntensity_Indicator = TryCast(ind, TickIntensity_Indicator)
            If ti IsNot Nothing Then
                ti.AddTick(tickTime)
            End If
        Next
    End Sub

    Public Sub CalculateAll(candles As List(Of CandleItem))
        If candles Is Nothing OrElse candles.Count = 0 Then Return
        _engine.CalculateAll(candles)
    End Sub

    Public Sub UpdateLast(candles As List(Of CandleItem))
        If candles Is Nothing OrElse candles.Count = 0 Then Return
        _engine.UpdateLast(candles)
    End Sub

    Public Function GetTickResults() As List(Of IndicatorResult)
        If _engine Is Nothing OrElse _engine.Results Is Nothing Then
            Return Nothing
        End If

        For Each kv As KeyValuePair(Of String, List(Of IndicatorResult)) In _engine.Results
            If kv.Key IsNot Nothing AndAlso kv.Key.StartsWith("TICKINT_", StringComparison.OrdinalIgnoreCase) Then
                Return kv.Value
            End If
        Next

        Return Nothing
    End Function
End Class

