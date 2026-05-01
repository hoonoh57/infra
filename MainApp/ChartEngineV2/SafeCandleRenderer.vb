Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports SkiaSharp

Public Class SafeCandleRenderer
    Private ReadOnly _bullBody As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = SafeChartPalette.Bull, .IsAntialias = False}
    Private ReadOnly _bearBody As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = SafeChartPalette.Bear, .IsAntialias = False}
    Private ReadOnly _bullWick As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = SafeChartPalette.Bull, .StrokeWidth = 1.0F, .IsAntialias = False}
    Private ReadOnly _bearWick As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = SafeChartPalette.Bear, .StrokeWidth = 1.0F, .IsAntialias = False}
    Private ReadOnly _bullVol As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = SafeChartPalette.BullVolume, .IsAntialias = False}
    Private ReadOnly _bearVol As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = SafeChartPalette.BearVolume, .IsAntialias = False}

    Public Sub Render(canvas As SKCanvas,
                      candles As List(Of CandleItem),
                      state As SafeChartState,
                      geo As SafeChartGeometry,
                      options As SafeChartOptions)
        If canvas Is Nothing OrElse candles Is Nothing OrElse candles.Count = 0 Then Return

        Dim endIdx As Integer = state.EndIndex(candles.Count)
        If endIdx < state.StartIndex Then Return

        For i As Integer = state.StartIndex To endIdx
            Dim c As CandleItem = candles(i)
            If c Is Nothing Then Continue For

            Dim isBull As Boolean = c.Close >= c.Open
            Dim bodyPaint As SKPaint = If(isBull, _bullBody, _bearBody)
            Dim wickPaint As SKPaint = If(isBull, _bullWick, _bearWick)
            Dim volPaint As SKPaint = If(isBull, _bullVol, _bearVol)

            Dim x As Single = geo.XForIndex(i, state)
            Dim yHigh As Single = geo.YForPrice(CSng(c.High))
            Dim yLow As Single = geo.YForPrice(CSng(c.Low))
            Dim yOpen As Single = geo.YForPrice(CSng(c.Open))
            Dim yClose As Single = geo.YForPrice(CSng(c.Close))

            canvas.DrawLine(x, yHigh, x, yLow, wickPaint)

            Dim top As Single = Math.Min(yOpen, yClose)
            Dim bottom As Single = Math.Max(yOpen, yClose)
            If bottom - top < 1.0F Then bottom = top + 1.0F

            Dim halfW As Single = Math.Max(1.0F, state.CandleWidth / 2.0F)
            canvas.DrawRect(New SKRect(x - halfW, top, x + halfW, bottom), bodyPaint)

            If options.ShowVolume Then
                Dim yVol As Single = geo.YForVolume(c.Volume)
                canvas.DrawRect(New SKRect(x - halfW, yVol, x + halfW, geo.VolumeRect.Bottom), volPaint)
            End If
        Next
    End Sub
End Class
