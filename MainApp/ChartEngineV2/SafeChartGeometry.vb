Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports SkiaSharp

Public Class SafeChartGeometry
    Public Property MainRect As SKRect
    Public Property VolumeRect As SKRect
    Public Property IndicatorRect As SKRect

    Public Property PriceHigh As Single = 0.0F
    Public Property PriceLow As Single = 0.0F
    Public Property VolumeMax As Long = 1

    Public Sub Build(width As Integer, height As Integer, options As SafeChartOptions)
        Dim w As Single = CSng(Math.Max(1, width))
        Dim h As Single = CSng(Math.Max(1, height))

        Dim left As Single = options.MarginLeft
        Dim right As Single = w - options.MarginRight
        Dim top As Single = options.MarginTop
        Dim bottom As Single = h - options.MarginBottom

        If right <= left Then right = left + 10
        If bottom <= top Then bottom = top + 10

        Dim totalH As Single = bottom - top
        Dim indH As Single = If(options.ShowTickIntensityPanel, totalH * options.IndicatorPanelRatio, 0.0F)
        Dim volH As Single = If(options.ShowVolume, totalH * options.VolumePanelRatio, 0.0F)
        Dim mainH As Single = totalH - indH - volH - 4.0F

        If mainH < 40.0F Then mainH = Math.Max(20.0F, totalH * 0.65F)

        MainRect = New SKRect(left, top, right, top + mainH)
        VolumeRect = New SKRect(left, MainRect.Bottom + 2.0F, right, MainRect.Bottom + 2.0F + volH)
        IndicatorRect = New SKRect(left, VolumeRect.Bottom + 2.0F, right, bottom)
    End Sub

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
        If Single.IsNaN(value) Then Return IndicatorRect.Bottom
        If maxVal <= minVal Then Return IndicatorRect.Bottom
        Dim r As Single = (maxVal - value) / (maxVal - minVal)
        Return IndicatorRect.Top + r * IndicatorRect.Height
    End Function
End Class
