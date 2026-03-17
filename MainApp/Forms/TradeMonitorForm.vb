' ═══════════════════════════════════════════════════════════════
' TradeMonitorForm.vb — 매매 모니터 (잔고 + 미체결 + 손익 대시보드)
' ═══════════════════════════════════════════════════════════════
' 상단: 총 손익 현황 대시보드
' 하단 좌: 잔고 그리드 + 시장가 매도 버튼
' 하단 우: 미체결 그리드 + 주문 취소 버튼
' 실시간 틱으로 현재가/손익/보유정보 업데이트
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports [Shared]
Imports WeifenLuo.WinFormsUI.Docking

Public Class TradeMonitorForm
    Inherits DockFormBase

    ' ── 대시보드 (상단) ──
    Private _pnlDashboard As Panel
    Private _lblTotalAsset As Label
    Private _lblTotalPnL As Label
    Private _lblCash As Label
    Private _lblPositionCount As Label
    Private _lblZeroLossStatus As Label
    Private _lblDailyPnL As Label

    ' ── 잔고 패널 (하단 좌) ──
    Private _splitMain As SplitContainer
    Private _pnlBalance As Panel
    Private _gridBalance As DataGridView
    Private _toolBalance As ToolStrip
    Private _btnSellMarket As ToolStripButton
    Private _btnSellAll As ToolStripButton
    Private _lblBalanceCount As ToolStripLabel

    ' ── 미체결 패널 (하단 우) ──
    Private _pnlOrders As Panel
    Private _gridOrders As DataGridView
    Private _toolOrders As ToolStrip
    Private _btnCancelOrder As ToolStripButton
    Private _btnCancelAll As ToolStripButton
    Private _lblOrderCount As ToolStripLabel

    ' ── Bus 핸들러 추적 ──
    Private ReadOnly _handlers As New List(Of KeyValuePair(Of String, Action(Of Msg)))()

    ' ── 업데이트 스로틀 ──
    Private _lastBalanceRefresh As DateTime = DateTime.MinValue
    Private _lastOrderRefresh As DateTime = DateTime.MinValue
    Private _lastDashboardRefresh As DateTime = DateTime.MinValue
    Private Const REFRESH_INTERVAL_MS As Integer = 300

    Public Sub New()
        Me.Text = "매매 모니터"
        Me.DockAreas = DockAreas.DockBottom Or DockAreas.DockTop Or DockAreas.Float Or DockAreas.Document
        Me.ShowHint = DockState.DockBottom
        InitControls()
        SubscribeBus()

        ' 이미 동기화 완료 상태라면 즉시 초기 로드
        If TradeManager.I.IsSynced Then
            InitialLoad()
        End If
    End Sub

    Public Overrides ReadOnly Property DefaultDockState As DockState
        Get
            Return DockState.DockBottom
        End Get
    End Property

    ' ═══════════════════════════════════════
    ' UI 초기화
    ' ═══════════════════════════════════════

    Private Sub InitControls()
        Me.SuspendLayout()

        ' ══════════════════════
        ' 상단: 대시보드 패널
        ' ══════════════════════
        _pnlDashboard = New Panel()
        _pnlDashboard.Dock = DockStyle.Top
        _pnlDashboard.Height = 50
        _pnlDashboard.BackColor = Color.FromArgb(25, 25, 35)
        _pnlDashboard.Padding = New Padding(8, 4, 8, 4)

        Dim dashFlow As New FlowLayoutPanel()
        dashFlow.Dock = DockStyle.Fill
        dashFlow.FlowDirection = FlowDirection.LeftToRight
        dashFlow.WrapContents = False
        dashFlow.AutoSize = False

        _lblTotalAsset = CreateDashLabel("총자산: —", Color.White, True)
        _lblTotalPnL = CreateDashLabel("총손익: —", Color.White, True)
        _lblCash = CreateDashLabel("예수금: —", Color.LightGray, False)
        _lblPositionCount = CreateDashLabel("보유: 0종목", Color.LightGray, False)
        _lblDailyPnL = CreateDashLabel("당일실현: —", Color.LightGray, False)
        _lblZeroLossStatus = CreateDashLabel("ZeroLoss: OFF", Color.Gray, False)

        dashFlow.Controls.AddRange({_lblTotalAsset, _lblTotalPnL, _lblCash,
                                     _lblPositionCount, _lblDailyPnL, _lblZeroLossStatus})
        _pnlDashboard.Controls.Add(dashFlow)

        ' ══════════════════════
        ' 하단: 좌우 분할
        ' ══════════════════════
        _splitMain = New SplitContainer()
        _splitMain.Dock = DockStyle.Fill
        _splitMain.Orientation = Orientation.Vertical
        _splitMain.SplitterWidth = 4
        _splitMain.BackColor = Color.FromArgb(50, 50, 50)

        ' ── 좌: 잔고 ──
        InitBalancePanel()
        _splitMain.Panel1.Controls.Add(_gridBalance)
        _splitMain.Panel1.Controls.Add(_toolBalance)

        ' ── 우: 미체결 ──
        InitOrdersPanel()
        _splitMain.Panel2.Controls.Add(_gridOrders)
        _splitMain.Panel2.Controls.Add(_toolOrders)

        ' ── 조립 ──
        Me.Controls.Add(_splitMain)
        Me.Controls.Add(_pnlDashboard)

        Me.ResumeLayout(False)

        ' 50:50 분할 (Layout 후 설정)
        AddHandler Me.Shown, Sub(s, e)
                                  Try
                                      _splitMain.SplitterDistance = _splitMain.Width \ 2
                                  Catch
                                  End Try
                              End Sub
    End Sub

    Private Function CreateDashLabel(text As String, color As Color, bold As Boolean) As Label
        Dim lbl As New Label()
        lbl.Text = text
        lbl.ForeColor = color
        lbl.Font = New Font("맑은 고딕", If(bold, 11, 9), If(bold, FontStyle.Bold, FontStyle.Regular))
        lbl.AutoSize = True
        lbl.Margin = New Padding(0, 6, 20, 0)
        Return lbl
    End Function

    ' ═══════════════════════════════════════
    ' 잔고 패널
    ' ═══════════════════════════════════════

    Private Sub InitBalancePanel()
        ' ── 툴바 ──
        _toolBalance = New ToolStrip()
        _toolBalance.GripStyle = ToolStripGripStyle.Hidden
        _toolBalance.BackColor = Color.FromArgb(40, 40, 50)
        _toolBalance.ForeColor = Color.White

        Dim lblTitle As New ToolStripLabel("보유종목")
        lblTitle.Font = New Font("맑은 고딕", 9, FontStyle.Bold)
        lblTitle.ForeColor = Color.LightSkyBlue

        _btnSellMarket = New ToolStripButton("선택 매도")
        _btnSellMarket.ForeColor = Color.Tomato
        AddHandler _btnSellMarket.Click, AddressOf OnSellMarketClick

        _btnSellAll = New ToolStripButton("전량 청산")
        _btnSellAll.ForeColor = Color.OrangeRed
        AddHandler _btnSellAll.Click, AddressOf OnSellAllClick

        _lblBalanceCount = New ToolStripLabel("0종목")
        _lblBalanceCount.ForeColor = Color.LightGray

        _toolBalance.Items.AddRange({lblTitle, New ToolStripSeparator(),
                                      _btnSellMarket, _btnSellAll,
                                      New ToolStripSeparator(), _lblBalanceCount})

        ' ── 그리드 ──
        _gridBalance = CreateGrid()
        _gridBalance.Columns.AddRange({
            Col("종목코드", 70),
            Col("종목명", 100),
            Col("보유", 55, DataGridViewContentAlignment.MiddleRight),
            Col("매도가능", 55, DataGridViewContentAlignment.MiddleRight),
            Col("평균가", 75, DataGridViewContentAlignment.MiddleRight),
            Col("현재가", 75, DataGridViewContentAlignment.MiddleRight),
            Col("평가금액", 90, DataGridViewContentAlignment.MiddleRight),
            Col("손익", 80, DataGridViewContentAlignment.MiddleRight),
            Col("수익률", 65, DataGridViewContentAlignment.MiddleRight),
            Col("전략", 70)
        })
    End Sub

    ' ═══════════════════════════════════════
    ' 미체결 패널
    ' ═══════════════════════════════════════

    Private Sub InitOrdersPanel()
        ' ── 툴바 ──
        _toolOrders = New ToolStrip()
        _toolOrders.GripStyle = ToolStripGripStyle.Hidden
        _toolOrders.BackColor = Color.FromArgb(40, 40, 50)
        _toolOrders.ForeColor = Color.White

        Dim lblTitle As New ToolStripLabel("미체결")
        lblTitle.Font = New Font("맑은 고딕", 9, FontStyle.Bold)
        lblTitle.ForeColor = Color.SandyBrown

        _btnCancelOrder = New ToolStripButton("선택 취소")
        _btnCancelOrder.ForeColor = Color.Tomato
        AddHandler _btnCancelOrder.Click, AddressOf OnCancelOrderClick

        _btnCancelAll = New ToolStripButton("전체 취소")
        _btnCancelAll.ForeColor = Color.OrangeRed
        AddHandler _btnCancelAll.Click, AddressOf OnCancelAllClick

        _lblOrderCount = New ToolStripLabel("0건")
        _lblOrderCount.ForeColor = Color.LightGray

        _toolOrders.Items.AddRange({lblTitle, New ToolStripSeparator(),
                                     _btnCancelOrder, _btnCancelAll,
                                     New ToolStripSeparator(), _lblOrderCount})

        ' ── 그리드 ──
        _gridOrders = CreateGrid()
        _gridOrders.Columns.AddRange({
            Col("주문번호", 80),
            Col("종목코드", 70),
            Col("종목명", 100),
            Col("구분", 45),
            Col("주문수량", 60, DataGridViewContentAlignment.MiddleRight),
            Col("주문가격", 75, DataGridViewContentAlignment.MiddleRight),
            Col("체결량", 55, DataGridViewContentAlignment.MiddleRight),
            Col("미체결", 55, DataGridViewContentAlignment.MiddleRight),
            Col("상태", 60),
            Col("전략", 70),
            Col("시간", 70)
        })
    End Sub

    ' ═══════════════════════════════════════
    ' 그리드 공통 생성
    ' ═══════════════════════════════════════

    Private Function CreateGrid() As DataGridView
        Dim g As New DataGridView()
        g.Dock = DockStyle.Fill
        g.AllowUserToAddRows = False
        g.AllowUserToDeleteRows = False
        g.AllowUserToResizeRows = False
        g.ReadOnly = True
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        g.MultiSelect = False
        g.RowHeadersVisible = False
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        g.BorderStyle = BorderStyle.None
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        g.ColumnHeadersHeight = 28
        g.RowTemplate.Height = 24
        g.EnableHeadersVisualStyles = False

        ' 다크 테마
        g.BackgroundColor = Color.FromArgb(30, 30, 38)
        g.GridColor = Color.FromArgb(55, 55, 65)
        g.DefaultCellStyle.BackColor = Color.FromArgb(30, 30, 38)
        g.DefaultCellStyle.ForeColor = Color.WhiteSmoke
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 70, 100)
        g.DefaultCellStyle.SelectionForeColor = Color.White
        g.DefaultCellStyle.Font = New Font("Consolas", 9)
        g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 40, 55)
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.LightGray
        g.ColumnHeadersDefaultCellStyle.Font = New Font("맑은 고딕", 8.5F, FontStyle.Bold)
        g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
        Return g
    End Function

    Private Shared Function Col(name As String, width As Integer,
                                Optional align As DataGridViewContentAlignment = DataGridViewContentAlignment.MiddleLeft) As DataGridViewTextBoxColumn
        Dim c As New DataGridViewTextBoxColumn()
        c.Name = name
        c.HeaderText = name
        c.Width = width
        c.DefaultCellStyle.Alignment = align
        c.SortMode = DataGridViewColumnSortMode.NotSortable
        Return c
    End Function

    ' ═══════════════════════════════════════
    ' MessageBus 구독
    ' ═══════════════════════════════════════

    Private Sub SubscribeBus()
        ' ── 초기 동기화 완료 → 최초 1회 API 데이터 로드 ──
        Subscribe(Topics.TRADE_SYNC_COMPLETE, AddressOf OnSyncComplete)

        ' ── 이후 실시간 신호로만 업데이트 ──
        Subscribe(Topics.TICK, AddressOf OnTick)
        Subscribe(Topics.TRADE_ORDER_FILLED, AddressOf OnTradeEvent)
        Subscribe(Topics.TRADE_POSITION_UPDATED, AddressOf OnTradeEvent)
        Subscribe(Topics.TRADE_BALANCE_UPDATED, AddressOf OnTradeEvent)
        Subscribe(Topics.TRADE_ORDER_ACCEPTED, AddressOf OnTradeEvent)
        Subscribe(Topics.TRADE_ORDER_REJECTED, AddressOf OnTradeEvent)
        Subscribe(Topics.TRADE_RISK_ALERT, AddressOf OnTradeEvent)
    End Sub

    Private Sub Subscribe(topic As String, handler As Action(Of Msg))
        MessageBus.I.On(topic, handler)
        _handlers.Add(New KeyValuePair(Of String, Action(Of Msg))(topic, handler))
    End Sub

    Protected Overrides Sub UnsubscribeAll()
        For Each kv In _handlers
            MessageBus.I.Off(kv.Key, kv.Value)
        Next
        _handlers.Clear()
    End Sub

    ' ═══════════════════════════════════════
    ' 초기 동기화 완료 → 최초 1회 로드
    ' ═══════════════════════════════════════
    '
    ' TradeManager 설계 원칙:
    '   1) 프로그램 시작 시 1회만 OPW00018(잔고) + OPT10075(미체결) TR 조회
    '   2) 이후 잔고/주문은 Chejan 이벤트로만 실시간 추적 (TR 재조회 절대 금지)
    '   3) 실시간 틱 → 보유종목 현재가 업데이트
    '
    ' TradeMonitorForm은 이 원칙을 그대로 따름:
    '   TRADE_SYNC_COMPLETE → InitialLoad() (1회)
    '   TICK → 현재가/손익 갱신
    '   TRADE_* 이벤트 → 잔고/미체결 변동 반영

    Private Sub OnSyncComplete(m As Msg)
        AppLogger.I.Info($"매매 모니터: 초기 동기화 수신 — 잔고 {m.Int("positionCount")}종목, " &
                         $"미체결 {m.Int("orderCount")}건, 예수금 {m.Lng("cash"):N0}원", "TradeMonitor")
        SafeUI(Sub() InitialLoad())
    End Sub

    ''' <summary>
    ''' 최초 1회: TradeManager 인메모리 상태에서 그리드 전체 채우기.
    ''' TradeManager는 이미 OPW00018/OPT10075로 동기화 완료된 상태.
    ''' </summary>
    Private Sub InitialLoad()
        RefreshBalance()
        RefreshOrders()
        RefreshDashboard()

        ' 보유종목 실시간 구독 확인 (TradeManager가 이미 했지만 안전장치)
        Dim positions = TradeManager.I.GetPositions()
        If positions.Count > 0 Then
            Dim codes = String.Join(";", positions.Select(Function(p) p.Code))
            MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", codes)
        End If

        AppLogger.I.Info($"매매 모니터: 초기 로드 완료 — " &
                         $"잔고 {positions.Count}종목, 미체결 {TradeManager.I.GetActiveOrders().Count}건", "TradeMonitor")
    End Sub

    ' ═══════════════════════════════════════
    ' 실시간 신호 → 잔고 현재가/손익 업데이트
    ' ═══════════════════════════════════════

    Private Sub OnTick(m As Msg)
        ' 보유종목 틱만 처리 (TradeManager.OnTick이 이미 현재가 갱신)
        Dim code = m.Str("code")
        If String.IsNullOrEmpty(code) Then Return
        If Not TradeManager.I.HasPosition(code) Then Return

        Dim now = DateTime.Now
        If (now - _lastBalanceRefresh).TotalMilliseconds < REFRESH_INTERVAL_MS Then Return
        _lastBalanceRefresh = now

        SafeUI(Sub()
                   RefreshBalance()
                   RefreshDashboard()
               End Sub)
    End Sub

    Private Sub OnTradeEvent(m As Msg)
        SafeUI(Sub()
                   RefreshBalance()
                   RefreshOrders()
                   RefreshDashboard()
               End Sub)
    End Sub

    Private Sub SafeUI(action As Action)
        If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(action)
            Else
                action()
            End If
        Catch
        End Try
    End Sub

    ' ═══════════════════════════════════════
    ' 대시보드 갱신
    ' ═══════════════════════════════════════

    Private Sub RefreshDashboard()
        Dim now = DateTime.Now
        If (now - _lastDashboardRefresh).TotalMilliseconds < REFRESH_INTERVAL_MS Then Return
        _lastDashboardRefresh = now

        Dim tm = TradeManager.I
        Dim totalEval = tm.TotalEvalAmount
        Dim totalPnL = tm.TotalProfitLoss
        Dim cash = tm.AvailableCash
        Dim totalAsset = totalEval + cash
        Dim posCount = tm.PositionCount

        _lblTotalAsset.Text = $"총자산: {totalAsset:N0}원"
        _lblCash.Text = $"예수금: {cash:N0}원"
        _lblPositionCount.Text = $"보유: {posCount}종목"

        ' 총 손익 색상
        If totalPnL > 0 Then
            _lblTotalPnL.Text = $"총손익: +{totalPnL:N0}원"
            _lblTotalPnL.ForeColor = Color.Tomato
        ElseIf totalPnL < 0 Then
            _lblTotalPnL.Text = $"총손익: {totalPnL:N0}원"
            _lblTotalPnL.ForeColor = Color.DodgerBlue
        Else
            _lblTotalPnL.Text = "총손익: 0원"
            _lblTotalPnL.ForeColor = Color.White
        End If

        ' ZeroLoss 전략 상태
        Dim zl = ZeroLossLiveStrategy.I
        If zl.IsRunning Then
            Dim zlPnL = zl.DailyRealizedPnL
            _lblZeroLossStatus.Text = zl.GetStatusSummary()
            _lblZeroLossStatus.ForeColor = Color.LimeGreen

            If zlPnL >= 0 Then
                _lblDailyPnL.Text = $"당일실현: +{zlPnL:0.00}%"
                _lblDailyPnL.ForeColor = Color.Tomato
            Else
                _lblDailyPnL.Text = $"당일실현: {zlPnL:0.00}%"
                _lblDailyPnL.ForeColor = Color.DodgerBlue
            End If
        Else
            _lblZeroLossStatus.Text = "ZeroLoss: OFF"
            _lblZeroLossStatus.ForeColor = Color.Gray
            _lblDailyPnL.Text = "당일실현: —"
            _lblDailyPnL.ForeColor = Color.LightGray
        End If
    End Sub

    ' ═══════════════════════════════════════
    ' 잔고 그리드 갱신
    ' ═══════════════════════════════════════

    Private Sub RefreshBalance()
        Dim positions = TradeManager.I.GetPositions()
        _lblBalanceCount.Text = $"{positions.Count}종목"

        ' 행 수 맞추기
        While _gridBalance.Rows.Count > positions.Count
            _gridBalance.Rows.RemoveAt(_gridBalance.Rows.Count - 1)
        End While
        While _gridBalance.Rows.Count < positions.Count
            _gridBalance.Rows.Add()
        End While

        For i = 0 To positions.Count - 1
            Dim pos = positions(i)
            Dim row = _gridBalance.Rows(i)

            row.Cells(0).Value = pos.Code
            row.Cells(1).Value = pos.Name
            row.Cells(2).Value = pos.Quantity.ToString("N0")
            row.Cells(3).Value = pos.AvailableQty.ToString("N0")
            row.Cells(4).Value = pos.AvgPrice.ToString("N0")
            row.Cells(5).Value = pos.CurrentPrice.ToString("N0")
            row.Cells(6).Value = pos.EvalAmount.ToString("N0")

            ' 손익 색상
            If pos.ProfitLoss > 0 Then
                row.Cells(7).Value = $"+{pos.ProfitLoss:N0}"
                row.Cells(7).Style.ForeColor = Color.Tomato
                row.Cells(8).Value = $"+{pos.ProfitRate:0.00}%"
                row.Cells(8).Style.ForeColor = Color.Tomato
            ElseIf pos.ProfitLoss < 0 Then
                row.Cells(7).Value = pos.ProfitLoss.ToString("N0")
                row.Cells(7).Style.ForeColor = Color.DodgerBlue
                row.Cells(8).Value = $"{pos.ProfitRate:0.00}%"
                row.Cells(8).Style.ForeColor = Color.DodgerBlue
            Else
                row.Cells(7).Value = "0"
                row.Cells(7).Style.ForeColor = Color.WhiteSmoke
                row.Cells(8).Value = "0.00%"
                row.Cells(8).Style.ForeColor = Color.WhiteSmoke
            End If

            ' 전략명 (TradeManager 주문 이력에서 추출 시도)
            row.Cells(9).Value = ""
            row.Tag = pos
        Next
    End Sub

    ' ═══════════════════════════════════════
    ' 미체결 그리드 갱신
    ' ═══════════════════════════════════════

    Private Sub RefreshOrders()
        Dim orders = TradeManager.I.GetActiveOrders().Where(Function(o) Not o.IsDone).ToList()
        _lblOrderCount.Text = $"{orders.Count}건"

        While _gridOrders.Rows.Count > orders.Count
            _gridOrders.Rows.RemoveAt(_gridOrders.Rows.Count - 1)
        End While
        While _gridOrders.Rows.Count < orders.Count
            _gridOrders.Rows.Add()
        End While

        For i = 0 To orders.Count - 1
            Dim ord = orders(i)
            Dim row = _gridOrders.Rows(i)

            row.Cells(0).Value = If(ord.KiwoomOrderNo <> "", ord.KiwoomOrderNo, ord.OrderId.Substring(0, 8))
            row.Cells(1).Value = ord.Code
            row.Cells(2).Value = ord.Name
            row.Cells(3).Value = ord.SideText
            row.Cells(4).Value = ord.OrderQty.ToString("N0")
            row.Cells(5).Value = If(ord.OrderPrice > 0, ord.OrderPrice.ToString("N0"), "시장가")
            row.Cells(6).Value = ord.FilledQty.ToString("N0")
            row.Cells(7).Value = ord.UnfilledQty.ToString("N0")
            row.Cells(8).Value = ord.Status.ToString()
            row.Cells(9).Value = ord.StrategyName
            row.Cells(10).Value = ord.RequestTime.ToString("HH:mm:ss")

            ' 매수/매도 색상
            If ord.Side = OrderSide.Buy Then
                row.Cells(3).Style.ForeColor = Color.Tomato
            Else
                row.Cells(3).Style.ForeColor = Color.DodgerBlue
            End If

            row.Tag = ord
        Next
    End Sub

    ' ═══════════════════════════════════════
    ' 매도 버튼 핸들러
    ' ═══════════════════════════════════════

    Private Sub OnSellMarketClick(sender As Object, e As EventArgs)
        If _gridBalance.SelectedRows.Count = 0 Then
            MessageBox.Show("매도할 종목을 선택하세요.", "매도", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim pos = TryCast(_gridBalance.SelectedRows(0).Tag, PositionItem)
        If pos Is Nothing OrElse pos.AvailableQty <= 0 Then
            MessageBox.Show("매도 가능한 수량이 없습니다.", "매도", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        If MessageBox.Show($"{pos.Name} ({pos.Code}) {pos.AvailableQty}주를 시장가 매도하시겠습니까?",
                           "시장가 매도 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            Return
        End If

        TradeManager.I.SellMarket(pos.Code, pos.AvailableQty, "수동매도", "TradeMonitor 시장가 매도")
        AppLogger.I.Trade($"수동 매도: {pos.Code} {pos.Name} {pos.AvailableQty}주 시장가", "TradeMonitor")
    End Sub

    Private Sub OnSellAllClick(sender As Object, e As EventArgs)
        Dim positions = TradeManager.I.GetPositions()
        If positions.Count = 0 Then
            MessageBox.Show("보유 종목이 없습니다.", "전량 청산", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        If MessageBox.Show($"보유 중인 {positions.Count}종목 전체를 시장가 매도하시겠습니까?",
                           "전량 청산 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        For Each pos In positions
            If pos.AvailableQty > 0 Then
                TradeManager.I.SellMarket(pos.Code, pos.AvailableQty, "전량청산", "TradeMonitor 전량 청산")
            End If
        Next
        AppLogger.I.Trade($"전량 청산 실행: {positions.Count}종목", "TradeMonitor")
    End Sub

    ' ═══════════════════════════════════════
    ' 미체결 취소 버튼 핸들러
    ' ═══════════════════════════════════════

    Private Sub OnCancelOrderClick(sender As Object, e As EventArgs)
        If _gridOrders.SelectedRows.Count = 0 Then
            MessageBox.Show("취소할 주문을 선택하세요.", "주문 취소", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim ord = TryCast(_gridOrders.SelectedRows(0).Tag, OrderItem)
        If ord Is Nothing OrElse ord.IsDone Then
            MessageBox.Show("취소할 수 없는 주문입니다.", "주문 취소", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        If MessageBox.Show($"{ord.SideText} {ord.Name} ({ord.Code}) {ord.UnfilledQty}주 주문을 취소하시겠습니까?",
                           "주문 취소 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            Return
        End If

        Dim orderNo = If(ord.KiwoomOrderNo <> "", ord.KiwoomOrderNo, ord.OrderId)
        MessageBus.I.Emit(Topics.ORDER_CANCEL,
                          "code", ord.Code,
                          "orderNo", orderNo,
                          "qty", ord.UnfilledQty)
        AppLogger.I.Trade($"주문 취소 요청: {ord.SideText} {ord.Code} {ord.Name} {ord.UnfilledQty}주", "TradeMonitor")
    End Sub

    Private Sub OnCancelAllClick(sender As Object, e As EventArgs)
        Dim orders = TradeManager.I.GetActiveOrders().Where(Function(o) Not o.IsDone).ToList()
        If orders.Count = 0 Then
            MessageBox.Show("미체결 주문이 없습니다.", "전체 취소", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        If MessageBox.Show($"미체결 {orders.Count}건 전체를 취소하시겠습니까?",
                           "전체 취소 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        For Each ord In orders
            Dim orderNo = If(ord.KiwoomOrderNo <> "", ord.KiwoomOrderNo, ord.OrderId)
            MessageBus.I.Emit(Topics.ORDER_CANCEL,
                              "code", ord.Code,
                              "orderNo", orderNo,
                              "qty", ord.UnfilledQty)
        Next
        AppLogger.I.Trade($"전체 미체결 취소: {orders.Count}건", "TradeMonitor")
    End Sub

End Class
