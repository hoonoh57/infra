Option Strict On
Option Explicit On
Option Infer Off

Imports System

Public Class SafeChartOptions
    Public Property ShowVolume As Boolean = True
    Public Property ShowCrosshair As Boolean = True
    Public Property ShowCurrentPriceLine As Boolean = True
    Public Property ShowPrevCloseLine As Boolean = True
    Public Property ShowTickIntensityPanel As Boolean = True

    Public Property InitialVisibleBars As Integer = 100
    Public Property CandleWidth As Single = 8.0F
    Public Property CandleGap As Single = 2.0F

    Public Property MarginLeft As Single = 10.0F
    Public Property MarginRight As Single = 80.0F
    Public Property MarginTop As Single = 8.0F
    Public Property MarginBottom As Single = 24.0F

    Public Property VolumePanelRatio As Single = 0.16F
    Public Property IndicatorPanelRatio As Single = 0.22F

    Public Property TickIndicatorPrefix As String = "TICKINT_"
End Class
