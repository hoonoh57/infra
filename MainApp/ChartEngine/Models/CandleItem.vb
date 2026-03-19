' ═══════════════════════════════════════════════════════════════
' CandleItem.vb — 캔들(봉) 데이터 모델
' ═══════════════════════════════════════════════════════════════
' v4.0 수정: TickCount, IntervalSec, NormalizedTickSum 추가
' ═══════════════════════════════════════════════════════════════

''' <summary>캔들(봉) 하나의 OHLCV + 거래대금 데이터</summary>
Public Class CandleItem

    Public Property Dt As DateTime
    Public Property Open As Single
    Public Property High As Single
    Public Property Low As Single
    Public Property Close As Single
    Public Property Volume As Long
    Public Property TradeAmount As Long = 0        ' 거래대금 (원)

    ' ── v4.0 추가 ──
    ''' <summary>이 캔들 구간 내 체결 건수 (틱 카운트)</summary>
    Public Property TickCount As Integer = 0

    ''' <summary>이 캔들의 시간 간격(초). 동적 캔들 전환 시 캔들마다 다를 수 있음.</summary>
    Public Property IntervalSec As Integer = 0

    ''' <summary>1분 정규화 TickSum = TickCount × (60 / IntervalSec). CandleBuilder가 계산.</summary>
    Public Property NormalizedTickSum As Double = 0

    ''' <summary>
    ''' 실시간 틱으로 마지막 캔들 업데이트.
    ''' </summary>
    Public Sub UpdateFromTick(price As Single, vol As Long, tickTime As DateTime)
        If price > High Then High = price
        If price < Low OrElse Low = 0 Then Low = price
        Close = price
        Volume += vol
        TradeAmount += CLng(price) * vol
        TickCount += 1
    End Sub

    ''' <summary>새 캔들 초기화용</summary>
    Public Shared Function Create(dt As DateTime, price As Single) As CandleItem
        Return New CandleItem With {
            .Dt = dt, .Open = price, .High = price,
            .Low = price, .Close = price, .Volume = 0,
            .TickCount = 1}
    End Function

    ''' <summary>양봉 여부</summary>
    Public ReadOnly Property IsBullish As Boolean
        Get
            Return Close >= Open
        End Get
    End Property

    ''' <summary>음봉 여부</summary>
    Public ReadOnly Property IsBearish As Boolean
        Get
            Return Close < Open
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return $"{Dt:yyyy-MM-dd HH:mm:ss} O={Open:N0} H={High:N0} L={Low:N0} C={Close:N0} V={Volume:N0} T={TickCount}"
    End Function
End Class
