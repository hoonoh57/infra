' ═══════════════════════════════════════════════════════════════
' SimTradeForm.vb — 모의매매 전용 폼 (v4.2 리팩토링)
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports MainApp.SimTrade
Imports MainApp.SimTrade.Circuit
Imports [Shared]

Public Class SimTradeForm
    Inherits Form
    Implements ISimTradeView

    ' ── 엔진 (가변 로직) ──
    Private ReadOnly _settings As New SimTradeSettings()
    Private _engine As SimTradeEngine

    ' ── UI 빌더 ──
    Private ReadOnly _ui As New SimTradeUI()
    Private ReadOnly _diagnostics As New SimTradeDiagnosticsUI()
    Private ReadOnly _btnDataCheck As New Button()
    Private ReadOnly _btnBatchValidate As New Button()

    ' ── 타이머 ──
    Private WithEvents _tmrRefresh As New Timer()
    Private WithEvents _tmrLog As New Timer()

    ' ── 회로 디자이너 ──
    Private _circuitForm As CircuitDesignerForm = Nothing


    ' ═══════════════════════════════════════
    ' 생성 / 소멸
    ' ═══════════════════════════════════════

    Public Sub New()
        _ui.Build(Me)
        InstallDataCheckButton()
        InstallBatchValidatorButton()
        _diagnostics.Build(_ui.TabControl)
        _engine = New SimTradeEngine(_settings, Me)

        AddHandler _ui.BtnCondition.Click, AddressOf OnConditionClick
        AddHandler _ui.BtnStart.Click, AddressOf OnStartClick
        AddHandler _ui.BtnStop.Click, AddressOf OnStopClick
        AddHandler _ui.BtnCircuit.Click, AddressOf OnCircuitClick
        AddHandler _btnDataCheck.Click, AddressOf OnDataCheckClick
        AddHandler _btnBatchValidate.Click, AddressOf OnBatchValidatorClick
        AddHandler _ui.DgvWatch.CellDoubleClick, AddressOf OnWatchGridCellDoubleClick

        _tmrRefresh.Interval = SimTradeConst.REFRESH_TIMER_INTERVAL_MS
        _tmrLog.Interval = SimTradeConst.LOG_TIMER_INTERVAL_MS
        _tmrLog.Start()

        _ui.LoadSettingsToUI(_settings)
    End Sub

    Private Sub InstallDataCheckButton()
        _btnDataCheck.Text = "데이터확인"
        _btnDataCheck.Width = 90
        _btnDataCheck.Height = 30
        _btnDataCheck.Left = 370
        _btnDataCheck.Top = 10
        _btnDataCheck.FlatStyle = FlatStyle.Flat
        _btnDataCheck.BackColor = Color.FromArgb(60, 60, 65)
        _btnDataCheck.ForeColor = Color.White

        For Each ctrl As Control In Me.Controls
            Dim pnl As Panel = TryCast(ctrl, Panel)
            If pnl IsNot Nothing AndAlso pnl.Dock = DockStyle.Top Then
                pnl.Controls.Add(_btnDataCheck)
                Exit For
            End If
        Next
    End Sub

    Private Sub InstallBatchValidatorButton()
        _btnBatchValidate.Text = "Batch"
        _btnBatchValidate.Width = 95
        _btnBatchValidate.Height = 30
        _btnBatchValidate.Left = 465
        _btnBatchValidate.Top = 10
        _btnBatchValidate.FlatStyle = FlatStyle.Flat
        _btnBatchValidate.BackColor = Color.FromArgb(60, 80, 70)
        _btnBatchValidate.ForeColor = Color.White

        For Each ctrl As Control In Me.Controls
            Dim pnl As Panel = TryCast(ctrl, Panel)
            If pnl IsNot Nothing AndAlso pnl.Dock = DockStyle.Top Then
                pnl.Controls.Add(_btnBatchValidate)
                Exit For
            End If
        Next
    End Sub



    ' ═══════════════════════════════════════
    ' Form 이벤트
    ' ═══════════════════════════════════════

    Private Sub SimTradeForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Log("모의매매 폼 로드 (v4.2). 조건식을 선택한 뒤 [시작]을 누르세요.")
        Log("★ 엔진: CandleBuilder + SignalEvaluator(7조건) + FilterEngine(6종) + OrderSimulator")
        Log($"★ 캔들 간격: 개장={_settings.CandleInterval_Open}초, 초반={_settings.CandleInterval_EarlyMorning}초, 정상={_settings.CandleInterval_Normal}초")
        Log("★ 수익검증: TickSum 진단 / 순위→수익 검증 탭이 활성화되었습니다.")
        Log("★ 데이터확인: 감시그리드 종목 선택 후 [데이터확인] 또는 행 더블클릭으로 내부 데이터 전체 확인")
    End Sub

    Private Sub SimTradeForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        _engine.Stop()
    End Sub


    ' ═══════════════════════════════════════
    ' 조건검색
    ' ═══════════════════════════════════════

    Private Sub OnConditionClick(sender As Object, e As EventArgs)
        Dim dlg As New ConditionSelectDialog()
        If dlg.ShowDialog(Me) = DialogResult.OK Then
            _engine.ConditionName = dlg.SelectedConditionName
            _engine.ConditionIndex = dlg.SelectedConditionIndex
            Log($"조건식 선택: [{_engine.ConditionIndex}] {_engine.ConditionName}")
            UpdateStatus($"조건식: {_engine.ConditionName} | 대기 중", Color.Gray)
        End If
    End Sub


    ' ═══════════════════════════════════════
    ' 데이터 확인
    ' ═══════════════════════════════════════

    Private Sub OnDataCheckClick(sender As Object, e As EventArgs)
        ShowSelectedStockDataDebug()
    End Sub

    Private Sub OnWatchGridCellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e Is Nothing OrElse e.RowIndex < 0 Then Return
        ShowSelectedStockDataDebug()
    End Sub

    Private Sub ShowSelectedStockDataDebug()
        Dim code As String = GetSelectedWatchCode()
        If String.IsNullOrWhiteSpace(code) Then
            MessageBox.Show(Me, "감시 그리드에서 먼저 종목을 선택하세요.", "데이터 확인", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim state As StockState = _engine.Manager.GetState(code)
        If state Is Nothing Then
            MessageBox.Show(Me, "선택 종목의 상태를 찾을 수 없습니다: " & code, "데이터 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim frm As New SimTradeStockDataDebugForm(state)
        frm.Show(Me)
        Log("[데이터확인] " & state.Code & " " & state.Name & " 내부 데이터 창 열림")
    End Sub

    Private Function GetSelectedWatchCode() As String
        If _ui Is Nothing OrElse _ui.DgvWatch Is Nothing Then Return ""
        If _ui.DgvWatch.CurrentRow Is Nothing Then Return ""
        If _ui.DgvWatch.CurrentRow.Cells Is Nothing OrElse _ui.DgvWatch.CurrentRow.Cells.Count = 0 Then Return ""

        Dim value As Object = _ui.DgvWatch.CurrentRow.Cells(0).Value
        If value Is Nothing Then Return ""
        Return value.ToString().Trim()
    End Function


    ' ═══════════════════════════════════════
    ' 회로 디자이너
    ' ═══════════════════════════════════════

    Private Sub OnCircuitClick(sender As Object, e As EventArgs)
        If _circuitForm IsNot Nothing AndAlso Not _circuitForm.IsDisposed Then
            _circuitForm.BringToFront()
            Return
        End If

        _circuitForm = New CircuitDesignerForm(_settings, _engine.Manager)
        _circuitForm.Show(Me)
        Log("회로 설계기 열림")
    End Sub

    Private Sub OnBatchValidatorClick(sender As Object, e As EventArgs)
        Dim frm As New ConditionBatchValidatorForm(_settings, _engine.Manager)
        frm.Show(Me)
        Log("Condition Batch Validator 열림")
    End Sub




    ' ═══════════════════════════════════════
    ' 시작 / 중지
    ' ═══════════════════════════════════════

    Private Sub OnStartClick(sender As Object, e As EventArgs)
        If _engine.ConditionIndex < 0 Then
            MessageBox.Show("먼저 조건식을 선택하세요.")
            Return
        End If

        _ui.ApplySettingsFromUI(_settings)
        _engine.InitializeEngines()
        _engine.LogCurrentSettings()

        _ui.BtnStart.Enabled = False
        _ui.BtnStop.Enabled = True
        _ui.BtnCondition.Enabled = False
        _ui.SetSettingsEnabled(False)
        UpdateStatus($"● 실행 중 | {_engine.ConditionName}", Color.Lime)
        _tmrRefresh.Start()

        _engine.Start()
    End Sub

    Private Sub OnStopClick(sender As Object, e As EventArgs)
        _engine.Stop()
        _tmrRefresh.Stop()
        _ui.BtnStart.Enabled = True
        _ui.BtnStop.Enabled = False
        _ui.BtnCondition.Enabled = True
        _ui.SetSettingsEnabled(True)
        UpdateStatus("■ 중지됨", Color.Gray)
    End Sub


    ' ═══════════════════════════════════════
    ' ISimTradeView 구현
    ' ═══════════════════════════════════════

    Public Sub Log(message As String) Implements ISimTradeView.Log
        _ui.EnqueueLog(message)
    End Sub

    Public Sub SafeUI(action As Action) Implements ISimTradeView.SafeUI
        If Me.InvokeRequired Then
            Me.BeginInvoke(action)
        Else
            action.Invoke()
        End If
    End Sub

    Public Sub UpdateStatus(text As String, color As Color) Implements ISimTradeView.UpdateStatus
        SafeUI(Sub()
                   _ui.LblStatus.Text = text
                   _ui.LblStatus.ForeColor = color
               End Sub)
    End Sub

    Public Sub UpdateSummary(text As String) Implements ISimTradeView.UpdateSummary
        SafeUI(Sub() _ui.LblSummary.Text = text)
    End Sub

    Public Sub RequestWatchRefresh() Implements ISimTradeView.RequestWatchRefresh
    End Sub

    Public Sub RequestPositionRefresh() Implements ISimTradeView.RequestPositionRefresh
    End Sub

    Public Sub AddHistoryRow(record As TradeRecord) Implements ISimTradeView.AddHistoryRow
        SafeUI(Sub() _ui.AddHistoryRow(record))
    End Sub

    Public ReadOnly Property IsRunning As Boolean Implements ISimTradeView.IsRunning
        Get
            Return _engine.IsRunning
        End Get
    End Property


    ' ═══════════════════════════════════════
    ' 타이머
    ' ═══════════════════════════════════════

    Private Sub OnTimerRefresh(sender As Object, e As EventArgs) Handles _tmrRefresh.Tick
        If Not _engine.IsRunning Then Return

        Dim topResult As Top10Result = _engine.RefreshTopNRanking()

        Dim snapshots As List(Of StockStateSnapshot) = _engine.Manager.GetSnapshot()
        _ui.RefreshWatchGrid(snapshots)
        _diagnostics.Refresh(snapshots, topResult)

        Dim holdings As List(Of StockState) = _engine.Manager.GetHoldingStocks()
        _ui.RefreshPositionGrid(holdings)

        Dim stats As String = _engine.Simulator.GetStatsSummary()
        Dim presetName As String = _engine.GetTopNPresetName()
        Dim readyCount As Integer = _engine.Manager.CountByState(DataState.Ready)
        Dim tradingCount As Integer = _engine.Manager.CountByState(DataState.Trading)
        Dim total As Integer = _engine.Manager.TotalCount
        Dim top3Text As String = "-"
        If topResult IsNot Nothing AndAlso topResult.TopStocks IsNot Nothing AndAlso topResult.TopStocks.Count > 0 Then
            Dim topItems As New List(Of String)()
            Dim topLimit As Integer = Math.Min(3, topResult.TopStocks.Count)
            For i As Integer = 0 To topLimit - 1
                Dim item As Top10Score = topResult.TopStocks(i)
                topItems.Add($"{item.Code}({item.TotalScore:F0})")
            Next
            top3Text = String.Join(", ", topItems)
        End If

        _ui.LblSummary.Text = $"종목: {total} (Ready={readyCount}, 매매={tradingCount}) | 프리셋: {presetName} | Top3: {top3Text} | {stats}"
    End Sub

    Private Sub OnTimerLog(sender As Object, e As EventArgs) Handles _tmrLog.Tick
        _ui.FlushLog()
    End Sub

End Class




