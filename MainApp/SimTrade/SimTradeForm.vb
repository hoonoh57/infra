' ═══════════════════════════════════════════════════════════════
' SimTradeForm.vb — 모의매매 전용 폼 (v4.2 리팩토링)
' ═══════════════════════════════════════════════════════════════
' [v4.2] 불변/가변 분리 리팩토링.
'   - SimTradeConstants.vb : 불변 상수 · 정적 헬퍼 · 인터페이스
'   - SimTradeEngine.vb    : 가변 데이터 파이프라인 · 신호 · 상태
'   - SimTradeUI.vb        : UI 레이아웃(불변) · 그리드 갱신(가변)
'   - SimTradeForm.vb      : Form 본체(이 파일) — 접착 + 생명주기
'
' ★ v4.0 원칙서 전체 적용:
'   - CandleBuilder, SignalEvaluator, FilterEngine,
'     OrderSimulator, AdaptiveParamCalc, StateManager
' ★ 키움 모의매매 서버에 실제 주문 (지정가/시장가)
' ★ 캔들 다운로드: StockInfoManager → Cybos 일괄 고속
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports MainApp.SimTrade
Imports [Shared]

Public Class SimTradeForm
    Inherits Form
    Implements ISimTradeView

    ' ── 엔진 (가변 로직) ──
    Private ReadOnly _settings As New SimTradeSettings()
    Private _engine As SimTradeEngine

    ' ── UI 빌더 ──
    Private ReadOnly _ui As New SimTradeUI()

    ' ── 타이머 ──
    Private WithEvents _tmrRefresh As New Timer()
    Private WithEvents _tmrLog As New Timer()


    ' ═══════════════════════════════════════
    ' 생성 / 소멸
    ' ═══════════════════════════════════════

    Public Sub New()
        ' UI 레이아웃 빌드 (불변)
        _ui.Build(Me)

        ' 엔진 생성 (가변)
        _engine = New SimTradeEngine(_settings, Me)

        ' 이벤트 연결
        AddHandler _ui.BtnCondition.Click, AddressOf OnConditionClick
        AddHandler _ui.BtnStart.Click, AddressOf OnStartClick
        AddHandler _ui.BtnStop.Click, AddressOf OnStopClick

        ' 타이머 (간격은 불변 상수)
        _tmrRefresh.Interval = SimTradeConst.REFRESH_TIMER_INTERVAL_MS
        _tmrLog.Interval = SimTradeConst.LOG_TIMER_INTERVAL_MS
        _tmrLog.Start()

        ' 설정 로드
        _ui.LoadSettingsToUI(_settings)
    End Sub


    ' ═══════════════════════════════════════
    ' Form 이벤트
    ' ═══════════════════════════════════════

    Private Sub SimTradeForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Log("모의매매 폼 로드 (v4.2). 조건식을 선택한 뒤 [시작]을 누르세요.")
        Log("★ 엔진: CandleBuilder + SignalEvaluator(7조건) + FilterEngine(6종) + OrderSimulator")
        Log($"★ 캔들 간격: 개장={_settings.CandleInterval_Open}초, 초반={_settings.CandleInterval_EarlyMorning}초, 정상={_settings.CandleInterval_Normal}초")
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
    ' 시작 / 중지
    ' ═══════════════════════════════════════

    Private Sub OnStartClick(sender As Object, e As EventArgs)
        If _engine.ConditionIndex < 0 Then
            MessageBox.Show("먼저 조건식을 선택하세요.")
            Return
        End If

        ' UI → Settings
        _ui.ApplySettingsFromUI(_settings)

        ' 엔진 재초기화 (파라미터 변경 반영)
        _engine.InitializeEngines()

        ' 설정 로그
        _engine.LogCurrentSettings()

        ' UI 상태 전환
        _ui.BtnStart.Enabled = False
        _ui.BtnStop.Enabled = True
        _ui.BtnCondition.Enabled = False
        _ui.SetSettingsEnabled(False)
        UpdateStatus($"● 실행 중 | {_engine.ConditionName}", Color.Lime)
        _tmrRefresh.Start()

        ' 엔진 시작
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
        ' 타이머에서 처리
    End Sub

    Public Sub RequestPositionRefresh() Implements ISimTradeView.RequestPositionRefresh
        ' 타이머에서 처리
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
    ' 타이머 (갱신 주기는 불변 상수)
    ' ═══════════════════════════════════════

    Private Sub OnTimerRefresh(sender As Object, e As EventArgs) Handles _tmrRefresh.Tick
        If Not _engine.IsRunning Then Return

        ' 감시 그리드
        Dim snapshots = _engine.Manager.GetSnapshot()
        _ui.RefreshWatchGrid(snapshots)

        ' 포지션 그리드
        Dim holdings = _engine.Manager.GetHoldingStocks()
        _ui.RefreshPositionGrid(holdings)

        ' 요약
        Dim stats = _engine.Simulator.GetStatsSummary()
        Dim readyCount = _engine.Manager.CountByState(DataState.Ready)
        Dim tradingCount = _engine.Manager.CountByState(DataState.Trading)
        Dim total = _engine.Manager.TotalCount
        _ui.LblSummary.Text = $"종목: {total} (Ready={readyCount}, 매매={tradingCount}) | {stats}"
    End Sub

    Private Sub OnTimerLog(sender As Object, e As EventArgs) Handles _tmrLog.Tick
        _ui.FlushLog()
    End Sub

End Class