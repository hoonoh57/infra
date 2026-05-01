Option Strict On
Option Explicit On
Option Infer Off

Imports SkiaSharp

Public NotInheritable Class SafeChartPalette
    Private Sub New()
    End Sub

    Public Shared ReadOnly Background As New SKColor(24, 26, 32)
    Public Shared ReadOnly Grid As New SKColor(42, 46, 54)
    Public Shared ReadOnly AxisText As New SKColor(150, 158, 170)
    Public Shared ReadOnly Bull As New SKColor(234, 57, 67)
    Public Shared ReadOnly Bear As New SKColor(46, 134, 222)
    Public Shared ReadOnly BullVolume As New SKColor(234, 57, 67, 90)
    Public Shared ReadOnly BearVolume As New SKColor(46, 134, 222, 90)
    Public Shared ReadOnly PanelBorder As New SKColor(60, 65, 75)
    Public Shared ReadOnly CurrentPrice As New SKColor(255, 193, 7, 210)
    Public Shared ReadOnly Crosshair As New SKColor(120, 130, 150, 180)
    Public Shared ReadOnly TextBright As New SKColor(230, 235, 245)

    Public Shared ReadOnly IndicatorColors As SKColor() = {
        New SKColor(255, 193, 7),
        New SKColor(0, 188, 212),
        New SKColor(233, 30, 99),
        New SKColor(76, 175, 80),
        New SKColor(255, 152, 0),
        New SKColor(171, 71, 188),
        New SKColor(255, 255, 255),
        New SKColor(139, 195, 74)
    }
End Class
