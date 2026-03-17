' ═══════════════════════════════════════════════════════════════
' MainShell.vb — 메인 도킹 셸
' ═══════════════════════════════════════════════════════════════
' 불변 설계: 새 서브폼은 ShowDockForm() 호출 한 줄로 추가.
' MainShell 코드를 수정할 필요 없음.
' ═══════════════════════════════════════════════════════════════

Imports System.Diagnostics
Imports System.Windows.Forms
Imports [Shared]
Imports WeifenLuo.WinFormsUI.Docking

Public Class MainShell

    ' ─── 도킹 폼 인스턴스 (싱글톤 관리) ───
    Private ReadOnly _dockForms As New Dictionary(Of String, DockFormBase)(StringComparer.OrdinalIgnoreCase)
    Private _mnuStrategyLabTest As ToolStripMenuItem
    Private _mnuStrategySweep As ToolStripMenuItem
    Private _mnuResearchDbManager As ToolStripMenuItem
    Private _mnuSrcKosdaq150 As ToolStripMenuItem
    Private _mnuZeroLoss As ToolStripMenuItem

    ' ─── 타이머 ───
    Private WithEvents _clockTimer As Timer

    ' ════════════════════════════════════════
    ' 초기화
    ' ════════════════════════════════════════

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)

        ' UI 컨텍스트 설정 (EmitOnUI가 동작하려면 필수)
        MessageBus.I.SetUIContext(System.Threading.SynchronizationContext.Current)

        ' 시계 타이머
        _clockTimer = New Timer()
        _clockTimer.Interval = 1000
        _clockTimer.Start()

        ' Bus 구독 (상태바 업데이트)
        MessageBus.I.On(Topics.SYS_SERVER_STATUS, AddressOf OnServerStatus)
        MessageBus.I.On(Topics.SYS_AUTOTRADE, AddressOf OnAutoTradeStatus)
        MessageBus.I.On(Topics.UI_CHART_OPEN, AddressOf OnChartOpen)
        EnsureStrategyLabTestMenu()
        EnsureStrategySweepMenu()
        EnsureResearchDbManagerMenu()
        EnsureKosdaq150Menu()
        EnsureZeroLossMenu()
        MainApp.Services.ResearchDbMaintenanceService.Instance.Start()

        ' ── 기본 폼 배치 ──
        ' 1) 로그 폼 (하단)
        ShowDockForm(Of LogForm)(DockState.DockBottom)

        ' 2) 매매 모니터 (하단, 로그 옆)
        '    로그인 전에도 표시 — TRADE_SYNC_COMPLETE 수신 시 초기 데이터 자동 채움
        ShowDockForm(Of TradeMonitorForm)(DockState.DockBottom)

        ' 로그 시작 메시지
        AppLogger.I.Info("═══════════════════════════════════════", "MainShell")
        AppLogger.I.Info("  AutoTrading System 시작", "MainShell")
        AppLogger.I.Info($"  시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", "MainShell")
        AppLogger.I.Info("═══════════════════════════════════════", "MainShell")

        AppLogger.I.Info("MainShell 로드 완료. 도킹 패널 준비됨.", "MainShell")
        AppLogger.I.Info("메뉴 [데이터 → 키움 로그인]으로 시작하세요.", "MainShell")
    End Sub

    ' ════════════════════════════════════════
    ' 도킹 폼 관리 (핵심 메서드)
    ' ════════════════════════════════════════

    ''' <summary>
    ''' 도킹 폼을 표시한다. 이미 있으면 활성화, 없으면 생성.
    ''' 새 서브폼 추가 시 이 메서드 한 줄이면 충분.
    ''' </summary>
    Public Function ShowDockForm(Of T As {DockFormBase, New})(Optional state As DockState = DockState.Unknown) As T
        Dim key = GetType(T).Name

        ' 이미 존재하면 보이기 + 활성화
        If _dockForms.ContainsKey(key) Then
            Dim existing = DirectCast(_dockForms(key), T)
            If existing.IsHidden Then
                existing.Show(dockPanel)
            End If
            existing.Activate()
            AppLogger.I.Debug($"폼 활성화: {key}", "Shell")
            Return existing
        End If

        ' 새로 생성
        Dim frm As New T()
        Dim dockStateRet = If(state = DockState.Unknown, frm.DefaultDockState, state)
        frm.Show(dockPanel, dockStateRet)
        _dockForms(key) = frm
        AppLogger.I.Debug($"폼 생성: {key} → {dockStateRet}", "Shell")
        Return frm
    End Function

    ''' <summary>
    ''' Document 영역에 고유 키로 폼을 추가 (차트 등 멀티 인스턴스용)
    ''' </summary>
    Public Function ShowDocumentForm(Of T As {DockFormBase, New})(uniqueKey As String, Optional setup As Action(Of T) = Nothing) As T
        ' 이미 있으면 활성화
        If _dockForms.ContainsKey(uniqueKey) Then
            Dim existing = DirectCast(_dockForms(uniqueKey), T)
            If existing.IsHidden Then existing.Show(dockPanel)
            existing.Activate()
            Return existing
        End If

        Dim frm As New T()
        setup?.Invoke(frm)
        frm.Show(dockPanel, DockState.Document)
        _dockForms(uniqueKey) = frm
        AppLogger.I.Info($"Document 폼 생성: {uniqueKey}", "Shell")
        Return frm
    End Function

    Public Sub ShowDataView(stockCode As String, dataArrays As List(Of ChartDataArray))
        Dim frm = ShowDockForm(Of frmDataView)(DockState.DockBottom)
        frm.SetData(stockCode, dataArrays)

        Try
            Dim logForm As LogForm = Nothing
            If _dockForms.ContainsKey(NameOf(logForm)) Then
                logForm = TryCast(_dockForms(NameOf(logForm)), LogForm)
            End If

            If logForm IsNot Nothing AndAlso logForm.Pane IsNot Nothing Then
                frm.Show(logForm.Pane, DockAlignment.Right, 0.45R)
            Else
                frm.Show(dockPanel, DockState.DockBottom)
            End If
        Catch
            frm.Show(dockPanel, DockState.DockBottom)
        End Try

        frm.Activate()
    End Sub

    ' ════════════════════════════════════════
    ' 데이터소스 메뉴 핸들러
    ' ════════════════════════════════════════

    Private Sub mnuShowStockInfo_Click(sender As Object, e As EventArgs) Handles mnuShowStockInfo.Click
        ShowDockForm(Of StockInfoForm)(DockState.DockLeft)
    End Sub

    Private Sub mnuSrcCondition_Click(sender As Object, e As EventArgs) Handles mnuSrcCondition.Click
        ' 종목정보 폼 먼저 표시
        ShowDockForm(Of StockInfoForm)(DockState.DockLeft)

        Using dlg As New ConditionSelectDialog()
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                AppLogger.I.Info($"조건검색 실행: [{dlg.SelectedConditionIndex}] {dlg.SelectedConditionName}", "DataSource")

                ' 조건검색 실행 → 결과 수신 → StockInfoManager에 추가
                MessageBus.I.On(Topics.CONDITION_SEARCH_RESULT, AddressOf OnConditionSearchResult)
                MessageBus.I.Emit(Topics.CONDITION_START,
                                  "name", dlg.SelectedConditionName,
                                  "index", dlg.SelectedConditionIndex)
            End If
        End Using
    End Sub

    ' 모의매매 메뉴 클릭 핸들러 (삭제 시 이 줄만 제거)
    Private Sub OnSimTradeClick(sender As Object, e As EventArgs) Handles mnuSimTrade.Click
        Dim f As New SimTradeForm()
        f.Show()
    End Sub


    Private Sub OnConditionSearchResult(m As Msg)
        MessageBus.I.Off(Topics.CONDITION_SEARCH_RESULT, AddressOf OnConditionSearchResult)

        If Not m.Bool("success") Then
            AppLogger.I.Error($"조건검색 실패: {m.Str("message")}", "DataSource")
            Return
        End If

        Dim codes = m.Arr(Of String)("codes")
        Dim condName = m.Str("condName", "")

        If codes Is Nothing OrElse codes.Length = 0 Then
            AppLogger.I.Warn($"조건검색 결과: 종목 없음 ({condName})", "DataSource")
            Return
        End If

        AppLogger.I.Info($"조건검색 결과: {codes.Length}종목 ({condName})", "DataSource")
        StockInfoManager.I.AddStocks(codes, DataSourceType.조건검색, condName)

        ' 코스피 지수도 함께 추가 (전략 오버레이용)
        StockInfoManager.I.AddStocks({"001"}, DataSourceType.조건검색, "코스피지수")
    End Sub

    Private Sub mnuSrcSector_Click(sender As Object, e As EventArgs) Handles mnuSrcSector.Click
        ShowDockForm(Of StockInfoForm)(DockState.DockLeft)

        Using dlg As New SectorSelectDialog(SectorSelectDialog.SectorMode.주도섹터)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Dim codes = dlg.SelectedCodes
                If codes Is Nothing OrElse codes.Length = 0 Then
                    AppLogger.I.Warn($"주도섹터 종목 없음: [{dlg.SelectedCode}] {dlg.SelectedName}", "DataSource")
                    Return
                End If

                AppLogger.I.Info($"주도섹터 선택: [{dlg.SelectedCode}] {dlg.SelectedName} / {codes.Length}종목", "DataSource")
                StockInfoManager.I.AddStocks(codes, DataSourceType.주도섹터, dlg.SelectedName)
            End If
        End Using
    End Sub

    Private Sub OnSectorStocksResult(m As Msg)
        MessageBus.I.Off(Topics.SECTOR_STOCKS_RESULT, AddressOf OnSectorStocksResult)

        Dim rows = m.DictList("rows")
        If rows Is Nothing OrElse rows.Count = 0 Then
            AppLogger.I.Warn("섹터 종목 없음", "DataSource")
            Return
        End If

        Dim codes = rows.Select(Function(r)
                                    If r.ContainsKey("code") Then Return r("code")
                                    Return ""
                                End Function).Where(Function(c) c <> "").ToArray()

        AppLogger.I.Info($"섹터 종목: {codes.Length}종목", "DataSource")
        Dim sectorCode = m.Str("sectorCode", "")
        StockInfoManager.I.AddStocks(codes, DataSourceType.주도섹터, sectorCode)
    End Sub

    Private Sub mnuSrcProgramBuy_Click(sender As Object, e As EventArgs) Handles mnuSrcProgramBuy.Click
        ShowDockForm(Of StockInfoForm)(DockState.DockLeft)
        AppLogger.I.Info("프로그램순매수 상위 조회 요청", "DataSource")
        ' TODO: 프로그램순매수 상위 종목 추출 로직
        AppLogger.I.Warn("프로그램순매수 상위 — 추후 구현", "DataSource")
    End Sub

    Private Sub mnuSrcFavorite_Click(sender As Object, e As EventArgs) Handles mnuSrcFavorite.Click
        ShowDockForm(Of StockInfoForm)(DockState.DockLeft)
        Using dlg As New WatchlistSelectDialog()
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Dim codesRet = dlg.SelectedCodes
                If codesRet IsNot Nothing AndAlso codesRet.Length > 0 Then
                    AppLogger.I.Info($"관심종목 추가: {codesRet.Length}종목 [{dlg.SelectedGroupName}]", "DataSource")
                    StockInfoManager.I.AddStocks(codesRet, DataSourceType.관심종목, dlg.SelectedGroupName)
                End If
            End If
        End Using
    End Sub

    Private Sub mnuSrcKospiFollow_Click(sender As Object, e As EventArgs) Handles mnuSrcKospiFollow.Click
        ShowDockForm(Of StockInfoForm)(DockState.DockLeft)
        AppLogger.I.Info("코스피 추종종목 조회 요청", "DataSource")
        ' TODO: 코스피 시총 상위 N종목 자동 추출
        AppLogger.I.Warn("코스피 추종 — 추후 구현", "DataSource")
    End Sub

    Private Sub mnuSrcKosdaqFollow_Click(sender As Object, e As EventArgs) Handles mnuSrcKosdaqFollow.Click
        ShowDockForm(Of StockInfoForm)(DockState.DockLeft)
        AppLogger.I.Info("코스닥 추종종목 조회 요청", "DataSource")
        ' TODO: 코스닥 시총 상위 N종목 자동 추출
        AppLogger.I.Warn("코스닥 추종 — 추후 구현", "DataSource")
    End Sub

    ' ════════════════════════════════════════
    ' 메뉴 이벤트
    ' ════════════════════════════════════════

    Private Sub mnuExit_Click(sender As Object, e As EventArgs) Handles mnuExit.Click
        Me.Close()
    End Sub

    Private Sub mnuNewChart_Click(sender As Object, e As EventArgs) Handles mnuNewChart.Click
        Dim code = InputBox("종목코드 입력:", "새 차트", "005930")
        If String.IsNullOrWhiteSpace(code) Then Return

        code = SharedUtil.NormalizeChartCode(code)
        AppLogger.I.Info($"새 차트 요청: {code}", "Shell")

        MessageBus.I.Emit(Topics.UI_CHART_OPEN, "code", code)
    End Sub

    Private Sub EnsureStrategyLabTestMenu()
        If _mnuStrategyLabTest IsNot Nothing OrElse mnuTradeTest Is Nothing Then Return

        _mnuStrategyLabTest = New ToolStripMenuItem("StrategyLab Test...")
        AddHandler _mnuStrategyLabTest.Click, AddressOf OnStrategyLabTestClick

        mnuTradeTest.DropDownItems.Add(New ToolStripSeparator())
        mnuTradeTest.DropDownItems.Add(_mnuStrategyLabTest)
    End Sub

    Private Sub OnStrategyLabTestClick(sender As Object, e As EventArgs)
        ShowDockForm(Of StrategyLabDockForm)(DockState.Document)
        AppLogger.I.Info("StrategyLab opened inside MainApp.", "Shell")
    End Sub

    Private Sub EnsureStrategySweepMenu()
        If _mnuStrategySweep IsNot Nothing OrElse mnuTradeTest Is Nothing Then Return

        _mnuStrategySweep = New ToolStripMenuItem("Strategy Sweep...")
        AddHandler _mnuStrategySweep.Click, AddressOf OnStrategySweepClick
        mnuTradeTest.DropDownItems.Add(_mnuStrategySweep)
    End Sub

    Private Sub OnStrategySweepClick(sender As Object, e As EventArgs)
        ShowDockForm(Of StrategySweepForm)(DockState.Document)
        AppLogger.I.Info("Strategy Sweep opened.", "Shell")
    End Sub

    Private Sub EnsureResearchDbManagerMenu()
        If _mnuResearchDbManager IsNot Nothing OrElse mnuData Is Nothing Then Return

        _mnuResearchDbManager = New ToolStripMenuItem("연구 DB 관리...")
        AddHandler _mnuResearchDbManager.Click, AddressOf OnResearchDbManagerClick

        mnuData.DropDownItems.Add(New ToolStripSeparator())
        mnuData.DropDownItems.Add(_mnuResearchDbManager)
    End Sub

    Private Sub OnResearchDbManagerClick(sender As Object, e As EventArgs)
        Using dlg As New ResearchDbManagerDialog()
            dlg.ShowDialog(Me)
        End Using
        AppLogger.I.Info("Research DB manager dialog opened.", "Shell")
    End Sub

    Private Sub EnsureKosdaq150Menu()
        If _mnuSrcKosdaq150 IsNot Nothing OrElse mnuDataSource Is Nothing Then Return

        _mnuSrcKosdaq150 = New ToolStripMenuItem("KOSDAQ150...")
        AddHandler _mnuSrcKosdaq150.Click, AddressOf OnKosdaq150Click
        mnuDataSource.DropDownItems.Add(_mnuSrcKosdaq150)
    End Sub

    Private Sub OnKosdaq150Click(sender As Object, e As EventArgs)
        ShowDockForm(Of StockInfoForm)(DockState.DockLeft)

        Using dlg As New Kosdaq150SelectionDialog()
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return

            Dim codes = dlg.SelectedCodes
            If codes Is Nothing OrElse codes.Length = 0 Then
                AppLogger.I.Warn("KOSDAQ150 후보 종목이 없습니다.", "DataSource")
                Return
            End If

            StockInfoManager.I.AddStocks(codes, DataSourceType.수동추가, dlg.SourceDetail)
            AppLogger.I.Info($"KOSDAQ150 종목 추가: {codes.Length}종목 / {dlg.SourceDetail}", "DataSource")
        End Using
    End Sub

    Private Sub OnChartOpen(m As Msg)
        Dim code = m.Str("code")
        If String.IsNullOrEmpty(code) Then Return

        SafeUI(Sub()
                   ShowDocumentForm(Of ChartForm)($"Chart_{code}", Sub(f) f.SetStock(code))
               End Sub)
    End Sub

    Private Sub mnuAutoTradeToggle_CheckedChanged(sender As Object, e As EventArgs) Handles mnuAutoTradeToggle.CheckedChanged
        Dim enabled = mnuAutoTradeToggle.Checked
        MessageBus.I.Emit(Topics.SYS_AUTOTRADE, "enabled", enabled)
        AppLogger.I.Info($"자동매매 {If(enabled, "ON", "OFF")}", "Shell")
    End Sub

    ' ════════════════════════════════════════
    ' TradeManager 가혹 테스트 메뉴
    ' ════════════════════════════════════════

    Private Sub RunTestAsync(testAction As Action(Of TradeManagerTest))
        Threading.ThreadPool.QueueUserWorkItem(Sub()
                                                   Try
                                                       Dim tester As New TradeManagerTest()
                                                       testAction(tester)
                                                   Catch ex As Exception
                                                       AppLogger.I.Error($"테스트 예외: {ex.Message}", "TMTest")
                                                   End Try
                                               End Sub)
    End Sub

    Private Sub mnuTestAll_Click(sender As Object, e As EventArgs) Handles mnuTestAll.Click
        RunTestAsync(Sub(t) t.RunAllTests())
    End Sub

    Private Sub mnuTestSync_Click(sender As Object, e As EventArgs) Handles mnuTestSync.Click
        RunTestAsync(Sub(t)
                         t.Test01_Initialization()
                         t.Test02_SyncSimulation()
                     End Sub)
    End Sub

    Private Sub mnuTestOrder_Click(sender As Object, e As EventArgs) Handles mnuTestOrder.Click
        RunTestAsync(Sub(t)
                         t.Test02_SyncSimulation()
                         t.Test03_OrderValidation()
                         t.Test04_OrderAndFill()
                     End Sub)
    End Sub

    Private Sub mnuTestPartialFill_Click(sender As Object, e As EventArgs) Handles mnuTestPartialFill.Click
        RunTestAsync(Sub(t)
                         t.Test02_SyncSimulation()
                         t.Test05_PartialFill()
                     End Sub)
    End Sub

    Private Sub mnuTestBalance_Click(sender As Object, e As EventArgs) Handles mnuTestBalance.Click
        RunTestAsync(Sub(t)
                         t.Test02_SyncSimulation()
                         t.Test06_BalanceChange()
                     End Sub)
    End Sub

    Private Sub mnuTestMulti_Click(sender As Object, e As EventArgs) Handles mnuTestMulti.Click
        RunTestAsync(Sub(t)
                         t.Test02_SyncSimulation()
                         t.Test07_MultiStockSimultaneous()
                     End Sub)
    End Sub

    Private Sub mnuTestStopLoss_Click(sender As Object, e As EventArgs) Handles mnuTestStopLoss.Click
        RunTestAsync(Sub(t)
                         t.Test02_SyncSimulation()
                         t.Test08_StopLossTakeProfit()
                     End Sub)
    End Sub

    Private Sub mnuTestDuplicate_Click(sender As Object, e As EventArgs) Handles mnuTestDuplicate.Click
        RunTestAsync(Sub(t)
                         t.Test02_SyncSimulation()
                         t.Test09_DuplicateOrderBlock()
                     End Sub)
    End Sub

    Private Sub mnuTestExternal_Click(sender As Object, e As EventArgs) Handles mnuTestExternal.Click
        RunTestAsync(Sub(t)
                         t.Test02_SyncSimulation()
                         t.Test10_ExternalOrderTracking()
                     End Sub)
    End Sub

    Private Sub mnuLogin_Click(sender As Object, e As EventArgs) Handles mnuLogin.Click
        AppLogger.I.Info("키움 로그인 요청...", "Shell")
        MessageBus.I.Emit(Topics.AUTH_LOGIN_REQUEST)
    End Sub

    Private Sub mnuServerStatus_Click(sender As Object, e As EventArgs) Handles mnuServerStatus.Click
        AppLogger.I.Info("서버 상태 조회 요청...", "Shell")
        MessageBus.I.Emit(Topics.AUTH_STATUS_REQUEST)
    End Sub

    Private Sub mnuShowLog_Click(sender As Object, e As EventArgs) Handles mnuShowLog.Click
        ShowDockForm(Of LogForm)(DockState.DockBottom)
    End Sub

    ' ── 창 메뉴: 나머지 서브폼들 (구현 시 연결) ──

    Private Sub mnuShowCondition_Click(sender As Object, e As EventArgs) Handles mnuShowCondition.Click
        ' TODO: ShowDockForm(Of ConditionForm)(DockState.DockLeft)
        AppLogger.I.Warn("ConditionForm 미구현", "Shell")
    End Sub

    Private Sub mnuShowStockList_Click(sender As Object, e As EventArgs) Handles mnuShowStockList.Click
        ' TODO: ShowDockForm(Of StockListForm)(DockState.DockLeft)
        AppLogger.I.Warn("StockListForm 미구현", "Shell")
    End Sub

    Private Sub mnuShowBalance_Click(sender As Object, e As EventArgs) Handles mnuShowBalance.Click
        ShowTradeMonitor()
    End Sub

    Private Sub mnuShowOrderLog_Click(sender As Object, e As EventArgs) Handles mnuShowOrderLog.Click
        ShowTradeMonitor()
    End Sub

    Private Sub mnuShowOpenOrders_Click(sender As Object, e As EventArgs) Handles mnuShowOpenOrders.Click
        ShowTradeMonitor()
    End Sub

    Private Sub ShowTradeMonitor()
        ShowDockForm(Of TradeMonitorForm)(DockState.DockBottom)
    End Sub

    ' ════════════════════════════════════════
    ' 상태바 업데이트
    ' ════════════════════════════════════════

    Private Sub OnServerStatus(m As Msg)
        SafeUI(Sub()
                   If m.Has("kiwoom") Then
                       Dim connected = m.Bool("kiwoom")
                       lblKiwoomStatus.Text = $"키움: {If(connected, "연결됨", "끊김")}"
                       lblKiwoomStatus.ForeColor = If(connected, Drawing.Color.LimeGreen, Drawing.Color.Red)
                   End If
                   If m.Has("cybos") Then
                       Dim connected = m.Bool("cybos")
                       lblCybosStatus.Text = $"사이보스: {If(connected, "연결됨", "끊김")}"
                       lblCybosStatus.ForeColor = If(connected, Drawing.Color.LimeGreen, Drawing.Color.Red)
                   End If
               End Sub)
    End Sub

    Private Sub OnAutoTradeStatus(m As Msg)
        SafeUI(Sub()
                   Dim enabled = m.Bool("enabled")
                   lblAutoTrade.Text = $"자동매매: {If(enabled, "ON", "OFF")}"
                   lblAutoTrade.ForeColor = If(enabled, Drawing.Color.LimeGreen, Drawing.Color.Gray)
                   mnuAutoTradeToggle.Checked = enabled
               End Sub)
    End Sub

    Private Sub _clockTimer_Tick(sender As Object, e As EventArgs) Handles _clockTimer.Tick
        lblTime.Text = DateTime.Now.ToString("HH:mm:ss")
    End Sub

    ' ════════════════════════════════════════
    ' Bus 이벤트 → 로그 연동 (로그인 결과 등)
    ' ════════════════════════════════════════

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)

        ' 로그인 결과 수신
        MessageBus.I.On(Topics.AUTH_LOGIN_RESULT, Sub(m)
                                                      If m.Bool("success") Then
                                                          AppLogger.I.Info($"키움 로그인 성공. 계좌: {m.Str("accountNo")}", "Auth")
                                                          SafeUI(Sub()
                                                                     lblKiwoomStatus.Text = "키움: 연결됨"
                                                                     lblKiwoomStatus.ForeColor = Drawing.Color.LimeGreen
                                                                 End Sub)
                                                      Else
                                                          AppLogger.I.Error($"키움 로그인 실패: {m.Str("message")}", "Auth")
                                                      End If
                                                  End Sub)

        ' 캔들 로드 결과
        MessageBus.I.On(Topics.CANDLE_LOADED, Sub(m)
                                                  If m.Has("provider") AndAlso Not RuntimeChartSettings.IsMarketDataProvider(m.Str("provider")) Then Return
                                                  Dim code = m.Str("code")
                                                  Dim rows = m.DictList("rows")
                                                  Dim cnt = If(rows IsNot Nothing, rows.Count, 0)
                                                  Dim tf = m.Str("timeframe", "")
                                                  Dim provider = m.Str("provider", "")
                                                  If String.IsNullOrWhiteSpace(tf) Then tf = InferRowsTimeframe(rows)
                                                  AppLogger.I.Info($"캔들 수신: {code} [{tf}] → {cnt}건 provider:{provider}", "Data")
                                              End Sub)

        ' 틱캔들 로드 결과 (TickIntensity 동기화용)
        MessageBus.I.On(Topics.TICK_CANDLE_REQUEST, Sub(m)
                                                        Dim code = m.Str("code")
                                                        Dim tickUnit = RuntimeChartSettings.NormalizeTickUnit(m.Int("tickUnit", RuntimeChartSettings.DefaultTickUnit))
                                                        Dim reqCnt = m.Int("count", 0)
                                                        Dim stopTime = m.Str("stopTime", "")
                                                        If String.IsNullOrWhiteSpace(stopTime) Then
                                                            AppLogger.I.Info($"틱캔들 요청: {code} [{RuntimeChartSettings.TickTimeframe(tickUnit)}] 요청:{reqCnt}", "Data")
                                                        Else
                                                            AppLogger.I.Info($"틱캔들 요청: {code} [{RuntimeChartSettings.TickTimeframe(tickUnit)}] stopTime:{stopTime} (count:{reqCnt})", "Data")
                                                        End If
                                                    End Sub)
        MessageBus.I.On(Topics.TICK_CANDLE_LOADED, Sub(m)
                                                       Dim code = m.Str("code")
                                                       Dim rows = m.DictList("rows")
                                                       Dim cnt = If(rows IsNot Nothing, rows.Count, 0)
                                                       Dim tf = m.Str("timeframe", RuntimeChartSettings.TickTimeframe(RuntimeChartSettings.DefaultTickUnit))
                                                       Dim reqCnt = m.Int("requestedCount", 0)
                                                       Dim provider = m.Str("provider", "")
                                                       Dim stopTime = m.Str("stopTime", "")
                                                       If String.IsNullOrWhiteSpace(stopTime) Then
                                                           AppLogger.I.Info($"틱캔들 수신: {code} [{tf}] → {cnt}건 (요청:{reqCnt}) provider:{provider}", "Data")
                                                       Else
                                                           AppLogger.I.Info($"틱캔들 수신: {code} [{tf}] → {cnt}건 (stopTime:{stopTime}) provider:{provider}", "Data")
                                                       End If
                                                   End Sub)

        MessageBus.I.On(Topics.PROGRAM_TRADE_REQUEST, Sub(m)
                                                          Dim code = m.Str("code")
                                                          Dim reqCnt = m.Int("count", 0)
                                                          Dim stopTime = m.Str("stopTime", "")
                                                          AppLogger.I.Info($"프로그램순매수 요청: {code} count:{reqCnt} stopTime:{stopTime}", "Data")
                                                      End Sub)

        MessageBus.I.On(Topics.PROGRAM_TRADE_RESULT, Sub(m)
                                                         Dim code = m.Str("code")
                                                         Dim rows = m.DictList("rows")
                                                         Dim cnt = If(rows IsNot Nothing, rows.Count, 0)
                                                         Dim provider = m.Str("provider", "")
                                                         AppLogger.I.Info($"프로그램순매수 수신: {code} → {cnt}건 provider:{provider}", "Data")
                                                     End Sub)


        ' 주문 체결
        MessageBus.I.On(Topics.ORDER_EXECUTED, Sub(m)
                                                   AppLogger.I.Trade($"체결: {m.Str("종목명")} {m.Str("주문구분")} {m.Str("체결량")}주 @{m.Str("체결가")}", "Order")
                                               End Sub)

        ' 조건검색 실시간 편입/이탈
        MessageBus.I.On(Topics.CONDITION_HIT, Sub(m)
                                                  Dim hitType = m.Str("type")
                                                  Dim code = m.Str("code")
                                                  Dim condName = m.Str("condName", "")
                                                  AppLogger.I.Info($"조건편입: [{condName}] {code} ({hitType})", "Condition")

                                                  ' 편입(I) → StockInfoManager에 추가 (정보→캔들→실시간 파이프라인)
                                                  If hitType = "I" AndAlso Not String.IsNullOrEmpty(code) Then
                                                      StockInfoManager.I.AddStocks({code}, DataSourceType.조건검색, condName)
                                                  End If
                                              End Sub)
    End Sub

    ' ════════════════════════════════════════
    ' 유틸
    ' ════════════════════════════════════════

    Private Sub SafeUI(action As Action)
        If Me.InvokeRequired Then
            Try
                Me.BeginInvoke(action)
            Catch
            End Try
        Else
            action()
        End If
    End Sub

    Private Shared Function InferRowsTimeframe(rows As List(Of Dictionary(Of String, String))) As String
        If rows Is Nothing OrElse rows.Count < 2 Then Return "n/a"
        Dim d0 = ParseRowDateTime(rows(0))
        Dim d1 = ParseRowDateTime(rows(1))
        If d0 = DateTime.MinValue OrElse d1 = DateTime.MinValue Then Return "n/a"

        Dim diff = Math.Abs((d1 - d0).TotalMinutes)
        If diff >= 1440 Then Return "d1"
        If diff < 0.5 Then Return "m1"
        Return $"m{Math.Max(1, CInt(Math.Round(diff)))}"
    End Function

    Private Shared Function ParseRowDateTime(row As Dictionary(Of String, String)) As DateTime
        If row Is Nothing Then Return DateTime.MinValue
        Dim d = ""
        Dim t = ""
        If row.ContainsKey("date") Then d = row("date")
        If row.ContainsKey("time") Then t = row("time")
        If d = "" AndAlso row.ContainsKey("dt") Then Return SharedUtil.ToDateTime(row("dt"))
        If d = "" Then Return DateTime.MinValue
        Dim baseDt = SharedUtil.ToDateTime(d)
        If baseDt = DateTime.MinValue Then Return DateTime.MinValue
        If String.IsNullOrWhiteSpace(t) Then Return baseDt
        Dim digits = New String(t.Where(Function(ch) Char.IsDigit(ch)).ToArray())
        If digits.Length > 0 Then digits = digits.PadLeft(6, "0"c)
        If digits.Length < 6 Then Return baseDt
        Dim hh As Integer
        Dim mm As Integer
        Dim ss As Integer
        If Not Integer.TryParse(digits.Substring(0, 2), hh) Then Return baseDt
        If Not Integer.TryParse(digits.Substring(2, 2), mm) Then Return baseDt
        If Not Integer.TryParse(digits.Substring(4, 2), ss) Then Return baseDt
        hh = Math.Max(0, Math.Min(23, hh))
        mm = Math.Max(0, Math.Min(59, mm))
        ss = Math.Max(0, Math.Min(59, ss))
        Return New DateTime(baseDt.Year, baseDt.Month, baseDt.Day, hh, mm, ss)
    End Function

    ' ════════════════════════════════════════
    ' ZeroLoss 전략 메뉴
    ' ════════════════════════════════════════

    Private Sub EnsureZeroLossMenu()
        If _mnuZeroLoss IsNot Nothing OrElse mnuTradeTest Is Nothing Then Return

        mnuTradeTest.DropDownItems.Add(New ToolStripSeparator())

        _mnuZeroLoss = New ToolStripMenuItem("Zero Loss 전략 시작...")
        AddHandler _mnuZeroLoss.Click, AddressOf OnZeroLossClick
        mnuTradeTest.DropDownItems.Add(_mnuZeroLoss)

        Dim mnuBatch As New ToolStripMenuItem("ZeroLoss 배치 분석...")
        AddHandler mnuBatch.Click, AddressOf OnZeroLossBatchClick
        mnuTradeTest.DropDownItems.Add(mnuBatch)

        Dim mnuExperiment As New ToolStripMenuItem("ZeroLoss 파라미터 실험...")
        AddHandler mnuExperiment.Click, Sub(s, ev) ShowDockForm(Of ZeroLossExperimentForm)(WeifenLuo.WinFormsUI.Docking.DockState.Document)
        mnuTradeTest.DropDownItems.Add(mnuExperiment)
    End Sub

    Private Sub OnZeroLossClick(sender As Object, e As EventArgs)
        Dim strategy = ZeroLossLiveStrategy.I

        If strategy.IsRunning Then
            If MessageBox.Show("Zero Loss 전략을 중지하시겠습니까?",
                               "ZeroLoss", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                strategy.Stop()
                _mnuZeroLoss.Text = "Zero Loss 전략 시작..."
                AppLogger.I.Info("ZeroLoss 전략 중지됨", "Shell")
            End If
            Return
        End If

        ' ── KOSDAQ150 선택 다이얼로그 ──
        ShowDockForm(Of StockInfoForm)(DockState.DockLeft)

        Using dlg As New Kosdaq150SelectionDialog()
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return

            Dim codes = dlg.SelectedCodes
            If codes Is Nothing OrElse codes.Length = 0 Then
                AppLogger.I.Warn("KOSDAQ150 후보 종목이 없습니다.", "ZeroLoss")
                Return
            End If

            ' ── StockInfoManager 파이프라인: 종목정보 → 캔들 → 실시간 구독 ──
            StockInfoManager.I.AddStocks(codes, DataSourceType.수동추가, $"ZeroLoss {dlg.SourceDetail}")
            AppLogger.I.Info($"ZeroLoss: KOSDAQ150 {codes.Length}종목 로드 시작", "ZeroLoss")

            ' ── 유니버스 설정 + 전략 시작 ──
            strategy.SetUniverse(codes)
            strategy.Start()

            _mnuZeroLoss.Text = $"Zero Loss 전략 중지 ({codes.Length}종목)"
            AppLogger.I.Info($"ZeroLoss 전략 시작: {codes.Length}종목 모니터링 중", "ZeroLoss")
        End Using
    End Sub

    Private Sub OnZeroLossBatchClick(sender As Object, e As EventArgs)
        Dim fromDate = New DateTime(2025, 12, 1)
        Dim toDate = DateTime.Today

        AppLogger.I.Info($"ZeroLoss 배치 분석 시작: {fromDate:yyyy-MM-dd} ~ {toDate:yyyy-MM-dd}", "Batch")
        Me.Cursor = Cursors.WaitCursor

        Threading.Tasks.Task.Run(Of String)(
            Function() As String
                Try
                    Dim analyzer As New Services.ZeroLossBatchAnalyzer()
                    Return analyzer.RunBatchAnalysis(fromDate, toDate)
                Catch ex As Exception
                    Return $"ERROR: {ex.Message}{Environment.NewLine}{ex.StackTrace}"
                End Try
            End Function).ContinueWith(
            Sub(t As Threading.Tasks.Task(Of String))
                SafeUI(Sub()
                           Me.Cursor = Cursors.Default
                           Dim report = t.Result

                           ' 리포트를 파일로 저장
                           Dim reportPath = IO.Path.Combine(Application.StartupPath, $"ZeroLoss_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt")
                           IO.File.WriteAllText(reportPath, report, System.Text.Encoding.UTF8)

                           ' 리포트를 시스템로그에 출력
                           AppLogger.I.Info($"ZeroLoss 배치 분석 완료 → {reportPath}", "Batch")

                           ' 리포트 파일 열기
                           Try
                               Process.Start("notepad.exe", reportPath)
                           Catch
                           End Try
                       End Sub)
            End Sub)
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _clockTimer?.Stop()

        ' ZeroLoss 전략 정리
        If ZeroLossLiveStrategy.I.IsRunning Then
            ZeroLossLiveStrategy.I.Stop()
        End If

        ' 모든 도킹 폼 정리
        For Each kv In _dockForms
            Try : kv.Value.Dispose() : Catch : End Try
        Next

        AppLogger.I.Info("시스템 종료", "MainShell")
        MyBase.OnFormClosing(e)
    End Sub

End Class
