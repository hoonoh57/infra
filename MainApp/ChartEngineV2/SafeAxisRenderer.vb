Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports SkiaSharp

Public Class SafeAxisRenderer
    Private ReadOnly _gridPaint As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = SafeChartPalette.Grid, .StrokeWidth = 1.0F, .IsAntialias = False}
    Private ReadOnly _borderPaint As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = SafeChartPalette.PanelBorder, .StrokeWidth = 1.0F}
    Private ReadOnly _textPaint As New SKPaint With {.Color = SafeChartPalette.AxisText, .TextSize = 11.0F, .IsAntialias = True}

    Public Sub Render(canvas As SKCanvas, candles As List(Of CandleItem), state As SafeChartState, geo As SafeChartGeometry)
        If canvas Is Nothing Then Return

        canvas.DrawRect(geo.MainRect, _borderPaint)
        canvas.DrawRect(geo.VolumeRect, _borderPaint)
        canvas.DrawRect(geo.IndicatorRect, _borderPaint)

        For i As Integer = 1 To 4
            Dim y As Single = geo.MainRect.Top + geo.MainRect.Height * i / 5.0F
            canvas.DrawLine(geo.MainRect.Left, y, geo.MainRect.Right, y, _gridPaint)
        Next

        If geo.PriceHigh > geo.PriceLow Then
            For i As Integer = 0 To 4
                Dim price As Single = geo.PriceHigh - (geo.PriceHigh - geo.PriceLow) * i / 4.0F
                Dim y As Single = geo.YForPrice(price)
                canvas.DrawText(price.ToString("N0"), geo.MainRect.Right + 4.0F, y + 4.0F, _textPaint)
            Next
        End If

        If candles IsNot Nothing AndAlso candles.Count > 0 Then
            Dim endIdx As Integer = state.EndIndex(candles.Count)
            Dim stepVal As Integer = Math.Max(1, state.VisibleCount \ 6)
            For i As Integer = state.StartIndex To endIdx Step stepVal
                Dim x As Single = geo.XForIndex(i, state)
                canvas.DrawLine(x, geo.MainRect.Top, x, geo.IndicatorRect.Bottom, _gridPaint)
                canvas.DrawText(candles(i).Dt.ToString("HH:mm"), x - 16.0F, geo.IndicatorRect.Bottom + 14.0F, _textPaint)
            Next
        End If
    End Sub
End Class
