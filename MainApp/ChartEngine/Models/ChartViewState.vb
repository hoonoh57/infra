' ═══════════════════════════════════════════════════════════════
' ChartViewState.vb — 차트 뷰 상태
' ═══════════════════════════════════════════════════════════════

''' <summary>차트 뷰(줌, 스크롤, 십자선 등) 상태</summary>
Public Class ChartViewState
    Public Property StartIndex As Integer = 0
    Public Property CandleWidth As Single = 8
    Public Property Gap As Single = 2
    Public Property VisibleCount As Integer = 120
    Public Property ShowCrosshair As Boolean = True
    Public Property CrosshairX As Single = 0
    Public Property CrosshairY As Single = 0
    Public Property PanelHeightRatio As Single = 0.18F  ' 패널 높이 비율

    Public ReadOnly Property EndIndex As Integer
        Get
            Return StartIndex + VisibleCount - 1
        End Get
    End Property

    Public Sub ZoomIn()
        If CandleWidth < 40 Then
            CandleWidth += 2
            VisibleCount = Math.Max(10, VisibleCount - 5)
        End If
    End Sub

    Public Sub ZoomOut()
        If CandleWidth > 3 Then
            CandleWidth -= 2
            VisibleCount = VisibleCount + 5
        End If
    End Sub
End Class
