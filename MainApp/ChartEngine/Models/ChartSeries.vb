' ═══════════════════════════════════════════════════════════════
' ChartSeries.vb — 시리즈 렌더링 스타일 정의
' ═══════════════════════════════════════════════════════════════

''' <summary>차트 시리즈 렌더링 스타일</summary>
Public Enum SeriesStyle
    Line = 0
    DashedLine = 1
    Histogram = 2
    Area = 3
    Dot = 4
    ColoredLine = 5       ' Up/Down 조건부 색상 라인
End Enum

''' <summary>지표 개별 출력 시리즈 정의</summary>
Public Class ChartSeries
    Public Property IndicatorName As String = ""
    Public Property ValueKey As String = ""      ' "Value", "Up", "Down" 등
    Public Property Style As SeriesStyle = SeriesStyle.Line
    Public Property ColorHex As String = "#FFFFFF"
    Public Property StrokeWidth As Single = 1.5F
    Public Property PanelIndex As Integer = 0
    Public Property IsVisible As Boolean = True
    Public Property AltColorHex As String = ""   ' 조건부 색상 라인 대체 색상
End Class
