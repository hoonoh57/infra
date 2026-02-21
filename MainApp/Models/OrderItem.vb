' ═══════════════════════════════════════════════════════════════
' OrderItem.vb — 주문/미체결 모델
' ═══════════════════════════════════════════════════════════════

Imports [Shared]

Public Enum OrderSide
    Buy = 1
    Sell = 2
End Enum

Public Enum OrderPriceType
    Market = 0    ' 시장가
    Limit = 1     ' 지정가
End Enum

Public Enum OrderStatus
    Pending = 0       ' 큐에서 대기
    Submitted = 1     ' API 전송됨
    Accepted = 2      ' 접수됨
    PartialFill = 3   ' 부분체결
    Filled = 4        ' 전량체결
    Cancelled = 5     ' 취소됨
    Rejected = 6      ' 거부됨
    Failed = 7        ' 전송 실패
End Enum

Public Class OrderItem

    ' ─── 식별 ───
    Public Property OrderId As String = ""              ' 내부 주문 ID (GUID)
    Public Property KiwoomOrderNo As String = ""        ' 키움 주문번호 (접수 후 부여)
    Public Property OrigOrderNo As String = ""          ' 원주문번호 (정정/취소 시)

    ' ─── 종목 ───
    Public Property Code As String = ""
    Public Property Name As String = ""

    ' ─── 주문 내용 ───
    Public Property Side As OrderSide = OrderSide.Buy
    Public Property PriceType As OrderPriceType = OrderPriceType.Market
    Public Property OrderQty As Integer = 0             ' 주문수량
    Public Property OrderPrice As Integer = 0           ' 주문가격 (시장가=0)
    Public Property FilledQty As Integer = 0            ' 체결수량
    Public Property FilledPrice As Integer = 0          ' 체결가격
    Public Property UnfilledQty As Integer = 0          ' 미체결수량

    ' ─── 상태 ───
    Public Property Status As OrderStatus = OrderStatus.Pending
    Public Property StatusText As String = ""
    Public Property Message As String = ""

    ' ─── 시간 ───
    Public Property RequestTime As DateTime = DateTime.Now
    Public Property AcceptTime As DateTime = DateTime.MinValue
    Public Property FillTime As DateTime = DateTime.MinValue

    ' ─── 전략 연결 ───
    Public Property StrategyName As String = ""         ' 어떤 전략이 발주했는지
    Public Property Reason As String = ""               ' 발주 사유

    ''' <summary>Chejan 주문체결 이벤트로 업데이트</summary>
    Public Sub UpdateFromChejan(m As Msg)
        Dim kno = m.Str("주문번호") : If kno <> "" Then KiwoomOrderNo = kno
        Dim nm = m.Str("종목명") : If nm <> "" Then Name = nm

        StatusText = m.Str("주문상태")

        Dim fq = SharedUtil.SafeInt(m.Str("체결량"))
        If fq > 0 Then
            FilledQty += fq
            UnfilledQty = OrderQty - FilledQty
        End If

        Dim fp = SharedUtil.SafeInt(m.Str("체결가"))
        If fp > 0 Then FilledPrice = fp

        Dim uq = SharedUtil.SafeInt(m.Str("미체결수량"))
        If uq >= 0 Then UnfilledQty = uq

        ' 상태 판단
        If StatusText.Contains("체결") AndAlso UnfilledQty = 0 Then
            Status = OrderStatus.Filled
            FillTime = DateTime.Now
        ElseIf StatusText.Contains("체결") AndAlso UnfilledQty > 0 Then
            Status = OrderStatus.PartialFill
        ElseIf StatusText.Contains("접수") Then
            Status = OrderStatus.Accepted
            AcceptTime = DateTime.Now
        ElseIf StatusText.Contains("확인") Then
            Status = OrderStatus.Accepted
        ElseIf StatusText.Contains("취소") Then
            Status = OrderStatus.Cancelled
        End If
    End Sub

    ''' <summary>초기 동기화용 (OPT10075 미체결)</summary>
    Public Sub UpdateFromTrSync(row As Dictionary(Of String, String))
        If row Is Nothing Then Return

        If row.ContainsKey("종목코드") Then Code = SharedUtil.NormalizeCode(row("종목코드"))
        If row.ContainsKey("종목명") Then Name = row("종목명").Trim()
        If row.ContainsKey("주문번호") Then KiwoomOrderNo = row("주문번호").Trim()
        If row.ContainsKey("주문상태") Then StatusText = row("주문상태").Trim()

        Dim oq = 0 : If row.ContainsKey("주문수량") Then Integer.TryParse(row("주문수량").Trim(), oq)
        OrderQty = oq

        Dim op = 0 : If row.ContainsKey("주문가격") Then Integer.TryParse(row("주문가격").Trim().Replace(",", ""), op)
        OrderPrice = Math.Abs(op)

        Dim uq = 0 : If row.ContainsKey("미체결수량") Then Integer.TryParse(row("미체결수량").Trim(), uq)
        UnfilledQty = uq

        FilledQty = OrderQty - UnfilledQty

        Dim gub = If(row.ContainsKey("주문구분"), row("주문구분").Trim(), "")
        If gub.Contains("매수") Then Side = OrderSide.Buy
        If gub.Contains("매도") Then Side = OrderSide.Sell

        Status = OrderStatus.Accepted
    End Sub

    Public ReadOnly Property SideText As String
        Get
            Return If(Side = OrderSide.Buy, "매수", "매도")
        End Get
    End Property

    Public ReadOnly Property IsDone As Boolean
        Get
            Return Status = OrderStatus.Filled OrElse Status = OrderStatus.Cancelled OrElse
                   Status = OrderStatus.Rejected OrElse Status = OrderStatus.Failed
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return $"[{Status}] {SideText} {Code} {Name} {OrderQty}주 @{OrderPrice:N0} 체결:{FilledQty}"
    End Function

End Class
