' ═══════════════════════════════════════════════════════════════
' SimTradeUI.vb — UI 레이아웃 생성 · 그리드 갱신 · 로그 관리
' ═══════════════════════════════════════════════════════════════
' [v4.2] SimTradeForm.vb에서 분리.
'   UI 컨트롤 생성/배치는 불변 (레이아웃 변경 시만 수정).
'   그리드 데이터 갱신과 로그 출력은 가변.
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports MainApp.SimTrade

Namespace SimTrade

    Public Class SimTradeUI

#Region "컨트롤 참조 (읽기 전용 — 외부에서 이벤트 연결용)"

        ' ── 메인 UI ──
        Public ReadOnly Property DgvWatch As DataGridView
        Public ReadOnly Property DgvPositions As DataGridView
        Public ReadOnly Property DgvHistory As DataGridView
        Public ReadOnly Property RtbLog As RichTextBox
        Public ReadOnly Property LblStatus As Label
        Public ReadOnly Property LblSummary As Label
        Public ReadOnly Property BtnCondition As Button
        Public ReadOnly Property BtnStart As Button
        Public ReadOnly Property BtnStop As Button
        Public ReadOnly Property BtnCircuit As Button          ' ★ 추가: 회로설계 버튼
        Public ReadOnly Property TabControl As TabControl
        Public ReadOnly Property PnlSettings As Panel

        ' ── 설정 컨트롤 ──
        Public ReadOnly Property NudST_Period As NumericUpDown
        Public ReadOnly Property NudST_Multiplier As NumericUpDown
        Public ReadOnly Property NudRSI_Period As NumericUpDown
        Public ReadOnly Property NudRSI_Overbought As NumericUpDown
        Public ReadOnly Property ChkVolumeConfirm As CheckBox
        Public ReadOnly Property NudMaxPosition As NumericUpDown
        Public ReadOnly Property NudPositionSize As NumericUpDown
        Public ReadOnly Property NudStopLoss As NumericUpDown
        Public ReadOnly Property NudTakeProfit As NumericUpDown
        Public ReadOnly Property NudTrailingStop As NumericUpDown
        Public ReadOnly Property ChkTrailingStop As CheckBox
        Public ReadOnly Property NudCandleInterval As NumericUpDown
        Public ReadOnly Property NudMinCandles As NumericUpDown
        Public ReadOnly Property TxtStartTime As TextBox
        Public ReadOnly Property TxtNoNewBuy As TextBox
        Public ReadOnly Property TxtForceClose As TextBox
        Public ReadOnly Property CboBuyOrder As ComboBox
        Public ReadOnly Property CboSellOrder As ComboBox
        Public ReadOnly Property ChkEnableTopNFilter As CheckBox
        Public ReadOnly Property NudTopNCount As NumericUpDown
        Public ReadOnly Property CboTopNPreset As ComboBox
        Public ReadOnly Property NudTopTickWeight As NumericUpDown
        Public ReadOnly Property NudTopAmountWeight As NumericUpDown
        Public ReadOnly Property NudTopTrendWeight As NumericUpDown
        Public ReadOnly Property NudTopMomentumWeight As NumericUpDown

        ' ── 로그 큐 ──
        Private ReadOnly _logQueue As New System.Collections.Concurrent.ConcurrentQueue(Of String)

#End Region

#Region "빌드 (불변 레이아웃)"

        Public Sub Build(form As Form)
            form.Text = "모의매매 v4.2"
            form.Size = New Size(1400, 900)
            form.StartPosition = FormStartPosition.CenterScreen
            form.BackColor = Color.FromArgb(30, 30, 30)
            form.ForeColor = Color.White

            ' ── 상단 패널 (버튼 + 상태) ──
            Dim pnlTop As New Panel()
            pnlTop.Dock = DockStyle.Top
            pnlTop.Height = 50
            pnlTop.BackColor = Color.FromArgb(45, 45, 48)

            _BtnCondition = CreateButton("조건식", 10, 10, 80, 30)
            _BtnStart = CreateButton("▶ 시작", 100, 10, 80, 30)
            _BtnStop = CreateButton("■ 중지", 190, 10, 80, 30)
            _BtnStop.Enabled = False
            _BtnCircuit = CreateButton("회로설계", 280, 10, 80, 30)   ' ★ 추가
            _LblStatus = CreateLabel("대기 중", 380, 15, 400, 20)     ' ★ X좌표 290→380
            _LblStatus.ForeColor = Color.Gray
            _LblSummary = CreateLabel("", 790, 15, 600, 20)           ' ★ X좌표 700→790
            _LblSummary.ForeColor = Color.Cyan

            pnlTop.Controls.AddRange({_BtnCondition, _BtnStart, _BtnStop, _BtnCircuit, _LblStatus, _LblSummary})

            ' ── 탭 컨트롤 ──
            _TabControl = New TabControl()
            _TabControl.Dock = DockStyle.Fill
            _TabControl.BackColor = Color.FromArgb(30, 30, 30)

            Dim tabWatch As New TabPage("감시")
            _DgvWatch = CreateGrid()
            For Each colName In SimTradeConst.WATCH_COLUMNS
                _DgvWatch.Columns.Add(colName, colName)
            Next
            ConfigureWatchGridColumns()
            tabWatch.Controls.Add(_DgvWatch)

            Dim tabPos As New TabPage("포지션")
            _DgvPositions = CreateGrid()
            For Each colName In SimTradeConst.POSITION_COLUMNS
                _DgvPositions.Columns.Add(colName, colName)
            Next
            tabPos.Controls.Add(_DgvPositions)

            Dim tabHist As New TabPage("매매이력")
            _DgvHistory = CreateGrid()
            For Each colName In SimTradeConst.HISTORY_COLUMNS
                _DgvHistory.Columns.Add(colName, colName)
            Next
            tabHist.Controls.Add(_DgvHistory)

            Dim tabSettings As New TabPage("설정")
            _PnlSettings = New Panel()
            _PnlSettings.Dock = DockStyle.Fill
            _PnlSettings.AutoScroll = True
            _PnlSettings.BackColor = Color.FromArgb(40, 40, 40)
            BuildSettingsPanel(_PnlSettings)
            tabSettings.Controls.Add(_PnlSettings)

            _TabControl.TabPages.AddRange({tabWatch, tabPos, tabHist, tabSettings})

            ' ── 로그 패널 ──
            Dim splitMain As New SplitContainer()
            splitMain.Dock = DockStyle.Fill
            splitMain.Orientation = Orientation.Horizontal
            splitMain.SplitterDistance = 550
            splitMain.Panel1.Controls.Add(_TabControl)

            _RtbLog = New RichTextBox()
            _RtbLog.Dock = DockStyle.Fill
            _RtbLog.ReadOnly = True
            _RtbLog.BackColor = Color.Black
            _RtbLog.ForeColor = Color.LightGray
            _RtbLog.Font = New Font("Consolas", 9)
            _RtbLog.ScrollBars = RichTextBoxScrollBars.Vertical
            splitMain.Panel2.Controls.Add(_RtbLog)

            form.Controls.Add(splitMain)
            form.Controls.Add(pnlTop)
        End Sub

#End Region

#Region "설정 패널 빌드"

        Private Sub BuildSettingsPanel(pnl As Panel)
            Dim y = 10
            Const ROW_H = 35

            _NudST_Period = AddNumericRow(pnl, "ST Period:", 1, 50, 10, y) : y += ROW_H
            _NudST_Multiplier = AddNumericRow(pnl, "ST Multiplier:", 0.5D, 10D, 3D, y, 1) : y += ROW_H
            _NudRSI_Period = AddNumericRow(pnl, "RSI Period:", 2, 50, 14, y) : y += ROW_H
            _NudRSI_Overbought = AddNumericRow(pnl, "RSI 과매수:", 50, 95, 75, y) : y += ROW_H
            _ChkVolumeConfirm = AddCheckRow(pnl, "거래량 확인:", True, y) : y += ROW_H
            _NudMaxPosition = AddNumericRow(pnl, "최대 포지션:", 1, 20, 5, y) : y += ROW_H
            _NudPositionSize = AddNumericRow(pnl, "포지션 비중%:", 1, 100, 15, y) : y += ROW_H
            _NudStopLoss = AddNumericRow(pnl, "손절%:", -20D, 0D, -3D, y, 1) : y += ROW_H
            _NudTakeProfit = AddNumericRow(pnl, "익절%:", 0D, 30D, 5D, y, 1) : y += ROW_H
            _NudTrailingStop = AddNumericRow(pnl, "트레일링%:", -10D, 0D, -1.5D, y, 1) : y += ROW_H
            _ChkTrailingStop = AddCheckRow(pnl, "트레일링 활성:", True, y) : y += ROW_H
            _NudCandleInterval = AddNumericRow(pnl, "캔들 간격(초):", 5, 300, 10, y) : y += ROW_H
            _NudMinCandles = AddNumericRow(pnl, "최소 캔들수:", 5, 200, 30, y) : y += ROW_H

            _TxtStartTime = AddTextRow(pnl, "매매 시작:", "09:05", y) : y += ROW_H
            _TxtNoNewBuy = AddTextRow(pnl, "매수 종료:", "14:30", y) : y += ROW_H
            _TxtForceClose = AddTextRow(pnl, "강제 청산:", "15:15", y) : y += ROW_H

            _CboBuyOrder = AddComboRow(pnl, "매수주문:", {"시장가", "최우선호가", "현재가"}, 1, y) : y += ROW_H
            _CboSellOrder = AddComboRow(pnl, "매도주문:", {"시장가", "최우선호가", "현재가"}, 0, y) : y += ROW_H
            _ChkEnableTopNFilter = AddCheckRow(pnl, "TopN 필터 사용:", False, y) : y += ROW_H
            _NudTopNCount = AddNumericRow(pnl, "TopN 개수:", 1, 50, 10, y) : y += ROW_H
            _CboTopNPreset = AddComboRow(pnl, "TopN 프리셋:", {"기본형", "틱강도 중심형", "거래대금 중심형", "추세 중심형", "실적기반 자동"}, 0, y) : y += ROW_H
            AddHandler _CboTopNPreset.SelectedIndexChanged, AddressOf OnTopNPresetChanged
            _NudTopTickWeight = AddNumericRow(pnl, "Top Tick 가중치:", 0D, 100D, 25D, y, 1) : y += ROW_H
            _NudTopAmountWeight = AddNumericRow(pnl, "Top Amount 가중치:", 0D, 100D, 20D, y, 1) : y += ROW_H
            _NudTopTrendWeight = AddNumericRow(pnl, "Top Trend 가중치:", 0D, 100D, 25D, y, 1) : y += ROW_H
            _NudTopMomentumWeight = AddNumericRow(pnl, "Top Momentum 가중치:", 0D, 100D, 30D, y, 1) : y += ROW_H
        End Sub

#End Region


#Region "감시 그리드 가독성"

        Private Sub ConfigureWatchGridColumns()
            If _DgvWatch Is Nothing OrElse _DgvWatch.Columns.Count = 0 Then Return

            _DgvWatch.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None

            SetWatchColumn("코드", 75)
            SetWatchColumn("종목명", 130)
            SetWatchColumn("현재가", 75)
            SetWatchColumn("등락률", 65)
            SetWatchColumn("거래량", 90)

            SetWatchColumn("ST", 40)
            SetWatchColumn("JMA", 45)
            SetWatchColumn("TickSum", 70)
            SetWatchColumn("OBV", 45)
            SetWatchColumn("RSI", 45)
            SetWatchColumn("MACD", 60)

            SetWatchColumn("TopN", 50)
            SetWatchColumn("TopScore", 70)
            SetWatchColumn("TopTick", 65)
            SetWatchColumn("TopAmt", 65)
            SetWatchColumn("TopTrend", 75)

            SetWatchColumn("상태", 60)
            SetWatchColumn("신호", 220, DataGridViewAutoSizeColumnMode.Fill)
            SetWatchColumn("봉수", 50)
        End Sub

        Private Sub SetWatchColumn(headerText As String,
                                   width As Integer,
                                   Optional autoSizeMode As DataGridViewAutoSizeColumnMode = DataGridViewAutoSizeColumnMode.None)
            If _DgvWatch Is Nothing Then Return
            If Not _DgvWatch.Columns.Contains(headerText) Then Return

            Dim col As DataGridViewColumn = _DgvWatch.Columns(headerText)
            col.Width = width
            col.MinimumWidth = Math.Min(width, 40)
            col.AutoSizeMode = autoSizeMode
        End Sub

        Private Shared Sub ApplyWatchRowStyle(row As DataGridViewRow, s As StockStateSnapshot)
            If row Is Nothing OrElse s Is Nothing Then Return

            Dim baseColor As Color = Color.FromArgb(35, 35, 35)
            Dim foreColor As Color = Color.White

            If s.TopNRank = 1 Then
                row.DefaultCellStyle.BackColor = Color.FromArgb(75, 48, 38)
                row.DefaultCellStyle.ForeColor = foreColor
            ElseIf s.TopNRank = 2 Then
                row.DefaultCellStyle.BackColor = Color.FromArgb(60, 55, 38)
                row.DefaultCellStyle.ForeColor = foreColor
            ElseIf s.TopNRank = 3 Then
                row.DefaultCellStyle.BackColor = Color.FromArgb(45, 58, 50)
                row.DefaultCellStyle.ForeColor = foreColor
            ElseIf s.TopNRank > 0 Then
                row.DefaultCellStyle.BackColor = Color.FromArgb(42, 48, 58)
                row.DefaultCellStyle.ForeColor = foreColor
            Else
                row.DefaultCellStyle.BackColor = baseColor
                row.DefaultCellStyle.ForeColor = foreColor
            End If
        End Sub

#End Region
#Region "그리드 갱신"

        Public Sub RefreshWatchGrid(snapshots As List(Of StockStateSnapshot))
            If snapshots Is Nothing Then Return
            Dim sorted = snapshots.
                OrderBy(Function(s) If(s.TopNRank > 0, 0, 1)).
                ThenBy(Function(s) If(s.TopNRank > 0, s.TopNRank, Integer.MaxValue)).
                ThenBy(Function(s) s.Code).
                ToList()

            If _DgvWatch.Rows.Count = sorted.Count Then
                For i = 0 To sorted.Count - 1
                    Dim s = sorted(i)
                    Dim row = _DgvWatch.Rows(i)
                    row.Cells(0).Value = s.Code
                    row.Cells(1).Value = s.Name
                    row.Cells(2).Value = s.CurrentPrice.ToString("N0")
                    row.Cells(3).Value = s.ChangeRate.ToString("F2") & "%"
                    row.Cells(4).Value = s.DayVolume.ToString("N0")
                    row.Cells(5).Value = SimTradeHelper.DirectionChar(s.ST_Direction)
                    row.Cells(6).Value = SimTradeHelper.DirectionChar(s.JMA_Direction)
                    row.Cells(7).Value = If(Double.IsNaN(s.TickSum_Normalized), "-", s.TickSum_Normalized.ToString("F1"))
                    row.Cells(8).Value = SimTradeHelper.DirectionChar(s.OBV_Direction)
                    row.Cells(9).Value = If(Double.IsNaN(s.RSI_Value), "-", s.RSI_Value.ToString("F0"))
                    row.Cells(10).Value = If(Double.IsNaN(s.MACD_Histogram), "-", s.MACD_Histogram.ToString("F2"))
                    row.Cells(11).Value = If(s.TopNRank > 0, s.TopNRank.ToString(), "-")
                    row.Cells(12).Value = If(s.TopNScore > 0, s.TopNScore.ToString("F1"), "-")
                    row.Cells(13).Value = If(s.TopTickScore > 0, s.TopTickScore.ToString("F1"), "-")
                    row.Cells(14).Value = If(s.TopAmountScore > 0, s.TopAmountScore.ToString("F1"), "-")
                    row.Cells(15).Value = If(s.TopTrendScore > 0, s.TopTrendScore.ToString("F1"), "-")
                    row.Cells(16).Value = SimTradeHelper.StateText(s.State)
                    row.Cells(17).Value = s.LastSignal
                    row.Cells(18).Value = s.CandleCount.ToString()
                    ApplyWatchRowStyle(row, s)
                Next
            Else
                _DgvWatch.SuspendLayout()
                _DgvWatch.Rows.Clear()
                For Each s In sorted
                    Dim rowIdx As Integer = _DgvWatch.Rows.Add(
                        s.Code, s.Name, s.CurrentPrice.ToString("N0"),
                        s.ChangeRate.ToString("F2") & "%", s.DayVolume.ToString("N0"),
                        SimTradeHelper.DirectionChar(s.ST_Direction),
                        SimTradeHelper.DirectionChar(s.JMA_Direction),
                        If(Double.IsNaN(s.TickSum_Normalized), "-", s.TickSum_Normalized.ToString("F1")),
                        SimTradeHelper.DirectionChar(s.OBV_Direction),
                        If(Double.IsNaN(s.RSI_Value), "-", s.RSI_Value.ToString("F0")),
                        If(Double.IsNaN(s.MACD_Histogram), "-", s.MACD_Histogram.ToString("F2")),
                        If(s.TopNRank > 0, s.TopNRank.ToString(), "-"),
                        If(s.TopNScore > 0, s.TopNScore.ToString("F1"), "-"),
                        SimTradeHelper.StateText(s.State),
                        s.LastSignal, s.CandleCount.ToString())
                    ApplyWatchRowStyle(_DgvWatch.Rows(rowIdx), s)
                Next
                _DgvWatch.ResumeLayout()
            End If
        End Sub

        Public Sub RefreshPositionGrid(holdings As List(Of StockState))
            If holdings Is Nothing Then Return
            _DgvPositions.SuspendLayout()
            _DgvPositions.Rows.Clear()
            For Each s In holdings
                Dim pnlColor = If(s.CurrentPnLRate >= 0, Color.Red, Color.Blue)
                Dim rowIdx = _DgvPositions.Rows.Add(
                    s.Code, s.Name, s.BuyPrice.ToString("N0"), s.CurrentPrice.ToString("N0"),
                    s.BuyQty, s.CurrentPnLRate.ToString("F2") & "%",
                    s.HighSinceBuy.ToString("N0"), "", s.LastSignal)
                _DgvPositions.Rows(rowIdx).Cells(5).Style.ForeColor = pnlColor
            Next
            _DgvPositions.ResumeLayout()
        End Sub

        Public Sub AddHistoryRow(record As TradeRecord)
            _DgvHistory.Rows.Insert(0,
                record.Code, record.Name, record.BuyPrice.ToString("N0"),
                record.SellPrice.ToString("N0"), record.SellQty,
                record.NetProfit.ToString("N0"), record.NetProfitRate.ToString("F2") & "%",
                record.TotalCost.ToString("N0"), record.HoldingBars,
                record.SellReason, DateTime.Now.ToString("HH:mm:ss"))
        End Sub

#End Region

#Region "로그 관리"

        Public Sub EnqueueLog(message As String)
            _logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}")
        End Sub

        Public Sub FlushLog()
            Dim count = 0
            Dim msg As String = Nothing
            While count < SimTradeConst.MAX_LOG_PER_BATCH AndAlso _logQueue.TryDequeue(msg)
                _RtbLog.AppendText(msg & vbCrLf)
                count += 1
            End While
            If count > 0 Then
                If _RtbLog.Lines.Length > SimTradeConst.MAX_LOG_LINES Then
                    Dim excess = _RtbLog.Lines.Length - SimTradeConst.MAX_LOG_LINES
                    Dim charIdx = _RtbLog.GetFirstCharIndexFromLine(excess)
                    _RtbLog.Select(0, charIdx)
                    _RtbLog.SelectedText = ""
                End If
                _RtbLog.SelectionStart = _RtbLog.TextLength
                _RtbLog.ScrollToCaret()
            End If
        End Sub

#End Region


#Region "TopN 프리셋"

        Private Sub OnTopNPresetChanged(sender As Object, e As EventArgs)
            If _CboTopNPreset Is Nothing Then Return
            If _NudTopTickWeight Is Nothing OrElse _NudTopAmountWeight Is Nothing OrElse
               _NudTopTrendWeight Is Nothing OrElse _NudTopMomentumWeight Is Nothing Then Return

            Select Case _CboTopNPreset.SelectedIndex
                Case 1
                    ' 틱강도 중심형
                    _NudTopTickWeight.Value = 40D
                    _NudTopAmountWeight.Value = 20D
                    _NudTopTrendWeight.Value = 25D
                    _NudTopMomentumWeight.Value = 15D

                Case 2
                    ' 거래대금 중심형
                    _NudTopTickWeight.Value = 20D
                    _NudTopAmountWeight.Value = 40D
                    _NudTopTrendWeight.Value = 25D
                    _NudTopMomentumWeight.Value = 15D

                Case 3
                    ' 추세 중심형
                    _NudTopTickWeight.Value = 20D
                    _NudTopAmountWeight.Value = 20D
                    _NudTopTrendWeight.Value = 40D
                    _NudTopMomentumWeight.Value = 20D

                                Case 4
                    ' 실적기반 자동
                    ' 현재 단계에서는 기존 입력값을 유지한다.
                    ' 향후 백테스트/실전 누적 평가 결과가 산출되면 이 4개 값을 자동 갱신한다.
                    Return

                Case Else
                    ' 기본형
                    _NudTopTickWeight.Value = 25D
                    _NudTopAmountWeight.Value = 20D
                    _NudTopTrendWeight.Value = 25D
                    _NudTopMomentumWeight.Value = 30D
            End Select
        End Sub


        Private Sub UpdateTopNPresetSelectionFromWeights()
            If _CboTopNPreset Is Nothing Then Return
            If _NudTopTickWeight Is Nothing OrElse _NudTopAmountWeight Is Nothing OrElse
               _NudTopTrendWeight Is Nothing OrElse _NudTopMomentumWeight Is Nothing Then Return

            Dim tick As Decimal = _NudTopTickWeight.Value
            Dim amount As Decimal = _NudTopAmountWeight.Value
            Dim trend As Decimal = _NudTopTrendWeight.Value
            Dim momentum As Decimal = _NudTopMomentumWeight.Value

            If tick = 40D AndAlso amount = 20D AndAlso trend = 25D AndAlso momentum = 15D Then
                _CboTopNPreset.SelectedIndex = 1
            ElseIf tick = 20D AndAlso amount = 40D AndAlso trend = 25D AndAlso momentum = 15D Then
                _CboTopNPreset.SelectedIndex = 2
            ElseIf tick = 20D AndAlso amount = 20D AndAlso trend = 40D AndAlso momentum = 20D Then
                _CboTopNPreset.SelectedIndex = 3
            Else
                _CboTopNPreset.SelectedIndex = 0
            End If
        End Sub
#End Region
#Region "설정 UI ↔ SimTradeSettings"

        Public Sub ApplySettingsFromUI(settings As SimTradeSettings)
            settings.ST_Period = CInt(_NudST_Period.Value)
            settings.ST_Multiplier = CDbl(_NudST_Multiplier.Value)
            settings.RSI_Period = CInt(_NudRSI_Period.Value)
            settings.RSI_OverboughtLimit = CDbl(_NudRSI_Overbought.Value)
            settings.RequireVolumeConfirm = _ChkVolumeConfirm.Checked
            settings.MaxPositionCount = CInt(_NudMaxPosition.Value)
            settings.PositionSizeRate = CDbl(_NudPositionSize.Value) / 100.0
            settings.StopLossRate = CDbl(_NudStopLoss.Value)
            settings.TakeProfitRate = CDbl(_NudTakeProfit.Value)
            settings.TrailingStopRate = CDbl(_NudTrailingStop.Value)
            settings.EnableTrailingStop = _ChkTrailingStop.Checked
            settings.CandleIntervalSec = CInt(_NudCandleInterval.Value)
            settings.MinCandlesForSignal = CInt(_NudMinCandles.Value)
            Dim ts As TimeSpan
            If TimeSpan.TryParse(_TxtStartTime.Text.Trim(), ts) Then settings.TradingStartTime = ts
            If TimeSpan.TryParse(_TxtNoNewBuy.Text.Trim(), ts) Then settings.NoNewBuyAfter = ts
            If TimeSpan.TryParse(_TxtForceClose.Text.Trim(), ts) Then settings.ForceCloseTime = ts
            settings.BuyOrderType = CType(_CboBuyOrder.SelectedIndex, SimOrderType)
            settings.SellOrderType = CType(_CboSellOrder.SelectedIndex, SimOrderType)
            settings.EnableTopNFilter = _ChkEnableTopNFilter.Checked
            settings.TopNCount = CInt(_NudTopNCount.Value)
            settings.TopTickWeight = CDbl(_NudTopTickWeight.Value)
            settings.TopAmountWeight = CDbl(_NudTopAmountWeight.Value)
            settings.TopTrendWeight = CDbl(_NudTopTrendWeight.Value)
            settings.TopMomentumWeight = CDbl(_NudTopMomentumWeight.Value)
            settings.TopNPresetIndex = If(_CboTopNPreset IsNot Nothing, _CboTopNPreset.SelectedIndex, 0)
            settings.EnableAutoTopNWeightPreset = (settings.TopNPresetIndex = 4)
            If settings.EnableAutoTopNWeightPreset Then
                settings.AutoTopNWeightSource = "실적기반 자동: 향후 백테스트/실전 누적 성과 기반 자동 제안"
            Else
                settings.AutoTopNWeightSource = ""
            End If
        End Sub

        Public Sub LoadSettingsToUI(settings As SimTradeSettings)
            _NudST_Period.Value = settings.ST_Period
            _NudST_Multiplier.Value = CDec(settings.ST_Multiplier)
            _NudRSI_Period.Value = settings.RSI_Period
            _NudRSI_Overbought.Value = CDec(settings.RSI_OverboughtLimit)
            _ChkVolumeConfirm.Checked = settings.RequireVolumeConfirm
            _NudMaxPosition.Value = settings.MaxPositionCount
            _NudPositionSize.Value = CDec(settings.PositionSizeRate * 100)
            _NudStopLoss.Value = CDec(settings.StopLossRate)
            _NudTakeProfit.Value = CDec(settings.TakeProfitRate)
            _NudTrailingStop.Value = CDec(settings.TrailingStopRate)
            _ChkTrailingStop.Checked = settings.EnableTrailingStop
            _NudCandleInterval.Value = settings.CandleIntervalSec
            _NudMinCandles.Value = settings.MinCandlesForSignal
            _TxtStartTime.Text = settings.TradingStartTime.ToString("hh\:mm")
            _TxtNoNewBuy.Text = settings.NoNewBuyAfter.ToString("hh\:mm")
            _TxtForceClose.Text = settings.ForceCloseTime.ToString("hh\:mm")
            _CboBuyOrder.SelectedIndex = CInt(settings.BuyOrderType)
            _CboSellOrder.SelectedIndex = CInt(settings.SellOrderType)
            _ChkEnableTopNFilter.Checked = settings.EnableTopNFilter
            _NudTopNCount.Value = settings.TopNCount
            _NudTopTickWeight.Value = CDec(settings.TopTickWeight)
            _NudTopAmountWeight.Value = CDec(settings.TopAmountWeight)
            _NudTopTrendWeight.Value = CDec(settings.TopTrendWeight)
            _NudTopMomentumWeight.Value = CDec(settings.TopMomentumWeight)
            UpdateTopNPresetSelectionFromWeights()
        End Sub

        Public Sub SetSettingsEnabled(enabled As Boolean)
            If _PnlSettings Is Nothing Then Return
            For Each ctrl As Control In _PnlSettings.Controls
                If TypeOf ctrl Is NumericUpDown OrElse TypeOf ctrl Is TextBox OrElse
                   TypeOf ctrl Is ComboBox OrElse TypeOf ctrl Is CheckBox Then
                    ctrl.Enabled = enabled
                End If
            Next
        End Sub

#End Region

#Region "UI 팩토리"

        Private Shared Function CreateButton(text As String, x As Integer, y As Integer,
                                              w As Integer, h As Integer) As Button
            Dim btn As New Button()
            btn.Text = text
            btn.Location = New Point(x, y)
            btn.Size = New Size(w, h)
            btn.FlatStyle = FlatStyle.Flat
            btn.BackColor = Color.FromArgb(60, 60, 65)
            btn.ForeColor = Color.White
            Return btn
        End Function

        Private Shared Function CreateLabel(text As String, x As Integer, y As Integer,
                                             w As Integer, h As Integer) As Label
            Dim lbl As New Label()
            lbl.Text = text
            lbl.Location = New Point(x, y)
            lbl.Size = New Size(w, h)
            lbl.AutoSize = False
            Return lbl
        End Function

        Private Shared Function CreateGrid() As DataGridView
            Dim dgv As New DataGridView()
            dgv.Dock = DockStyle.Fill
            dgv.ReadOnly = True
            dgv.AllowUserToAddRows = False
            dgv.AllowUserToDeleteRows = False
            dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            dgv.BackgroundColor = Color.FromArgb(30, 30, 30)
            dgv.ForeColor = Color.White
            dgv.GridColor = Color.FromArgb(60, 60, 60)
            dgv.DefaultCellStyle.BackColor = Color.FromArgb(35, 35, 35)
            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 80, 120)
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(50, 50, 55)
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
            dgv.EnableHeadersVisualStyles = False
            dgv.RowHeadersVisible = False
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            Return dgv
        End Function

        Private Shared Function AddNumericRow(pnl As Panel, label As String,
                                               min As Decimal, max As Decimal, def As Decimal,
                                               y As Integer, Optional decimals As Integer = 0) As NumericUpDown
            Dim lbl As New Label()
            lbl.Text = label
            lbl.Location = New Point(10, y + 3)
            lbl.Size = New Size(120, 20)
            lbl.ForeColor = Color.White
            pnl.Controls.Add(lbl)

            Dim nud As New NumericUpDown()
            nud.Location = New Point(140, y)
            nud.Size = New Size(100, 25)
            nud.Minimum = min
            nud.Maximum = max
            nud.Value = def
            nud.DecimalPlaces = decimals
            nud.BackColor = Color.FromArgb(50, 50, 55)
            nud.ForeColor = Color.White
            If decimals > 0 Then nud.Increment = 0.1D
            pnl.Controls.Add(nud)
            Return nud
        End Function

        Private Shared Function AddCheckRow(pnl As Panel, label As String,
                                             def As Boolean, y As Integer) As CheckBox
            Dim chk As New CheckBox()
            chk.Text = label
            chk.Location = New Point(10, y)
            chk.Size = New Size(230, 25)
            chk.Checked = def
            chk.ForeColor = Color.White
            pnl.Controls.Add(chk)
            Return chk
        End Function

        Private Shared Function AddTextRow(pnl As Panel, label As String,
                                            def As String, y As Integer) As TextBox
            Dim lbl As New Label()
            lbl.Text = label
            lbl.Location = New Point(10, y + 3)
            lbl.Size = New Size(120, 20)
            lbl.ForeColor = Color.White
            pnl.Controls.Add(lbl)

            Dim txt As New TextBox()
            txt.Location = New Point(140, y)
            txt.Size = New Size(100, 25)
            txt.Text = def
            txt.BackColor = Color.FromArgb(50, 50, 55)
            txt.ForeColor = Color.White
            pnl.Controls.Add(txt)
            Return txt
        End Function

        Private Shared Function AddComboRow(pnl As Panel, label As String,
                                             items As String(), defIdx As Integer, y As Integer) As ComboBox
            Dim lbl As New Label()
            lbl.Text = label
            lbl.Location = New Point(10, y + 3)
            lbl.Size = New Size(120, 20)
            lbl.ForeColor = Color.White
            pnl.Controls.Add(lbl)

            Dim cbo As New ComboBox()
            cbo.Location = New Point(140, y)
            cbo.Size = New Size(100, 25)
            cbo.DropDownStyle = ComboBoxStyle.DropDownList
            cbo.Items.AddRange(items)
            cbo.SelectedIndex = defIdx
            cbo.BackColor = Color.FromArgb(50, 50, 55)
            cbo.ForeColor = Color.White
            pnl.Controls.Add(cbo)
            Return cbo
        End Function

#End Region

    End Class

End Namespace



















