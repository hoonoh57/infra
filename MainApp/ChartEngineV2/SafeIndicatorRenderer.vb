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
    Private ReadOnly _borderPaint As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = SafeChartPalette.Grid, .StrokeWidth = 1.0F, .IsAntialias = False}

    Public Sub RenderIndicators(canvas As SKCanvas,
                                indicators As List(Of IIndicator),
                                results As Dictionary(Of String, List(Of IndicatorResult)),
                                candles As List(Of CandleItem),
                                state As SafeChartState,
                                geo As SafeChartGeometry)
        If canvas Is Nothing OrElse indicators Is Nothing OrElse results Is Nothing OrElse candles Is Nothing Then Return
        If indicators.Count = 0 OrElse candles.Count = 0 Then Return

        Dim colorIndex As Integer = 0
        Dim panelLegendCount As New Dictionary(Of Integer, Integer)()

        For Each ind As IIndicator In indicators
            If ind Is Nothing Then Continue For

            Dim list As List(Of IndicatorResult) = Nothing
            If Not results.TryGetValue(ind.Name, list) Then Continue For
            If list Is Nothing OrElse list.Count = 0 Then Continue For

            If ind.PanelIndex <= 0 Then
                RenderOverlayLines(canvas, ind, list, candles, state, geo, colorIndex)
            Else
                Dim legendIndex As Integer = 0
                If panelLegendCount.ContainsKey(ind.PanelIndex) Then legendIndex = panelLegendCount(ind.PanelIndex)
                RenderPanelIndicator(canvas, ind, list, candles, state, geo, colorIndex, legendIndex)
                panelLegendCount(ind.PanelIndex) = legendIndex + 1
            End If

            colorIndex += 1
        Next
    End Sub

    Private Sub RenderOverlayLines(canvas As SKCanvas,
                                   ind As IIndicator,
                                   list As List(Of IndicatorResult),
                                   candles As List(Of CandleItem),
                                   state As SafeChartState,
                                   geo As SafeChartGeometry,
                                   colorIndex As Integer)
        Dim keys As List(Of String) = CollectKeys(list)
        If keys.Count = 0 Then Return

        Dim keyIndex As Integer = 0
        For Each key As String In keys
            If IsNonDrawableKey(key) Then Continue For
            Dim color As SKColor = SafeChartPalette.IndicatorColors((colorIndex + keyIndex) Mod SafeChartPalette.IndicatorColors.Length)
            DrawLineOnMain(canvas, list, key, state, geo, color)
            keyIndex += 1
        Next

        canvas.DrawText(ind.Name, geo.MainRect.Left + 4.0F, geo.MainRect.Top + 14.0F + 14.0F * colorIndex, _textPaint)
    End Sub

    Private Sub RenderPanelIndicator(canvas As SKCanvas,
                                     ind As IIndicator,
                                     list As List(Of IndicatorResult),
                                     candles As List(Of CandleItem),
                                     state As SafeChartState,
                                     geo As SafeChartGeometry,
                                     colorIndex As Integer,
                                     legendIndex As Integer)
        Dim panelRect As SKRect = geo.GetPanelRect(ind.PanelIndex)
        canvas.DrawRect(panelRect, _borderPaint)

        If IsTickIntensityResult(list) Then
            RenderTickLike(canvas, ind.Name, list, candles, state, geo, panelRect, colorIndex, legendIndex)
            Return
        End If

        Dim minVal As Single = Single.MaxValue
        Dim maxVal As Single = Single.MinValue
        Dim keys As List(Of String) = CollectKeys(list)
        If keys.Count = 0 Then Return

        Dim endIdx As Integer = Math.Min(state.EndIndex(candles.Count), list.Count - 1)
        If endIdx < state.StartIndex Then Return

        For i As Integer = state.StartIndex To endIdx
            Dim r As IndicatorResult = list(i)
            If r Is Nothing OrElse r.Values Is Nothing Then Continue For
            For Each key As String In keys
                If IsNonDrawableKey(key) Then Continue For
                Dim v As Single = r.Val(key)
                If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then Continue For
                If v < minVal Then minVal = v
                If v > maxVal Then maxVal = v
            Next
        Next

        If minVal = Single.MaxValue OrElse maxVal = Single.MinValue Then Return
        If maxVal <= minVal Then
            maxVal += 1.0F
            minVal -= 1.0F
        End If

        Dim keyIndex As Integer = 0
        For Each key As String In keys
            If IsNonDrawableKey(key) Then Continue For
            Dim color As SKColor = SafeChartPalette.IndicatorColors((colorIndex + keyIndex) Mod SafeChartPalette.IndicatorColors.Length)
            DrawLineOnPanel(canvas, list, key, state, geo, panelRect, minVal, maxVal, color)
            keyIndex += 1
        Next

        canvas.DrawText(ind.Name, panelRect.Left + 4.0F, panelRect.Top + 14.0F + 14.0F * legendIndex, _textPaint)
    End Sub

    Private Sub RenderTickLike(canvas As SKCanvas,
                               name As String,
                               list As List(Of IndicatorResult),
                               candles As List(Of CandleItem),
                               state As SafeChartState,
                               geo As SafeChartGeometry,
                               panelRect As SKRect,
                               colorIndex As Integer,
                               legendIndex As Integer)
        Dim endIdx As Integer = Math.Min(state.EndIndex(candles.Count), list.Count - 1)
        If endIdx < state.StartIndex Then Return

        Dim maxVal As Single = 1.0F
        For i As Integer = state.StartIndex To endIdx
            Dim v As Single = Math.Abs(list(i).Val("TickSum"))
            If Not Single.IsNaN(v) AndAlso v > maxVal Then maxVal = v
        Next

        Dim zeroY As Single = geo.YForIndicator(0.0F, -maxVal, maxVal, panelRect)
        For i As Integer = state.StartIndex To endIdx
            Dim v As Single = list(i).Val("TickSum")
            If Single.IsNaN(v) Then Continue For

            Dim x As Single = geo.XForIndex(i, state)
            Dim y As Single = geo.YForIndicator(v, -maxVal, maxVal, panelRect)
            Dim halfW As Single = Math.Max(1.0F, state.CandleWidth / 2.0F)
            Dim p As SKPaint = If(v >= 0, _histBull, _histBear)
            canvas.DrawRect(New SKRect(x - halfW, Math.Min(y, zeroY), x + halfW, Math.Max(y, zeroY)), p)
        Next

        DrawLineOnPanel(canvas, list, "MA5", state, geo, panelRect, -maxVal, maxVal, SafeChartPalette.IndicatorColors(1))
        DrawLineOnPanel(canvas, list, "MA20", state, geo, panelRect, -maxVal, maxVal, SafeChartPalette.IndicatorColors(4))
        canvas.DrawText(name, panelRect.Left + 4.0F, panelRect.Top + 14.0F + 14.0F * legendIndex, _textPaint)
    End Sub

    Private Sub DrawLineOnMain(canvas As SKCanvas,
                               results As List(Of IndicatorResult),
                               key As String,
                               state As SafeChartState,
                               geo As SafeChartGeometry,
                               color As SKColor)
        _linePaint.Color = color
        Dim path As New SKPath()
        Dim started As Boolean = False
        Dim endIdx As Integer = Math.Min(state.EndIndex(results.Count), results.Count - 1)

        For i As Integer = state.StartIndex To endIdx
            Dim v As Single = results(i).Val(key)
            If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then
                started = False
                Continue For
            End If

            Dim x As Single = geo.XForIndex(i, state)
            Dim y As Single = geo.YForPrice(v)
            If Not started Then
                path.MoveTo(x, y)
                started = True
            Else
                path.LineTo(x, y)
            End If
        Next

        canvas.DrawPath(path, _linePaint)
    End Sub

    Private Sub DrawLineOnPanel(canvas As SKCanvas,
                                results As List(Of IndicatorResult),
                                key As String,
                                state As SafeChartState,
                                geo As SafeChartGeometry,
                                panelRect As SKRect,
                                minVal As Single,
                                maxVal As Single,
                                color As SKColor)
        _linePaint.Color = color
        Dim path As New SKPath()
        Dim started As Boolean = False
        Dim endIdx As Integer = Math.Min(state.EndIndex(results.Count), results.Count - 1)

        For i As Integer = state.StartIndex To endIdx
            Dim v As Single = results(i).Val(key)
            If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then
                started = False
                Continue For
            End If

            Dim x As Single = geo.XForIndex(i, state)
            Dim y As Single = geo.YForIndicator(v, minVal, maxVal, panelRect)
            If Not started Then
                path.MoveTo(x, y)
                started = True
            Else
                path.LineTo(x, y)
            End If
        Next

        canvas.DrawPath(path, _linePaint)
    End Sub

    Private Shared Function CollectKeys(results As List(Of IndicatorResult)) As List(Of String)
        Dim keys As New List(Of String)()
        If results Is Nothing Then Return keys

        For i As Integer = 0 To results.Count - 1
            Dim r As IndicatorResult = results(i)
            If r Is Nothing OrElse r.Values Is Nothing Then Continue For
            For Each key As String In r.Values.Keys
                If Not keys.Contains(key) Then keys.Add(key)
            Next
            If keys.Count > 0 Then Exit For
        Next

        Return keys
    End Function

    Private Shared Function IsTickIntensityResult(results As List(Of IndicatorResult)) As Boolean
        If results Is Nothing OrElse results.Count = 0 Then Return False
        For i As Integer = 0 To results.Count - 1
            Dim r As IndicatorResult = results(i)
            If r IsNot Nothing AndAlso r.Values IsNot Nothing Then
                Return r.Values.ContainsKey("TickSum")
            End If
        Next
        Return False
    End Function

    Private Shared Function IsNonDrawableKey(key As String) As Boolean
        If String.IsNullOrWhiteSpace(key) Then Return True
        If String.Equals(key, "Direction", StringComparison.OrdinalIgnoreCase) Then Return True
        Return False
    End Function
End Class
