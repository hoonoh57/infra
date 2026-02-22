' ═══════════════════════════════════════════════════════════════
' IStrategy.vb — 전략 인터페이스
' ═══════════════════════════════════════════════════════════════

''' <summary>
''' 매매 전략 인터페이스.
''' 지표 결과를 참조하여 매매 신호를 발생시킨다.
''' </summary>
Public Interface IStrategy
    ReadOnly Property Name As String
    ReadOnly Property DisplayName As String

    ''' <summary>이 전략이 요구하는 지표 이름 목록</summary>
    Function RequiredIndicators() As List(Of String)

    ''' <summary>현재 캔들 기준으로 신호 평가</summary>
    Function Evaluate(stockCode As String,
                      candles As List(Of CandleItem),
                      indicatorResults As Dictionary(Of String, List(Of IndicatorResult))) As List(Of StrategySignal)
End Interface
