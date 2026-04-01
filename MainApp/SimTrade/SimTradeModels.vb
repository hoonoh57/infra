' ═══════════════════════════════════════════════════════════════
' SimTradeModels.vb — 모의매매 전용 모델 (격리)
' ═══════════════════════════════════════════════════════════════
' 원칙서 v3.0 기준. 제7조 파라미터, 제12조 상태머신, 제13조 StockState.
' 삭제 시 SimTradeForm.vb와 함께 제거. MainApp 의존성 없음.
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

#Region "열거형"

    ''' <summary>주문 유형</summary>
    Public Enum SimOrderType
        Market = 0
        LimitBestBid = 1
        LimitCurrentPrice = 2
    End Enum

    ''' <summary>종목 데이터 상태 (제12조 상태머신)</summary>
    Public Enum DataState
        None = 0
        Detected = 1
        Downloading = 2
        Analyzing = 3
        Ready = 4
        Trading = 5
        Closed = 6
        Excluded = 7
    End Enum

    ''' <summary>전략 프로파일 모드 (제15조)</summary>
    Public Enum ProfileMode
        Auto = 0
        OnlyA = 1
        OnlyB = 2
    End Enum

    ''' <summary>TickSum 임계값 모드</summary>
    Public Enum TickThresholdMode
        Fixed = 0
        DayMax = 1
    End Enum

    ''' <summary>필터 동작 모드 (제8조)</summary>
    Public Enum FilterMode
        Off = 0
        Observe = 1
        Block = 2
    End Enum

    ''' <summary>파라미터 타입 (ParamDef용)</summary>
    Public Enum ParamType
        IntNumber = 0
        DecNumber = 1
        Bool = 2
        Choice = 3
        TimeSpan = 4
        Money = 5
    End Enum

#End Region

#Region "SimTradeSettings — 제7조 전체 파라미터"

    ''' <summary>모의매매 전략 설정 (원칙서 제7조)</summary>
    Public Class SimTradeSettings

        ' ── 종목 포착 ──
        Public Property ConditionName As String = ""
        Public Property ConditionIndex As Integer = -1
        Public Property UseRealtimeCondition As Boolean = True

        ' ── 모멘텀/거래량 강화 (v4.0) ──
        Public Property MACD_RequireAllPositive As Boolean = True    ' ← 신규
        Public Property VOL_RequireAboveMA As Boolean = True         ' ← 신규

        ' ── 메인 트리거 (축1: 가격 추세) ──
        Public Property ST_Period As Integer = 10
        Public Property ST_Multiplier As Double = 3.0
        Public Property JMA_Period As Integer = 14
        Public Property JMA_Phase As Integer = 50
        Public Property JMA_Power As Integer = 2

        ' ── 메인 트리거 (축2: 시장 참여) ──
        Public Property TICKINT_Timeframe As Integer = 1
        Public Property TICKINT_Threshold As Double = 5.0
        Public Property TICKINT_NormalizeToMinute As Boolean = True
        Public Property TICKINT_UseReferenceCandle As Boolean = True
        Public Property TICKINT_RatioMin As Double = 0.8

        ' ── 메인 트리거 (축3: 자금 흐름) ──
        Public Property OBV_MAPeriod As Integer = 20

        ' ── 컨펌 ──
        Public Property ConfirmBars As Integer = 3
        Public Property ConfirmBars_JMA As Integer = 2
        Public Property ConfirmBars_MACD As Integer = 3              ' ← 신규

        ' ── 보조 지표 ──
        Public Property RSI_Period As Integer = 14
        Public Property RSI_MomentumLower As Double = 60.0
        Public Property RSI_OverboughtLimit As Double = 75.0
        Public Property RequireVolumeConfirm As Boolean = True    ' ← 이 줄 추가
        Public Property VOL_Period As Integer = 20
        Public Property MACD_Fast As Integer = 7
        Public Property MACD_Slow As Integer = 14
        Public Property MACD_Signal As Integer = 9

        ' ── 캔들/타이밍 ──
        Public Property CandleIntervalSec As Integer = 10
        Public Property MinCandlesForSignal As Integer = 30
        Public Property TradingStartTime As TimeSpan = TimeSpan.Parse("09:05")
        Public Property NoNewBuyAfter As TimeSpan = TimeSpan.Parse("14:30")
        Public Property ForceCloseTime As TimeSpan = TimeSpan.Parse("15:15")
        ' ── 동적 캔들 주기 (v4.0) ──
        Public Property CandleInterval_Open As Integer = 10          ' ← 신규 09:00~09:10
        Public Property CandleInterval_EarlyMorning As Integer = 20  ' ← 신규 09:10~09:30
        Public Property CandleInterval_Normal As Integer = 30        ' ← 신규 09:30~14:30
        Public Property CandleInterval_Close As Integer = 30         ' ← 신규 14:30~15:15
        Public Property Phase_Open_End As TimeSpan = TimeSpan.Parse("09:10")          ' ← 신규
        Public Property Phase_EarlyMorning_End As TimeSpan = TimeSpan.Parse("09:30")  ' ← 신규
        Public Property Phase_Normal_End As TimeSpan = TimeSpan.Parse("14:30")        ' ← 신규
        Public Property EarlyPhase_ConfirmBars_JMA As Integer = 5    ' ← 신규

        Public Property CooldownSec As Integer = 300
        ' ── Grace Period (v4.0) ──
        Public Property GracePeriod_Bars As Integer = 5              ' ← 신규
        Public Property GracePeriod_ExitConditions As Integer = 2    ' ← 신규
        Public Property MinProfitInGrace As Double = 0.5             ' ← 신규
        Public Property MaxHoldWithoutNewHigh As Integer = 30        ' ← 신규

        ' ── 포지션/리스크 ──
        Public Property MaxPositionCount As Integer = 5
        Public Property PositionSizeRate As Double = 0.15
        Public Property StopLossRate As Double = -3.0
        Public Property TakeProfitRate As Double = 5.0
        Public Property TrailingStopRate As Double = -1.5
        Public Property TightenedTrailingRate As Double = -0.8       ' ← 신규
        Public Property EnableTrailingStop As Boolean = True
        Public Property MinRiskReward As Double = 1.2
        Public Property MaxSpreadRate As Double = 0.5

        ' ── 비용 ──
        Public Property BuyCommissionRate As Double = 0.015
        Public Property SellCommissionRate As Double = 0.015
        Public Property TransactionTaxRate As Double = 0.20
        Public Property EstimatedSlippage As Double = 0.3

        ' ── 주문 ──
        Public Property BuyOrderType As SimOrderType = SimOrderType.LimitBestBid
        Public Property SellOrderType As SimOrderType = SimOrderType.Market

        ' ── 전략 프로파일 (제15조) ──
        Public Property ActiveProfileMode As ProfileMode = ProfileMode.Auto
        Public Property ProfileA_EndTime As TimeSpan = TimeSpan.Parse("11:30")
        Public Property ProfileA_SwitchToB As Boolean = True
        Public Property ProfileB_TickMode As TickThresholdMode = TickThresholdMode.Fixed
        Public Property ProfileB_DayMaxRatio As Double = 0.6

        ' ── Adaptive (제2조 2-4) ──
        Public Property AdaptiveMode As Boolean = False
        Public Property Adaptive_LookbackDays As Integer = 20
        Public Property Adaptive_TickSumMultiplier As Double = 1.2
        Public Property Adaptive_RSI_Percentile As Double = 25.0

        ' ── 사전 데이터 (제12조) ──
        Public Property PreMarket_DailyDays As Integer = 60
        Public Property PreMarket_MinuteDays As Integer = 5
        Public Property PreMarket_TickDays As Integer = 2
        Public Property RefCandle_LookbackDays As Integer = 10
        Public Property RefCandle_VolumeMultiple As Double = 2.0
        Public Property MaxParallelDownload As Integer = 3

        ' ── 제외 조건 (제10조) ──
        Public Property Exclude_MA_Period As Integer = 200
        Public Property Exclude_MinAvgDailyAmount As Long = 500000000
        Public Property Exclude_MaxDayGain As Double = 20.0
        Public Property Exclude_MinMorningTickSum As Double = 2.0
        Public Property Exclude_ST_DownBars As Integer = 60
        Public Property Exclude_MinAmountBy10AM As Long = 300000000
        Public Property Exclude_VI_NoRecovery As Boolean = True

    End Class

#End Region

#Region "StockState — 제13조 단일 진실 소스"

    ''' <summary>종목별 전체 상태 (StateManager가 관리)</summary>
    Public Class StockState

        ' ── 기본 정보 ──
        Public Property Code As String = ""
        Public Property Name As String = ""
        Public Property State As DataState = DataState.None
        Public Property ExclusionReason As String = ""

        ' ── 실시간 시세 ──
        Public Property CurrentPrice As Integer = 0
        Public Property Ask1 As Integer = 0
        Public Property Bid1 As Integer = 0
        Public Property DayOpen As Integer = 0
        Public Property PrevClose As Integer = 0
        Public Property ChangeRate As Double = 0
        Public Property DayVolume As Long = 0
        Public Property DayAmount As Long = 0
        Public Property Strength As Double = 0
        Public Property UpperLimitPrice As Integer = 0               ' ← 신규 상한가
        Public Property VI_NearRate As Double = 0.9                  ' ← 신규 VI 근접 판정 비율

        ' ── 캔들 ──
        Public Property Candles As New List(Of CandleItem)
        Public Property CurrentCandleStart As DateTime = DateTime.MinValue

        ' ── 지표 최신값 ──
        Public Property ST_Direction As Double = Double.NaN
        Public Property JMA_Direction As Double = Double.NaN
        Public Property JMA_PrevDirection As Double = Double.NaN
        Public Property JMA_TurnBar As Integer = -1
        Public Property TickBarCount As Integer = 0
        Public Property TickSum_Normalized As Double = Double.NaN
        Public Property TickMA5_Normalized As Double = Double.NaN
        Public Property TickMA20_Normalized As Double = Double.NaN
        Public Property OBV_Direction As Double = Double.NaN
        Public Property RSI_Value As Double = Double.NaN
        Public Property MACD_Histogram As Double = Double.NaN
        Public Property Volume_Ratio As Double = Double.NaN

        ' ── 기준봉 (제2조 2-3) ──
        Public Property ReferenceCandleHigh As Integer = 0
        Public Property ReferenceCandleTickSum As Double = 0
        Public Property ReferenceCandleVolume As Long = 0
        Public Property ReferenceCandleDate As DateTime = DateTime.MinValue
        Public Property HasReferenceCandle As Boolean = False

        ' ── 포지션 ──
        Public Property HasPosition As Boolean = False
        Public Property BuyPrice As Integer = 0
        Public Property BuyQty As Integer = 0
        Public Property BuyTime As DateTime = DateTime.MinValue
        Public Property HighSinceBuy As Integer = 0
        Public Property CurrentPnLRate As Double = 0

        ' ── 신호/필터 ──
        Public Property LastSignal As String = ""
        Public Property LastSignalTime As DateTime = DateTime.MinValue
        Public Property LastBuyTime As DateTime = DateTime.MinValue
        Public Property FilterResults As New Dictionary(Of String, Boolean)

        ' ── 지표 엔진 참조 ──
        Public Property Engine As New IndicatorEngine
        Public Property IsSubscribed As Boolean = False
        Public Property AddedTime As DateTime = DateTime.Now

        ' ── 당일 통계 ──
        Public Property DayMaxTickSum As Double = 0
        Public Property MorningAvgTickSum As Double = 0
        Public Property AmountBy10AM As Long = 0

    End Class

#End Region

#Region "ParamDef — 파라미터 정의 테이블"

    ''' <summary>UI 동적 생성용 파라미터 정의</summary>
    Public Class ParamDef
        Public Property Key As String = ""
        Public Property Group As String = ""
        Public Property Label As String = ""
        Public Property ParamType As ParamType = SimTrade.ParamType.DecNumber
        Public Property DefaultValue As Object = Nothing
        Public Property MinValue As Object = Nothing
        Public Property MaxValue As Object = Nothing
        Public Property StepValue As Object = Nothing
        Public Property Choices As String() = Nothing
        Public Property Tooltip As String = ""
    End Class

#End Region

#Region "FilterDef — 필터 정의 테이블"

    ''' <summary>위험 필터 정의 (제8조)</summary>
    Public Class FilterDef
        Public Property Id As String = ""
        Public Property Category As String = ""
        Public Property DisplayName As String = ""
        Public Property DefaultMode As FilterMode = FilterMode.Observe
        Public Property RelatedParams As String() = Nothing
        Public Property Description As String = ""
    End Class

#End Region

#Region "WatchItem — 하위 호환용 (기존 코드 참조 보전)"

    ''' <summary>기존 WatchItem — StockState로 점진 전환 예정</summary>
    Public Class WatchItem
        Public Property Code As String = ""
        Public Property Name As String = ""
        Public Property CurrentPrice As Integer = 0
        Public Property Ask1 As Integer = 0
        Public Property Bid1 As Integer = 0
        Public Property ChangeRate As Double = 0
        Public Property Volume As Long = 0
        Public Property Strength As Double = 0
        Public Property Candles As New List(Of CandleItem)
        Public Property Engine As New IndicatorEngine
        Public Property LastSignal As String = ""
        Public Property AddedTime As DateTime = DateTime.Now
        Public Property HighSinceBuy As Integer = 0
        Public Property IsSubscribed As Boolean = False
        Public Property CurrentCandleStart As DateTime = DateTime.MinValue
    End Class

#End Region

End Namespace
