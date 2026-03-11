' ═══════════════════════════════════════════════════════════════
' TradeManager.vb — 중앙 매매관리자
' ═══════════════════════════════════════════════════════════════
'
' ★ 핵심 설계 원칙:
'   1) 잔고/미체결은 Chejan 이벤트로만 실시간 추적 (TR 조회 금지)
'   2) 프로그램 시작 시 1회만 OPW00018 + OPT10075로 초기 동기화
'   3) 모든 주문은 OrderQueue를 통해 직렬화 (초당 제한 준수)
'   4) 중복 주문, 과다 주문, 잔고 초과를 사전 차단
'   5) 전략은 TradeManager에게 "매매 의도"만 전달하면 됨
'
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Concurrent
Imports System.Threading
Imports [Shared]

Public Class TradeManager

    ' ─── 싱글톤 ───
    Private Shared _instance As TradeManager
    Private Shared ReadOnly _singletonLock As New Object()

    Public Shared ReadOnly Property I As TradeManager
        Get
            If _instance Is Nothing Then
                SyncLock _singletonLock
                    If _instance Is Nothing Then _instance = New TradeManager()
                End SyncLock
            End If
            Return _instance
        End Get
    End Property

    ' ═══════════════════════════════════════
    ' 인메모리 상태 (TR 조회 없이 유지)
    ' ═══════════════════════════════════════

    ''' <summary>보유종목 (code → PositionItem)</summary>
    Private ReadOnly _positions As New ConcurrentDictionary(Of String, PositionItem)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>활성 주문 (orderId → OrderItem). 완료된 주문은 _orderHistory로 이동.</summary>
    Private ReadOnly _activeOrders As New ConcurrentDictionary(Of String, OrderItem)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>키움주문번호 → 내부 orderId 매핑</summary>
    Private ReadOnly _kiwoomToOrderId As New ConcurrentDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>완료된 주문 이력 (최근 500건)</summary>
    Private ReadOnly _orderHistory As New ConcurrentQueue(Of OrderItem)()
    Private Const MAX_HISTORY As Integer = 500

    ''' <summary>예수금/주문가능금액 (초기 동기화 시 설정)</summary>
    Public Property AvailableCash As Long = 0

    ''' <summary>계좌번호</summary>
    Public Property AccountNo As String = ""

    ''' <summary>자동매매 활성화 여부</summary>
    Public Property AutoTradeEnabled As Boolean = False

    ''' <summary>초기 동기화 완료 여부</summary>
    Public Property IsSynced As Boolean = False

    ' ═══════════════════════════════════════
    ' 리스크 설정
    ' ═══════════════════════════════════════

    ''' <summary>종목당 최대 보유 금액</summary>
    Public Property MaxPositionAmount As Long = 5_000_000

    ''' <summary>종목당 최대 보유 수량</summary>
    Public Property MaxPositionQty As Integer = 1000

    ''' <summary>전체 최대 보유 종목 수</summary>
    Public Property MaxPositionCount As Integer = 20

    ''' <summary>동일 종목 동시 미체결 주문 최대 수</summary>
    Public Property MaxPendingOrdersPerStock As Integer = 2

    ''' <summary>손절 비율 (%)</summary>
    Public Property StopLossRate As Double = -3.0

    ''' <summary>익절 비율 (%)</summary>
    Public Property TakeProfitRate As Double = 5.0

    ' ═══════════════════════════════════════
    ' 주문 큐 (초당 제한 준수)
    ' ═══════════════════════════════════════

    Private ReadOnly _orderQueue As New ConcurrentQueue(Of OrderItem)()
    Private ReadOnly _queueTimer As Timer
    Private ReadOnly _recentSends As New List(Of Long)()
    Private ReadOnly _queueLock As New Object()
    Private Const MAX_ORDERS_PER_SEC As Integer = 3  ' 안전 마진 (제한 5 중 3만 사용)

    ' ═══════════════════════════════════════
    ' 초기화
    ' ═══════════════════════════════════════

    Private Sub New()
        ' 주문 큐 타이머 (300ms 간격)
        _queueTimer = New Timer(AddressOf ProcessOrderQueue, Nothing, 1000, 300)

        ' ── Bus 구독 ──

        ' 체잔 이벤트 (★ 가장 중요: API 호출 없이 잔고/주문 추적)
        MessageBus.I.On(Topics.ORDER_EXECUTED, AddressOf OnChejanOrder)
        MessageBus.I.On(Topics.ORDER_BALANCE_CHANGED, AddressOf OnChejanBalance)

        ' 실시간 틱 → 보유종목 현재가 업데이트
        MessageBus.I.On(Topics.TICK, AddressOf OnTick)

        ' 주문 요청 (전략/플러그인 → TradeManager)
        MessageBus.I.On(Topics.TRADE_ORDER_REQUEST, AddressOf OnOrderRequest)

        ' 자동매매 토글
        MessageBus.I.On(Topics.SYS_AUTOTRADE, AddressOf OnAutoTradeToggle)

        ' 동기화 요청
        MessageBus.I.On(Topics.TRADE_SYNC_REQUEST, AddressOf OnSyncRequest)

        ' 로그인 결과 → 자동 동기화
        MessageBus.I.On(Topics.AUTH_LOGIN_RESULT, AddressOf OnLoginResult)

        AppLogger.I.Info("TradeManager 초기화 완료", "Trade")
    End Sub

    ' ═══════════════════════════════════════
    ' 공개 조회 (인메모리, API 호출 없음!)
    ' ═══════════════════════════════════════

    ''' <summary>보유종목 전체 조회</summary>
    Public Function GetPositions() As List(Of PositionItem)
        Return _positions.Values.Where(Function(p) p.Quantity > 0).ToList()
    End Function

    ''' <summary>특정 종목 보유 조회</summary>
    Public Function GetPosition(code As String) As PositionItem
        Dim p As PositionItem = Nothing
        _positions.TryGetValue(code, p)
        Return p
    End Function

    ''' <summary>특정 종목 보유수량</summary>
    Public Function GetHoldingQty(code As String) As Integer
        Dim p = GetPosition(code)
        Return If(p IsNot Nothing, p.Quantity, 0)
    End Function

    ''' <summary>특정 종목 매도가능수량</summary>
    Public Function GetAvailableQty(code As String) As Integer
        Dim p = GetPosition(code)
        Return If(p IsNot Nothing, p.AvailableQty, 0)
    End Function

    ''' <summary>특정 종목 보유 여부</summary>
    Public Function HasPosition(code As String) As Boolean
        Return GetHoldingQty(code) > 0
    End Function

    ''' <summary>활성 주문 전체 조회</summary>
    Public Function GetActiveOrders() As List(Of OrderItem)
        Return _activeOrders.Values.ToList()
    End Function

    ''' <summary>특정 종목의 미체결 주문 수</summary>
    Public Function GetPendingOrderCount(code As String) As Integer
        Return _activeOrders.Values.Where(Function(o) o.Code = code AndAlso Not o.IsDone).Count()
    End Function

    ''' <summary>전체 보유종목 수</summary>
    Public ReadOnly Property PositionCount As Integer
        Get
            Return _positions.Values.Where(Function(p) p.Quantity > 0).Count()
        End Get
    End Property

    ''' <summary>전체 평가금액</summary>
    Public ReadOnly Property TotalEvalAmount As Long
        Get
            Return _positions.Values.Where(Function(p) p.Quantity > 0).Sum(Function(p) p.EvalAmount)
        End Get
    End Property

    ''' <summary>전체 평가손익</summary>
    Public ReadOnly Property TotalProfitLoss As Long
        Get
            Return _positions.Values.Where(Function(p) p.Quantity > 0).Sum(Function(p) p.ProfitLoss)
        End Get
    End Property

    ''' <summary>주문 이력</summary>
    Public Function GetOrderHistory() As List(Of OrderItem)
        Return _orderHistory.ToList()
    End Function

    ' ═══════════════════════════════════════
    ' 주문 요청 (전략 → TradeManager)
    ' ═══════════════════════════════════════

    ''' <summary>
    ''' 매매 의도를 전달. TradeManager가 검증 후 큐에 넣음.
    ''' 전략은 이 메서드만 호출하면 됨.
    ''' </summary>
    Public Function RequestOrder(code As String,
                                  side As OrderSide,
                                  qty As Integer,
                                  Optional price As Integer = 0,
                                  Optional priceType As OrderPriceType = OrderPriceType.Market,
                                  Optional strategyName As String = "",
                                  Optional reason As String = "") As OrderItem

        ' 1) 기본 검증
        If Not IsSynced Then
            AppLogger.I.Warn($"주문 거부: 초기 동기화 미완료 ({code})", "Trade")
            Return Nothing
        End If

        If Not AutoTradeEnabled AndAlso strategyName <> "" Then
            AppLogger.I.Warn($"주문 거부: 자동매매 OFF ({code} {side})", "Trade")
            Return Nothing
        End If

        ' 2) 리스크 검증
        Dim rejectReason = ValidateOrder(code, side, qty, price)
        If rejectReason <> "" Then
            AppLogger.I.Warn($"주문 거부: {rejectReason} ({code} {side} {qty}주)", "Trade")

            Dim rm As New Msg(Topics.TRADE_ORDER_REJECTED)
            rm("code") = code
            rm("reason") = rejectReason
            MessageBus.I.EmitOnUI(rm)
            Return Nothing
        End If

        ' 3) 주문 생성
        Dim order As New OrderItem()
        order.OrderId = Guid.NewGuid().ToString("N")
        order.Code = code
        order.Side = side
        order.PriceType = priceType
        order.OrderQty = qty
        order.OrderPrice = price
        order.UnfilledQty = qty
        order.StrategyName = strategyName
        order.Reason = reason
        order.Status = OrderStatus.Pending

        ' 종목명 조회 (인메모리)
        Dim si = StockInfoManager.I.GetItem(code)
        If si IsNot Nothing Then order.Name = si.Name

        ' 4) 큐에 등록
        _activeOrders(order.OrderId) = order
        _orderQueue.Enqueue(order)

        AppLogger.I.Trade($"주문 접수: {order.SideText} {code} {order.Name} {qty}주 @{If(price = 0, "시장가", price.ToString("N0"))} [{strategyName}] {reason}", "Trade")

        Dim am As New Msg(Topics.TRADE_ORDER_ACCEPTED)
        am("orderId") = order.OrderId
        am("code") = code
        am("side") = side.ToString()
        am("qty") = qty
        MessageBus.I.EmitOnUI(am)

        Return order
    End Function

    ' ─── Bus 경유 주문 요청 핸들러 ───

    Private Sub OnOrderRequest(m As Msg)
        Dim code = m.Str("code")
        Dim sideStr = m.Str("side", "buy").ToLower()
        Dim side = If(sideStr.Contains("sell"), OrderSide.Sell, OrderSide.Buy)
        Dim qty = m.Int("qty", 1)
        Dim price = m.Int("price", 0)
        Dim priceType = If(price > 0, OrderPriceType.Limit, OrderPriceType.Market)
        Dim strategy = m.Str("strategy", "")
        Dim reason = m.Str("reason", "")

        RequestOrder(code, side, qty, price, priceType, strategy, reason)
    End Sub

    ' ═══════════════════════════════════════
    ' 리스크 검증
    ' ═══════════════════════════════════════

    Private Function ValidateOrder(code As String, side As OrderSide, qty As Integer, price As Integer) As String

        ' 수량 검증
        If qty <= 0 Then Return "수량이 0 이하"

        ' 동일종목 미체결 주문 수 제한
        If GetPendingOrderCount(code) >= MaxPendingOrdersPerStock Then
            Return $"동일종목 미체결 {MaxPendingOrdersPerStock}건 초과"
        End If

        If side = OrderSide.Buy Then
            ' ── 매수 검증 ──

            ' 보유종목 수 제한
            If Not HasPosition(code) AndAlso PositionCount >= MaxPositionCount Then
                Return $"최대 보유종목 {MaxPositionCount}개 초과"
            End If

            ' 종목당 최대 금액
            Dim estimatedPrice = If(price > 0, price, GetEstimatedPrice(code))
            Dim estimatedAmount = CLng(estimatedPrice) * qty
            Dim currentPos = GetPosition(code)
            Dim currentAmount As Long = If(currentPos IsNot Nothing, currentPos.EvalAmount, 0)
            If currentAmount + estimatedAmount > MaxPositionAmount Then
                Return $"종목당 최대금액 {MaxPositionAmount:N0}원 초과"
            End If

            ' 주문가능금액 (인메모리 추정)
            If AvailableCash > 0 AndAlso estimatedAmount > AvailableCash Then
                Return $"주문가능금액 부족 (필요:{estimatedAmount:N0}, 가용:{AvailableCash:N0})"
            End If

            ' 종목당 최대 수량
            Dim currentQty = GetHoldingQty(code)
            If currentQty + qty > MaxPositionQty Then
                Return $"종목당 최대수량 {MaxPositionQty}주 초과"
            End If

        Else
            ' ── 매도 검증 ──

            Dim availQty = GetAvailableQty(code)
            If availQty < qty Then
                Return $"매도가능수량 부족 (가용:{availQty}, 요청:{qty})"
            End If
        End If

        Return ""  ' 통과
    End Function

    Private Function GetEstimatedPrice(code As String) As Integer
        ' 인메모리에서 현재가 추정
        Dim pos = GetPosition(code)
        If pos IsNot Nothing AndAlso pos.CurrentPrice > 0 Then Return pos.CurrentPrice

        Dim si = StockInfoManager.I.GetItem(code)
        If si IsNot Nothing AndAlso si.Price > 0 Then Return si.Price

        Return 0
    End Function

    ' ═══════════════════════════════════════
    ' 주문 큐 처리 (초당 제한 준수)
    ' ═══════════════════════════════════════

    Private Sub ProcessOrderQueue(state As Object)
        SyncLock _queueLock
            ' 1초 이전 기록 제거
            Dim now = DateTime.Now.Ticks
            Dim oneSecAgo = now - TimeSpan.TicksPerSecond
            _recentSends.RemoveAll(Function(t) t < oneSecAgo)

            ' 여유가 있으면 큐에서 꺼내서 전송
            While _recentSends.Count < MAX_ORDERS_PER_SEC
                Dim order As OrderItem = Nothing
                If Not _orderQueue.TryDequeue(order) Then Exit While

                _recentSends.Add(now)
                SendOrderToApi(order)
            End While
        End SyncLock
    End Sub

    Private Sub SendOrderToApi(order As OrderItem)
        Try
            order.Status = OrderStatus.Submitted

            Dim topic As String
            If order.Side = OrderSide.Buy Then
                topic = If(order.PriceType = OrderPriceType.Market, Topics.ORDER_BUY_MARKET, Topics.ORDER_BUY_LIMIT)
            Else
                topic = If(order.PriceType = OrderPriceType.Market, Topics.ORDER_SELL_MARKET, Topics.ORDER_SELL_LIMIT)
            End If

            ' 매도 시 가용수량 선차감 (중복 매도 방지)
            If order.Side = OrderSide.Sell Then
                Dim pos = GetPosition(order.Code)
                If pos IsNot Nothing Then
                    pos.AvailableQty = Math.Max(0, pos.AvailableQty - order.OrderQty)
                End If
            End If

            ' 매수 시 가용현금 선차감
            If order.Side = OrderSide.Buy Then
                Dim est = CLng(If(order.OrderPrice > 0, order.OrderPrice, GetEstimatedPrice(order.Code))) * order.OrderQty
                AvailableCash = Math.Max(0, AvailableCash - est)
            End If

            MessageBus.I.Emit(topic,
                              "code", order.Code,
                              "qty", order.OrderQty,
                              "price", order.OrderPrice,
                              "accountNo", AccountNo)

            AppLogger.I.Trade($"주문 전송: {order.SideText} {order.Code} {order.OrderQty}주 → API", "Trade")

        Catch ex As Exception
            order.Status = OrderStatus.Failed
            order.Message = ex.Message
            AppLogger.I.Error($"주문 전송 실패: {order.Code} {ex.Message}", "Trade")
            MoveToHistory(order)
        End Try
    End Sub

    ' ═══════════════════════════════════════
    ' Chejan 이벤트 처리 (★ 핵심: TR 없이 상태 추적)
    ' ═══════════════════════════════════════

    ''' <summary>주문체결 이벤트 (sGubun=0)</summary>
    Private Sub OnChejanOrder(m As Msg)
        Dim kiwoomNo = m.Str("주문번호")
        Dim code = m.Str("종목코드")
        If String.IsNullOrEmpty(code) Then code = SharedUtil.NormalizeCode(m.Str("종목코드"))

        ' 내부 주문 찾기
        Dim orderId As String = ""
        Dim order As OrderItem = Nothing

        If kiwoomNo <> "" AndAlso _kiwoomToOrderId.TryGetValue(kiwoomNo, orderId) Then
            _activeOrders.TryGetValue(orderId, order)
        End If

        ' 내부 주문이 없으면 (외부 HTS에서 발주한 주문 등) 추적용으로 생성
        If order Is Nothing AndAlso kiwoomNo <> "" Then
            order = _activeOrders.Values.FirstOrDefault(Function(o) o.Code = code AndAlso o.Status = OrderStatus.Submitted AndAlso o.KiwoomOrderNo = "")
            If order IsNot Nothing Then
                order.KiwoomOrderNo = kiwoomNo
                _kiwoomToOrderId(kiwoomNo) = order.OrderId
            Else
                ' 외부 주문 → 추적 등록
                order = New OrderItem()
                order.OrderId = $"EXT_{kiwoomNo}"
                order.KiwoomOrderNo = kiwoomNo
                order.Code = code
                order.StrategyName = "외부"
                _activeOrders(order.OrderId) = order
                _kiwoomToOrderId(kiwoomNo) = order.OrderId
            End If
        End If

        If order IsNot Nothing Then
            order.UpdateFromChejan(m)

            AppLogger.I.Trade($"체결: {order}", "Trade")

            ' 완료된 주문 → 이력으로 이동
            If order.IsDone Then
                MoveToHistory(order)
            End If

            ' UI 알림
            Dim fm As New Msg(Topics.TRADE_ORDER_FILLED)
            fm("orderId") = order.OrderId
            fm("code") = order.Code
            fm("side") = order.SideText
            fm("filledQty") = order.FilledQty
            fm("filledPrice") = order.FilledPrice
            fm("status") = order.Status.ToString()
            MessageBus.I.EmitOnUI(fm)
        End If
    End Sub

    ''' <summary>잔고변경 이벤트 (sGubun=1)</summary>
    Private Sub OnChejanBalance(m As Msg)
        Dim code = SharedUtil.NormalizeCode(m.Str("종목코드"))
        If String.IsNullOrEmpty(code) Then Return

        Dim pos = _positions.GetOrAdd(code, Function(k)
                                                  Dim p As New PositionItem()
                                                  p.Code = k
                                                  Return p
                                              End Function)

        pos.UpdateFromChejan(m)

        ' 수량 0 → 제거
        If pos.Quantity <= 0 Then
            _positions.TryRemove(code, Nothing)
            AppLogger.I.Trade($"포지션 청산: {code} {pos.Name}", "Trade")
        Else
            AppLogger.I.Trade($"잔고 변경: {pos}", "Trade")
        End If

        ' UI 알림
        Dim pm As New Msg(Topics.TRADE_POSITION_UPDATED)
        pm("code") = code
        pm("name") = pos.Name
        pm("qty") = pos.Quantity
        pm("avgPrice") = pos.AvgPrice
        pm("currentPrice") = pos.CurrentPrice
        pm("profitRate") = pos.ProfitRate
        MessageBus.I.EmitOnUI(pm)

        ' 전체 잔고 알림
        Dim bm As New Msg(Topics.TRADE_BALANCE_UPDATED)
        bm("positionCount") = PositionCount
        bm("totalEval") = TotalEvalAmount
        bm("totalPnl") = TotalProfitLoss
        bm("cash") = AvailableCash
        MessageBus.I.EmitOnUI(bm)
    End Sub

    ' ═══════════════════════════════════════
    ' 실시간 틱 → 보유종목 현재가 업데이트
    ' ═══════════════════════════════════════

    Private _lastStopLossCheck As DateTime = DateTime.MinValue

    Private Sub OnTick(m As Msg)
        Dim code = m.Str("code")
        If String.IsNullOrEmpty(code) Then Return

        Dim pos As PositionItem = Nothing
        If Not _positions.TryGetValue(code, pos) Then Return
        If pos.Quantity <= 0 Then Return

        Dim price = Math.Abs(CInt(m.Dbl("price")))
        If price <= 0 Then Return

        pos.UpdatePrice(price)

        ' ── 손절/익절 체크 (500ms 간격) ──
        Dim now = DateTime.Now
        If (now - _lastStopLossCheck).TotalMilliseconds < 500 Then Return
        _lastStopLossCheck = now

        If AutoTradeEnabled Then
            CheckStopLossTakeProfit(pos)
        End If
    End Sub

    Private Sub CheckStopLossTakeProfit(pos As PositionItem)
        If pos.ProfitRate <= StopLossRate Then
            ' 손절
            AppLogger.I.Trade($"★ 손절 발동: {pos.Code} {pos.Name} ({pos.ProfitRate:0.00}% ≤ {StopLossRate}%)", "Trade")
            RequestOrder(pos.Code, OrderSide.Sell, pos.AvailableQty,
                         priceType:=OrderPriceType.Market,
                         strategyName:="StopLoss",
                         reason:=$"손절 {pos.ProfitRate:0.00}%")

            Dim rm As New Msg(Topics.TRADE_RISK_ALERT)
            rm("type") = "STOPLOSS"
            rm("code") = pos.Code
            rm("rate") = pos.ProfitRate
            MessageBus.I.EmitOnUI(rm)

        ElseIf pos.ProfitRate >= TakeProfitRate Then
            ' 익절
            AppLogger.I.Trade($"★ 익절 발동: {pos.Code} {pos.Name} ({pos.ProfitRate:0.00}% ≥ {TakeProfitRate}%)", "Trade")
            RequestOrder(pos.Code, OrderSide.Sell, pos.AvailableQty,
                         priceType:=OrderPriceType.Market,
                         strategyName:="TakeProfit",
                         reason:=$"익절 {pos.ProfitRate:0.00}%")

            Dim rm As New Msg(Topics.TRADE_RISK_ALERT)
            rm("type") = "TAKEPROFIT"
            rm("code") = pos.Code
            rm("rate") = pos.ProfitRate
            MessageBus.I.EmitOnUI(rm)
        End If
    End Sub

    ' ═══════════════════════════════════════
    ' 초기 동기화 (프로그램 시작 시 1회만)
    ' ═══════════════════════════════════════

    Private Sub OnLoginResult(m As Msg)
        If m.Bool("success") Then
            AccountNo = m.Str("accountNo", "")
            If AccountNo <> "" Then
                AppLogger.I.Info($"로그인 감지. 계좌: {AccountNo}. 초기 동기화 시작...", "Trade")
                StartSync()
            End If
        End If
    End Sub

    Private Sub OnSyncRequest(m As Msg)
        StartSync()
    End Sub

    Public Sub StartSync()
        If String.IsNullOrEmpty(AccountNo) Then
            AppLogger.I.Warn("동기화 불가: 계좌번호 없음", "Trade")
            Return
        End If

        AppLogger.I.Info("═══ 초기 동기화 시작 ═══", "Trade")

        ' 1) 잔고 조회 (OPW00018) — 1회만
        MessageBus.I.On(Topics.ACCOUNT_BALANCE_RESULT, AddressOf OnSyncBalanceResult)
        MessageBus.I.Emit(Topics.ACCOUNT_BALANCE_REQUEST,
                          "accountNo", AccountNo, "pass", "", "media", "00", "query", "2")
    End Sub

    Private Sub OnSyncBalanceResult(m As Msg)
        MessageBus.I.Off(Topics.ACCOUNT_BALANCE_RESULT, AddressOf OnSyncBalanceResult)

        If Not m.Bool("success") Then
            AppLogger.I.Error($"잔고 동기화 실패: {m.Str("message")}", "Trade")
            Return
        End If

        ' Summary — 예수금 정보 (SimpleSerializer로 전달된 flat 데이터일 수 있음)
        If m.Has("summary") Then
            Dim summary = TryCast(m("summary"), Dictionary(Of String, String))
            If summary IsNot Nothing Then
                Dim cash As Long = 0
                If summary.ContainsKey("추정예탁자산") Then Long.TryParse(summary("추정예탁자산").Trim().Replace(",", ""), cash)
                AvailableCash = Math.Abs(cash)
                AppLogger.I.Info($"추정예탁자산: {AvailableCash:N0}원", "Trade")
            End If
        Else
            ' flat 데이터로 전달된 경우
            Dim cashStr = m.Str("추정예탁자산", "")
            If cashStr <> "" Then
                Dim cash As Long = 0
                Long.TryParse(cashStr.Trim().Replace(",", ""), cash)
                AvailableCash = Math.Abs(cash)
                AppLogger.I.Info($"추정예탁자산: {AvailableCash:N0}원", "Trade")
            End If
        End If

        ' Holdings
        Dim items As List(Of Dictionary(Of String, String)) = Nothing
        If m.Has("items") Then items = TryCast(m("items"), List(Of Dictionary(Of String, String)))
        If items IsNot Nothing Then
            For Each row In items
                Dim code = SharedUtil.NormalizeCode(If(row.ContainsKey("종목코드"), row("종목코드"), ""))
                If String.IsNullOrEmpty(code) Then Continue For

                Dim pos = _positions.GetOrAdd(code, Function(k)
                                                          Dim p As New PositionItem()
                                                          p.Code = k
                                                          Return p
                                                      End Function)
                pos.UpdateFromTrSync(row)

                If pos.Quantity <= 0 Then
                    _positions.TryRemove(code, Nothing)
                Else
                    AppLogger.I.Info($"  보유: {pos}", "Trade")
                End If
            Next
        End If

        AppLogger.I.Info($"잔고 동기화 완료: {PositionCount}종목 보유", "Trade")

        ' 2) 미체결 조회 (OPT10075) — 1회만
        MessageBus.I.On(Topics.ACCOUNT_OPEN_ORDERS_RESULT, AddressOf OnSyncOpenOrdersResult)
        MessageBus.I.Emit(Topics.ACCOUNT_OPEN_ORDERS_REQUEST,
                          "accountNo", AccountNo, "pass", "", "media", "00")
    End Sub

    Private Sub OnSyncOpenOrdersResult(m As Msg)
        MessageBus.I.Off(Topics.ACCOUNT_OPEN_ORDERS_RESULT, AddressOf OnSyncOpenOrdersResult)

        If Not m.Bool("success") Then
            AppLogger.I.Error($"미체결 동기화 실패: {m.Str("message")}", "Trade")
            ' 미체결 실패해도 동기화 완료 처리
            FinishSync()
            Return
        End If

        Dim items As List(Of Dictionary(Of String, String)) = Nothing
        If m.Has("items") Then items = TryCast(m("items"), List(Of Dictionary(Of String, String)))
        If items IsNot Nothing Then
            For Each row In items
                Dim order As New OrderItem()
                order.OrderId = $"SYNC_{Guid.NewGuid():N}"
                order.UpdateFromTrSync(row)

                If order.Code <> "" AndAlso order.UnfilledQty > 0 Then
                    _activeOrders(order.OrderId) = order
                    If order.KiwoomOrderNo <> "" Then
                        _kiwoomToOrderId(order.KiwoomOrderNo) = order.OrderId
                    End If
                    AppLogger.I.Info($"  미체결: {order}", "Trade")
                End If
            Next
        End If

        AppLogger.I.Info($"미체결 동기화 완료: {_activeOrders.Count}건", "Trade")
        FinishSync()
    End Sub

    Private Sub FinishSync()
        IsSynced = True
        AppLogger.I.Info("═══ 초기 동기화 완료 ═══", "Trade")
        AppLogger.I.Info($"  보유종목: {PositionCount}개", "Trade")
        AppLogger.I.Info($"  미체결: {_activeOrders.Count}건", "Trade")
        AppLogger.I.Info($"  가용현금: {AvailableCash:N0}원", "Trade")
        AppLogger.I.Info("  ▶ 이후 잔고/주문은 Chejan 이벤트로만 추적 (TR 조회 없음)", "Trade")

        ' 보유종목 실시간 구독
        If PositionCount > 0 Then
            Dim codes = String.Join(";", _positions.Keys)
            MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", codes)
            AppLogger.I.Info($"  보유종목 실시간 구독: {codes}", "Trade")
        End If

        Dim sm As New Msg(Topics.TRADE_SYNC_COMPLETE)
        sm("positionCount") = PositionCount
        sm("orderCount") = _activeOrders.Count
        sm("cash") = AvailableCash
        MessageBus.I.EmitOnUI(sm)
    End Sub

    ' ═══════════════════════════════════════
    ' 자동매매 토글
    ' ═══════════════════════════════════════

    Private Sub OnAutoTradeToggle(m As Msg)
        AutoTradeEnabled = m.Bool("enabled")
        AppLogger.I.Info($"자동매매 {If(AutoTradeEnabled, "ON", "OFF")}", "Trade")
    End Sub

    ' ═══════════════════════════════════════
    ' 편의 메서드 (전략에서 직접 호출 가능)
    ' ═══════════════════════════════════════

    ''' <summary>시장가 매수</summary>
    Public Function BuyMarket(code As String, qty As Integer,
                               Optional strategy As String = "",
                               Optional reason As String = "") As OrderItem
        Return RequestOrder(code, OrderSide.Buy, qty, 0, OrderPriceType.Market, strategy, reason)
    End Function

    ''' <summary>지정가 매수</summary>
    Public Function BuyLimit(code As String, qty As Integer, price As Integer,
                              Optional strategy As String = "",
                              Optional reason As String = "") As OrderItem
        Return RequestOrder(code, OrderSide.Buy, qty, price, OrderPriceType.Limit, strategy, reason)
    End Function

    ''' <summary>시장가 매도</summary>
    Public Function SellMarket(code As String, qty As Integer,
                                Optional strategy As String = "",
                                Optional reason As String = "") As OrderItem
        Return RequestOrder(code, OrderSide.Sell, qty, 0, OrderPriceType.Market, strategy, reason)
    End Function

    ''' <summary>전량 시장가 매도</summary>
    Public Function SellAll(code As String,
                             Optional strategy As String = "",
                             Optional reason As String = "") As OrderItem
        Dim qty = GetAvailableQty(code)
        If qty <= 0 Then Return Nothing
        Return SellMarket(code, qty, strategy, reason)
    End Function

    ' ═══════════════════════════════════════
    ' 내부 유틸
    ' ═══════════════════════════════════════

    Private Sub MoveToHistory(order As OrderItem)
        _activeOrders.TryRemove(order.OrderId, Nothing)
        _orderHistory.Enqueue(order)

        ' 이력 크기 제한
        While _orderHistory.Count > MAX_HISTORY
            Dim dummy As OrderItem = Nothing
            _orderHistory.TryDequeue(dummy)
        End While
    End Sub

End Class
