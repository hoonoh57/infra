' ═══════════════════════════════════════════════════════════════
' CircuitDesignerForm.vb — Phase 1: 캔들 차트 + 3색 신호 + 전략 점수
' ═══════════════════════════════════════════════════════════════
' [v5.0] 캔들 차트 클릭 → 지표 계산 → 회로 평가 → 3색 LED + 게이지 + 점수
' "What You See Is What You Trade" 핵심 엔진
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

    ' ── 캔들 데이터 ──
    Private _candles As List(Of CandleItem) = Nothing
    Private _indicatorEngine As IndicatorEngine = Nothing
    Private _currentCandleIndex As Integer = -1
    Private _stockCode As String = ""
    Private _stockName As String = ""
    Private _externalStateManager As StateManager = Nothing

    ' ── 3색 점수 시스템 ──
    Private _conditionScores As New Dictionary(Of String, Double)  ' C1~C7 충족률 0.0~1.0+
    Private _overallScore As Double = 0.0
    Private _lastEvalResult As CircuitEvalResult = Nothing

    ' ── UI 컨트롤 ──
    Private WithEvents _pnlChart As New DoubleBufferedPanel()
    ' 미니 캔들 차트
    Private WithEvents _canvas As New PictureBox()      ' 회로도
    Private WithEvents _tmrRefresh As New Timer()
    Private _pnlParams As Panel                        ' 우측 파라미터 패널
    Private _lblInfo As Label
    Private _chkLive As CheckBox

    ' ── 상단 ──
    Private _txtStockCode As TextBox
    Private _btnLoadStock As Button
    Private _lblResult As Label

    ' ── 하단 타임라인 ──
    Private _pnlTimeline As Panel
    Private WithEvents _trkCandle As TrackBar
    Private _lblCandleInfo As Label

    ' ── 하단 스코어 바 ──
    Private WithEvents _pnlScore As New Panel()

    ' ── 드래그 ──
    Private _isDragging As Boolean = False
    Private _dragOffset As Point

    ' ── 차트 스크롤 ──
    Private _chartVisibleBars As Integer = 80
    Private _chartScrollOffset As Integer = 0

    ' ── 차트 크로스헤어 ──
    Private _chartMousePos As Point = New Point(-1, -1)
    Private _chartHoverIndex As Integer = -1

    ' ── 3색 상수 ──
    Private Shared ReadOnly COLOR_PASS As Color = Color.FromArgb(68, 136, 255)      ' 파란색
    Private Shared ReadOnly COLOR_NEAR As Color = Color.FromArgb(255, 170, 0)       ' 노란색
    Private Shared ReadOnly COLOR_FAIL As Color = Color.FromArgb(255, 68, 68)       ' 빨간색
    Private Shared ReadOnly COLOR_OFF As Color = Color.FromArgb(80, 80, 80)         ' OFF
    Private Shared ReadOnly COLOR_BG_DARK As Color = Color.FromArgb(20, 20, 28)
    Private Shared ReadOnly COLOR_BG_MID As Color = Color.FromArgb(30, 32, 40)
    Private Shared ReadOnly COLOR_BG_PANEL As Color = Color.FromArgb(35, 38, 48)

    Public Sub New(settings As SimTradeSettings, Optional stateManager As StateManager = Nothing)
        _settings = settings
        _externalStateManager = stateManager
        _engine = New CircuitEngine(settings)
        _circuit = CircuitEngine.CreateDefaultCircuit(settings)
        _engine.LoadCircuit(_circuit)

        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint, True)
        InitUI()
        _tmrRefresh.Interval = 500
        _tmrRefresh.Start()
    End Sub


#Region "UI 초기화"
    Private _splitMain As SplitContainer = Nothing  ' ★ 차트/회로도 분할

    Private Sub InitUI()
        Me.Text = "Strategy Circuit Tester v5.0 — What You See Is What You Trade"
        Me.Size = New Size(1400, 1000)
        Me.BackColor = COLOR_BG_DARK
        Me.ForeColor = Color.White
        Me.DoubleBuffered = True

        ' ── 상단 바: 종목 입력 ──
        Dim pnlTop As New Panel()
        pnlTop.Dock = DockStyle.Top
        pnlTop.Height = 40
        pnlTop.BackColor = Color.FromArgb(38, 40, 50)

        Dim lblCode As New Label()
        lblCode.Text = "종목코드:"
        lblCode.Location = New Point(10, 10)
        lblCode.AutoSize = True
        lblCode.ForeColor = Color.White
        pnlTop.Controls.Add(lblCode)

        _txtStockCode = New TextBox()
        _txtStockCode.Location = New Point(80, 7)
        _txtStockCode.Size = New Size(80, 25)
        _txtStockCode.BackColor = Color.FromArgb(50, 52, 60)
        _txtStockCode.ForeColor = Color.White
        pnlTop.Controls.Add(_txtStockCode)

        _btnLoadStock = New Button()
        _btnLoadStock.Text = "캔들 로드"
        _btnLoadStock.Location = New Point(170, 5)
        _btnLoadStock.Size = New Size(85, 28)
        _btnLoadStock.FlatStyle = FlatStyle.Flat
        _btnLoadStock.BackColor = Color.FromArgb(60, 65, 80)
        _btnLoadStock.ForeColor = Color.White
        AddHandler _btnLoadStock.Click, AddressOf OnLoadStock
        pnlTop.Controls.Add(_btnLoadStock)

        _lblResult = New Label()
        _lblResult.Text = "종목 코드 입력 후 [캔들 로드]"
        _lblResult.Location = New Point(270, 10)
        _lblResult.Size = New Size(800, 20)
        _lblResult.ForeColor = Color.Gray
        pnlTop.Controls.Add(_lblResult)

        ' ── 미니 캔들 차트 (스크롤 가능 — 서브차트 포함) ──
        _pnlChart.Dock = DockStyle.Fill
        _pnlChart.BackColor = COLOR_BG_DARK
        _pnlChart.AutoScroll = False  ' 직접 높이 관리

        ' ── 하단 스코어 바 ──
        _pnlScore.Dock = DockStyle.Bottom
        _pnlScore.Height = 50
        _pnlScore.BackColor = Color.FromArgb(28, 30, 38)

        ' ── 하단 타임라인 ──
        _pnlTimeline = New Panel()
        _pnlTimeline.Dock = DockStyle.Bottom
        _pnlTimeline.Height = 65
        _pnlTimeline.BackColor = Color.FromArgb(32, 34, 42)

        _trkCandle = New TrackBar()
        _trkCandle.Dock = DockStyle.Top
        _trkCandle.Height = 35
        _trkCandle.Minimum = 0
        _trkCandle.Maximum = 0
        _trkCandle.Value = 0
        _trkCandle.TickFrequency = 10
        _trkCandle.BackColor = Color.FromArgb(32, 34, 42)
        _pnlTimeline.Controls.Add(_trkCandle)

        _lblCandleInfo = New Label()
        _lblCandleInfo.Text = "캔들: - / -  |  시각: -  |  O/H/L/C: -  |  Vol: -"
        _lblCandleInfo.Dock = DockStyle.Bottom
        _lblCandleInfo.Height = 25
        _lblCandleInfo.ForeColor = Color.Cyan
        _lblCandleInfo.Font = New Font("Consolas", 9)
        _lblCandleInfo.TextAlign = ContentAlignment.MiddleLeft
        _lblCandleInfo.Padding = New Padding(10, 0, 0, 0)
        _pnlTimeline.Controls.Add(_lblCandleInfo)

        ' ── 하단 옵션 ──
        Dim pnlBottom As New Panel()
        pnlBottom.Dock = DockStyle.Bottom
        pnlBottom.Height = 35
        pnlBottom.BackColor = Color.FromArgb(38, 40, 50)

        _chkLive = New CheckBox()
        _chkLive.Text = "실시간 업데이트"
        _chkLive.Checked = False
        _chkLive.ForeColor = Color.White
        _chkLive.Location = New Point(10, 7)
        _chkLive.AutoSize = True
        pnlBottom.Controls.Add(_chkLive)

        Dim btnReset As New Button()
        btnReset.Text = "기본값 복원"
        btnReset.Location = New Point(160, 4)
        btnReset.Size = New Size(95, 26)
        btnReset.FlatStyle = FlatStyle.Flat
        btnReset.ForeColor = Color.White
        btnReset.BackColor = Color.FromArgb(60, 65, 80)
        AddHandler btnReset.Click, Sub(s, ev) ResetAllParams()
        pnlBottom.Controls.Add(btnReset)

        ' ── 캔버스 (회로도) ──
        _canvas.Dock = DockStyle.Fill
        _canvas.BackColor = COLOR_BG_DARK

        ' ── 우측 파라미터 패널 ──
        _pnlParams = New Panel()
        _pnlParams.Dock = DockStyle.Right
        _pnlParams.Width = 280
        _pnlParams.BackColor = COLOR_BG_PANEL
        _pnlParams.AutoScroll = True

        _lblInfo = New Label()
        _lblInfo.Text = "노드를 클릭하세요"
        _lblInfo.Dock = DockStyle.Top
        _lblInfo.Height = 30
        _lblInfo.ForeColor = Color.Cyan
        _lblInfo.Font = New Font("맑은 고딕", 10, FontStyle.Bold)
        _lblInfo.TextAlign = ContentAlignment.MiddleCenter
        _pnlParams.Controls.Add(_lblInfo)

        ' ── ★ SplitContainer: 차트(상) / 회로도(하) ──
        _splitMain = New SplitContainer()
        _splitMain.Dock = DockStyle.Fill
        _splitMain.Orientation = Orientation.Horizontal
        _splitMain.SplitterDistance = 350
        _splitMain.SplitterWidth = 6
        _splitMain.BackColor = Color.FromArgb(60, 65, 80)
        _splitMain.Panel1.BackColor = COLOR_BG_DARK
        _splitMain.Panel2.BackColor = COLOR_BG_DARK
        _splitMain.Panel1MinSize = 150
        _splitMain.Panel2MinSize = 150

        ' Panel1: 차트
        _splitMain.Panel1.Controls.Add(_pnlChart)

        ' Panel2: 회로도 + 파라미터
        _splitMain.Panel2.Controls.Add(_canvas)
        _splitMain.Panel2.Controls.Add(_pnlParams)

        ' ── 조립 순서 중요: Fill은 마지막 ──
        Me.Controls.Add(_splitMain)
        Me.Controls.Add(_pnlScore)
        Me.Controls.Add(_pnlTimeline)
        Me.Controls.Add(pnlBottom)
        Me.Controls.Add(pnlTop)
    End Sub


#End Region

#Region "종목 로드"

    Private Sub OnLoadStock(sender As Object, e As EventArgs)
        Dim code = _txtStockCode.Text.Trim()
        If String.IsNullOrEmpty(code) Then
            MessageBox.Show("종목코드를 입력하세요.", "알림")
            Return
        End If

        Dim loadedCandles As List(Of CandleItem) = Nothing
        Dim loadedName As String = ""
        Dim sourceDesc As String = ""

        ' 1) StateManager (모의매매 실행 중)
        Dim sm = GetStateManager()
        If sm IsNot Nothing Then
            Dim st = sm.GetState(code)
            If st IsNot Nothing AndAlso st.Candles IsNot Nothing AndAlso st.Candles.Count >= 5 Then
                loadedCandles = st.Candles.ToList()
                loadedName = st.Name
                sourceDesc = "모의매매(실시간)"
            End If
        End If

        ' 2) StockInfoManager 캐시
        If loadedCandles Is Nothing OrElse loadedCandles.Count < 5 Then
            Try
                Dim cached = StockInfoManager.I.GetCachedCandleItems(code)
                If cached IsNot Nothing AndAlso cached.Count >= 5 Then
                    loadedCandles = cached
                    loadedName = If(StockInfoManager.I.GetItem(code)?.Name, code)
                    sourceDesc = "캐시(StockInfoManager)"
                End If
            Catch
            End Try
        End If

        ' 3) 로컬 JSON 파일
        If loadedCandles Is Nothing OrElse loadedCandles.Count < 5 Then
            loadedCandles = LoadCandlesFromLocalJson(code)
            If loadedCandles IsNot Nothing AndAlso loadedCandles.Count >= 5 Then
                sourceDesc = "로컬파일(JSON)"
            End If
        End If

        If loadedCandles Is Nothing OrElse loadedCandles.Count < 5 Then
            _lblResult.Text = $"{code} — 캔들 데이터 없음. 모의매매 실행 또는 이전 캐시 필요."
            _lblResult.ForeColor = Color.OrangeRed
            Return
        End If

        ' 성공: 필드 세팅
        _candles = loadedCandles

        ' dt 보정: 기본값(0001-01-01)이면 인덱스 기반 가상 시간 생성
        Dim hasValidDt = _candles.Any(Function(c) c.Dt.Year > 2000)
        If Not hasValidDt Then
            Dim baseTime = New DateTime(2026, 3, 21, 9, 0, 0)
            For i = 0 To _candles.Count - 1
                _candles(i).Dt = baseTime.AddMinutes(i)
            Next
        End If
        _stockCode = code
        _stockName = If(String.IsNullOrEmpty(loadedName), code, loadedName)
        _indicatorEngine = New IndicatorEngine()
        RegisterIndicators()

        _trkCandle.Minimum = 0
        _trkCandle.Maximum = _candles.Count - 1
        _trkCandle.Value = _candles.Count - 1
        _currentCandleIndex = _candles.Count - 1
        _chartScrollOffset = Math.Max(0, _candles.Count - _chartVisibleBars)

        _lblResult.Text = $"{_stockCode} {_stockName} — {_candles.Count}개 로드 ({sourceDesc})"
        _lblResult.ForeColor = Color.LightGreen

        EvaluateAtCandle(_currentCandleIndex)
        _pnlChart.Invalidate()
        _canvas.Invalidate()
        _pnlScore.Invalidate()
    End Sub

    Private Function LoadCandlesFromLocalJson(code As String) As List(Of CandleItem)
        Try
            Dim baseDir = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                          "Infra", "StrategyLab", "candles")
            If Not IO.Directory.Exists(baseDir) Then Return Nothing
            For Each prov In IO.Directory.GetDirectories(baseDir)
                For Each tf In IO.Directory.GetDirectories(prov)
                    Dim path = IO.Path.Combine(tf, $"{code}.json")
                    If IO.File.Exists(path) Then
                        Dim json = IO.File.ReadAllText(path)
                        Dim dict = Newtonsoft.Json.JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(json)
                        Dim rows = TryCast(dict?("rows"), Newtonsoft.Json.Linq.JArray)
                        If rows IsNot Nothing AndAlso rows.Count > 0 Then
                            Dim list As New List(Of CandleItem)(rows.Count)
                            For Each r In rows
                                Dim d = r.ToObject(Of Dictionary(Of String, String))()
                                Dim ci As New CandleItem()

                                ' ── Dt 파싱: date+time 분리 필드 우선, dt 폴백 ──
                                Dim dateStr = ""
                                Dim timeStr = ""
                                Dim dtStr = ""
                                If d.ContainsKey("date") Then dateStr = d("date").Trim()
                                If d.ContainsKey("time") Then timeStr = d("time").Trim()
                                If d.ContainsKey("dt") Then dtStr = d("dt").Trim()

                                If dateStr <> "" AndAlso timeStr <> "" Then
                                    Dim combined = dateStr & timeStr.PadLeft(4, "0"c)
                                    If Not DateTime.TryParseExact(combined, "yyyyMMddHHmm",
                                        Globalization.CultureInfo.InvariantCulture,
                                        Globalization.DateTimeStyles.None, ci.Dt) Then
                                        DateTime.TryParseExact(combined, "yyyyMMddHHmmss",
                                            Globalization.CultureInfo.InvariantCulture,
                                            Globalization.DateTimeStyles.None, ci.Dt)
                                    End If
                                ElseIf dtStr <> "" Then
                                    If Not DateTime.TryParse(dtStr, ci.Dt) Then
                                        If Not DateTime.TryParseExact(dtStr, "yyyyMMddHHmmss",
                                            Globalization.CultureInfo.InvariantCulture,
                                            Globalization.DateTimeStyles.None, ci.Dt) Then
                                            DateTime.TryParseExact(dtStr, "yyyyMMddHHmm",
                                                Globalization.CultureInfo.InvariantCulture,
                                                Globalization.DateTimeStyles.None, ci.Dt)
                                        End If
                                    End If
                                End If

                                Dim sv As String = Nothing
                                If d.TryGetValue("open", sv) Then Single.TryParse(sv, ci.Open)
                                If d.TryGetValue("high", sv) Then Single.TryParse(sv, ci.High)
                                If d.TryGetValue("low", sv) Then Single.TryParse(sv, ci.Low)
                                If d.TryGetValue("close", sv) Then Single.TryParse(sv, ci.Close)
                                Dim lv As String = Nothing
                                If d.TryGetValue("volume", lv) Then Long.TryParse(lv, ci.Volume)
                                list.Add(ci)
                            Next
                            If list.Count >= 5 Then Return list
                        End If
                    End If
                Next
            Next
        Catch
        End Try
        Return Nothing
    End Function


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

    Private Function GetStateManager() As StateManager
        If _externalStateManager IsNot Nothing Then Return _externalStateManager

        Dim parentForm = TryCast(Me.Owner, SimTradeForm)
        If parentForm Is Nothing Then Return Nothing
        Try
            Dim engineField = GetType(SimTradeForm).GetField("_engine", Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Instance)
            If engineField Is Nothing Then Return Nothing
            Dim eng = TryCast(engineField.GetValue(parentForm), SimTradeEngine)
            If eng Is Nothing Then Return Nothing
            Return eng.Manager
        Catch
            Return Nothing
        End Try
    End Function


#End Region

#Region "캔들 위치 → 지표 → 회로 평가 → 3색 점수"

    Private Sub OnTrackBarScroll(sender As Object, e As EventArgs) Handles _trkCandle.Scroll
        If _candles Is Nothing OrElse _candles.Count = 0 Then Return
        _currentCandleIndex = _trkCandle.Value
        EvaluateAtCandle(_currentCandleIndex)
        EnsureCandleVisible(_currentCandleIndex)
        _pnlChart.Invalidate()
        _canvas.Invalidate()
        _pnlScore.Invalidate()
    End Sub

    Private Sub EvaluateAtCandle(candleIdx As Integer)
        If _candles Is Nothing OrElse candleIdx < 0 OrElse candleIdx >= _candles.Count Then Return
        If _indicatorEngine Is Nothing Then Return

        ' 0~candleIdx까지의 캔들로 지표 전체 재계산
        Dim subCandles = _candles.Take(candleIdx + 1).ToList()
        _indicatorEngine.CalculateAll(subCandles)

        ' 캔들 정보 라벨
        Dim c = _candles(candleIdx)
        _lblCandleInfo.Text = $"캔들: {candleIdx + 1}/{_candles.Count}  |  " &
                              $"{c.Dt:HH:mm:ss}  |  " &
                              $"O={c.Open:N0} H={c.High:N0} L={c.Low:N0} C={c.Close:N0}  |  " &
                              $"Vol={c.Volume:N0}"

        ' 임시 StockState 구성
        Dim tempState As New StockState()
        tempState.Code = _stockCode
        tempState.Name = _stockName
        tempState.CurrentPrice = CInt(c.Close)
        ExtractIndicators(tempState)

        ' 회로 평가
        _lastEvalResult = _engine.Evaluate(tempState, 0, 0, 0)

        ' ── 3색 점수 계산 ──
        CalcConditionScores(tempState)

        ' 결과 표시
        Dim buyText = If(_lastEvalResult.BuySignal, "● 매수 신호!", "○ 매수 없음")
        Dim condText = $"{_lastEvalResult.BuyConditionsMet}/7"
        Dim filterText = If(_lastEvalResult.ActiveFilterBlocks.Count > 0,
                            $"차단: {String.Join(",", _lastEvalResult.ActiveFilterBlocks)}", "필터 통과")
        _lblResult.Text = $"{_stockCode} {_stockName}  |  {buyText} ({condText})  |  {filterText}  |  점수: {_overallScore:F0}/100"
        _lblResult.ForeColor = If(_lastEvalResult.BuySignal, Color.FromArgb(68, 200, 255), Color.FromArgb(200, 200, 200))

        If _selectedNode IsNot Nothing Then ShowNodeParams(_selectedNode)
    End Sub

    ''' <summary>
    ''' 각 조건(C1~C7)의 충족률을 0.0 ~ 1.0+ 로 계산.
    ''' 1.0 이상 = PASS(파란색), 0.7~0.99 = NEAR(노란색), 0.7 미만 = FAIL(빨간색)
    ''' </summary>
    Private Sub CalcConditionScores(state As StockState)
        _conditionScores.Clear()

        ' C1: ST Direction (1=통과, -1=실패) → 비율: Direction > 0 이면 100%, 아니면 0%
        _conditionScores("C1_ST") = If(state.ST_Direction > 0, 1.0, 0.0)

        ' C2: JMA Direction > 0 AND TurnBar 범위 내
        Dim confirmBars = 2
        Dim c2Node = _circuit.GetNode("C2_JMA")
        If c2Node IsNot Nothing Then confirmBars = CInt(If(c2Node.GetParam("ConfirmBars")?.Value, 2))
        Dim jmaScore = 0.0
        If state.JMA_Direction > 0 Then
            jmaScore = 0.5  ' 방향은 맞음
            If state.JMA_TurnBar >= 0 AndAlso state.JMA_TurnBar <= confirmBars Then
                jmaScore = 1.0  ' 전환봉 범위 내
            End If
        End If
        _conditionScores("C2_JMA") = jmaScore

        ' C3: TickSum >= Threshold AND TickSum > MA5
        Dim threshold = 5.0
        Dim c3Node = _circuit.GetNode("C3_TICK")
        If c3Node IsNot Nothing Then threshold = CDbl(If(c3Node.GetParam("Threshold")?.Value, 5.0))
        Dim tickScore = 0.0
        If Not Double.IsNaN(state.TickSum_Normalized) AndAlso threshold > 0 Then
            Dim ratio = state.TickSum_Normalized / threshold
            tickScore = Math.Min(1.5, Math.Max(0, ratio))  ' cap at 150%
            ' MA5 보너스: 만족하면 유지, 불만족하면 80%로 제한
            If Double.IsNaN(state.TickMA5_Normalized) OrElse state.TickSum_Normalized <= state.TickMA5_Normalized Then
                tickScore = Math.Min(tickScore, 0.8)
            End If
        End If
        _conditionScores("C3_TICK") = tickScore

        ' C4: OBV Direction
        _conditionScores("C4_OBV") = If(state.OBV_Direction > 0, 1.0, 0.0)

        ' C5: 동시확인 (C1~C4 모두 통과해야)
        Dim sc1, sc2, sc3, sc4 As Double
        _conditionScores.TryGetValue("C1_ST", sc1)
        _conditionScores.TryGetValue("C2_JMA", sc2)
        _conditionScores.TryGetValue("C3_TICK", sc3)
        _conditionScores.TryGetValue("C4_OBV", sc4)
        Dim c5Score = (sc1 + sc2 + sc3 + sc4) / 4.0

        _conditionScores("C5_CONFIRM") = c5Score

        ' C6: MACD Histogram > 0
        Dim macdScore = 0.0
        If Not Double.IsNaN(state.MACD_Histogram) Then
            If state.MACD_Histogram > 0 Then
                macdScore = Math.Min(1.5, 1.0 + state.MACD_Histogram / 10.0)
            Else
                macdScore = Math.Max(0, 0.5 + state.MACD_Histogram / 10.0)
            End If
        End If
        _conditionScores("C6_MACD") = macdScore

        ' C7: Volume Ratio >= 100%
        Dim volScore = 0.0
        If Not Double.IsNaN(state.Volume_Ratio) AndAlso state.Volume_Ratio > 0 Then
            volScore = Math.Min(1.5, state.Volume_Ratio / 100.0)
        End If
        _conditionScores("C7_VOL") = volScore

        ' ── 종합 점수 (0~100) ──
        ' OFF 노드는 자동 100%(바이패스)
        Dim totalScore = 0.0
        Dim count = 0
        For Each cId In {"C1_ST", "C2_JMA", "C3_TICK", "C4_OBV", "C5_CONFIRM", "C6_MACD", "C7_VOL"}
            Dim node = _circuit.GetNode(cId)
            If node IsNot Nothing AndAlso Not node.Enabled Then
                totalScore += 1.0  ' 바이패스 = 만점
            Else
                Dim cScore As Double = 0.0
                _conditionScores.TryGetValue(cId, cScore)
                totalScore += Math.Min(1.0, cScore)

            End If
            count += 1
        Next

        _overallScore = If(count > 0, (totalScore / count) * 100.0, 0.0)

        ' 필터 차단 시 점수 반감
        If _lastEvalResult IsNot Nothing AndAlso _lastEvalResult.ActiveFilterBlocks.Count > 0 Then
            _overallScore *= 0.5
        End If
    End Sub

    Private Sub ExtractIndicators(state As StockState)
        If _indicatorEngine Is Nothing Then Return
        Dim results = _indicatorEngine.Results
        If results Is Nothing OrElse results.Count = 0 Then Return
        Dim idx = If(_currentCandleIndex >= 0, _currentCandleIndex, 0)

        ' ── ST ──
        Try
            Dim stList = FindResult(results, "ST_")
            If stList IsNot Nothing AndAlso stList.Count > idx Then
                state.ST_Direction = stList(idx).Val("Direction")
            End If
        Catch : End Try

        ' ── JMA (전환 후 경과봉 카운터 방식) ──
        Try
            Dim jmaList = FindResult(results, "JMA_")
            If jmaList IsNot Nothing AndAlso jmaList.Count > idx Then
                ' 현재 봉 Direction
                Dim curUp = jmaList(idx).Val("Up")
                Dim curDown = jmaList(idx).Val("Down")
                If Not Single.IsNaN(curUp) AndAlso Single.IsNaN(curDown) Then
                    state.JMA_Direction = 1
                ElseIf Single.IsNaN(curUp) AndAlso Not Single.IsNaN(curDown) Then
                    state.JMA_Direction = -1
                ElseIf Not Single.IsNaN(curUp) AndAlso Not Single.IsNaN(curDown) Then
                    state.JMA_Direction = 1
                Else
                    state.JMA_Direction = 0
                End If

                ' 이전 봉 Direction
                If idx > 0 AndAlso jmaList.Count > idx - 1 Then
                    Dim prevUp = jmaList(idx - 1).Val("Up")
                    Dim prevDown = jmaList(idx - 1).Val("Down")
                    If Not Single.IsNaN(prevUp) AndAlso Single.IsNaN(prevDown) Then
                        state.JMA_PrevDirection = 1
                    ElseIf Single.IsNaN(prevUp) AndAlso Not Single.IsNaN(prevDown) Then
                        state.JMA_PrevDirection = -1
                    ElseIf Not Single.IsNaN(prevUp) AndAlso Not Single.IsNaN(prevDown) Then
                        state.JMA_PrevDirection = 1
                    Else
                        state.JMA_PrevDirection = 0
                    End If
                End If

                ' TurnBar: 카운터 방식 (StateManager.UpdateIndicators와 통일)
                ' 현재 방향과 이전 방향이 다르면 전환점 = 0, 같으면 누적 카운트
                state.JMA_TurnBar = -1
                Dim curDir = CInt(state.JMA_Direction)
                Dim prevDir = CInt(state.JMA_PrevDirection)
                If curDir > 0 Then
                    ' 상승 중: 전환점부터 경과봉 카운트
                    Dim turnCount = 0
                    For k = idx To 1 Step -1
                        If k >= jmaList.Count Then Continue For
                        Dim kUp = jmaList(k).Val("Up")
                        Dim kDown = jmaList(k).Val("Down")
                        Dim kDir As Integer = 0
                        If Not Single.IsNaN(kUp) AndAlso Single.IsNaN(kDown) Then
                            kDir = 1
                        ElseIf Single.IsNaN(kUp) AndAlso Not Single.IsNaN(kDown) Then
                            kDir = -1
                        ElseIf Not Single.IsNaN(kUp) AndAlso Not Single.IsNaN(kDown) Then
                            kDir = 1 ' 전환점
                        End If
                        If kDir <> 1 Then
                            state.JMA_TurnBar = turnCount
                            Exit For
                        End If
                        turnCount += 1
                        If turnCount > 100 Then Exit For
                    Next
                    If state.JMA_TurnBar = -1 AndAlso turnCount > 0 Then
                        state.JMA_TurnBar = turnCount
                    End If
                End If

            End If
        Catch : End Try

        ' ── TickIntensity (폴백: CandleItem.NormalizedTickSum) ──
        Try
            Dim tickOk = False
            Dim tiList = FindResult(results, "TICKINT_")
            If tiList IsNot Nothing AndAlso tiList.Count > idx Then
                Dim ts = tiList(idx).Val("TickSum")
                Dim m5 = tiList(idx).Val("MA5")
                If Not Single.IsNaN(ts) Then
                    state.TickSum_Normalized = ts
                    state.TickMA5_Normalized = m5
                    tickOk = True
                End If
            End If
            If Not tickOk AndAlso _candles IsNot Nothing AndAlso idx < _candles.Count Then
                Dim ci = _candles(idx)
                If ci.NormalizedTickSum <> 0 OrElse ci.TickCount > 0 Then
                    state.TickSum_Normalized = ci.NormalizedTickSum
                    Dim sum5 As Double = 0 : Dim cnt5 As Integer = 0
                    For k = Math.Max(0, idx - 4) To idx
                        Dim ck = _candles(k)
                        If ck.NormalizedTickSum <> 0 OrElse ck.TickCount > 0 Then
                            sum5 += Math.Abs(ck.NormalizedTickSum)
                            cnt5 += 1
                        End If
                    Next
                    state.TickMA5_Normalized = If(cnt5 >= 5, sum5 / cnt5, Double.NaN)
                End If
            End If
        Catch : End Try

        ' ── OBV ──
        Try
            Dim obvList = FindResult(results, "OBV_")
            If obvList IsNot Nothing AndAlso obvList.Count > idx Then
                state.OBV_Direction = obvList(idx).Val("Direction")
            End If
        Catch : End Try

        ' ── RSI ──
        Try
            Dim rsiList = FindResult(results, "RSI_")
            If rsiList IsNot Nothing AndAlso rsiList.Count > idx Then
                state.RSI_Value = rsiList(idx).Val("Value")
            End If
        Catch : End Try

        ' ── MACD ──
        Try
            Dim macdList = FindResult(results, "MACD_")
            If macdList IsNot Nothing AndAlso macdList.Count > idx Then
                state.MACD_Histogram = macdList(idx).Val("Histogram")
            End If
        Catch : End Try

        ' ── Volume ──
        Try
            Dim volList = FindResult(results, "VOL_")
            If volList IsNot Nothing AndAlso volList.Count > idx Then
                state.Volume_Ratio = volList(idx).Val("Ratio")
            End If
        Catch : End Try
    End Sub



    Private Shared Function FindResult(results As Dictionary(Of String, List(Of IndicatorResult)),
                                       prefix As String) As List(Of IndicatorResult)
        For Each kv In results
            If kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then Return kv.Value
        Next
        Return Nothing
    End Function

#End Region

#Region "미니 캔들 차트 렌더링"

    Private Sub OnChartPaint(sender As Object, e As PaintEventArgs) Handles _pnlChart.Paint
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(COLOR_BG_DARK)

        If _candles Is Nothing OrElse _candles.Count = 0 Then
            Using br As New SolidBrush(Color.Gray)
                g.DrawString("종목을 로드하면 캔들 차트가 여기에 표시됩니다.", New Font("맑은 고딕", 10), br, 20, 100)
            End Using
            Return
        End If

        Dim chartRect As New Rectangle(50, 10, _pnlChart.Width - 70, _pnlChart.Height - 30)
        Dim startIdx = Math.Max(0, _chartScrollOffset)
        Dim endIdx = Math.Min(_candles.Count - 1, startIdx + _chartVisibleBars - 1)
        If endIdx < startIdx Then Return

        Dim minPrice As Single = Single.MaxValue
        Dim maxPrice As Single = Single.MinValue
        For i = startIdx To endIdx
            If _candles(i).Low > 0 AndAlso _candles(i).Low < minPrice Then minPrice = _candles(i).Low
            If _candles(i).High > maxPrice Then maxPrice = _candles(i).High
        Next

        ' 지표값도 가격 범위에 포함 (ST, JMA)
        If _indicatorEngine IsNot Nothing AndAlso _indicatorEngine.Results IsNot Nothing Then
            Dim stRes = FindResult(_indicatorEngine.Results, "ST_")
            Dim jmaRes = FindResult(_indicatorEngine.Results, "JMA_")
            For i = startIdx To endIdx
                If stRes IsNot Nothing AndAlso i < stRes.Count Then
                    Dim vu = stRes(i).Val("Up") : Dim vd = stRes(i).Val("Down")
                    If Not Single.IsNaN(vu) AndAlso vu > 0 Then
                        If vu < minPrice Then minPrice = vu
                        If vu > maxPrice Then maxPrice = vu
                    End If
                    If Not Single.IsNaN(vd) AndAlso vd > 0 Then
                        If vd < minPrice Then minPrice = vd
                        If vd > maxPrice Then maxPrice = vd
                    End If
                End If
                If jmaRes IsNot Nothing AndAlso i < jmaRes.Count Then
                    Dim ju = jmaRes(i).Val("Up")
                    Dim jd = jmaRes(i).Val("Down")
                    If Not Single.IsNaN(ju) AndAlso ju > 0 Then
                        If ju < minPrice Then minPrice = ju
                        If ju > maxPrice Then maxPrice = ju
                    End If
                    If Not Single.IsNaN(jd) AndAlso jd > 0 Then
                        If jd < minPrice Then minPrice = jd
                        If jd > maxPrice Then maxPrice = jd
                    End If
                End If
            Next
        End If

        If minPrice >= maxPrice Then Return
        Dim pricePadding = (maxPrice - minPrice) * 0.05F
        minPrice -= pricePadding
        maxPrice += pricePadding
        Dim priceRange = maxPrice - minPrice
        If priceRange <= 0 Then Return

        Dim barCount = endIdx - startIdx + 1
        Dim barWidth = CSng(chartRect.Width) / barCount
        Dim bodyWidth = Math.Max(1.0F, barWidth * 0.6F)

        ' 그리드
        Using gridPen As New Pen(Color.FromArgb(35, 40, 55), 0.5F)
            For i = 0 To 4
                Dim yy = chartRect.Top + CInt(chartRect.Height * i / 4.0)
                g.DrawLine(gridPen, chartRect.Left, yy, chartRect.Right, yy)
                Dim price = maxPrice - priceRange * i / 4.0F
                Using br As New SolidBrush(Color.FromArgb(100, 110, 130))
                    g.DrawString(price.ToString("N0"), New Font("Consolas", 7), br, 2, yy - 6)
                End Using
            Next
        End Using

        ' 캔들 그리기
        For i = startIdx To endIdx
            Dim c = _candles(i)
            Dim barX = chartRect.Left + (i - startIdx) * barWidth + barWidth / 2.0F
            Dim isBull = c.Close >= c.Open

            Dim highY = chartRect.Top + CInt((maxPrice - c.High) / priceRange * chartRect.Height)
            Dim lowY = chartRect.Top + CInt((maxPrice - c.Low) / priceRange * chartRect.Height)
            Dim openY = chartRect.Top + CInt((maxPrice - c.Open) / priceRange * chartRect.Height)
            Dim closeY = chartRect.Top + CInt((maxPrice - c.Close) / priceRange * chartRect.Height)

            Dim bodyTop = Math.Min(openY, closeY)
            Dim bodyBot = Math.Max(openY, closeY)
            If bodyBot - bodyTop < 1 Then bodyBot = bodyTop + 1

            Dim candleColor = If(isBull, Color.FromArgb(220, 60, 60), Color.FromArgb(50, 120, 220))

            Using wickPen As New Pen(candleColor, 1.0F)
                g.DrawLine(wickPen, barX, highY, barX, lowY)
            End Using

            Dim bodyRect As New RectangleF(barX - bodyWidth / 2.0F, bodyTop, bodyWidth, bodyBot - bodyTop)
            Using br As New SolidBrush(candleColor)
                g.FillRectangle(br, bodyRect)
            End Using

            If i = _currentCandleIndex Then
                Using hlPen As New Pen(Color.FromArgb(180, 255, 255, 100), 1.5F)
                    hlPen.DashStyle = DashStyle.Dash
                    g.DrawLine(hlPen, barX, chartRect.Top, barX, chartRect.Bottom)
                End Using
                Dim markerPts = {
                    New PointF(barX - 5, chartRect.Bottom + 2),
                    New PointF(barX + 5, chartRect.Bottom + 2),
                    New PointF(barX, chartRect.Bottom - 6)}
                Using br As New SolidBrush(Color.Yellow)
                    g.FillPolygon(br, markerPts)
                End Using
            End If
        Next

        ' ═══ 지표 오버레이 ═══
        If _indicatorEngine IsNot Nothing AndAlso _indicatorEngine.Results IsNot Nothing Then
            ' SuperTrend
            Dim stResults = FindResult(_indicatorEngine.Results, "ST_")
            If stResults IsNot Nothing Then
                DrawIndicatorLine(g, stResults, "Up", startIdx, endIdx, barWidth, chartRect,
                                  minPrice, maxPrice, priceRange, Color.FromArgb(180, 0, 200, 100), 1.5F)
                DrawIndicatorLine(g, stResults, "Down", startIdx, endIdx, barWidth, chartRect,
                                  minPrice, maxPrice, priceRange, Color.FromArgb(180, 220, 60, 60), 1.5F)
            End If

            ' JMA 상승=초록, 하락=빨강 (SuperTrend 스타일 두 색)
            Dim jmaResults = FindResult(_indicatorEngine.Results, "JMA_")
            If jmaResults IsNot Nothing Then
                DrawIndicatorLine(g, jmaResults, "Up", startIdx, endIdx, barWidth, chartRect,
                                  minPrice, maxPrice, priceRange, Color.FromArgb(220, 0, 220, 100), 2.5F)
                DrawIndicatorLine(g, jmaResults, "Down", startIdx, endIdx, barWidth, chartRect,
                                  minPrice, maxPrice, priceRange, Color.FromArgb(220, 255, 60, 60), 2.5F)
            End If
        End If

        ' ═══ 크로스헤어 ═══
        If _chartMousePos.X >= chartRect.Left AndAlso _chartMousePos.X <= chartRect.Right AndAlso
           _chartMousePos.Y >= chartRect.Top AndAlso _chartMousePos.Y <= chartRect.Bottom Then

            Using crossPen As New Pen(Color.FromArgb(120, 200, 200, 200), 0.8F)
                crossPen.DashStyle = DashStyle.Dot
                g.DrawLine(crossPen, _chartMousePos.X, chartRect.Top, _chartMousePos.X, chartRect.Bottom)
            End Using

            Using crossPen As New Pen(Color.FromArgb(120, 200, 200, 200), 0.8F)
                crossPen.DashStyle = DashStyle.Dot
                g.DrawLine(crossPen, chartRect.Left, _chartMousePos.Y, chartRect.Right, _chartMousePos.Y)
            End Using

            Dim hoverPrice = maxPrice - (CSng(_chartMousePos.Y - chartRect.Top) / chartRect.Height) * priceRange
            Using priceBg As New SolidBrush(Color.FromArgb(200, 40, 45, 60))
                Dim priceText = hoverPrice.ToString("N0")
                Dim pFont As New Font("Consolas", 7.5F)
                Dim priceSize = g.MeasureString(priceText, pFont)
                g.FillRectangle(priceBg, 0, _chartMousePos.Y - priceSize.Height / 2,
                                chartRect.Left - 2, priceSize.Height + 2)
                Using br As New SolidBrush(Color.FromArgb(255, 220, 100))
                    g.DrawString(priceText, pFont, br, 2, _chartMousePos.Y - priceSize.Height / 2)
                End Using
                pFont.Dispose()
            End Using

            If _chartHoverIndex >= 0 AndAlso _chartHoverIndex < _candles.Count Then
                Dim hc = _candles(_chartHoverIndex)
                Dim dtText As String = If(hc.Dt.Year > 2000, hc.Dt.ToString("yyyy-MM-dd HH:mm"), $"#{_chartHoverIndex + 1}")
                Dim infoText = $"{dtText}  O={hc.Open:N0}  H={hc.High:N0}  L={hc.Low:N0}  C={hc.Close:N0}  V={hc.Volume:N0}"
                Dim infoFont As New Font("Consolas", 8)
                Dim infoSize = g.MeasureString(infoText, infoFont)

                Dim timeText As String = If(hc.Dt.Year > 2000, hc.Dt.ToString("HH:mm"), $"#{_chartHoverIndex + 1}")
                Dim timeFont As New Font("Consolas", 7.5F)
                Dim timeSize = g.MeasureString(timeText, timeFont)
                Dim timeX = _chartMousePos.X - timeSize.Width / 2
                Dim timeY = chartRect.Bottom + 1
                Using tbg As New SolidBrush(Color.FromArgb(200, 40, 45, 60))
                    g.FillRectangle(tbg, timeX - 2, timeY, timeSize.Width + 4, timeSize.Height)
                End Using
                Using br As New SolidBrush(Color.FromArgb(255, 220, 100))
                    g.DrawString(timeText, timeFont, br, timeX, timeY)
                End Using

                Dim boxX = chartRect.Left + 5
                Dim boxY = chartRect.Top + 2
                Using bgBr As New SolidBrush(Color.FromArgb(210, 25, 28, 38))
                    g.FillRectangle(bgBr, boxX - 2, boxY - 1, infoSize.Width + 4, infoSize.Height + 2)
                End Using
                Dim isBull2 = hc.Close >= hc.Open
                Using br As New SolidBrush(If(isBull2, Color.FromArgb(255, 100, 100), Color.FromArgb(100, 160, 255)))
                    g.DrawString(infoText, infoFont, br, boxX, boxY)
                End Using
                infoFont.Dispose()
                timeFont.Dispose()
            End If
        End If
    End Sub


    Private Sub DrawIndicatorLine(g As Graphics,
                                   results As List(Of IndicatorResult),
                                   valueKey As String,
                                   startIdx As Integer, endIdx As Integer,
                                   barWidth As Single,
                                   chartRect As Rectangle,
                                   minPrice As Single, maxPrice As Single, priceRange As Single,
                                   lineColor As Color, lineWidth As Single)
        If results Is Nothing OrElse priceRange <= 0 Then Return

        Dim pts As New List(Of PointF)

        For i = startIdx To endIdx
            If i >= results.Count Then Exit For
            Dim v = results(i).Val(valueKey)
            If Single.IsNaN(v) OrElse v <= 0 Then
                If pts.Count >= 2 Then
                    Using pen As New Pen(lineColor, lineWidth)
                        pen.LineJoin = Drawing2D.LineJoin.Round
                        g.DrawLines(pen, pts.ToArray())
                    End Using
                End If
                pts.Clear()
                Continue For
            End If

            Dim barX = chartRect.Left + (i - startIdx) * barWidth + barWidth / 2.0F
            Dim yPos = CSng(chartRect.Top + (maxPrice - v) / priceRange * chartRect.Height)
            pts.Add(New PointF(barX, yPos))
        Next

        If pts.Count >= 2 Then
            Using pen As New Pen(lineColor, lineWidth)
                pen.LineJoin = Drawing2D.LineJoin.Round
                g.DrawLines(pen, pts.ToArray())
            End Using
        End If
    End Sub

    Private Sub OnChartMouseClick(sender As Object, e As MouseEventArgs) Handles _pnlChart.MouseClick
        If _candles Is Nothing OrElse _candles.Count = 0 Then Return

        Dim chartRect As New Rectangle(50, 10, _pnlChart.Width - 70, _pnlChart.Height - 30)
        If Not chartRect.Contains(e.Location) Then Return

        Dim startIdx = Math.Max(0, _chartScrollOffset)
        Dim endIdx = Math.Min(_candles.Count - 1, startIdx + _chartVisibleBars - 1)
        Dim barCount = endIdx - startIdx + 1
        If barCount <= 0 Then Return

        Dim barWidth = CSng(chartRect.Width) / barCount
        Dim clickedBar = CInt((e.X - chartRect.Left) / barWidth)
        Dim newIdx = startIdx + clickedBar
        newIdx = Math.Max(0, Math.Min(_candles.Count - 1, newIdx))

        _currentCandleIndex = newIdx
        _trkCandle.Value = newIdx
        EvaluateAtCandle(newIdx)
        _pnlChart.Invalidate()
        _canvas.Invalidate()
        _pnlScore.Invalidate()
    End Sub

    Private _lastChartInvalidate As DateTime = DateTime.MinValue

    Private Sub OnChartMouseMove(sender As Object, e As MouseEventArgs) Handles _pnlChart.MouseMove
        _chartMousePos = e.Location

        Dim chartRect As New Rectangle(50, 10, _pnlChart.Width - 70, _pnlChart.Height - 30)
        If _candles IsNot Nothing AndAlso _candles.Count > 0 AndAlso chartRect.Contains(e.Location) Then
            Dim startIdx = Math.Max(0, _chartScrollOffset)
            Dim endIdx = Math.Min(_candles.Count - 1, startIdx + _chartVisibleBars - 1)
            Dim barCount = endIdx - startIdx + 1
            If barCount > 0 Then
                Dim barWidth = CSng(chartRect.Width) / barCount
                Dim hoverBar = CInt((e.X - chartRect.Left) / barWidth)
                _chartHoverIndex = Math.Max(0, Math.Min(_candles.Count - 1, startIdx + hoverBar))
            Else
                _chartHoverIndex = -1
            End If
        Else
            _chartHoverIndex = -1
        End If

        ' 30ms 이상 간격일 때만 다시 그리기 (깜박임 방지)
        If (DateTime.Now - _lastChartInvalidate).TotalMilliseconds > 30 Then
            _lastChartInvalidate = DateTime.Now
            _pnlChart.Invalidate()
        End If
    End Sub


    Private Sub OnChartMouseLeave(sender As Object, e As EventArgs) Handles _pnlChart.MouseLeave
        _chartMousePos = New Point(-1, -1)
        _chartHoverIndex = -1
        _pnlChart.Invalidate()
    End Sub

    Private Sub OnChartMouseWheel(sender As Object, e As MouseEventArgs) Handles _pnlChart.MouseWheel
        If _candles Is Nothing Then Return
        If e.Delta > 0 Then
            _chartScrollOffset = Math.Max(0, _chartScrollOffset - 5)
        Else
            _chartScrollOffset = Math.Min(Math.Max(0, _candles.Count - _chartVisibleBars), _chartScrollOffset + 5)
        End If
        _pnlChart.Invalidate()
    End Sub

    Private Sub EnsureCandleVisible(idx As Integer)
        If idx < _chartScrollOffset Then
            _chartScrollOffset = Math.Max(0, idx - 5)
        ElseIf idx >= _chartScrollOffset + _chartVisibleBars Then
            _chartScrollOffset = Math.Min(Math.Max(0, _candles.Count - _chartVisibleBars), idx - _chartVisibleBars + 10)
        End If
    End Sub

#End Region

#Region "회로도 렌더링 (3색 LED + 게이지)"

    Private Sub OnCanvasPaint(sender As Object, e As PaintEventArgs) Handles _canvas.Paint
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(COLOR_BG_DARK)
        If _circuit Is Nothing Then Return

        ' ── 와이어 ──
        For Each wire In _circuit.Wires
            Dim fromNode = _circuit.GetNode(wire.FromNodeId)
            Dim toNode = _circuit.GetNode(wire.ToNodeId)
            If fromNode Is Nothing OrElse toNode Is Nothing Then Continue For

            Dim wireColor As Color
            Select Case wire.State
                Case WireState.Active : wireColor = COLOR_PASS
                Case WireState.Blocked : wireColor = COLOR_FAIL
                Case WireState.Warning : wireColor = COLOR_NEAR
                Case Else : wireColor = Color.FromArgb(55, 60, 75)
            End Select

            Using pen As New Pen(wireColor, If(wire.State = WireState.Active, 2.5F, 1.5F))
                Dim p1 = New Point(fromNode.X + fromNode.Width, fromNode.CenterPoint.Y)
                Dim p2 = New Point(toNode.X, toNode.CenterPoint.Y)
                Dim midX = (p1.X + p2.X) \ 2
                g.DrawBezier(pen, p1, New Point(midX, p1.Y), New Point(midX, p2.Y), p2)
            End Using
        Next

        ' ── 노드 ──
        For Each node In _circuit.Nodes
            DrawCircuitNode(g, node)
        Next
    End Sub

    Private Sub DrawCircuitNode(g As Graphics, node As CircuitNode)
        Dim rect As New Rectangle(node.X, node.Y, node.Width, node.Height)

        ' 배경색
        Dim bgColor = If(Not node.Enabled, Color.FromArgb(40, 40, 48),
                      If(node.IsTriggered, Color.FromArgb(20, 55, 45),
                         Color.FromArgb(40, 45, 60)))
        If _selectedNode IsNot Nothing AndAlso _selectedNode.Id = node.Id Then
            bgColor = Color.FromArgb(55, 70, 100)
        End If

        Using brush As New SolidBrush(bgColor)
            g.FillRoundedRectangle(brush, rect, 8)
        End Using

        ' 테두리 (3색)
        Dim borderColor = GetNodeSignalColor(node)
        If Not node.Enabled Then borderColor = COLOR_OFF
        If _selectedNode IsNot Nothing AndAlso _selectedNode.Id = node.Id Then
            borderColor = Color.FromArgb(255, 200, 50)
        End If
        Using pen As New Pen(borderColor, If(_selectedNode IsNot Nothing AndAlso _selectedNode.Id = node.Id, 2.5F, 1.5F))
            g.DrawRoundedRectangle(pen, rect, 8)
        End Using

        ' ── 3색 LED ──
        Dim ledColor = GetNodeSignalColor(node)
        If Not node.Enabled Then ledColor = COLOR_OFF
        DrawLED3Color(g, rect.Right - 16, rect.Top + 5, 10, ledColor)

        ' ── ON/OFF 스위치 ──
        DrawMiniSwitch(g, rect.X + 4, rect.Y + 5, 14, 8, node.Enabled)

        ' ── 노드 이름 ──
        Using font As New Font("맑은 고딕", 8.5F, FontStyle.Bold),
              br As New SolidBrush(If(node.Enabled, Color.White, Color.FromArgb(100, 100, 110)))
            g.DrawString(node.Name, font, br, rect.X + 22, rect.Y + 3)
        End Using

        ' ── 프로브 텍스트 ──
        If Not String.IsNullOrEmpty(node.ProbeText) Then
            Using font As New Font("Consolas", 7.5F),
                  br As New SolidBrush(If(node.IsTriggered, Color.FromArgb(150, 255, 200), Color.FromArgb(160, 165, 180)))
                g.DrawString(node.ProbeText, font, br, rect.X + 5, rect.Y + 20)
            End Using
        End If

        ' ── 게이지 바 (조건 노드만) ──
        If node.NodeType = NodeType.Condition AndAlso node.Enabled Then
            DrawGaugeBar(g, node, rect)
        End If

        ' ── 비활성 X 오버레이 ──
        If Not node.Enabled Then
            Using overlay As New SolidBrush(Color.FromArgb(100, 0, 0, 0))
                g.FillRoundedRectangle(overlay, rect, 8)
            End Using
            Using xPen As New Pen(Color.FromArgb(120, 255, 80, 80), 1.5F)
                g.DrawLine(xPen, rect.X + 4, rect.Y + 4, rect.Right - 4, rect.Bottom - 4)
                g.DrawLine(xPen, rect.Right - 4, rect.Y + 4, rect.X + 4, rect.Bottom - 4)
            End Using
        End If
    End Sub

    ''' <summary>노드별 3색 결정: PASS=파란, NEAR=노란, FAIL=빨간</summary>
    Private Function GetNodeSignalColor(node As CircuitNode) As Color
        If Not node.Enabled Then Return COLOR_OFF

        ' 조건 노드 → 충족률 기반 3색
        If node.NodeType = NodeType.Condition Then
            Dim score As Double = 0
            If _conditionScores.TryGetValue(node.Id, score) Then
                If score >= 1.0 Then Return COLOR_PASS
                If score >= 0.7 Then Return COLOR_NEAR
                Return COLOR_FAIL
            End If
            Return COLOR_OFF
        End If

        ' 기타 노드 → 트리거 기반
        If node.IsTriggered Then Return COLOR_PASS
        Return COLOR_FAIL
    End Function

    Private Sub DrawLED3Color(g As Graphics, x As Integer, y As Integer, size As Integer, color As Color)
        Dim ledRect As New Rectangle(x, y, size, size)

        ' 글로우
        If color <> COLOR_OFF Then
            Using glowBrush As New SolidBrush(Color.FromArgb(50, color))
                g.FillEllipse(glowBrush, x - 3, y - 3, size + 6, size + 6)
            End Using
        End If

        Using br As New SolidBrush(color)
            g.FillEllipse(br, ledRect)
        End Using

        ' 하이라이트
        Using hl As New SolidBrush(Color.FromArgb(70, 255, 255, 255))
            g.FillEllipse(hl, x + 2, y + 1, Math.Max(1, size \ 2), Math.Max(1, size \ 2 - 1))
        End Using
    End Sub

    Private Sub DrawMiniSwitch(g As Graphics, x As Integer, y As Integer, w As Integer, h As Integer, isOn As Boolean)
        Dim swColor = If(isOn, Color.FromArgb(0, 160, 80), Color.FromArgb(120, 50, 50))
        Using br As New SolidBrush(swColor)
            g.FillRectangle(br, x, y, w, h)
        End Using
        Dim knobX = If(isOn, x + w - 6, x + 1)
        Using kb As New SolidBrush(Color.White)
            g.FillRectangle(kb, knobX, y + 1, 5, h - 2)
        End Using
    End Sub

    ''' <summary>조건 노드 하단에 충족률 게이지 바 표시</summary>
    Private Sub DrawGaugeBar(g As Graphics, node As CircuitNode, rect As Rectangle)
        Dim score As Double = 0.0
        _conditionScores.TryGetValue(node.Id, score)

        Dim barY = rect.Bottom - 12
        Dim barX = rect.X + 5
        Dim barW = rect.Width - 10
        Dim barH = 6

        ' 배경
        Using bg As New SolidBrush(Color.FromArgb(25, 30, 40))
            g.FillRectangle(bg, barX, barY, barW, barH)
        End Using

        ' 충전
        Dim fillW = CInt(Math.Min(1.0, score) * barW)
        If fillW > 0 Then
            Dim fillColor = If(score >= 1.0, COLOR_PASS, If(score >= 0.7, COLOR_NEAR, COLOR_FAIL))
            Using br As New SolidBrush(fillColor)
                g.FillRectangle(br, barX, barY, fillW, barH)
            End Using
        End If

        ' 퍼센트 텍스트
        Dim pct = CInt(Math.Min(150, score * 100))
        Using font As New Font("Consolas", 6.5F),
              br As New SolidBrush(Color.FromArgb(180, 190, 210))
            g.DrawString($"{pct}%", font, br, barX + barW + 2, barY - 2)
        End Using
    End Sub

#End Region

#Region "스코어 바 렌더링"

    Private Sub OnScorePaint(sender As Object, e As PaintEventArgs) Handles _pnlScore.Paint
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(Color.FromArgb(28, 30, 38))

        Dim x = 15
        Dim y = 8
        Dim barHeight = 30

        ' ── 종합 점수 바 ──
        Dim scoreBarWidth = 200
        Using bg As New SolidBrush(Color.FromArgb(35, 40, 55))
            g.FillRectangle(bg, x, y, scoreBarWidth, barHeight)
        End Using

        Dim fillW = CInt(Math.Min(100, _overallScore) / 100.0 * scoreBarWidth)
        If fillW > 0 Then
            Dim fillColor = If(_overallScore >= 85, COLOR_PASS, If(_overallScore >= 60, COLOR_NEAR, COLOR_FAIL))
            Using br As New SolidBrush(fillColor)
                g.FillRectangle(br, x, y, fillW, barHeight)
            End Using
        End If

        Using font As New Font("Consolas", 14, FontStyle.Bold),
              br As New SolidBrush(Color.White)
            g.DrawString($"전략 점수: {_overallScore:F0}/100", font, br, x + 5, y + 4)
        End Using

        ' ── C1~C7 미니 인디케이터 ──
        x = scoreBarWidth + 30
        Dim conditions = {"C1_ST", "C2_JMA", "C3_TICK", "C4_OBV", "C5_CONFIRM", "C6_MACD", "C7_VOL"}
        Dim condLabels = {"ST", "JMA", "TICK", "OBV", "확인", "MACD", "VOL"}

        For i = 0 To conditions.Length - 1
            Dim cId = conditions(i)
            Dim node = _circuit.GetNode(cId)
            Dim isOff = (node IsNot Nothing AndAlso Not node.Enabled)
            Dim score As Double = 0
            _conditionScores.TryGetValue(cId, score)

            ' 미니 LED
            Dim ledColor As Color
            If isOff Then
                ledColor = COLOR_OFF
            ElseIf score >= 1.0 Then
                ledColor = COLOR_PASS
            ElseIf score >= 0.7 Then
                ledColor = COLOR_NEAR
            Else
                ledColor = COLOR_FAIL
            End If

            DrawLED3Color(g, x, y + 2, 12, ledColor)

            Using font As New Font("Consolas", 8, FontStyle.Bold),
                  br As New SolidBrush(If(isOff, Color.Gray, Color.White))
                g.DrawString(condLabels(i), font, br, x + 16, y + 1)
            End Using

            ' 미니 게이지
            Dim miniBarX = x + 16
            Dim miniBarY = y + 18
            Dim miniBarW = 45
            Dim miniBarH = 4
            Using bg As New SolidBrush(Color.FromArgb(35, 40, 55))
                g.FillRectangle(bg, miniBarX, miniBarY, miniBarW, miniBarH)
            End Using
            If Not isOff Then
                Dim mfW = CInt(Math.Min(1.0, score) * miniBarW)
                If mfW > 0 Then
                    Using br As New SolidBrush(ledColor)
                        g.FillRectangle(br, miniBarX, miniBarY, mfW, miniBarH)
                    End Using
                End If
            End If

            x += 80
        Next

        ' 필터 상태
        If _lastEvalResult IsNot Nothing AndAlso _lastEvalResult.ActiveFilterBlocks.Count > 0 Then
            Using font As New Font("Consolas", 9, FontStyle.Bold),
                  br As New SolidBrush(COLOR_FAIL)
                g.DrawString($"[필터 차단: {String.Join(", ", _lastEvalResult.ActiveFilterBlocks)}]",
                             font, br, x + 10, y + 5)
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
            If _currentCandleIndex >= 0 Then EvaluateAtCandle(_currentCandleIndex)
            ShowNodeParams(_selectedNode)
            _canvas.Invalidate()
            _pnlScore.Invalidate()
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

        ' ── 3색 상태 표시 ──
        Dim statusPanel As New Panel()
        statusPanel.Location = New Point(10, y)
        statusPanel.Size = New Size(260, 30)
        statusPanel.BackColor = Color.FromArgb(30, 35, 45)
        _pnlParams.Controls.Add(statusPanel)

        Dim signalColor = GetNodeSignalColor(node)
        Dim signalText As String
        If Not node.Enabled Then
            signalText = "● OFF (바이패스)"
        ElseIf signalColor = COLOR_PASS Then
            signalText = "● PASS — 조건 충족"
        ElseIf signalColor = COLOR_NEAR Then
            signalText = "◐ NEAR — 근접 (70~99%)"
        Else
            signalText = "○ FAIL — 미충족"
        End If

        Dim lblStatus As New Label()
        lblStatus.Text = signalText
        lblStatus.Location = New Point(5, 5)
        lblStatus.Size = New Size(250, 20)
        lblStatus.ForeColor = signalColor
        lblStatus.Font = New Font("Consolas", 10, FontStyle.Bold)
        statusPanel.Controls.Add(lblStatus)
        y += 40

        ' ── 충족률 (조건 노드) ──
        If node.NodeType = NodeType.Condition AndAlso node.Enabled Then
            Dim score As Double = 0
            _conditionScores.TryGetValue(node.Id, score)
            Dim lblScore As New Label()
            lblScore.Text = $"충족률: {score * 100:F1}%"
            lblScore.Location = New Point(10, y)
            lblScore.Size = New Size(260, 20)
            lblScore.ForeColor = Color.FromArgb(180, 220, 255)
            lblScore.Font = New Font("Consolas", 9)
            _pnlParams.Controls.Add(lblScore)
            y += 25
        End If

        ' ── ON/OFF ──
        If Not node.Locked Then
            Dim chk As New CheckBox()
            chk.Text = "활성화"
            chk.Checked = node.Enabled
            chk.Location = New Point(10, y)
            chk.ForeColor = Color.White
            chk.AutoSize = True
            AddHandler chk.CheckedChanged, Sub(s, ev)
                                               node.Enabled = chk.Checked
                                               _lblInfo.Text = $"{node.Name} ({If(node.Enabled, "ON", "OFF")})"
                                               If _currentCandleIndex >= 0 Then EvaluateAtCandle(_currentCandleIndex)
                                               _canvas.Invalidate()
                                               _pnlScore.Invalidate()
                                               ShowNodeParams(node)
                                           End Sub
            _pnlParams.Controls.Add(chk)
            y += 30
        End If

        ' ── 파라미터 ──
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
                    nud.BackColor = Color.FromArgb(50, 52, 60)
                    nud.ForeColor = Color.White
                    Dim capturedParam = param
                    AddHandler nud.ValueChanged, Sub(s, ev)
                                                     capturedParam.Value = nud.Value
                                                     If _currentCandleIndex >= 0 Then EvaluateAtCandle(_currentCandleIndex)
                                                     _canvas.Invalidate()
                                                     _pnlScore.Invalidate()
                                                 End Sub
                    _pnlParams.Controls.Add(nud)

                Case ParamDataType.Bool
                    Dim chk As New CheckBox()
                    chk.Checked = CBool(If(param.Value, param.DefaultValue))
                    chk.Location = New Point(120, y)
                    chk.ForeColor = Color.White
                    Dim capturedParam = param
                    AddHandler chk.CheckedChanged, Sub(s, ev)
                                                       capturedParam.Value = chk.Checked
                                                       If _currentCandleIndex >= 0 Then EvaluateAtCandle(_currentCandleIndex)
                                                       _canvas.Invalidate()
                                                       _pnlScore.Invalidate()
                                                   End Sub
                    _pnlParams.Controls.Add(chk)
            End Select
            y += 35
        Next

        ' ── 프로브 표시 ──
        If Not String.IsNullOrEmpty(node.ProbeText) Then
            Dim lblProbe As New Label()
            lblProbe.Text = $"[프로브] {node.ProbeText}"
            lblProbe.Location = New Point(10, y + 10)
            lblProbe.Size = New Size(260, 20)
            lblProbe.ForeColor = Color.LightGreen
            lblProbe.Font = New Font("Consolas", 9)
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
        _pnlScore.Invalidate()
        If _selectedNode IsNot Nothing Then ShowNodeParams(_selectedNode)
    End Sub

#End Region

#Region "타이머"

    Private Sub OnRefresh(sender As Object, e As EventArgs) Handles _tmrRefresh.Tick
        If _chkLive IsNot Nothing AndAlso _chkLive.Checked Then
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
            _pnlChart.Invalidate()
            _canvas.Invalidate()
            _pnlScore.Invalidate()
        End If
    End Sub

#End Region

End Class

''' <summary>더블 버퍼링 지원 Panel (차트 깜박임 방지)</summary>
Public Class DoubleBufferedPanel
    Inherits Panel
    Public Sub New()
        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.UserPaint, True)
        Me.UpdateStyles()
    End Sub
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
