Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports SkiaSharp

Public Class SafeChartGeometry
    Public Property MainRect As SKRect
    Public Property VolumeRect As SKRect
    Public Property IndicatorRect As SKRect
    Public Property PanelRects As New Dictionary(Of Integer, SKRect)()

    Public Property PriceHigh As Single = 0.0F
    Public Property PriceLow As Single = 0.0F
    Public Property VolumeMax As Long = 1

    Public Sub Build(width As Integer, height As Integer, options As SafeChartOptions)
        Build(width, height, options, New List(Of Integer)())
    End Sub

    Public Sub Build(width As Integer,
                     height As Integer,
                     options As SafeChartOptions,
                     panelIndexes As List(Of Integer))
        Dim w As Single = CSng(Math.Max(1, width))
        Dim h As Single = CSng(Math.Max(1, height))

        Dim left As Single = options.MarginLeft
        Dim right As Single = w - options.MarginRight
        Dim top As Single = options.MarginTop
        Dim bottom As Single = h - options.MarginBottom

        If right <= left Then right = left + 10
        If bottom <= top Then bottom = top + 10

        If panelIndexes Is Nothing Then panelIndexes = New List(Of Integer)()
        panelIndexes.Sort()

        PanelRects.Clear()

        Dim panelCount As Integer = panelIndexes.Count
        Dim totalH As Single = bottom - top
        Dim volH As Single = If(options.ShowVolume, totalH * options.VolumePanelRatio, 0.0F)
        Dim panelTotalH As Single = 0.0F

        If panelCount > 0 Then
            panelTotalH = totalH * Math.Min(0.48F, Math.Max(0.16F, options.IndicatorPanelRatio * panelCount))
        End If

        Dim sepTotal As Single = 2.0F * CSng(Math.Max(0, panelCount)) + 2.0F
        Dim mainH As Single = totalH - volH - panelTotalH - sepTotal

        If mainH < 80.0F Then
            mainH = Math.Max(60.0F, totalH * 0.55F)
            panelTotalH = Math.Max(0.0F, totalH - mainH - volH - sepTotal)
        End If

        MainRect = New SKRect(left, top, right, top + mainH)
        VolumeRect = New SKRect(left, MainRect.Bottom + 2.0F, right, MainRect.Bottom + 2.0F + volH)

        Dim panelTop As Single = VolumeRect.Bottom + 2.0F
        If panelCount > 0 AndAlso panelTotalH > 0.0F Then
            Dim eachH As Single = panelTotalH / CSng(panelCount)
            For i As Integer = 0 To panelIndexes.Count - 1
                Dim panelIndex As Integer = panelIndexes(i)
                Dim r As New SKRect(left, panelTop, right, panelTop + eachH)
                PanelRects(panelIndex) = r
                panelTop = r.Bottom + 2.0F
            Next
            IndicatorRect = New SKRect(left, VolumeRect.Bottom + 2.0F, right, Math.Min(bottom, panelTop))
        Else
            IndicatorRect = New SKRect(left, VolumeRect.Bottom + 2.0F, right, bottom)
        End If
    End Sub

    Public Function GetPanelRect(panelIndex As Integer) As SKRect
        Dim r As SKRect = Nothing
        If PanelRects IsNot Nothing AndAlso PanelRects.TryGetValue(panelIndex, r) Then Return r
        Return IndicatorRect
    End Function

    Public Function XForIndex(index As Integer, state As SafeChartState) As Single
        Dim local As Integer = index - state.StartIndex
        Return MainRect.Left + local * (state.CandleWidth + state.Gap) + state.CandleWidth / 2.0F
    End Function

    Public Function YForPrice(price As Single) As Single
        If PriceHigh <= PriceLow Then Return MainRect.Bottom
        Dim r As Single = (PriceHigh - price) / (PriceHigh - PriceLow)
        Return MainRect.Top + r * MainRect.Height
    End Function

    Public Function YForVolume(vol As Long) As Single
        If VolumeMax <= 0 Then Return VolumeRect.Bottom
        Dim r As Single = CSng(Math.Min(1.0R, CDbl(vol) / CDbl(VolumeMax)))
        Return VolumeRect.Bottom - r * VolumeRect.Height
    End Function

    Public Function YForIndicator(value As Single, minVal As Single, maxVal As Single) As Single
        Return YForIndicator(value, minVal, maxVal, IndicatorRect)
    End Function

    Public Function YForIndicator(value As Single, minVal As Single, maxVal As Single, panelRect As SKRect) As Single
        If Single.IsNaN(value) Then Return panelRect.Bottom
        If maxVal <= minVal Then Return panelRect.Bottom
        Dim r As Single = (maxVal - value) / (maxVal - minVal)
        Return panelRect.Top + r * panelRect.Height
    End Function
End Class
