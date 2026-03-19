' ═══════════════════════════════════════════════════════════════
' OrderSimulator.vb — 주문 실행/시뮬레이션 엔진 (원칙서 v4.0 제6조)
' ═══════════════════════════════════════════════════════════════
' ★ SignalEvaluator 결과 → 실제 주문 실행 또는 시뮬레이션
' ★ 슬리피지 계산, 거래비용 반영, 손익비 사전검증
' ★ 주문 이력 기록 + 이벤트 발화
' ═══════════════════════════════════════════════════════════════

Imports [Shared]

Namespace SimTrade

#Region "주문/매매 기록 모델"

    ''' <summary>주문 요청 정보</summary>
    Public Class SimOrder
        Public Property OrderId As String = ""
        Public Property Code As String = ""
        Public Property Name As String = ""
        Public Property Side As String = ""                ' "BUY" / "SELL"
        Public Property OrderType As SimOrderType = SimOrderType.Market
        Public Property RequestPrice As Integer = 0
        Public Property RequestQty As Integer = 0
        Public Property FilledPrice As Integer = 0
        Public Property FilledQty As Integer = 0
        Public Property SlippageRate As Double = 0         ' 실제 슬리피지 (%)
        Public Property Commission As Long = 0             ' 수수료 (원)
        Public Property Tax As Long = 0                    ' 거래세 (원)
        Public Property TotalCost As Long = 0              ' 수수료 + 세금 합계
        Public Property Reason As String = ""              ' 매수/매도 사유
        Public Property Priority As String = ""            ' 매도 시 P0~P8
        Public Property Profile As String = ""             ' A / B
        Public Property OrderTime As DateTime = DateTime.Now
        Public Property FilledTime As DateTime = DateTime.MinValue
        Public Property Status As String = "대기"          ' 대기/체결/실패/취소
    End Class

    ''' <summary>매매 완결 기록 (매수~매도 1쌍)</summary>
    Public Class TradeRecord
        Public Property Code As String = ""
        Public Property Name As String = ""
        Public Property BuyPrice As Integer = 0
        Public Property BuyQty As Integer = 0
        Public Property BuyTime As DateTime = DateTime.MinValue
        Public Property BuyReason As String = ""
        Public Property SellPrice As Integer = 0
        Public Property SellQty As Integer = 0
        Public Property SellTime As DateTime = DateTime.MinValue
        Public Property SellReason As String = ""
        Public Property SellPriority As String = ""
        Public Property GrossProfit As Long = 0            ' 세전 손익
        Public Property TotalCost As Long = 0              ' 매수+매도 비용 합계
        Public Property NetProfit As Long = 0              ' 순손익
        Public Property NetProfitRate As Double = 0        ' 순수익률 (%)
        Public Property HoldingBars As Integer = 0         ' 보유 캔들 수
        Public Property MaxDrawdown As Double = 0          ' 보유 중 최대 하락폭 (%)
        Public Property Profile As String = ""
    End Class

#End Region

    ''' <summary>
    ''' 주문 실행 및 매매 기록 관리.
    ''' 모의매매에서는 TradeManager를 통해 키움 모의서버에 실제 주문을 보내고,
    ''' 동시에 비용/슬리피지를 기록한다.
    ''' </summary>
    Public Class OrderSimulator

        Private ReadOnly _settings As SimTradeSettings
        Private ReadOnly _stateManager As StateManager

        ' ── 기록 ──
        Private ReadOnly _orderHistory As New List(Of SimOrder)
        Private ReadOnly _tradeHistory As New List(Of TradeRecord)
        Private ReadOnly _pendingBuys As New Dictionary(Of String, SimOrder)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _lock As New Object()

        ' ── 이벤트 ──
        Public Event OrderExecuted(order As SimOrder)
        Public Event TradeCompleted(record As TradeRecord)
        Public Event OrderFailed(code As String, reason As String)

        ' ── 통계 ──
        Private _totalTrades As Integer = 0
        Private _winCount As Integer = 0
        Private _lossCount As Integer = 0
        Private _totalNetProfit As Long = 0
        Private _totalCost As Long = 0
        Private _maxConsecutiveLoss As Integer = 0
        Private _currentConsecutiveLoss As Integer = 0
        Private _peakEquity As Long = 0
        Private _maxDrawdownRate As Double = 0

        Public Sub New(settings As SimTradeSettings, stateManager As StateManager)
            _settings = settings
            _stateManager = stateManager
        End Sub


        ' ════════════════════════════════════════
        ' 매수 주문
        ' ════════════════════════════════════════

        ''' <summary>
        ''' 매수 신호 결과를 받아 실제 주문을 실행한다.
        ''' </summary>
        Public Sub ExecuteBuy(state As StockState, buyResult As BuySignalResult)
            If Not buyResult.ShouldBuy Then Return
            If buyResult.SuggestedQty <= 0 OrElse buyResult.SuggestedPrice <= 0 Then Return

            Dim order As New SimOrder() With {
                .OrderId = GenerateOrderId(),
                .Code = state.Code,
                .Name = state.Name,
                .Side = "BUY",
                .OrderType = _settings.BuyOrderType,
                .RequestPrice = buyResult.SuggestedPrice,
                .RequestQty = buyResult.SuggestedQty,
                .Reason = buyResult.Reason,
                .Profile = buyResult.Profile,
                .OrderTime = DateTime.Now
            }

            ' 비용 사전계산
            Dim amount = CLng(order.RequestPrice) * order.RequestQty
            order.Commission = CLng(amount * _settings.BuyCommissionRate / 100.0)
            order.TotalCost = order.Commission  ' 매수 시 세금 없음

            ' 실제 주문 전송
            Try
                Select Case _settings.BuyOrderType
                    Case SimOrderType.Market
                        TradeManager.I.BuyMarket(state.Code, order.RequestQty, "SimTrade", order.Reason)
                    Case SimOrderType.LimitBestBid
                        TradeManager.I.BuyLimit(state.Code, order.RequestQty, order.RequestPrice, "SimTrade", order.Reason)
                    Case SimOrderType.LimitCurrentPrice
                        TradeManager.I.BuyLimit(state.Code, order.RequestQty, order.RequestPrice, "SimTrade", order.Reason)
                End Select

                order.Status = "전송"

                ' 대기 목록에 추가 (체결 수신 시 매칭)
                SyncLock _lock
                    _pendingBuys(state.Code) = order
                    _orderHistory.Add(order)
                End SyncLock

                RaiseEvent OrderExecuted(order)

            Catch ex As Exception
                order.Status = "실패"
                SyncLock _lock
                    _orderHistory.Add(order)
                End SyncLock
                RaiseEvent OrderFailed(state.Code, ex.Message)
            End Try
        End Sub


        ' ════════════════════════════════════════
        ' 매도 주문
        ' ════════════════════════════════════════

        ''' <summary>
        ''' 매도 신호 결과를 받아 실제 주문을 실행한다.
        ''' </summary>
        Public Sub ExecuteSell(state As StockState, sellResult As SellSignalResult)
            If Not sellResult.ShouldSell Then Return

            Dim qty = TradeManager.I.GetAvailableQty(state.Code)
            If qty <= 0 Then Return

            ' 분할매도 (향후 확장)
            If sellResult.IsPartialSell Then
                qty = CInt(Math.Ceiling(qty * sellResult.SellRatio))
                If qty <= 0 Then qty = 1
            End If

            Dim sellPrice = GetSellPrice(state)
            If sellPrice <= 0 Then Return

            Dim order As New SimOrder() With {
                .OrderId = GenerateOrderId(),
                .Code = state.Code,
                .Name = state.Name,
                .Side = "SELL",
                .OrderType = _settings.SellOrderType,
                .RequestPrice = sellPrice,
                .RequestQty = qty,
                .Reason = sellResult.Reason,
                .Priority = sellResult.Priority,
                .OrderTime = DateTime.Now
            }

            ' 비용 사전계산
            Dim amount = CLng(sellPrice) * qty
            order.Commission = CLng(amount * _settings.SellCommissionRate / 100.0)
            order.Tax = CLng(amount * _settings.TransactionTaxRate / 100.0)
            order.TotalCost = order.Commission + order.Tax

            ' 실제 주문 전송
            Try
                Select Case _settings.SellOrderType
                    Case SimOrderType.Market
                        TradeManager.I.SellMarket(state.Code, qty, "SimTrade", order.Reason)
                    Case SimOrderType.LimitBestBid
                        Dim price = If(state.Bid1 > 0, state.Bid1, state.CurrentPrice)
                        TradeManager.I.RequestOrder(state.Code, OrderSide.Sell, qty,
                            price, OrderPriceType.Limit, "SimTrade", order.Reason)
                    Case SimOrderType.LimitCurrentPrice
                        TradeManager.I.RequestOrder(state.Code, OrderSide.Sell, qty,
                            state.CurrentPrice, OrderPriceType.Limit, "SimTrade", order.Reason)
                End Select

                order.Status = "전송"
                SyncLock _lock
                    _orderHistory.Add(order)
                End SyncLock

                RaiseEvent OrderExecuted(order)

            Catch ex As Exception
                order.Status = "실패"
                SyncLock _lock
                    _orderHistory.Add(order)
                End SyncLock
                RaiseEvent OrderFailed(state.Code, ex.Message)
            End Try
        End Sub


        ' ════════════════════════════════════════
        ' 체결 처리
        ' ════════════════════════════════════════

        ''' <summary>
        ''' 매수 체결 수신 시 호출. StateManager에 포지션 등록.
        ''' </summary>
        Public Sub OnBuyFilled(code As String, filledPrice As Integer, filledQty As Integer)
            SyncLock _lock
                Dim order As SimOrder = Nothing
                If _pendingBuys.TryGetValue(code, order) Then
                    order.FilledPrice = filledPrice
                    order.FilledQty = filledQty
                    order.FilledTime = DateTime.Now
                    order.Status = "체결"

                    ' 슬리피지 계산
                    If order.RequestPrice > 0 Then
                        order.SlippageRate = (filledPrice - order.RequestPrice) / CDbl(order.RequestPrice) * 100.0
                    End If

                    _pendingBuys.Remove(code)
                End If
            End SyncLock

            ' StateManager에 포지션 등록
            _stateManager.RegisterPosition(code, filledPrice, filledQty)
        End Sub


        ''' <summary>
        ''' 매도 체결 수신 시 호출. TradeRecord 생성 + 통계 갱신.
        ''' </summary>
        Public Sub OnSellFilled(code As String, filledPrice As Integer, filledQty As Integer)
            Dim state = _stateManager.GetState(code)
            If state Is Nothing Then Return

            Dim record As New TradeRecord() With {
                .Code = code,
                .Name = state.Name,
                .BuyPrice = state.BuyPrice,
                .BuyQty = state.BuyQty,
                .BuyTime = state.BuyTime,
                .SellPrice = filledPrice,
                .SellQty = filledQty,
                .SellTime = DateTime.Now
            }

            ' 마지막 매도 주문에서 사유/우선순위 가져오기
            Dim lastSellOrder = GetLastSellOrder(code)
            If lastSellOrder IsNot Nothing Then
                record.SellReason = lastSellOrder.Reason
                record.SellPriority = lastSellOrder.Priority
                record.Profile = lastSellOrder.Profile
            End If

            ' 손익 계산
            Dim buyAmount = CLng(record.BuyPrice) * record.BuyQty
            Dim sellAmount = CLng(record.SellPrice) * record.SellQty
            record.GrossProfit = sellAmount - buyAmount

            ' 비용 계산
            Dim buyCost = CLng(buyAmount * _settings.BuyCommissionRate / 100.0)
            Dim sellCost = CLng(sellAmount * _settings.SellCommissionRate / 100.0)
            Dim tax = CLng(sellAmount * _settings.TransactionTaxRate / 100.0)
            record.TotalCost = buyCost + sellCost + tax

            record.NetProfit = record.GrossProfit - record.TotalCost
            If buyAmount > 0 Then
                record.NetProfitRate = record.NetProfit / CDbl(buyAmount) * 100.0
            End If

            ' 보유 봉 수 계산
            record.HoldingBars = GetBarsBetween(state.Candles, record.BuyTime, record.SellTime)

            SyncLock _lock
                _tradeHistory.Add(record)
            End SyncLock

            ' 통계 갱신
            UpdateStats(record)

            ' StateManager 포지션 해제
            _stateManager.ClearPosition(code)

            RaiseEvent TradeCompleted(record)
        End Sub


        ' ════════════════════════════════════════
        ' 통계 조회
        ' ════════════════════════════════════════

        Public ReadOnly Property TotalTrades As Integer
            Get
                Return _totalTrades
            End Get
        End Property

        Public ReadOnly Property WinRate As Double
            Get
                If _totalTrades = 0 Then Return 0
                Return (_winCount / CDbl(_totalTrades)) * 100.0
            End Get
        End Property

        Public ReadOnly Property TotalNetProfit As Long
            Get
                Return _totalNetProfit
            End Get
        End Property

        Public ReadOnly Property TotalCostPaid As Long
            Get
                Return _totalCost
            End Get
        End Property

        Public ReadOnly Property MaxConsecutiveLoss As Integer
            Get
                Return _maxConsecutiveLoss
            End Get
        End Property

        Public ReadOnly Property MaxDrawdownRate As Double
            Get
                Return _maxDrawdownRate
            End Get
        End Property

        ''' <summary>승률, 손익비, MDD 등 종합 통계 문자열</summary>
        Public Function GetStatsSummary() As String
            Dim avgWin = 0.0, avgLoss = 0.0
            SyncLock _lock
                Dim wins = _tradeHistory.Where(Function(t) t.NetProfit > 0).ToList()
                Dim losses = _tradeHistory.Where(Function(t) t.NetProfit <= 0).ToList()
                If wins.Count > 0 Then avgWin = wins.Average(Function(t) t.NetProfitRate)
                If losses.Count > 0 Then avgLoss = losses.Average(Function(t) Math.Abs(t.NetProfitRate))
            End SyncLock

            Dim profitFactor = If(avgLoss > 0, avgWin / avgLoss, 0)

            Return $"총{_totalTrades}건 | 승률{WinRate:F1}% | " &
                   $"승{_winCount}/패{_lossCount} | " &
                   $"평균승{avgWin:F2}%/패{avgLoss:F2}% | " &
                   $"손익비{profitFactor:F2} | " &
                   $"연패{_maxConsecutiveLoss} | " &
                   $"MDD{_maxDrawdownRate:F1}% | " &
                   $"순손익{_totalNetProfit:N0} | 비용{_totalCost:N0}"
        End Function

        ''' <summary>주문 이력 반환</summary>
        Public Function GetOrderHistory() As List(Of SimOrder)
            SyncLock _lock
                Return New List(Of SimOrder)(_orderHistory)
            End SyncLock
        End Function

        ''' <summary>매매 기록 반환</summary>
        Public Function GetTradeHistory() As List(Of TradeRecord)
            SyncLock _lock
                Return New List(Of TradeRecord)(_tradeHistory)
            End SyncLock
        End Function


        ' ════════════════════════════════════════
        ' 내부 헬퍼
        ' ════════════════════════════════════════

        Private Sub UpdateStats(record As TradeRecord)
            _totalTrades += 1
            _totalNetProfit += record.NetProfit
            _totalCost += record.TotalCost

            If record.NetProfit > 0 Then
                _winCount += 1
                _currentConsecutiveLoss = 0
            Else
                _lossCount += 1
                _currentConsecutiveLoss += 1
                If _currentConsecutiveLoss > _maxConsecutiveLoss Then
                    _maxConsecutiveLoss = _currentConsecutiveLoss
                End If
            End If

            ' MDD 계산 (누적 손익 기준)
            If _totalNetProfit > _peakEquity Then _peakEquity = _totalNetProfit
            If _peakEquity > 0 Then
                Dim dd = (_peakEquity - _totalNetProfit) / CDbl(_peakEquity) * 100.0
                If dd > _maxDrawdownRate Then _maxDrawdownRate = dd
            End If
        End Sub

        Private Function GetSellPrice(state As StockState) As Integer
            Select Case _settings.SellOrderType
                Case SimOrderType.LimitBestBid
                    Return If(state.Bid1 > 0, state.Bid1, state.CurrentPrice)
                Case SimOrderType.LimitCurrentPrice
                    Return state.CurrentPrice
                Case Else
                    Return state.CurrentPrice
            End Select
        End Function

        Private Function GetLastSellOrder(code As String) As SimOrder
            SyncLock _lock
                Return _orderHistory.LastOrDefault(Function(o) o.Code = code AndAlso o.Side = "SELL")
            End SyncLock
        End Function

        Private Function GetBarsBetween(candles As List(Of CandleItem),
                                         startTime As DateTime, endTime As DateTime) As Integer
            If candles Is Nothing OrElse candles.Count = 0 Then Return 0
            Dim count = 0
            For Each c In candles
                If c.Dt >= startTime AndAlso c.Dt <= endTime Then count += 1
            Next
            Return count
        End Function

        Private Shared _orderSeq As Integer = 0
        Private Shared Function GenerateOrderId() As String
            _orderSeq += 1
            Return $"SIM_{DateTime.Now:yyyyMMdd}_{_orderSeq:D5}"
        End Function

    End Class

End Namespace
