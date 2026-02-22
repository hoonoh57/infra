' ═══════════════════════════════════════════════════════════════
' IIndicator.vb — 지표 인터페이스
' ═══════════════════════════════════════════════════════════════

''' <summary>
''' 모든 지표가 구현해야 할 인터페이스.
''' Calculate: 전체 캔들 → 전체 결과
''' UpdateLast: 마지막 캔들 변경 → 마지막 결과만 증분 갱신
''' </summary>
Public Interface IIndicator
    ReadOnly Property Name As String
    ReadOnly Property DisplayName As String
    ReadOnly Property PanelIndex As Integer           ' 0=오버레이, 1+=하단 패널
    Property Parameters As Dictionary(Of String, Object)

    Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult)
    Function UpdateLast(candles As List(Of CandleItem),
                        prevResults As List(Of IndicatorResult)) As IndicatorResult
End Interface
