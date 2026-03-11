Imports System.Collections.Generic

Public Class ChartProfileIndicatorItem
    Public Property IndicatorType As String = ""
    Public Property IndicatorName As String = ""
    Public Property DisplayOrder As Integer = 0
    Public Property PanelIndex As Integer = 0
    Public Property Parameters As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
End Class

Public Class ChartProfileContextOptions
    Public Property ShowCurrentPriceLine As Boolean = True
    Public Property ShowPrevCloseLine As Boolean = True
    Public Property ShowViLine As Boolean = False
    Public Property ShowDayChangeLines As Boolean = True
    Public Property ShowCrosshair As Boolean = True
    Public Property IsAutoScaleY As Boolean = True
    Public Property ManualMaxPrice As Single = 0
    Public Property ManualMinPrice As Single = 0
    Public Property CandleWidth As Single = 8
    Public Property Gap As Single = 2
    Public Property VisibleCount As Integer = 120
    Public Property PanelHeightRatio As Single = 0.18F
End Class

Public Class ChartProfileData
    Public Property Indicators As New List(Of ChartProfileIndicatorItem)()
    Public Property ContextOptions As New ChartProfileContextOptions()
    Public Property LastModified As DateTime = DateTime.Now
End Class
