Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports SkiaSharp

Public Class SafeIndicatorRenderer
    Private ReadOnly _linePaint As New SKPaint With {.Style = SKPaintStyle.Stroke, .StrokeWidth = 1.5F, .IsAntialias = True}
    Private ReadOnly _histBull As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = SafeChartPalette.Bull, .IsAntialias = False}
    Private ReadOnly _histBear As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = SafeChartPalette.Bear, .IsAntialias = False}
    Private ReadOnly _textPaint As New SKPaint With {.Color = SafeChartPalette.AxisText, .TextSize = 11.0F, .IsAntialias = True}

    Public Sub RenderTickIntensity(canvas As SKCanvas,
                                   tickResults As List(Of IndicatorResult),
                                   candles As List(Of CandleItem),
                                   state As SafeChartState,
                                   geo As SafeChartGeometry)
        If canvas Is Nothing OrElse tickResults Is Nothing OrElse candles Is Nothing Then Return
        If tickResults.Count = 0 OrElse candles.Count = 0 Then Return

        Dim endIdx As Integer = state.EndIndex(candles.Count)
        If endIdx < state.StartIndex Then Return

        Dim minVal As Single = 0.0F
        Dim maxVal As Single = 1.0F

        For i As Integer = state.StartIndex To Math.Min(endIdx, tickResults.Count - 1)
            Dim v As Single = Math.Abs(tickResults(i).Val("TickSum"))
            If Not Single.IsNaN(v) Then
                If v > maxVal Then maxVal = v
            End If
        Next

        Dim zeroY As Single = geo.YForIndicator(0.0F, -maxVal, maxVal)

        For i As Integer = state.StartIndex To Math.Min(endIdx, tickResults.Count - 1)
            Dim v As Single = tickResults(i).Val("TickSum")
            If Single.IsNaN(v) Then Continue For

            Dim x As Single = geo.XForIndex(i, state)
            Dim y As Single = geo.YForIndicator(v, -maxVal, maxVal)
            Dim halfW As Single = Math.Max(1.0F, state.CandleWidth / 2.0F)
            Dim p As SKPaint = If(v >= 0, _histBull, _histBear)

            canvas.DrawRect(New SKRect(x - halfW, Math.Min(y, zeroY), x + halfW, Math.Max(y, zeroY)), p)
        Next

        DrawLine(canvas, tickResults, "MA5", state, geo, -maxVal, maxVal, SafeChartPalette.IndicatorColors(1))
        DrawLine(canvas, tickResults, "MA20", state, geo, -maxVal, maxVal, SafeChartPalette.IndicatorColors(4))

        canvas.DrawText("TICKINT_1", geo.IndicatorRect.Left + 4.0F, geo.IndicatorRect.Top + 14.0F, _textPaint)
    End Sub

    Private Sub DrawLine(canvas As SKCanvas,
                         results As List(Of IndicatorResult),
                         key As String,
                         state As SafeChartState,
                         geo As SafeChartGeometry,
                         minVal As Single,
                         maxVal As Single,
                         color As SKColor)
        _linePaint.Color = color

        Dim path As New SKPath()
        Dim started As Boolean = False
        Dim endIdx As Integer = Math.Min(state.EndIndex(results.Count), results.Count - 1)

        For i As Integer = state.StartIndex To endIdx
            Dim v As Single = results(i).Val(key)
            If Single.IsNaN(v) Then
                started = False
                Continue For
            End If

            Dim x As Single = geo.XForIndex(i, state)
            Dim y As Single = geo.YForIndicator(v, minVal, maxVal)

            If Not started Then
                path.MoveTo(x, y)
                started = True
            Else
                path.LineTo(x, y)
            End If
        Next

        canvas.DrawPath(path, _linePaint)
    End Sub
End Class
