' ═══════════════════════════════════════════════════════════════
' CircuitDesignerForm.vb — 전략 회로 설계기 + 캔들 타임라인 검증
' ═══════════════════════════════════════════════════════════════
' [v4.2] 종목 선택 → 캔들 로드 → 타임라인 슬라이더 드래그 →
'        해당 시점 지표 계산 → 회로 평가 → 노드별 LED/프로브 표시
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MainApp.SimTrade
Imports MainApp.SimTrade.Circuit

Public Class CircuitDesignerForm
    Inherits Form

    Private _engine As CircuitEngine
    Private _circuit As CircuitDefinition
    Private _settings As SimTradeSettings
    Private _selectedNode As CircuitNode = Nothing

    ' ── 캔들 타임라인 ──
    Private _candles As List(Of CandleItem) = Nothing
    Private _indicatorEngine As IndicatorEngine = Nothing
    Private _currentCandleIndex As Integer = -1
    Private _stockCode As String = ""
    Private _stockName As String = ""

    ' ── UI ──
    Private WithEvents _canvas As New PictureBox()
    Private WithEvents _tmrRefresh As New Timer()
    Private _pnlParams As Panel
    Private _lblInfo As Label
    Private _chkLive As CheckBox

    ' ── 타임라인 UI ──
    Private _pnlTimeline As Panel
    Private WithEvents _trkCandle As TrackBar
    Private _lblCandleInfo As Label
    Private _lblResult As Label
    Private _txtStockCode As TextBox
    Private _btnLoadStock As Button

    ' ── 드래그 ──
    Private _isDragging As Boolean = False
    Private _dragOffset As Point

    Public Sub New(settings As SimTradeSettings)
        _settings = settings
        _engine = New CircuitEngine(settings)
        _circuit = CircuitEngine.CreateDefaultCircuit(settings)
        _engine.LoadCircuit(_circuit)

        InitUI()
        _tmrRefresh.Interval = 500
        _tmrRefresh.Start()
    End Sub

    Private Sub InitUI()
        Me.Text = "Strategy Circuit Designer v2.0 — 캔들 타임라인 검증"
        Me.Size = New Size(1300, 900)
        Me.BackColor = Color.FromArgb(25, 25, 30)
        Me.ForeColor = Color.White
        Me.DoubleBuffered = True

        ' ── 상단: 종목 입력 + 로드 ──
        Dim pnlTop As New Panel()
        pnlTop.Dock = DockStyle.Top
        pnlTop.Height = 45
        pnlTop.BackColor = Color.FromArgb(40, 40, 45)

        Dim lblCode As New Label()
        lblCode.Text = "종목코드:"
        lblCode.Location = New Point(10, 12)
        lblCode.AutoSize = True
        lblCode.ForeColor = Color.White
        pnlTop.Controls.Add(lblCode)

        _txtStockCode = New TextBox()
        _txtStockCode.Location = New Point(80, 9)
        _txtStockCode.Size = New Size(80, 25)
        _txtStockCode.BackColor = Color.FromArgb(50, 50, 55)
        _txtStockCode.ForeColor = Color.White
        _txtStockCode.Text = ""
        pnlTop.Controls.Add(_txtStockCode)

        _btnLoadStock = New Button()
        _btnLoadStock.Text = "캔들 로드"
        _btnLoadStock.Location = New Point(170, 7)
        _btnLoadStock.Size = New Size(90, 28)
        _btnLoadStock.FlatStyle = FlatStyle.Flat
        _btnLoadStock.BackColor = Color.FromArgb(60, 60, 65)
        _btnLoadStock.ForeColor = Color.White
        AddHandler _btnLoadStock.Click, AddressOf OnLoadStock
        pnlTop.Controls.Add(_btnLoadStock)

        _lblResult = New Label()
        _lblResult.Text = "종목을 로드하세요"
        _lblResult.Location = New Point(280, 12)
        _lblResult.Size = New Size(700, 20)
        _lblResult.ForeColor = Color.Gray
        pnlTop.Controls.Add(_lblResult)

        ' ── 하단: 타임라인 슬라이더 ──
        _pnlTimeline = New Panel()
        _pnlTimeline.Dock = DockStyle.Bottom
        _pnlTimeline.Height = 70
        _pnlTimeline.BackColor = Color.FromArgb(35, 35, 40)

        _trkCandle = New TrackBar()
        _trkCandle.Dock = DockStyle.Top
        _trkCandle.Height = 35
        _trkCandle.Minimum = 0
        _trkCandle.Maximum = 0
        _trkCandle.Value = 0
        _trkCandle.TickFrequency = 10
        _trkCandle.BackColor = Color.FromArgb(35, 35, 40)
        _pnlTimeline.Controls.Add(_trkCandle)

        _lblCandleInfo = New Label()
        _lblCandleInfo.Text = "캔들: - / -  |  시각: -  |  O/H/L/C: -  |  Vol: -"
        _lblCandleInfo.Dock = DockStyle.Bottom
        _lblCandleInfo.Height = 30
        _lblCandleInfo.ForeColor = Color.Cyan
        _lblCandleInfo.TextAlign = ContentAlignment.MiddleLeft
        _lblCandleInfo.Padding = New Padding(10, 0, 0, 0)
        _pnlTimeline.Controls.Add(_lblCandleInfo)

        ' ── 캔버스 (회로도 렌더링) ──
        _canvas.Dock = DockStyle.Fill
        _canvas.BackColor = Color.FromArgb(20, 20, 25)

        ' ── 우측 파라미터 패널 ──
        _pnlParams = New Panel()
        _pnlParams.Dock = DockStyle.Right
        _pnlParams.Width = 280
        _pnlParams.BackColor = Color.FromArgb(35, 35, 40)
        _pnlParams.AutoScroll = True

        _lblInfo = New Label()
        _lblInfo.Text = "노드를 클릭하세요"
        _lblInfo.Dock = DockStyle.Top
        _lblInfo.Height = 30
        _lblInfo.ForeColor = Color.Cyan
        _lblInfo.TextAlign = ContentAlignment.MiddleCenter
        _pnlParams.Controls.Add(_lblInfo)

        ' ── 하단 옵션 ──
        Dim pnlBottom As New Panel()
        pnlBottom.Dock = DockStyle.Bottom
        pnlBottom.Height = 40
        pnlBottom.BackColor = Color.FromArgb(40, 40, 45)

        _chkLive = New CheckBox()
        _chkLive.Text = "실시간 업데이트"
        _chkLive.Checked = False
        _chkLive.ForeColor = Color.White
        _chkLive.Location = New Point(10, 8)
        _chkLive.AutoSize = True
        pnlBottom.Controls.Add(_chkLive)

        Dim btnReset As New Button()
        btnReset.Text = "기본값 복원"
        btnReset.Location = New Point(160, 6)
        btnReset.Size = New Size(100, 28)
        btnReset.FlatStyle = FlatStyle.Flat
        btnReset.ForeColor = Color.White
        btnReset.BackColor = Color.FromArgb(60, 60, 65)
        AddHandler btnReset.Click, Sub(s, e) ResetAllParams()
        pnlBottom.Controls.Add(btnReset)

        ' ── 조립 (Fill을 먼저, 나머지 나중) ──
        Me.Controls.Add(_canvas)
        Me.Controls.Add(_pnlParams)
        Me.Controls.Add(_pnlTimeline)
        Me.Controls.Add(pnlBottom)
        Me.Controls.Add(pnlTop)
    End Sub

#Region "종목 로드"

    Private Sub OnLoadStock(sender As Object, e As EventArgs)
        Dim code = _txtStockCode.Text.Trim()
        If String.IsNullOrEmpty(code) Then
            MessageBox.Show("종목코드를 입력하세요.", "알림")
            Return
        End If

        ' StateManager에서 캔들 가져오기 시도
        Dim stateManager = GetStateManager()
        If stateManager Is Nothing Then
            _lblResult.Text = "모의매매가 실행 중이 아닙니다. [시작] 후 다시 시도하세요."
            _lblResult.ForeColor = Color.OrangeRed
            Return
        End If

        Dim stockState = stateManager.GetState(code)
        If stockState Is Nothing Then
            _lblResult.Text = $"종목 {code}이(가) 감시 목록에 없습니다."
            _lblResult.ForeColor = Color.OrangeRed
            Return
        End If

        If stockState.Candles Is Nothing OrElse stockState.Candles.Count < 5 Then
            _lblResult.Text = $"{code} 캔들 수 부족 ({If(stockState.Candles IsNot Nothing, stockState.Candles.Count, 0)}개)"
            _lblResult.ForeColor = Color.OrangeRed
            Return
        End If

        ' 캔들 복사 (원본 훼손 방지)
        _candles = stockState.Candles.ToList()
        _stockCode = stockState.Code
        _stockName = stockState.Name
        _indicatorEngine = New IndicatorEngine()

        ' 지표 등록
        RegisterIndicators()

        ' 슬라이더 설정
        _trkCandle.Minimum = 0
        _trkCandle.Maximum = _candles.Count - 1
        _trkCandle.Value = _candles.Count - 1
        _currentCandleIndex = _candles.Count - 1

        _lblResult.Text = $"{_stockCode} {_stockName} — 캔들 {_candles.Count}개 로드 완료"
        _lblResult.ForeColor = Color.LightGreen

        ' 초기 평가
        EvaluateAtCandle(_currentCandleIndex)
        _canvas.Invalidate()
    End Sub

    Private Sub RegisterIndicators()
        If _indicatorEngine Is Nothing Then Return
        Try : _indicatorEngine.Register(New SuperTrend_Indicator(_settings.ST_Period, CSng(_settings.ST_Multiplier))) : Catch : End Try
        Try : _indicatorEngine.Register(New RSI_Indicator(_settings.RSI_Period)) : Catch : End Try
        Try : _indicatorEngine.Register(New Volume_Indicator(_settings.VOL_Period)) : Catch : End Try
        Try : _indicatorEngine.Register(New OBV_Indicator(_settings.OBV_MAPeriod)) : Catch : End Try
        Try : _indicatorEngine.Register(New TickIntensity_Indicator(_settings.TICKINT_Timeframe)) : Catch : End Try
        Try : _indicatorEngine.Register(New MACD_Indicator(_settings.MACD_Fast, _settings.MACD_Slow, _settings.MACD_Signal)) : Catch : End Try
        Try : _indicatorEngine.Register(New JMA_Indicator(_settings.JMA_Period, _settings.JMA_Phase, _settings.JMA_Power)) : Catch : End Try
    End Sub

    ''' <summary>부모 SimTradeForm의 엔진에서 StateManager를 가져온다.</summary>
    Private Function GetStateManager() As StateManager
        Dim parentForm = TryCast(Me.Owner, SimTradeForm)
        If parentForm Is Nothing Then Return Nothing
        Try
            ' SimTradeForm._engine.Manager에 접근
            Dim engineField = GetType(SimTradeForm).GetField("_engine", Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Instance)
            If engineField Is Nothing Then Return Nothing
            Dim engine = TryCast(engineField.GetValue(parentForm), SimTradeEngine)
            If engine Is Nothing Then Return Nothing
            Return engine.Manager
        Catch
            Return Nothing
        End Try
    End Function

#End Region

#Region "캔들 위치 변경 → 지표 계산 → 회로 평가"

    Private Sub OnTrackBarScroll(sender As Object, e As EventArgs) Handles _trkCandle.Scroll
        If _candles Is Nothing OrElse _candles.Count = 0 Then Return
        _currentCandleIndex = _trkCandle.Value
        EvaluateAtCandle(_currentCandleIndex)
        _canvas.Invalidate()
    End Sub

    ''' <summary>캔들 인덱스 위치에서 지표 계산 후 회로 평가</summary>
    Private Sub EvaluateAtCandle(candleIdx As Integer)
        If _candles Is Nothing OrElse candleIdx < 0 OrElse candleIdx >= _candles.Count Then Return
        If _indicatorEngine Is Nothing Then Return

        ' 0~candleIdx 까지의 캔들로 지표 전체 재계산
        Dim subCandles = _candles.Take(candleIdx + 1).ToList()
        _indicatorEngine.CalculateAll(subCandles)

        ' 현재 캔들 정보
        Dim c = _candles(candleIdx)
        _lblCandleInfo.Text = $"캔들: {candleIdx + 1}/{_candles.Count}  |  " &
                              $"시각: {c.Dt:HH:mm:ss}  |  " &
                              $"O={c.Open:N0} H={c.High:N0} L={c.Low:N0} C={c.Close:N0}  |  " &
                              $"Vol={c.Volume:N0}  Tick={c.TickCount}"

        ' 지표 결과 → 임시 StockState 구성
        Dim tempState As New StockState()
        tempState.Code = _stockCode
        tempState.Name = _stockName
        tempState.CurrentPrice = CInt(c.Close)

        ' 지표 값 추출
        ExtractIndicators(tempState)

        ' 회로 평가
        Dim result = _engine.Evaluate(tempState, 0, 0, 0)

        ' 결과 표시
        Dim buyText = If(result.BuySignal, "● 매수 신호!", "○ 매수 없음")
        Dim condText = $"{result.BuyConditionsMet}/7"
        Dim filterText = If(result.ActiveFilterBlocks.Count > 0,
                            $"차단: {String.Join(",", result.ActiveFilterBlocks)}", "필터 통과")
        _lblResult.Text = $"{_stockCode} {_stockName}  |  {buyText} ({condText})  |  {filterText}"
        _lblResult.ForeColor = If(result.BuySignal, Color.LightGreen, Color.FromArgb(200, 200, 200))

        ' 선택 노드 갱신
        If _selectedNode IsNot Nothing Then ShowNodeParams(_selectedNode)
    End Sub

    Private Sub ExtractIndicators(state As StockState)
        If _indicatorEngine Is Nothing Then Return
        Dim results = _indicatorEngine.Results
        If results Is Nothing OrElse results.Count = 0 Then Return

        Dim idx = If(_currentCandleIndex >= 0, _currentCandleIndex, 0)

        Try
            Dim stList = FindResult(results, "ST_")
            If stList IsNot Nothing AndAlso stList.Count > idx Then
                state.ST_Direction = stList(idx).Val("Direction")
            End If
        Catch : End Try

        Try
            Dim jmaList = FindResult(results, "JMA_")
            If jmaList IsNot Nothing AndAlso jmaList.Count > idx Then
                state.JMA_Direction = jmaList(idx).Val("Direction")
                If idx > 0 AndAlso jmaList.Count > idx - 1 Then
                    state.JMA_PrevDirection = jmaList(idx - 1).Val("Direction")
                End If
                If state.JMA_Direction > 0 AndAlso state.JMA_PrevDirection <= 0 Then
                    state.JMA_TurnBar = 0
                Else
                    state.JMA_TurnBar = -1
                End If
            End If
        Catch : End Try

        Try
            Dim tiList = FindResult(results, "TICKINT_")
            If tiList IsNot Nothing AndAlso tiList.Count > idx Then
                state.TickSum_Normalized = tiList(idx).Val("TickSum")
                state.TickMA5_Normalized = tiList(idx).Val("MA5")
            End If
        Catch : End Try

        Try
            Dim obvList = FindResult(results, "OBV_")
            If obvList IsNot Nothing AndAlso obvList.Count > idx Then
                state.OBV_Direction = obvList(idx).Val("Direction")
            End If
        Catch : End Try

        Try
            Dim rsiList = FindResult(results, "RSI_")
            If rsiList IsNot Nothing AndAlso rsiList.Count > idx Then
                state.RSI_Value = rsiList(idx).Val("Value")
            End If
        Catch : End Try

        Try
            Dim macdList = FindResult(results, "MACD_")
            If macdList IsNot Nothing AndAlso macdList.Count > idx Then
                state.MACD_Histogram = macdList(idx).Val("Histogram")
            End If
        Catch : End Try

        Try
            Dim volList = FindResult(results, "VOL_")
            If volList IsNot Nothing AndAlso volList.Count > idx Then
                state.Volume_Ratio = volList(idx).Val("Ratio")
            End If
        Catch : End Try
    End Sub

    ''' <summary>딕셔너리에서 접두사로 지표 결과 찾기</summary>
    Private Shared Function FindResult(results As Dictionary(Of String, List(Of IndicatorResult)),
                                    prefix As String) As List(Of IndicatorResult)
        For Each kv In results
            If kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then Return kv.Value
        Next
        Return Nothing
    End Function


#End Region

#Region "렌더링"

    Private Sub OnCanvasPaint(sender As Object, e As PaintEventArgs) Handles _canvas.Paint
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(Color.FromArgb(20, 20, 25))

        If _circuit Is Nothing Then Return

        ' ── 와이어 그리기 ──
        For Each wire In _circuit.Wires
            Dim fromNode = _circuit.GetNode(wire.FromNodeId)
            Dim toNode = _circuit.GetNode(wire.ToNodeId)
            If fromNode Is Nothing OrElse toNode Is Nothing Then Continue For

            Dim wireColor As Color
            Select Case wire.State
                Case WireState.Active : wireColor = Color.FromArgb(0, 220, 0)
                Case WireState.Blocked : wireColor = Color.FromArgb(220, 50, 50)
                Case WireState.Warning : wireColor = Color.FromArgb(220, 180, 0)
                Case Else : wireColor = Color.FromArgb(80, 80, 80)
            End Select

            Using pen As New Pen(wireColor, If(wire.State = WireState.Active, 2.5F, 1.5F))
                Dim p1 = New Point(fromNode.X + fromNode.Width, fromNode.CenterPoint.Y)
                Dim p2 = New Point(toNode.X, toNode.CenterPoint.Y)
                Dim midX = (p1.X + p2.X) \ 2
                g.DrawBezier(pen, p1, New Point(midX, p1.Y), New Point(midX, p2.Y), p2)
            End Using
        Next

        ' ── 노드 그리기 ──
        For Each node In _circuit.Nodes
            DrawNode(g, node)
        Next
    End Sub

    Private Sub DrawNode(g As Graphics, node As CircuitNode)
        Dim rect As New Rectangle(node.X, node.Y, node.Width, node.Height)

        Dim bgColor = If(Not node.Enabled, Color.FromArgb(50, 50, 55),
                      If(node.IsTriggered, Color.FromArgb(20, 80, 20),
                         Color.FromArgb(40, 50, 70)))
        If _selectedNode IsNot Nothing AndAlso _selectedNode.Id = node.Id Then
            bgColor = Color.FromArgb(70, 90, 130)
        End If

        Using brush As New SolidBrush(bgColor)
            g.FillRoundedRectangle(brush, rect, 8)
        End Using

        Dim borderColor = If(node.IsTriggered AndAlso node.Enabled, Color.Lime,
                          If(Not node.Enabled, Color.Gray, Color.FromArgb(100, 140, 200)))
        Using pen As New Pen(borderColor, If(_selectedNode IsNot Nothing AndAlso _selectedNode.Id = node.Id, 2.5F, 1.0F))
            g.DrawRoundedRectangle(pen, rect, 8)
        End Using

        ' LED
        Dim ledColor = If(node.Enabled, If(node.IsTriggered, Color.Lime, Color.FromArgb(100, 100, 100)), Color.Red)
        Using ledBrush As New SolidBrush(ledColor)
            g.FillEllipse(ledBrush, node.X + 5, node.Y + 5, 10, 10)
        End Using

        ' 이름
        Using font As New Font("맑은 고딕", 9, FontStyle.Bold)
            Using textBrush As New SolidBrush(Color.White)
                g.DrawString(node.Name, font, textBrush, node.X + 20, node.Y + 5)
            End Using
        End Using

        ' 프로브 값
        If node.ProbeText <> "" Then
            Using font As New Font("Consolas", 8)
                Dim probeColor = If(node.IsTriggered, Color.LightGreen, Color.FromArgb(180, 180, 180))
                Using textBrush As New SolidBrush(probeColor)
                    g.DrawString(node.ProbeText, font, textBrush, node.X + 5, node.Y + node.Height - 18)
                End Using
            End Using
        End If
    End Sub

#End Region

#Region "마우스 이벤트"

    Private Sub OnCanvasMouseDown(sender As Object, e As MouseEventArgs) Handles _canvas.MouseDown
        _selectedNode = HitTest(e.Location)
        If _selectedNode IsNot Nothing Then
            ShowNodeParams(_selectedNode)
            _isDragging = True
            _dragOffset = New Point(e.X - _selectedNode.X, e.Y - _selectedNode.Y)
        End If
        _canvas.Invalidate()
    End Sub

    Private Sub OnCanvasMouseMove(sender As Object, e As MouseEventArgs) Handles _canvas.MouseMove
        If _isDragging AndAlso _selectedNode IsNot Nothing Then
            _selectedNode.X = e.X - _dragOffset.X
            _selectedNode.Y = e.Y - _dragOffset.Y
            _canvas.Invalidate()
        End If
    End Sub

    Private Sub OnCanvasMouseUp(sender As Object, e As MouseEventArgs) Handles _canvas.MouseUp
        _isDragging = False
    End Sub

    Private Sub OnCanvasDoubleClick(sender As Object, e As EventArgs) Handles _canvas.DoubleClick
        If _selectedNode IsNot Nothing AndAlso Not _selectedNode.Locked Then
            _selectedNode.Enabled = Not _selectedNode.Enabled
            ' 노드 변경 → 즉시 재평가
            If _currentCandleIndex >= 0 Then EvaluateAtCandle(_currentCandleIndex)
            ShowNodeParams(_selectedNode)
            _canvas.Invalidate()
        End If
    End Sub

    Private Function HitTest(pt As Point) As CircuitNode
        For i = _circuit.Nodes.Count - 1 To 0 Step -1
            Dim n = _circuit.Nodes(i)
            Dim rect As New Rectangle(n.X, n.Y, n.Width, n.Height)
            If rect.Contains(pt) Then Return n
        Next
        Return Nothing
    End Function

#End Region

#Region "파라미터 패널"

    Private Sub ShowNodeParams(node As CircuitNode)
        Dim toRemove = _pnlParams.Controls.Cast(Of Control).Where(Function(c) c IsNot _lblInfo).ToList()
        For Each c In toRemove : _pnlParams.Controls.Remove(c) : Next

        _lblInfo.Text = $"{node.Name} ({If(node.Enabled, "ON", "OFF")})"

        Dim y = 40

        If Not node.Locked Then
            Dim chk As New CheckBox()
            chk.Text = "활성화"
            chk.Checked = node.Enabled
            chk.Location = New Point(10, y)
            chk.ForeColor = Color.White
            chk.AutoSize = True
            AddHandler chk.CheckedChanged, Sub(s, e)
                                               node.Enabled = chk.Checked
                                               _lblInfo.Text = $"{node.Name} ({If(node.Enabled, "ON", "OFF")})"
                                               If _currentCandleIndex >= 0 Then EvaluateAtCandle(_currentCandleIndex)
                                               _canvas.Invalidate()
                                           End Sub
            _pnlParams.Controls.Add(chk)
            y += 30
        End If

        For Each param In node.Params
            Dim lbl As New Label()
            lbl.Text = param.Label
            lbl.Location = New Point(10, y + 3)
            lbl.Size = New Size(100, 20)
            lbl.ForeColor = Color.White
            _pnlParams.Controls.Add(lbl)

            Select Case param.DataType
                Case ParamDataType.IntNumber, ParamDataType.DecNumber
                    Dim nud As New NumericUpDown()
                    nud.Location = New Point(120, y)
                    nud.Size = New Size(100, 25)
                    nud.Minimum = If(param.MinValue IsNot Nothing, CDec(param.MinValue), 0)
                    nud.Maximum = If(param.MaxValue IsNot Nothing, CDec(param.MaxValue), 1000)
                    nud.Value = CDec(If(param.Value, param.DefaultValue))
                    nud.DecimalPlaces = If(param.DataType = ParamDataType.DecNumber, 1, 0)
                    nud.Increment = If(param.StepValue IsNot Nothing, CDec(param.StepValue), 1D)
                    nud.BackColor = Color.FromArgb(50, 50, 55)
                    nud.ForeColor = Color.White
                    Dim capturedParam = param
                    AddHandler nud.ValueChanged, Sub(s, e)
                                                     capturedParam.Value = nud.Value
                                                     If _currentCandleIndex >= 0 Then EvaluateAtCandle(_currentCandleIndex)
                                                     _canvas.Invalidate()
                                                 End Sub
                    _pnlParams.Controls.Add(nud)

                Case ParamDataType.Bool
                    Dim chk As New CheckBox()
                    chk.Checked = CBool(If(param.Value, param.DefaultValue))
                    chk.Location = New Point(120, y)
                    chk.ForeColor = Color.White
                    Dim capturedParam = param
                    AddHandler chk.CheckedChanged, Sub(s, e)
                                                       capturedParam.Value = chk.Checked
                                                       If _currentCandleIndex >= 0 Then EvaluateAtCandle(_currentCandleIndex)
                                                       _canvas.Invalidate()
                                                   End Sub
                    _pnlParams.Controls.Add(chk)
            End Select

            y += 35
        Next

        ' 프로브 표시
        If node.ProbeText <> "" Then
            Dim lblProbe As New Label()
            lblProbe.Text = $"[프로브] {node.ProbeText}"
            lblProbe.Location = New Point(10, y + 10)
            lblProbe.Size = New Size(260, 20)
            lblProbe.ForeColor = Color.LightGreen
            _pnlParams.Controls.Add(lblProbe)
        End If
    End Sub

    Private Sub ResetAllParams()
        For Each node In _circuit.Nodes
            For Each param In node.Params
                param.Reset()
            Next
            node.Enabled = True
        Next
        If _currentCandleIndex >= 0 Then EvaluateAtCandle(_currentCandleIndex)
        _canvas.Invalidate()
        If _selectedNode IsNot Nothing Then ShowNodeParams(_selectedNode)
    End Sub

#End Region

#Region "타이머 갱신"

    Private Sub OnRefresh(sender As Object, e As EventArgs) Handles _tmrRefresh.Tick
        If _chkLive IsNot Nothing AndAlso _chkLive.Checked Then
            ' 실시간 모드: StateManager에서 최신 캔들 가져와서 재로드
            If Not String.IsNullOrEmpty(_stockCode) Then
                Dim mgr = GetStateManager()
                If mgr IsNot Nothing Then
                    Dim st = mgr.GetState(_stockCode)
                    If st IsNot Nothing AndAlso st.Candles IsNot Nothing Then
                        _candles = st.Candles.ToList()
                        _trkCandle.Maximum = Math.Max(0, _candles.Count - 1)
                        _trkCandle.Value = _candles.Count - 1
                        _currentCandleIndex = _candles.Count - 1
                        EvaluateAtCandle(_currentCandleIndex)
                    End If
                End If
            End If
            _canvas.Invalidate()
        End If
    End Sub

#End Region

End Class

''' <summary>Graphics 확장: 둥근 사각형</summary>
Module GraphicsExtensions
    <System.Runtime.CompilerServices.Extension()>
    Public Sub FillRoundedRectangle(g As Graphics, brush As Brush, rect As Rectangle, radius As Integer)
        Using path As New GraphicsPath()
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90)
            path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90)
            path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90)
            path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90)
            path.CloseFigure()
            g.FillPath(brush, path)
        End Using
    End Sub

    <System.Runtime.CompilerServices.Extension()>
    Public Sub DrawRoundedRectangle(g As Graphics, pen As Pen, rect As Rectangle, radius As Integer)
        Using path As New GraphicsPath()
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90)
            path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90)
            path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90)
            path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90)
            path.CloseFigure()
            g.DrawPath(pen, path)
        End Using
    End Sub
End Module
