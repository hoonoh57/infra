' ═══════════════════════════════════════════════════════════════
' MainShell.Designer.vb
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports WeifenLuo.WinFormsUI.Docking

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class MainShell
    Inherits DockContent 'System.Windows.Forms.Form

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.components = New System.ComponentModel.Container()

        ' ── DockPanel ──
        Me.dockPanel = New WeifenLuo.WinFormsUI.Docking.DockPanel()
        Me.dockPanel.Dock = System.Windows.Forms.DockStyle.Fill
        Me.dockPanel.DocumentStyle = WeifenLuo.WinFormsUI.Docking.DocumentStyle.DockingWindow
        Me.dockPanel.Theme = New WeifenLuo.WinFormsUI.Docking.VS2015DarkTheme()
        Me.dockPanel.ShowDocumentIcon = True

        ' ── 메뉴바 ──
        Me.menuStrip = New System.Windows.Forms.MenuStrip()

        ' 파일
        Me.mnuFile = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuFile.Text = "파일(&F)"

        Me.mnuExit = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuExit.Text = "종료(&X)"

        Me.mnuFile.DropDownItems.Add(Me.mnuExit)

        ' 차트
        Me.mnuChart = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuChart.Text = "차트(&C)"

        Me.mnuNewChart = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuNewChart.Text = "새 차트(&N)"
        Me.mnuNewChart.ShortcutKeys = Keys.Control Or Keys.N

        Me.mnuChart.DropDownItems.Add(Me.mnuNewChart)

        Me.mnuDataSource = New ToolStripMenuItem()
        Me.mnuDataSource.Text = "데이터소스(&S)"

        Me.mnuSrcCondition = New ToolStripMenuItem()
        Me.mnuSrcCondition.Text = "조건검색..."

        Me.mnuSrcSector = New ToolStripMenuItem()
        Me.mnuSrcSector.Text = "주도섹터..."

        Me.mnuSrcProgramBuy = New ToolStripMenuItem()
        Me.mnuSrcProgramBuy.Text = "프로그램순매수 상위..."

        Me.mnuSrcFavorite = New ToolStripMenuItem()
        Me.mnuSrcFavorite.Text = "관심종목..."

        Me.mnuSrcKospiFollow = New ToolStripMenuItem()
        Me.mnuSrcKospiFollow.Text = "코스피 추종종목..."

        Me.mnuSrcKosdaqFollow = New ToolStripMenuItem()
        Me.mnuSrcKosdaqFollow.Text = "코스닥 추종종목..."

        Me.mnuDataSource.DropDownItems.AddRange({
            Me.mnuSrcCondition, Me.mnuSrcSector, Me.mnuSrcProgramBuy,
            New ToolStripSeparator(),
            Me.mnuSrcFavorite, Me.mnuSrcKospiFollow, Me.mnuSrcKosdaqFollow
        })

        ' 매매
        Me.mnuTrade = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuTrade.Text = "매매(&T)"

        Me.mnuAutoTradeToggle = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuAutoTradeToggle.Text = "자동매매 ON/OFF"
        Me.mnuAutoTradeToggle.CheckOnClick = True

        ' 모의매매
        Me.mnuSimTrade = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuSimTrade.Text = "모의매매(&S)"


        ' ── 가혹 테스트 하위 메뉴 ──
        Me.mnuTradeTest = New ToolStripMenuItem()
        Me.mnuTradeTest.Text = "★ 가혹 테스트"

        Me.mnuTestAll = New ToolStripMenuItem()
        Me.mnuTestAll.Text = "▶ 전체 일괄 테스트 (10종)"

        Me.mnuTestSync = New ToolStripMenuItem()
        Me.mnuTestSync.Text = "T01-02: 초기화 + 동기화"

        Me.mnuTestOrder = New ToolStripMenuItem()
        Me.mnuTestOrder.Text = "T03-04: 주문검증 + 체결"

        Me.mnuTestPartialFill = New ToolStripMenuItem()
        Me.mnuTestPartialFill.Text = "T05: 부분체결"

        Me.mnuTestBalance = New ToolStripMenuItem()
        Me.mnuTestBalance.Text = "T06: 잔고변경 (청산)"

        Me.mnuTestMulti = New ToolStripMenuItem()
        Me.mnuTestMulti.Text = "T07: 여러종목 동시매매"

        Me.mnuTestStopLoss = New ToolStripMenuItem()
        Me.mnuTestStopLoss.Text = "T08: 손절/익절"

        Me.mnuTestDuplicate = New ToolStripMenuItem()
        Me.mnuTestDuplicate.Text = "T09: 중복주문 차단"

        Me.mnuTestExternal = New ToolStripMenuItem()
        Me.mnuTestExternal.Text = "T10: 외부주문 추적"

        Me.mnuTradeTest.DropDownItems.AddRange({
            Me.mnuTestAll,
            New ToolStripSeparator(),
            Me.mnuTestSync, Me.mnuTestOrder, Me.mnuTestPartialFill,
            Me.mnuTestBalance, Me.mnuTestMulti, Me.mnuTestStopLoss,
            Me.mnuTestDuplicate, Me.mnuTestExternal
        })

        Me.mnuTrade.DropDownItems.AddRange({
            Me.mnuAutoTradeToggle,
            New ToolStripSeparator(),
            Me.mnuTradeTest
        })

        ' 데이터
        Me.mnuData = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuData.Text = "데이터(&D)"

        Me.mnuLogin = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuLogin.Text = "키움 로그인(&L)"

        Me.mnuServerStatus = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuServerStatus.Text = "서버 상태(&S)"

        Me.mnuData.DropDownItems.AddRange({Me.mnuLogin, Me.mnuServerStatus})

        ' 창
        Me.mnuWindow = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuWindow.Text = "창(&W)"

        Me.mnuShowStockInfo = New ToolStripMenuItem()
        Me.mnuShowStockInfo.Text = "종목정보 패널"

        Me.mnuShowLog = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuShowLog.Text = "시스템 로그"

        Me.mnuShowCondition = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuShowCondition.Text = "조건검색"

        Me.mnuShowStockList = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuShowStockList.Text = "종목목록"

        Me.mnuShowBalance = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuShowBalance.Text = "잔고현황"

        Me.mnuShowOrderLog = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuShowOrderLog.Text = "주문로그"

        Me.mnuShowOpenOrders = New System.Windows.Forms.ToolStripMenuItem()
        Me.mnuShowOpenOrders.Text = "미체결"

        Me.mnuWindow.DropDownItems.AddRange({Me.mnuShowStockInfo, New ToolStripSeparator(), Me.mnuShowLog, New ToolStripSeparator(),
                                              Me.mnuShowCondition, Me.mnuShowStockList,
                                              New ToolStripSeparator(),
                                              Me.mnuShowBalance, Me.mnuShowOrderLog, Me.mnuShowOpenOrders})

        Me.menuStrip.Items.AddRange({Me.mnuFile, Me.mnuChart, Me.mnuDataSource, Me.mnuTrade, Me.mnuData, Me.mnuWindow})

        ' ── 상태바 ──
        Me.statusStrip = New System.Windows.Forms.StatusStrip()

        Me.lblKiwoomStatus = New System.Windows.Forms.ToolStripStatusLabel()
        Me.lblKiwoomStatus.Text = "키움: 대기"
        Me.lblKiwoomStatus.BorderSides = ToolStripStatusLabelBorderSides.Right

        Me.lblCybosStatus = New System.Windows.Forms.ToolStripStatusLabel()
        Me.lblCybosStatus.Text = "사이보스: 대기"
        Me.lblCybosStatus.BorderSides = ToolStripStatusLabelBorderSides.Right

        Me.lblAutoTrade = New System.Windows.Forms.ToolStripStatusLabel()
        Me.lblAutoTrade.Text = "자동매매: OFF"
        Me.lblAutoTrade.BorderSides = ToolStripStatusLabelBorderSides.Right

        Me.lblStockCount = New System.Windows.Forms.ToolStripStatusLabel()
        Me.lblStockCount.Text = "종목: 0"
        Me.lblStockCount.BorderSides = ToolStripStatusLabelBorderSides.Right

        Me.lblTime = New System.Windows.Forms.ToolStripStatusLabel()
        Me.lblTime.Text = DateTime.Now.ToString("HH:mm:ss")
        Me.lblTime.Spring = True
        Me.lblTime.TextAlign = ContentAlignment.MiddleRight

        Me.statusStrip.Items.AddRange({Me.lblKiwoomStatus, Me.lblCybosStatus,
                                        Me.lblAutoTrade, Me.lblStockCount, Me.lblTime})

        ' ── Form 설정 ──
        Me.AutoScaleDimensions = New System.Drawing.SizeF(7.0!, 12.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1600, 900)
        Me.MainMenuStrip = Me.menuStrip
        Me.Name = "MainShell"
        Me.Text = "AutoTrading System"
        Me.WindowState = FormWindowState.Maximized

        Me.Controls.Add(Me.dockPanel)
        Me.Controls.Add(Me.statusStrip)
        Me.Controls.Add(Me.menuStrip)

    End Sub

    ' ── 컨트롤 선언 ──
    Friend Shadows WithEvents dockPanel As WeifenLuo.WinFormsUI.Docking.DockPanel
    Friend Shadows WithEvents menuStrip As System.Windows.Forms.MenuStrip
    Friend Shadows WithEvents statusStrip As System.Windows.Forms.StatusStrip

    Friend WithEvents mnuFile As ToolStripMenuItem
    Friend WithEvents mnuExit As ToolStripMenuItem
    Friend WithEvents mnuChart As ToolStripMenuItem
    Friend WithEvents mnuNewChart As ToolStripMenuItem
    Friend WithEvents mnuDataSource As ToolStripMenuItem
    Friend WithEvents mnuSrcCondition As ToolStripMenuItem
    Friend WithEvents mnuSrcSector As ToolStripMenuItem
    Friend WithEvents mnuSrcProgramBuy As ToolStripMenuItem
    Friend WithEvents mnuSrcFavorite As ToolStripMenuItem
    Friend WithEvents mnuSrcKospiFollow As ToolStripMenuItem
    Friend WithEvents mnuSrcKosdaqFollow As ToolStripMenuItem
    Friend WithEvents mnuTrade As ToolStripMenuItem

    Friend WithEvents mnuSimTrade As ToolStripMenuItem   '/////      

    Friend WithEvents mnuAutoTradeToggle As ToolStripMenuItem
    Friend WithEvents mnuTradeTest As ToolStripMenuItem
    Friend WithEvents mnuTestAll As ToolStripMenuItem
    Friend WithEvents mnuTestSync As ToolStripMenuItem
    Friend WithEvents mnuTestOrder As ToolStripMenuItem
    Friend WithEvents mnuTestPartialFill As ToolStripMenuItem
    Friend WithEvents mnuTestBalance As ToolStripMenuItem
    Friend WithEvents mnuTestMulti As ToolStripMenuItem
    Friend WithEvents mnuTestStopLoss As ToolStripMenuItem
    Friend WithEvents mnuTestDuplicate As ToolStripMenuItem
    Friend WithEvents mnuTestExternal As ToolStripMenuItem
    Friend WithEvents mnuData As ToolStripMenuItem
    Friend WithEvents mnuLogin As ToolStripMenuItem
    Friend WithEvents mnuServerStatus As ToolStripMenuItem
    Friend WithEvents mnuWindow As ToolStripMenuItem
    Friend WithEvents mnuShowStockInfo As ToolStripMenuItem
    Friend WithEvents mnuShowLog As ToolStripMenuItem
    Friend WithEvents mnuShowCondition As ToolStripMenuItem
    Friend WithEvents mnuShowStockList As ToolStripMenuItem
    Friend WithEvents mnuShowBalance As ToolStripMenuItem
    Friend WithEvents mnuShowOrderLog As ToolStripMenuItem
    Friend WithEvents mnuShowOpenOrders As ToolStripMenuItem

    Friend WithEvents lblKiwoomStatus As ToolStripStatusLabel
    Friend WithEvents lblCybosStatus As ToolStripStatusLabel
    Friend WithEvents lblAutoTrade As ToolStripStatusLabel
    Friend WithEvents lblStockCount As ToolStripStatusLabel
    Friend WithEvents lblTime As ToolStripStatusLabel

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso components IsNot Nothing Then
            components.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
