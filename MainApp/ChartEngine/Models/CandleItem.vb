' ═══════════════════════════════════════════════════════════════
' CandleItem.vb — 캔들(봉) 데이터 모델
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

    ''' <summary>
    ''' 실시간 틱으로 마지막 캔들 업데이트.
    ''' </summary>
    Public Sub UpdateFromTick(price As Single, vol As Long, tickTime As DateTime)
        If price > High Then High = price
        If price < Low OrElse Low = 0 Then Low = price
        Close = price
        Volume += vol
        TradeAmount += CLng(price) * vol
    End Sub

    ''' <summary>새 캔들 초기화용</summary>
    Public Shared Function Create(dt As DateTime, price As Single) As CandleItem
        Return New CandleItem With {
            .Dt = dt, .Open = price, .High = price,
            .Low = price, .Close = price, .Volume = 0}
    End Function

    Public Overrides Function ToString() As String
        Return $"{Dt:yyyy-MM-dd HH:mm} O={Open:N0} H={High:N0} L={Low:N0} C={Close:N0} V={Volume:N0}"
    End Function
End Class
