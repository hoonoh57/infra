Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic

Public Class SafeChartDataBuffer
    Private ReadOnly _lockObj As New Object()
    Private _candles As New List(Of CandleItem)()

    Public Property StockCode As String = ""
    Public Property StockName As String = ""
    Public Property PrevClose As Single = 0.0F

    Public Sub SetCandles(items As List(Of CandleItem), Optional prevCloseValue As Single = 0.0F)
        SyncLock _lockObj
            If items Is Nothing Then
                _candles = New List(Of CandleItem)()
            Else
                _candles = New List(Of CandleItem)(items)
            End If

            If prevCloseValue > 0 Then
                PrevClose = prevCloseValue
            End If
        End SyncLock
    End Sub

    Public Function Snapshot() As List(Of CandleItem)
        SyncLock _lockObj
            Return New List(Of CandleItem)(_candles)
        End SyncLock
    End Function

    Public ReadOnly Property Count As Integer
        Get
            SyncLock _lockObj
                Return _candles.Count
            End SyncLock
        End Get
    End Property

    Public Sub UpdateLastFromTick(price As Single, volume As Long, tickTime As DateTime, intervalMinutes As Integer)
        SyncLock _lockObj
            If _candles.Count = 0 Then Return

            Dim barTime As DateTime = AlignToMinuteBar(tickTime, intervalMinutes)
            Dim last As CandleItem = _candles(_candles.Count - 1)

            If last.Dt = DateTime.MinValue Then
                last.Dt = barTime
            End If

            If barTime > last.Dt Then
                Dim c As CandleItem = CandleItem.Create(barTime, price)
                c.UpdateFromTick(price, volume, tickTime)
                _candles.Add(c)
            Else
                last.UpdateFromTick(price, volume, tickTime)
            End If
        End SyncLock
    End Sub

    Private Shared Function AlignToMinuteBar(ts As DateTime, intervalMinutes As Integer) As DateTime
        Dim stepMin As Integer = Math.Max(1, intervalMinutes)
        Dim m As Integer = (ts.Minute \ stepMin) * stepMin
        Return New DateTime(ts.Year, ts.Month, ts.Day, ts.Hour, m, 0)
    End Function
End Class
