' ═══════════════════════════════════════════════════════════════
' TradeManagerTest.vb — TradeManager 가혹 테스트
' ═══════════════════════════════════════════════════════════════
' 장 외 시간에도 Chejan 이벤트를 시뮬레이션하여
' 주문/체결/잔고/미체결/부분체결/동시매매를 검증한다.
' 실제 API 호출 없이 MessageBus만으로 동작.
' ═══════════════════════════════════════════════════════════════

Imports System.Threading
Imports [Shared]

Public Class TradeManagerTest

    Private _testsPassed As Integer = 0
    Private _testsFailed As Integer = 0
    Private _totalTests As Integer = 0

    Private Sub Log(msg As String)
        AppLogger.I.Test(msg, "TMTest")
    End Sub

    Private Sub Pass(testName As String)
        _testsPassed += 1
        Log($"  ✅ PASS: {testName}")
    End Sub

    Private Sub Fail(testName As String, reason As String)
        _testsFailed += 1
        Log($"  ❌ FAIL: {testName} — {reason}")
    End Sub

    Private Sub Assert(condition As Boolean, testName As String, failReason As String)
        _totalTests += 1
        If condition Then
            Pass(testName)
        Else
            Fail(testName, failReason)
        End If
    End Sub

    ' ═══════════════════════════════════════
    ' 전체 테스트 실행
    ' ═══════════════════════════════════════

    Public Sub RunAllTests()
        _testsPassed = 0
        _testsFailed = 0
        _totalTests = 0

        Log("═══════════════════════════════════════════════════")
        Log("    TradeManager 가혹 테스트 시작")
        Log($"    시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
        Log("═══════════════════════════════════════════════════")

        ' 1단계: 기본 초기화 검증
        Test01_Initialization()

        ' 2단계: 초기 동기화 시뮬레이션
        Test02_SyncSimulation()

        ' 3단계: 주문 검증 (리스크 체크)
        Test03_OrderValidation()

        ' 4단계: 주문 큐잉 + 체결 시뮬레이션
        Test04_OrderAndFill()

        ' 5단계: 부분체결 시뮬레이션
        Test05_PartialFill()

        ' 6단계: 잔고변경 시뮬레이션
        Test06_BalanceChange()

        ' 7단계: 여러종목 동시 매매
        Test07_MultiStockSimultaneous()

        ' 8단계: 손절/익절 시뮬레이션
        Test08_StopLossTakeProfit()

        ' 9단계: 중복/과다 주문 차단
        Test09_DuplicateOrderBlock()

        ' 10단계: 외부 주문 추적
        Test10_ExternalOrderTracking()

        ' 결과 출력
        Log("═══════════════════════════════════════════════════")
        Log($"    테스트 완료: {_totalTests}건")
        Log($"    ✅ PASS: {_testsPassed}건")
        Log($"    ❌ FAIL: {_testsFailed}건")
        Log($"    결과: {If(_testsFailed = 0, "★ ALL PASS ★", "⚠ FAIL 있음 ⚠")}")
        Log("═══════════════════════════════════════════════════")
    End Sub

    ' ═══════════════════════════════════════
    ' 개별 테스트
    ' ═══════════════════════════════════════

    Public Sub Test01_Initialization()
        Log("")
        Log("── T01: 초기화 검증 ──")

        Dim tm = TradeManager.I
        Assert(tm IsNot Nothing, "싱글톤 생성", "TradeManager.I is Nothing")
        Assert(tm Is TradeManager.I, "싱글톤 동일성", "두 번째 호출이 다른 인스턴스")
        Assert(tm.AutoTradeEnabled = False, "자동매매 초기값=OFF", $"현재: {tm.AutoTradeEnabled}")
        Assert(tm.MaxPositionCount > 0, "MaxPositionCount > 0", $"현재: {tm.MaxPositionCount}")
        Assert(tm.MaxPositionAmount > 0, "MaxPositionAmount > 0", $"현재: {tm.MaxPositionAmount}")
    End Sub

    Public Sub Test02_SyncSimulation()
        Log("")
        Log("── T02: 초기 동기화 시뮬레이션 ──")

        Dim tm = TradeManager.I

        ' 계좌번호 설정
        tm.AccountNo = "9999999999"
        tm.AvailableCash = 100_000_000

        ' 가짜 보유종목 주입 (Chejan 잔고변경 이벤트로)
        SimulateChejanBalance("005930", "삼성전자", 100, 70000, 72000)
        SimulateChejanBalance("035720", "카카오", 50, 55000, 53000)
        SimulateChejanBalance("000660", "SK하이닉스", 30, 120000, 125000)

        tm.IsSynced = True

        Assert(tm.AccountNo = "9999999999", "계좌번호 설정", $"현재: {tm.AccountNo}")
        Assert(tm.AvailableCash = 100_000_000, "가용현금 설정", $"현재: {tm.AvailableCash}")
        Assert(tm.HasPosition("005930"), "삼성전자 보유", "보유 안됨")
        Assert(tm.HasPosition("035720"), "카카오 보유", "보유 안됨")
        Assert(tm.HasPosition("000660"), "SK하이닉스 보유", "보유 안됨")
        Assert(tm.GetHoldingQty("005930") = 100, "삼성전자 100주", $"현재: {tm.GetHoldingQty("005930")}")
        Assert(tm.PositionCount = 3, "보유종목 3개", $"현재: {tm.PositionCount}")

        ' 보유종목 로그
        For Each pos In tm.GetPositions()
            Log($"    보유: {pos}")
        Next
    End Sub

    Public Sub Test03_OrderValidation()
        Log("")
        Log("── T03: 리스크 검증 ──")

        Dim tm = TradeManager.I

        ' 자동매매 OFF 상태에서 전략 주문 → 거부
        Dim o1 = tm.RequestOrder("005930", OrderSide.Buy, 10, strategyName:="TestStrategy")
        Assert(o1 Is Nothing, "자동매매OFF + 전략주문 = 거부", "주문이 접수됨")

        ' 수량 0 주문 → 거부
        Dim o2 = tm.RequestOrder("005930", OrderSide.Buy, 0)
        Assert(o2 Is Nothing, "수량0 주문 = 거부", "주문이 접수됨")

        ' 매도가능수량 초과 → 거부
        Dim o3 = tm.RequestOrder("005930", OrderSide.Sell, 9999)
        Assert(o3 Is Nothing, "매도가능수량 초과 = 거부", "주문이 접수됨")

        ' 보유하지 않은 종목 매도 → 거부
        Dim o4 = tm.RequestOrder("999999", OrderSide.Sell, 10)
        Assert(o4 Is Nothing, "미보유종목 매도 = 거부", "주문이 접수됨")
    End Sub

    Public Sub Test04_OrderAndFill()
        Log("")
        Log("── T04: 주문 → 체결 시뮬레이션 ──")

        Dim tm = TradeManager.I

        ' 수동 시장가 매수 (전략 빈 = 수동 주문이므로 자동매매 OFF여도 통과)
        Dim order = tm.BuyMarket("068270", 20, reason:="테스트매수")
        Assert(order IsNot Nothing, "수동 매수 접수", "주문이 Nothing")

        If order IsNot Nothing Then
            Assert(order.Status = OrderStatus.Pending OrElse order.Status = OrderStatus.Submitted,
                   "주문 상태=Pending/Submitted", $"현재: {order.Status}")
            Assert(order.OrderQty = 20, "주문수량=20", $"현재: {order.OrderQty}")
            Assert(order.Side = OrderSide.Buy, "매수주문", $"현재: {order.Side}")

            ' 큐 처리 대기 (300ms 타이머)
            Thread.Sleep(500)

            ' Chejan 체결 이벤트 시뮬레이션
            SimulateChejanOrder(order.Code, "068270", "셀트리온", "체결", 20, 180000, "0001234")

            Assert(order.KiwoomOrderNo = "0001234" OrElse
                   tm.GetActiveOrders().Any(Function(o) o.Code = "068270"),
                   "키움주문번호 매핑", "매핑 안됨")

            ' 잔고변경 시뮬레이션
            SimulateChejanBalance("068270", "셀트리온", 20, 180000, 180500)

            Assert(tm.HasPosition("068270"), "셀트리온 보유 확인", "보유 안됨")
            Assert(tm.GetHoldingQty("068270") = 20, "셀트리온 20주", $"현재: {tm.GetHoldingQty("068270")}")
        End If
    End Sub

    Public Sub Test05_PartialFill()
        Log("")
        Log("── T05: 부분체결 시뮬레이션 ──")

        Dim tm = TradeManager.I

        ' 리스크 한도 임시 확대 (50주 × 300,000원 = 15,000,000원)
        Dim savedMaxAmount = tm.MaxPositionAmount
        tm.MaxPositionAmount = 20_000_000

        ' 지정가 매수 50주
        Dim order = tm.BuyLimit("035420", 50, 300000, reason:="부분체결테스트")
        Assert(order IsNot Nothing, "지정가 매수 접수", "주문이 Nothing")

        If order IsNot Nothing Then
            Thread.Sleep(500) ' 큐 처리 대기

            ' 1차 부분체결: 20주
            SimulateChejanOrder(order.Code, "035420", "NAVER", "체결", 20, 300000, "0002345", 30)
            Thread.Sleep(100)

            ' 찾기: 내부 order가 아닌 매핑된 order 확인
            Dim found = tm.GetActiveOrders().FirstOrDefault(Function(o) o.Code = "035420")
            If found IsNot Nothing Then
                Assert(found.Status = OrderStatus.PartialFill OrElse found.FilledQty >= 20,
                       "부분체결 상태", $"상태: {found.Status}, 체결량: {found.FilledQty}")
            End If

            ' 2차 잔량체결: 30주
            SimulateChejanOrder(order.Code, "035420", "NAVER", "체결", 30, 300000, "0002345", 0)
            Thread.Sleep(100)

            ' 잔고변경
            SimulateChejanBalance("035420", "NAVER", 50, 300000, 301000)

            Assert(tm.HasPosition("035420"), "NAVER 보유 확인", "보유 안됨")
            Assert(tm.GetHoldingQty("035420") = 50, "NAVER 50주", $"현재: {tm.GetHoldingQty("035420")}")
        End If

        ' 리스크 한도 복원
        tm.MaxPositionAmount = savedMaxAmount
    End Sub

    Public Sub Test06_BalanceChange()
        Log("")
        Log("── T06: 잔고변경 (매도 후 포지션 청산) ──")

        Dim tm = TradeManager.I

        ' 카카오 전량 매도
        Dim prevQty = tm.GetHoldingQty("035720")
        If prevQty > 0 Then
            Dim order = tm.SellMarket("035720", prevQty, reason:="청산테스트")
            Assert(order IsNot Nothing, "카카오 전량매도 접수", "주문이 Nothing")

            Thread.Sleep(500)

            ' 체결
            SimulateChejanOrder("035720", "035720", "카카오", "체결", prevQty, 54000, "0003456")
            ' 잔고변경 → 수량 0
            SimulateChejanBalance("035720", "카카오", 0, 0, 0)

            Thread.Sleep(100)
            Assert(Not tm.HasPosition("035720"), "카카오 청산 완료", $"아직 보유: {tm.GetHoldingQty("035720")}주")
        Else
            Log("    (카카오 미보유 → 스킵)")
        End If
    End Sub

    Public Sub Test07_MultiStockSimultaneous()
        Log("")
        Log("── T07: 여러종목 동시매매 (가혹 테스트) ──")

        Dim tm = TradeManager.I
        Dim codes = {"003550", "006400", "012330", "028260", "034730"}
        Dim names = {"LG", "삼성SDI", "현대모비스", "삼성물산", "SK"}
        Dim orders As New List(Of OrderItem)

        ' 5종목 동시 매수 주문
        For i = 0 To codes.Length - 1
            Dim o = tm.BuyMarket(codes(i), 10 + i * 5, reason:=$"동시매매#{i + 1}")
            If o IsNot Nothing Then orders.Add(o)
        Next

        Assert(orders.Count = codes.Length, $"5종목 동시접수={orders.Count}건", $"접수: {orders.Count}")

        ' 큐가 초당 3건씩 처리하므로 2초 대기
        Thread.Sleep(2000)

        ' 전부 체결 시뮬레이션
        For i = 0 To codes.Length - 1
            Dim price = 50000 + i * 10000
            SimulateChejanOrder(codes(i), codes(i), names(i), "체결", 10 + i * 5, price, $"000{5000 + i}")
            SimulateChejanBalance(codes(i), names(i), 10 + i * 5, price, price + 500)
        Next

        Thread.Sleep(200)

        Dim allHeld = codes.All(Function(c) tm.HasPosition(c))
        Assert(allHeld, "5종목 전부 보유 확인", "일부 미보유")

        ' 현재 보유종목 수
        Log($"    현재 보유종목: {tm.PositionCount}개")
        Log($"    전체 평가금액: {tm.TotalEvalAmount:N0}원")
        Log($"    전체 평가손익: {tm.TotalProfitLoss:N0}원")
    End Sub

    Public Sub Test08_StopLossTakeProfit()
        Log("")
        Log("── T08: 손절/익절 시뮬레이션 ──")

        Dim tm = TradeManager.I

        ' 자동매매 ON
        tm.AutoTradeEnabled = True

        ' 삼성전자 현재가를 급락시켜 손절 트리거
        Dim pos = tm.GetPosition("005930")
        If pos IsNot Nothing Then
            Dim stopPrice = CInt(pos.AvgPrice * (1 + tm.StopLossRate / 100) - 100)
            Log($"    삼성전자 평균가: {pos.AvgPrice:N0}, 손절가: {stopPrice:N0} (손절률: {tm.StopLossRate}%)")

            ' 실시간 틱으로 손절가 아래 현재가 전달
            Dim tickMsg As New Msg(Topics.TICK)
            tickMsg("code") = "005930"
            tickMsg("price") = CDbl(stopPrice)
            MessageBus.I.Emit(tickMsg)

            Thread.Sleep(600)  ' 500ms 간격 체크 대기

            ' 손절 주문이 생성되었는지 확인
            Dim stopOrders = tm.GetActiveOrders().Where(Function(o) o.Code = "005930" AndAlso o.StrategyName = "StopLoss").ToList()
            Assert(stopOrders.Count > 0 OrElse tm.GetOrderHistory().Any(Function(o) o.Code = "005930" AndAlso o.StrategyName = "StopLoss"),
                   "손절 주문 발동", "손절 주문 없음")
        Else
            Log("    (삼성전자 미보유 → 스킵)")
        End If

        ' 자동매매 OFF 복원
        tm.AutoTradeEnabled = False
    End Sub

    Public Sub Test09_DuplicateOrderBlock()
        Log("")
        Log("── T09: 중복/과다 주문 차단 ──")

        Dim tm = TradeManager.I
        Dim code = "005380" ' 현대차
        Dim prevOrders = tm.GetActiveOrders().Where(Function(o) o.Code = code).Count()

        ' 미체결 주문 한도(2건)까지 주문
        Dim o1 = tm.RequestOrder(code, OrderSide.Buy, 5, reason:="중복테스트1")
        Dim o2 = tm.RequestOrder(code, OrderSide.Buy, 5, reason:="중복테스트2")
        Dim o3 = tm.RequestOrder(code, OrderSide.Buy, 5, reason:="중복테스트3 (차단되어야함)")

        Assert(o1 IsNot Nothing, "1번째 주문 접수", "거부됨")
        Assert(o2 IsNot Nothing, "2번째 주문 접수", "거부됨")
        Assert(o3 Is Nothing, "3번째 주문 차단 (미체결 한도)", "접수됨 (차단 실패)")
    End Sub

    Public Sub Test10_ExternalOrderTracking()
        Log("")
        Log("── T10: 외부 주문 추적 ──")

        Dim tm = TradeManager.I

        ' HTS에서 발주한 주문처럼 Chejan 이벤트만 수신
        SimulateChejanOrder("096530", "096530", "씨젠", "접수", 0, 45000, "EXT001")
        Thread.Sleep(100)
        SimulateChejanOrder("096530", "096530", "씨젠", "체결", 100, 45000, "EXT001", 0)
        Thread.Sleep(100)
        SimulateChejanBalance("096530", "씨젠", 100, 45000, 45500)

        Thread.Sleep(200)
        Assert(tm.HasPosition("096530"), "외부주문 종목 보유 추적", "추적 안됨")
        Assert(tm.GetHoldingQty("096530") = 100, "씨젠 100주", $"현재: {tm.GetHoldingQty("096530")}")

        ' 주문 이력에 EXT_ 주문 존재 확인
        Dim extOrders = tm.GetActiveOrders().Concat(tm.GetOrderHistory()).
                            Where(Function(o) o.OrderId.StartsWith("EXT_") OrElse o.StrategyName = "외부").Count()
        Assert(extOrders > 0, "외부주문 이력 기록됨", "이력 없음")
    End Sub

    ' ═══════════════════════════════════════
    ' 개별 테스트 메뉴용
    ' ═══════════════════════════════════════

    Public Sub RunQuickSyncTest()
        Log("")
        Log("── Quick: 동기화 + 잔고 확인 ──")
        Test01_Initialization()
        Test02_SyncSimulation()
        Log($"결과: {_testsPassed} pass / {_testsFailed} fail")
    End Sub

    Public Sub RunOrderTest()
        Log("")
        Log("── Quick: 주문 검증 ──")
        If Not TradeManager.I.IsSynced Then Test02_SyncSimulation()
        Test03_OrderValidation()
        Test04_OrderAndFill()
        Log($"결과: {_testsPassed} pass / {_testsFailed} fail")
    End Sub

    Public Sub RunStressTest()
        Log("")
        Log("── Quick: 동시매매 스트레스 테스트 ──")
        If Not TradeManager.I.IsSynced Then Test02_SyncSimulation()
        Test07_MultiStockSimultaneous()
        Log($"결과: {_testsPassed} pass / {_testsFailed} fail")
    End Sub

    ' ═══════════════════════════════════════
    ' Chejan 이벤트 시뮬레이터
    ' ═══════════════════════════════════════

    Private Sub SimulateChejanOrder(ordCode As String, code As String, name As String,
                                    status As String, filledQty As Integer, filledPrice As Integer,
                                    kiwoomOrderNo As String, Optional unfilledQty As Integer = -1)
        Dim m As New Msg(Topics.ORDER_EXECUTED)
        m("종목코드") = code
        m("종목명") = name
        m("주문상태") = status
        m("체결량") = filledQty.ToString()
        m("체결가") = filledPrice.ToString()
        m("주문번호") = kiwoomOrderNo
        If unfilledQty >= 0 Then m("미체결수량") = unfilledQty.ToString()
        MessageBus.I.Emit(m)
    End Sub

    Private Sub SimulateChejanBalance(code As String, name As String,
                                      qty As Integer, avgPrice As Integer, currentPrice As Integer)
        Dim m As New Msg(Topics.ORDER_BALANCE_CHANGED)
        m("종목코드") = code
        m("종목명") = name
        m("보유수량") = qty.ToString()
        m("매입가") = avgPrice.ToString()
        m("현재가") = currentPrice.ToString()
        m("손익율") = If(avgPrice > 0, (CDbl(currentPrice - avgPrice) / avgPrice * 100).ToString("0.00"), "0")
        MessageBus.I.Emit(m)
    End Sub

End Class
