' ═══════════════════════════════════════════════════════════════
' SimTradeModels.vb — 모의매매 전용 모델 (격리)
' ═══════════════════════════════════════════════════════════════
' 삭제 시 SimTradeForm.vb와 함께 제거. MainApp 의존성 없음.
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

    ''' <summary>모의매매 전략 설정 (실험 파라미터)</summary>
    Public Class SimTradeSettings
        ' ── 종목 포착 ──
        Public Property ConditionName As String = ""
        Public Property ConditionIndex As Integer = -1
        Public Property UseRealtimeCondition As Boolean = True  ' 실시간 편입

        ' ── 캔들/지표 ──
        Public Property CandleIntervalSec As Integer = 10       ' 틱 → 캔들 주기
        Public Property MinCandlesForSignal As Integer = 30     ' 신호 판단 최소 캔들

        ' ── 매수 조건 (SuperTrend 기반) ──
        Public Property ST_Period As Integer = 10
        Public Property ST_Multiplier As Double = 3.0
        Public Property RSI_Period As Integer = 14
        Public Property RSI_OverboughtLimit As Double = 75.0    ' 이 이상이면 매수 금지
        Public Property RequireVolumeConfirm As Boolean = True  ' 거래량 확인

        ' ── 포지션/리스크 ──
        Public Property MaxPositionCount As Integer = 5
        Public Property PositionSizeRate As Double = 0.15       ' 총자산 대비 종목당 비중
        Public Property StopLossRate As Double = -3.0           ' 손절 %
        Public Property TakeProfitRate As Double = 5.0          ' 익절 %
        Public Property TrailingStopRate As Double = -1.5       ' 고점 대비 하락 시 매도
        Public Property EnableTrailingStop As Boolean = True

        ' ── 시간 ──
        Public Property TradingStartTime As TimeSpan = TimeSpan.Parse("09:05")
        Public Property NoNewBuyAfter As TimeSpan = TimeSpan.Parse("14:30")
        Public Property ForceCloseTime As TimeSpan = TimeSpan.Parse("15:15")

        ' ── 주문 방식 (모의매매 제약) ──
        Public Property BuyOrderType As SimOrderType = SimOrderType.LimitBestBid  ' 매수: 최우선매도호가 지정가
        Public Property SellOrderType As SimOrderType = SimOrderType.Market        ' 매도: 시장가
    End Class

    Public Enum SimOrderType
        Market = 0          ' 시장가
        LimitBestBid = 1    ' 최우선 매도호가(매수) / 매수호가(매도) 지정가
        LimitCurrentPrice = 2  ' 현재가 지정가
    End Enum

    ''' <summary>종목별 추적 상태 (폼 내부 전용)</summary>
    Public Class WatchItem
        Public Property Code As String = ""
        Public Property Name As String = ""
        Public Property CurrentPrice As Integer = 0
        Public Property Ask1 As Integer = 0             ' 최우선 매도호가
        Public Property Bid1 As Integer = 0             ' 최우선 매수호가
        Public Property ChangeRate As Double = 0
        Public Property Volume As Long = 0
        Public Property Strength As Double = 0          ' 체결강도
        Public Property Candles As New List(Of CandleItem)
        Public Property Engine As New IndicatorEngine
        Public Property LastSignal As String = ""       ' "매수대기"/"보유중"/"신호없음"
        Public Property AddedTime As DateTime = DateTime.Now
        Public Property HighSinceBuy As Integer = 0     ' 매수 후 최고가 (트레일링용)
        Public Property IsSubscribed As Boolean = False
        ' 캔들 빌딩용
        Public Property CurrentCandleStart As DateTime = DateTime.MinValue
    End Class

End Namespace
