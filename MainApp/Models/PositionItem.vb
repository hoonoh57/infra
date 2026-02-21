' ═══════════════════════════════════════════════════════════════
' PositionItem.vb — 보유종목(포지션) 모델
' ═══════════════════════════════════════════════════════════════
' Chejan 잔고변경 이벤트로만 업데이트. TR 조회 없음.
' ═══════════════════════════════════════════════════════════════

Imports [Shared]

Public Class PositionItem

    Public Property Code As String = ""
    Public Property Name As String = ""
    Public Property Quantity As Integer = 0          ' 보유수량
    Public Property AvailableQty As Integer = 0      ' 매도가능수량
    Public Property AvgPrice As Integer = 0          ' 평균매입가
    Public Property CurrentPrice As Integer = 0      ' 현재가 (실시간)
    Public Property PurchaseAmount As Long = 0       ' 매입금액
    Public Property EvalAmount As Long = 0           ' 평가금액
    Public Property ProfitLoss As Long = 0           ' 평가손익
    Public Property ProfitRate As Double = 0         ' 수익률 %
    Public Property LastUpdated As DateTime = DateTime.Now

    ''' <summary>현재가 기준 평가 재계산</summary>
    Public Sub Recalculate()
        If Quantity > 0 AndAlso AvgPrice > 0 Then
            PurchaseAmount = CLng(AvgPrice) * Quantity
            EvalAmount = CLng(CurrentPrice) * Quantity
            ProfitLoss = EvalAmount - PurchaseAmount
            ProfitRate = If(PurchaseAmount > 0, (CDbl(ProfitLoss) / PurchaseAmount) * 100, 0)
        Else
            PurchaseAmount = 0
            EvalAmount = 0
            ProfitLoss = 0
            ProfitRate = 0
        End If
        LastUpdated = DateTime.Now
    End Sub

    ''' <summary>Chejan 잔고변경 이벤트로 업데이트</summary>
    Public Sub UpdateFromChejan(m As Msg)
        Dim nm = m.Str("종목명") : If nm <> "" Then Name = nm

        Dim qty = SharedUtil.SafeInt(m.Str("보유수량"))
        If qty >= 0 Then
            Quantity = qty
            AvailableQty = qty   ' 체결 직후에는 전량 매도 가능으로 간주
        End If

        Dim avg = SharedUtil.SafeInt(m.Str("매입가"))
        If avg > 0 Then AvgPrice = avg

        Dim cur = SharedUtil.SafeInt(m.Str("현재가"))
        If cur > 0 Then CurrentPrice = cur

        Dim pr = SharedUtil.SafeDouble(m.Str("손익율"), True)
        ProfitRate = pr

        Recalculate()
    End Sub

    ''' <summary>실시간 틱으로 현재가 업데이트</summary>
    Public Sub UpdatePrice(price As Integer)
        If price > 0 Then
            CurrentPrice = price
            Recalculate()
        End If
    End Sub

    ''' <summary>초기 동기화용 (OPW00018 결과)</summary>
    Public Sub UpdateFromTrSync(row As Dictionary(Of String, String))
        If row Is Nothing Then Return

        If row.ContainsKey("종목명") Then Name = row("종목명").Trim()

        Dim q = 0 : If row.ContainsKey("보유수량") Then Integer.TryParse(row("보유수량").Trim(), q)
        Quantity = q

        Dim aq = 0 : If row.ContainsKey("매매가능수량") Then Integer.TryParse(row("매매가능수량").Trim(), aq)
        AvailableQty = If(aq > 0, aq, q)

        Dim ap = 0 : If row.ContainsKey("매입가") Then Integer.TryParse(row("매입가").Trim().Replace(",", ""), ap)
        AvgPrice = Math.Abs(ap)

        Dim cp = 0 : If row.ContainsKey("현재가") Then Integer.TryParse(row("현재가").Trim().Replace(",", ""), cp)
        CurrentPrice = Math.Abs(cp)

        Recalculate()
    End Sub

    Public Overrides Function ToString() As String
        Return $"{Code} {Name} {Quantity}주 @{AvgPrice:N0} → {CurrentPrice:N0} ({ProfitRate:+0.00;-0.00}%)"
    End Function

End Class
