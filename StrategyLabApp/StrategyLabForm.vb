Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms
Imports System.Windows.Forms.DataVisualization.Charting
Imports MainApp
Imports StrategyCore.Models
Imports StrategyCore.Services

Namespace StrategyLabApp
    Public Class StrategyLabForm
        Inherits Form

        Private ReadOnly _labFacade As StrategyLabFacade
        Private ReadOnly _packageBuilder As New StrategyPackageBuilder()
        Private ReadOnly _packageValidator As New StrategyPackageValidator()
        Private ReadOnly _manifestBuilder As New PromotionManifestBuilder()
        Private ReadOnly _sessionService As New StrategyLabSessionService()

        Private ReadOnly _txtHistory As New TextBox()
        Private ReadOnly _txtPrompt As New TextBox()
        Private ReadOnly _txtSymbol As New TextBox()
        Private ReadOnly _dtFrom As New DateTimePicker()
        Private ReadOnly _cboMode As New ComboBox()
        Private ReadOnly _numTarget As New NumericUpDown()
        Private ReadOnly _btnEvaluate As New Button()
        Private ReadOnly _btnSaveSession As New Button()
        Private ReadOnly _btnLoadSession As New Button()
        Private ReadOnly _btnSetBaseline As New Button()
        Private ReadOnly _btnSaveCandidate As New Button()
        Private ReadOnly _btnApplySuggestion As New Button()
        Private ReadOnly _btnPinPromotion As New Button()
        Private ReadOnly _btnExportBatchReport As New Button()
        Private ReadOnly _btnExportBatchPdfReport As New Button()
        Private ReadOnly _btnSavePackage As New Button()
        Private ReadOnly _chart As New Chart()
        Private ReadOnly _chartPanel As New Panel()
        Private ReadOnly _panelLabChart As New Panel()
        Private ReadOnly _gridKpi As New DataGridView()
        Private ReadOnly _gridDiagnosis As New DataGridView()
        Private ReadOnly _gridSuggestions As New DataGridView()
        Private ReadOnly _gridTrades As New DataGridView()
        Private ReadOnly _gridComparison As New DataGridView()
        Private ReadOnly _gridCandidates As New DataGridView()
        Private ReadOnly _lstCandidates As New ListBox()
        Private ReadOnly _lblStatus As New Label()
        Private ReadOnly _lblRecommendation As New Label()
        Private ReadOnly _txtRecommendationReason As New TextBox()
        Private ReadOnly _lblPromotionCandidate As New Label()
        Private ReadOnly _lblChartContext As New Label()
        Private ReadOnly _lblIndicatorContext As New Label()
        Private ReadOnly _lblCrosshairInfo As New Label()
        Private _rootSplit As SplitContainer
        Private _rightSplit As SplitContainer
        Private _fastChart As FastChartControl
        Private ReadOnly _embeddedMode As Boolean
        Private Const MinLeftPanelWidth As Integer = 320
        Private Const MinRightPanelWidth As Integer = 520
        Private Const MinTopPanelHeight As Integer = 360
        Private Const MinBottomPanelHeight As Integer = 180

        Private Class BatchReportOutcome
            Public Property Item As StockInfoItem
            Public Property Prompt As String = ""
            Public Property Result As StrategyLabResult
            Public Property ErrorMessage As String = ""
        End Class

        Private Class MacdOverlayData
            Public Property Macd As List(Of Double)
            Public Property Signal As List(Of Double)
            Public Property Histogram As List(Of Double)
        End Class

        Private _lastResult As StrategyLabResult
        Private _baselineResult As StrategyLabResult
        Private ReadOnly _candidateRecords As New List(Of StrategyLabCandidateRecord)()
        Private _activeCandidateId As String = ""
        Private _recommendedCandidateId As String = ""
        Private _promotionCandidateId As String = ""
        Private _candidateCounter As Integer = 0

        Public Sub New(Optional labFacade As StrategyLabFacade = Nothing, Optional embeddedMode As Boolean = False)
            _labFacade = If(labFacade, New StrategyLabFacade())
            _embeddedMode = embeddedMode
            Me.Text = "StrategyLabApp"
            Me.Width = 1600
            Me.Height = 980
            Me.StartPosition = FormStartPosition.CenterScreen
            Me.MinimumSize = If(_embeddedMode, New Size(980, 680), New Size(1280, 820))

            BuildLayout()
            ConfigureChart()
            ConfigureGrids()
            LoadDefaults()

            AddHandler Me.SizeChanged, AddressOf OnHostSizeChanged
        End Sub

        Public Sub RunSmokeTest()
            EvaluatePrompt("m3 macd supertrend 거래량20 기울기 전략", False)
            If _lastResult Is Nothing OrElse _lastResult.Report Is Nothing Then
                Throw New InvalidOperationException("Smoke test failed: evaluation result is missing.")
            End If

            Dim topSuggestion = If(_lastResult.ImprovementPlan?.Suggestions, Nothing)?.FirstOrDefault()
            If topSuggestion Is Nothing Then
                Throw New InvalidOperationException("Smoke test failed: no improvement suggestion was generated.")
            End If
            ApplySuggestionAndEvaluate(topSuggestion)

            OnSaveCandidate(Me, EventArgs.Empty)
            If _candidateRecords.Count = 0 Then
                Throw New InvalidOperationException("Smoke test failed: candidate list is empty.")
            End If

            OnPinPromotionCandidate(Me, EventArgs.Empty)
            If String.IsNullOrWhiteSpace(_promotionCandidateId) Then
                Throw New InvalidOperationException("Smoke test failed: promotion candidate was not pinned.")
            End If

            Dim sessionPath = _sessionService.SaveSession(BuildSession())
            If Not File.Exists(sessionPath) Then
                Throw New InvalidOperationException("Smoke test failed: session file was not created.")
            End If

            Dim loaded = _sessionService.LoadLatestSession()
            If loaded Is Nothing OrElse loaded.LastResult Is Nothing Then
                Throw New InvalidOperationException("Smoke test failed: session file was not loaded.")
            End If
            If loaded.BaselineResult Is Nothing Then
                Throw New InvalidOperationException("Smoke test failed: baseline result was not persisted.")
            End If

            Dim packageFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages")
            Dim pkg = _packageBuilder.BuildPackage(_lastResult.Definition, _lastResult.Report, Environment.UserName)
            Dim errors As List(Of String) = Nothing
            If Not _packageValidator.Validate(pkg, errors) Then
                Throw New InvalidOperationException("Smoke test failed: package validation failed - " & String.Join(", ", errors))
            End If

            Dim packagePath = _packageBuilder.SavePackage(pkg, packageFolder)
            If Not File.Exists(packagePath) Then
                Throw New InvalidOperationException("Smoke test failed: package file was not created.")
            End If
        End Sub

        Private Sub BuildLayout()
            _rootSplit = New SplitContainer With {
                .Dock = DockStyle.Fill,
                .BackColor = Color.FromArgb(30, 30, 36)
            }
            Me.Controls.Add(_rootSplit)

            _rightSplit = New SplitContainer With {
                .Dock = DockStyle.Fill,
                .Orientation = Orientation.Horizontal
            }
            _rootSplit.Panel2.Controls.Add(_rightSplit)

            Dim bottomTabs As New TabControl With {
                .Dock = DockStyle.Fill
            }
            _rightSplit.Panel2.Controls.Add(bottomTabs)

            Dim leftPanel As New TableLayoutPanel With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 1,
                .RowCount = 15,
                .BackColor = Color.FromArgb(37, 39, 46)
            }
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 34))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 132))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 58.0F))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 42.0F))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 44))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 56))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 36))
            leftPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 120))
            _rootSplit.Panel1.Controls.Add(leftPanel)

            Dim headerPanel As New TableLayoutPanel With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 3
            }
            headerPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            headerPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 84.0F))
            headerPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 84.0F))
            leftPanel.Controls.Add(headerPanel, 0, 0)

            _lblStatus.Dock = DockStyle.Fill
            _lblStatus.ForeColor = Color.White
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft
            _lblStatus.Padding = New Padding(10, 0, 0, 0)
            _lblStatus.Text = "Draft | Separate lab workspace"
            headerPanel.Controls.Add(_lblStatus, 0, 0)

            _btnSaveSession.Dock = DockStyle.Fill
            _btnSaveSession.Text = "Save"
            ApplyButtonTheme(_btnSaveSession)
            AddHandler _btnSaveSession.Click, AddressOf OnSaveSession
            headerPanel.Controls.Add(_btnSaveSession, 1, 0)

            _btnLoadSession.Dock = DockStyle.Fill
            _btnLoadSession.Text = "Load"
            ApplyButtonTheme(_btnLoadSession)
            AddHandler _btnLoadSession.Click, AddressOf OnLoadSession
            headerPanel.Controls.Add(_btnLoadSession, 2, 0)

            Dim settings As New TableLayoutPanel With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 2
            }
            settings.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 100))
            settings.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            leftPanel.Controls.Add(settings, 0, 1)

            AddLabeledControl(settings, 0, "Symbol", _txtSymbol)
            AddLabeledControl(settings, 1, "From", _dtFrom)
            AddLabeledControl(settings, 2, "Mode", _cboMode)
            AddLabeledControl(settings, 3, "Target %", _numTarget)

            _txtHistory.Dock = DockStyle.Fill
            _txtHistory.Multiline = True
            _txtHistory.ReadOnly = True
            _txtHistory.ScrollBars = ScrollBars.Vertical
            _txtHistory.BackColor = Color.FromArgb(26, 28, 34)
            _txtHistory.ForeColor = Color.Gainsboro
            leftPanel.Controls.Add(_txtHistory, 0, 2)

            _txtPrompt.Dock = DockStyle.Fill
            _txtPrompt.Multiline = True
            _txtPrompt.BackColor = Color.FromArgb(24, 26, 32)
            _txtPrompt.ForeColor = Color.White
            leftPanel.Controls.Add(_txtPrompt, 0, 3)

            _btnEvaluate.Dock = DockStyle.Fill
            _btnEvaluate.Text = "Evaluate Prompt"
            ApplyButtonTheme(_btnEvaluate, Color.FromArgb(52, 90, 150))
            AddHandler _btnEvaluate.Click, Sub() EvaluatePrompt(_txtPrompt.Text, True)
            leftPanel.Controls.Add(_btnEvaluate, 0, 4)

            _btnSetBaseline.Dock = DockStyle.Fill
            _btnSetBaseline.Text = "Save Base Version"
            ApplyButtonTheme(_btnSetBaseline)
            AddHandler _btnSetBaseline.Click, AddressOf OnSetBaseline
            leftPanel.Controls.Add(_btnSetBaseline, 0, 5)

            _btnSaveCandidate.Dock = DockStyle.Fill
            _btnSaveCandidate.Text = "Save Derived Version"
            ApplyButtonTheme(_btnSaveCandidate)
            AddHandler _btnSaveCandidate.Click, AddressOf OnSaveCandidate
            leftPanel.Controls.Add(_btnSaveCandidate, 0, 6)

            _btnApplySuggestion.Dock = DockStyle.Fill
            _btnApplySuggestion.Text = "Apply Top Suggestion"
            ApplyButtonTheme(_btnApplySuggestion, Color.FromArgb(84, 92, 46))
            AddHandler _btnApplySuggestion.Click, AddressOf OnApplyTopSuggestion
            leftPanel.Controls.Add(_btnApplySuggestion, 0, 7)

            _btnPinPromotion.Dock = DockStyle.Fill
            _btnPinPromotion.Text = "Pin Promotion Candidate"
            ApplyButtonTheme(_btnPinPromotion, Color.FromArgb(48, 104, 62))
            AddHandler _btnPinPromotion.Click, AddressOf OnPinPromotionCandidate
            leftPanel.Controls.Add(_btnPinPromotion, 0, 8)

            _btnExportBatchReport.Dock = DockStyle.Fill
            _btnExportBatchReport.Text = "Export Batch Report"
            ApplyButtonTheme(_btnExportBatchReport, Color.FromArgb(72, 70, 110))
            AddHandler _btnExportBatchReport.Click, AddressOf OnExportBatchReport
            leftPanel.Controls.Add(_btnExportBatchReport, 0, 9)

            _btnExportBatchPdfReport.Dock = DockStyle.Fill
            _btnExportBatchPdfReport.Text = "Export Batch PDF Report"
            ApplyButtonTheme(_btnExportBatchPdfReport, Color.FromArgb(92, 66, 110))
            AddHandler _btnExportBatchPdfReport.Click, AddressOf OnExportBatchPdfReport
            leftPanel.Controls.Add(_btnExportBatchPdfReport, 0, 10)

            _lblRecommendation.Dock = DockStyle.Fill
            _lblRecommendation.ForeColor = Color.Gold
            _lblRecommendation.BackColor = Color.FromArgb(30, 33, 40)
            _lblRecommendation.Padding = New Padding(8, 6, 8, 6)
            _lblRecommendation.Text = "Recommended: none"
            leftPanel.Controls.Add(_lblRecommendation, 0, 11)

            _txtRecommendationReason.Dock = DockStyle.Fill
            _txtRecommendationReason.Multiline = True
            _txtRecommendationReason.ReadOnly = True
            _txtRecommendationReason.BackColor = Color.FromArgb(24, 26, 32)
            _txtRecommendationReason.ForeColor = Color.Gainsboro
            _txtRecommendationReason.Text = "Recommendation reason will appear here."
            leftPanel.Controls.Add(_txtRecommendationReason, 0, 12)

            _lblPromotionCandidate.Dock = DockStyle.Fill
            _lblPromotionCandidate.ForeColor = Color.LightGreen
            _lblPromotionCandidate.BackColor = Color.FromArgb(30, 33, 40)
            _lblPromotionCandidate.Padding = New Padding(8, 6, 8, 6)
            _lblPromotionCandidate.Text = "Promotion candidate: none"
            leftPanel.Controls.Add(_lblPromotionCandidate, 0, 13)

            _lstCandidates.Dock = DockStyle.Fill
            _lstCandidates.BackColor = Color.FromArgb(26, 28, 34)
            _lstCandidates.ForeColor = Color.Gainsboro
            AddHandler _lstCandidates.SelectedIndexChanged, AddressOf OnCandidateSelected
            leftPanel.Controls.Add(_lstCandidates, 0, 14)

            _btnSavePackage.Text = "Save Package"
            ApplyButtonTheme(_btnSavePackage, Color.FromArgb(90, 64, 128))

            _chartPanel.Dock = DockStyle.Fill
            _chartPanel.BackColor = Color.FromArgb(24, 26, 32)
            _rightSplit.Panel1.Controls.Add(_chartPanel)

            Dim chartLayout As New TableLayoutPanel With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 1,
                .RowCount = 4,
                .BackColor = Color.FromArgb(24, 26, 32)
            }
            chartLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
            chartLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24))
            chartLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            chartLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24))
            _chartPanel.Controls.Add(chartLayout)

            _lblChartContext.Dock = DockStyle.Fill
            _lblChartContext.Padding = New Padding(10, 6, 10, 0)
            _lblChartContext.BackColor = Color.FromArgb(30, 33, 40)
            _lblChartContext.ForeColor = Color.Gainsboro
            _lblChartContext.Text = "Chart | no active version"
            chartLayout.Controls.Add(_lblChartContext, 0, 0)

            _lblIndicatorContext.Dock = DockStyle.Fill
            _lblIndicatorContext.Padding = New Padding(10, 4, 10, 0)
            _lblIndicatorContext.BackColor = Color.FromArgb(36, 39, 46)
            _lblIndicatorContext.ForeColor = Color.Silver
            _lblIndicatorContext.Text = "Indicators | none"
            chartLayout.Controls.Add(_lblIndicatorContext, 0, 1)

            _panelLabChart.Dock = DockStyle.Fill
            _panelLabChart.Margin = New Padding(0)
            _panelLabChart.Padding = New Padding(0)
            _panelLabChart.BackColor = Color.FromArgb(24, 26, 32)
            chartLayout.Controls.Add(_panelLabChart, 0, 2)

            _lblCrosshairInfo.Dock = DockStyle.Fill
            _lblCrosshairInfo.Padding = New Padding(10, 4, 10, 0)
            _lblCrosshairInfo.BackColor = Color.FromArgb(30, 33, 40)
            _lblCrosshairInfo.ForeColor = Color.Silver
            _lblCrosshairInfo.Text = "Crosshair | built into chart"
            chartLayout.Controls.Add(_lblCrosshairInfo, 0, 3)

            ResetFastChart()

            AddGridTab(bottomTabs, "KPI", _gridKpi)
            AddGridTab(bottomTabs, "Compare", _gridComparison)
            AddGridTab(bottomTabs, "Versions", _gridCandidates)
            AddGridTab(bottomTabs, "Diagnosis", _gridDiagnosis)
            AddGridTab(bottomTabs, "Suggestions", _gridSuggestions)
            AddGridTab(bottomTabs, "Trades", _gridTrades)

            ApplyResponsiveLayout()
        End Sub

        Private Sub OnHostSizeChanged(sender As Object, e As EventArgs)
            ApplyResponsiveLayout()
        End Sub

        Private Sub ApplyResponsiveLayout()
            If _rootSplit Is Nothing OrElse _rightSplit Is Nothing Then Return
            If Me.ClientSize.Width <= 0 OrElse Me.ClientSize.Height <= 0 Then Return

            Dim availableRootWidth = Math.Max(0, _rootSplit.Width - _rootSplit.SplitterWidth)
            Dim safeLeftMin = Math.Min(MinLeftPanelWidth, Math.Max(0, availableRootWidth \ 2))
            Dim safeRightMin = Math.Min(MinRightPanelWidth, Math.Max(0, availableRootWidth - safeLeftMin))
            _rootSplit.Panel1MinSize = safeLeftMin
            _rootSplit.Panel2MinSize = safeRightMin

            If availableRootWidth > safeLeftMin + safeRightMin Then
                Dim preferredLeft = CInt(Math.Round(availableRootWidth * 0.26R))
                preferredLeft = Math.Max(safeLeftMin, preferredLeft)
                preferredLeft = Math.Min(preferredLeft, availableRootWidth - safeRightMin)
                _rootSplit.SplitterDistance = preferredLeft
            End If

            Dim availableRightHeight = Math.Max(0, _rightSplit.Height - _rightSplit.SplitterWidth)
            Dim safeTopMin = Math.Min(MinTopPanelHeight, Math.Max(0, availableRightHeight \ 2))
            Dim safeBottomMin = Math.Min(MinBottomPanelHeight, Math.Max(0, availableRightHeight - safeTopMin))
            _rightSplit.Panel1MinSize = safeTopMin
            _rightSplit.Panel2MinSize = safeBottomMin

            If availableRightHeight > safeTopMin + safeBottomMin Then
                Dim preferredTop = CInt(Math.Round(availableRightHeight * 0.64R))
                preferredTop = Math.Max(safeTopMin, preferredTop)
                preferredTop = Math.Min(preferredTop, availableRightHeight - safeBottomMin)
                _rightSplit.SplitterDistance = preferredTop
            End If
        End Sub

        Private Shared Sub ApplyButtonTheme(button As Button, Optional backColor As Color = Nothing)
            If button Is Nothing Then Return

            button.FlatStyle = FlatStyle.Flat
            button.FlatAppearance.BorderColor = Color.Gainsboro
            button.FlatAppearance.BorderSize = 1
            button.BackColor = If(backColor = Nothing, Color.FromArgb(42, 45, 54), backColor)
            button.ForeColor = Color.White
            button.UseVisualStyleBackColor = False
        End Sub

        Private Shared Sub AddLabeledControl(host As TableLayoutPanel, rowIndex As Integer, labelText As String, ctl As Control)
            host.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
            Dim lbl As New Label With {
                .Text = labelText,
                .Dock = DockStyle.Fill,
                .ForeColor = Color.Gainsboro,
                .TextAlign = ContentAlignment.MiddleLeft,
                .Padding = New Padding(6, 0, 0, 0)
            }
            ctl.Dock = DockStyle.Fill
            host.Controls.Add(lbl, 0, rowIndex)
            host.Controls.Add(ctl, 1, rowIndex)
        End Sub

        Private Sub ConfigureChart()
            ResetFastChart()
        End Sub

        Private Sub ResetFastChart()
            If _fastChart IsNot Nothing Then
                _panelLabChart.Controls.Remove(_fastChart)
                _fastChart.Dispose()
            End If

            _fastChart = New FastChartControl() With {
                .Dock = DockStyle.Fill
            }
            _panelLabChart.Controls.Add(_fastChart)
        End Sub

        Private Shared Function CreateChartArea(name As String,
                                                x As Single,
                                                y As Single,
                                                width As Single,
                                                height As Single,
                                                showXAxisLabels As Boolean) As ChartArea
            Dim area As New ChartArea(name)
            area.BackColor = Color.FromArgb(17, 20, 27)
            area.Position = New ElementPosition(x, y, width, height)
            area.InnerPlotPosition = New ElementPosition(8.0F, 6.0F, 84.0F, 84.0F)
            area.AxisX.LabelStyle.ForeColor = Color.Silver
            area.AxisX.LabelStyle.Format = "MM-dd HH:mm"
            area.AxisX.LineColor = Color.FromArgb(55, 58, 68)
            area.AxisX.MajorGrid.LineColor = Color.FromArgb(35, 38, 46)
            area.AxisX.MajorGrid.Enabled = False
            area.AxisX.IntervalType = DateTimeIntervalType.Hours
            area.AxisX.IsMarginVisible = True
            area.AxisX.LabelStyle.Enabled = showXAxisLabels
            area.CursorX.IsUserEnabled = False
            area.CursorX.IsUserSelectionEnabled = False
            area.CursorX.LineColor = Color.FromArgb(180, 220, 220, 220)
            area.CursorX.LineDashStyle = ChartDashStyle.Dash
            area.AxisY.LabelStyle.ForeColor = Color.Silver
            area.AxisY.LineColor = Color.FromArgb(55, 58, 68)
            area.AxisY.MajorGrid.LineColor = Color.FromArgb(35, 38, 46)
            area.CursorY.IsUserEnabled = False
            area.CursorY.IsUserSelectionEnabled = False
            area.CursorY.LineColor = Color.FromArgb(180, 220, 220, 220)
            area.CursorY.LineDashStyle = ChartDashStyle.Dash
            area.AxisX.IntervalAutoMode = IntervalAutoMode.VariableCount
            Return area
        End Function

        Private Sub ConfigureGrids()
            ConfigureGrid(_gridKpi)
            ConfigureGrid(_gridComparison)
            ConfigureGrid(_gridCandidates)
            ConfigureGrid(_gridDiagnosis)
            ConfigureGrid(_gridSuggestions)
            ConfigureGrid(_gridTrades)

            _gridKpi.Columns.Add("Metric", "Metric")
            _gridKpi.Columns.Add("Value", "Value")

            _gridComparison.Columns.Add("Metric", "Metric")
            _gridComparison.Columns.Add("Baseline", "Baseline")
            _gridComparison.Columns.Add("Current", "Current")
            _gridComparison.Columns.Add("Delta", "Delta")

            _gridCandidates.Columns.Add("Version", "Version")
            _gridCandidates.Columns.Add("Type", "Type")
            _gridCandidates.Columns.Add("Parent", "Parent")
            _gridCandidates.Columns.Add("Primary", "Primary")
            _gridCandidates.Columns.Add("Secondary", "Secondary")
            _gridCandidates.Columns.Add("Drawdown", "Drawdown")
            _gridCandidates.Columns.Add("AvgReturn", "Avg Return")
            _gridCandidates.Columns.Add("DeltaVsBase", "Delta vs Base")
            _gridCandidates.Columns.Add("Recommended", "Recommended")
            _gridCandidates.Columns.Add("Promotion", "Promotion")
            _gridCandidates.Columns.Add("Change", "Change")
            _gridCandidates.Columns.Add("Prompt", "Prompt")
            AddHandler _gridCandidates.CellDoubleClick, AddressOf OnCandidateGridCellDoubleClick

            _gridDiagnosis.Columns.Add("Category", "Category")
            _gridDiagnosis.Columns.Add("Severity", "Severity")
            _gridDiagnosis.Columns.Add("Observation", "Observation")
            _gridDiagnosis.Columns.Add("Recommendation", "Recommendation")

            _gridSuggestions.Columns.Add("Priority", "Priority")
            _gridSuggestions.Columns.Add("Title", "Title")
            _gridSuggestions.Columns.Add("Template", "Template")
            _gridSuggestions.Columns.Add("Expected", "Expected")
            _gridSuggestions.Columns.Add("Action", "Action")
            _gridSuggestions.Columns.Add("PromptHint", "Prompt Hint")
            AddHandler _gridSuggestions.CellDoubleClick, AddressOf OnSuggestionCellDoubleClick

            _gridTrades.Columns.Add("Symbol", "Symbol")
            _gridTrades.Columns.Add("EntryTime", "EntryTime")
            _gridTrades.Columns.Add("ExitTime", "ExitTime")
            _gridTrades.Columns.Add("NetReturn", "NetReturn")
            _gridTrades.Columns.Add("TargetHit", "TargetHit")
            _gridTrades.Columns.Add("ExitReason", "ExitReason")
            _gridTrades.Columns.Add("Notes", "Notes")
        End Sub

        Private Shared Sub ConfigureGrid(grid As DataGridView)
            grid.BackgroundColor = Color.FromArgb(22, 24, 30)
            grid.EnableHeadersVisualStyles = False
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(36, 38, 46)
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
            grid.DefaultCellStyle.BackColor = Color.FromArgb(22, 24, 30)
            grid.DefaultCellStyle.ForeColor = Color.Gainsboro
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(55, 76, 112)
            grid.DefaultCellStyle.SelectionForeColor = Color.White
            grid.RowHeadersVisible = False
            grid.AllowUserToAddRows = False
            grid.AllowUserToDeleteRows = False
            grid.ReadOnly = True
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        End Sub

        Private Shared Sub AddGridTab(tabControl As TabControl, title As String, grid As DataGridView)
            Dim page As New TabPage(title) With {
                .BackColor = Color.FromArgb(30, 30, 36)
            }
            grid.Dock = DockStyle.Fill
            page.Controls.Add(grid)
            tabControl.TabPages.Add(page)
        End Sub

        Private Sub LoadDefaults()
            _txtSymbol.Text = "005930"
            _dtFrom.Value = DateTime.Today.AddDays(-1)
            _cboMode.DropDownStyle = ComboBoxStyle.DropDownList
            _cboMode.Items.AddRange(New Object() {TradeMode.Intraday, TradeMode.Swing})
            _cboMode.SelectedIndex = 0
            _txtSymbol.BackColor = Color.White
            _txtSymbol.ForeColor = Color.Black
            _dtFrom.CalendarMonthBackground = Color.White
            _dtFrom.CalendarForeColor = Color.Black
            _dtFrom.CalendarTitleBackColor = Color.FromArgb(52, 90, 150)
            _dtFrom.CalendarTitleForeColor = Color.White
            _cboMode.BackColor = Color.White
            _cboMode.ForeColor = Color.Black
            _numTarget.BackColor = Color.White
            _numTarget.ForeColor = Color.Black
            _numTarget.DecimalPlaces = 2
            _numTarget.Increment = 0.1D
            _numTarget.Minimum = 0.1D
            _numTarget.Maximum = 20D
            _numTarget.Value = 2D
            _txtPrompt.Text = "m3 macd supertrend 거래량20 기울기 기준으로 목표 2% 전략을 평가해줘"
            AppendHistory("System", "StrategyLabApp initialized. Prompt workspace is isolated from MainApp.")
        End Sub

        Private Sub EvaluatePrompt(prompt As String, appendUserPrompt As Boolean)
            If String.IsNullOrWhiteSpace(prompt) Then
                MessageBox.Show(Me, "프롬프트를 입력하세요.", "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            If appendUserPrompt Then
                AppendHistory("User", prompt.Trim())
            End If

            Dim mode = DirectCast(_cboMode.SelectedItem, TradeMode)
            Dim targetRate = CDbl(_numTarget.Value) / 100.0R
            Dim result As StrategyLabResult = Nothing
            Try
                result = _labFacade.EvaluatePrompt(prompt, mode, _txtSymbol.Text.Trim(), _dtFrom.Value.Date, targetRate, 5000, New CostModel())
            Catch ex As Exception
                _lblStatus.Text = "Evaluation failed"
                AppendHistory("System", $"Evaluation failed: {ex.Message}")
                MessageBox.Show(Me, ex.Message, "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End Try

            _lastResult = result
            If _baselineResult Is Nothing Then
                _baselineResult = CloneResult(result)
                AppendHistory("System", $"Baseline initialized from first evaluation: {_baselineResult.Definition.Name}")
            End If

            RenderChart(result.Report)
            RenderKpi(result)
            RenderComparison(result)
            RenderDiagnosis(result)
            RenderSuggestions(result)
            RenderTrades(result.Report)
            RenderCandidateSummary()
            _lblStatus.Text = BuildStatusText(result, "Draft")
            AppendHistory("System", $"Evaluated {result.Definition.Name}. Primary={result.Report.PrimaryMetric:P0}, Secondary={result.Report.SecondaryMetric:P2}. Diagnosis={result.Diagnosis.Summary}")
        End Sub

        Private Sub RenderChart(report As StrategyBaselineReport)
            If report Is Nothing OrElse report.Candles Is Nothing OrElse report.Candles.Count = 0 Then
                UpdateChartContextLabel()
                _lblIndicatorContext.Text = "Indicators | none"
                Return
            End If

            ResetFastChart()
            _fastChart.SetStaticChartContext(_txtSymbol.Text.Trim(), ResolveChartType(_lastResult?.Definition), _txtSymbol.Text.Trim())
            _fastChart.LoadCandles(ConvertCandles(report), ResolvePrevClose(report))
            _fastChart.ShowAllCandles()
            ApplyFastChartIndicators(_lastResult?.Definition)
            _fastChart.SetStrategySignals(BuildChartSignals(report))
            UpdateChartContextLabel()
        End Sub

        Private Shared Function ResolveChartType(definition As StrategyDefinition) As String
            Dim timeframe = definition?.Timeframes?.FirstOrDefault()
            If String.IsNullOrWhiteSpace(timeframe) Then Return "minute"

            Dim normalized = timeframe.Trim().ToLowerInvariant()
            If normalized.StartsWith("m", StringComparison.OrdinalIgnoreCase) OrElse
               normalized.StartsWith("t", StringComparison.OrdinalIgnoreCase) Then
                Return "minute"
            End If
            If normalized = "d" OrElse normalized = "daily" Then Return "daily"
            If normalized = "w" OrElse normalized = "weekly" Then Return "weekly"
            If normalized = "mo" OrElse normalized = "monthly" Then Return "monthly"
            Return "minute"
        End Function

        Private Shared Function ConvertCandles(report As StrategyBaselineReport) As List(Of CandleItem)
            Dim candles As New List(Of CandleItem)()
            If report Is Nothing OrElse report.Candles Is Nothing Then Return candles

            For Each candle In report.Candles
                candles.Add(New CandleItem With {
                    .Dt = candle.Time,
                    .Open = CSng(candle.Open),
                    .High = CSng(candle.High),
                    .Low = CSng(candle.Low),
                    .Close = CSng(candle.Close),
                    .Volume = CLng(Math.Max(0, candle.Volume))
                })
            Next

            Return candles
        End Function

        Private Shared Function ResolvePrevClose(report As StrategyBaselineReport) As Single
            If report Is Nothing OrElse report.Candles Is Nothing OrElse report.Candles.Count = 0 Then Return 0
            If report.Candles.Count = 1 Then Return CSng(report.Candles(0).Close)
            Return CSng(report.Candles(0).Open)
        End Function

        Private Sub ApplyFastChartIndicators(definition As StrategyDefinition)
            If _fastChart Is Nothing OrElse definition Is Nothing OrElse definition.Indicators Is Nothing Then
                _lblIndicatorContext.Text = "Indicators | none"
                Return
            End If

            Dim added As New List(Of String)
            Dim usedNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each indicator In definition.Indicators
                If indicator Is Nothing OrElse String.IsNullOrWhiteSpace(indicator.IndicatorType) Then Continue For
                Dim mapped = MapIndicatorName(indicator.IndicatorType)
                If String.IsNullOrWhiteSpace(mapped) OrElse usedNames.Contains(mapped) Then Continue For
                _fastChart.AddIndicatorByName(mapped)
                usedNames.Add(mapped)
                added.Add(mapped)
            Next

            If added.Count = 0 Then
                _lblIndicatorContext.Text = "Indicators | none"
            Else
                _lblIndicatorContext.Text = $"Indicators | {String.Join(", ", added)}"
            End If
        End Sub

        Private Shared Function MapIndicatorName(indicatorType As String) As String
            Select Case If(indicatorType, "").Trim()
                Case "JMA", "VWAP", "SuperTrend", "MACD", "RSI", "Volume"
                    Return indicatorType.Trim()
                Case Else
                    Return ""
            End Select
        End Function

        Private Sub RenderKpi(result As StrategyLabResult)
            _gridKpi.Rows.Clear()
            _gridKpi.Rows.Add("Strategy", result.Definition.Name)
            _gridKpi.Rows.Add("Baseline", If(_baselineResult IsNot Nothing, _baselineResult.Definition.Name, "-"))
            _gridKpi.Rows.Add("Mode", result.Definition.TradeMode.ToString())
            _gridKpi.Rows.Add("Timeframes", String.Join(", ", result.Definition.Timeframes))
            _gridKpi.Rows.Add("Indicators", String.Join(", ", result.Definition.Indicators.Select(Function(item) item.IndicatorType)))
            _gridKpi.Rows.Add("Trade Count", result.Report.TradeCount.ToString())
            _gridKpi.Rows.Add("Target Hits", $"{result.Report.TargetHitCount}/{Math.Max(0, result.Report.TradeCount)}")
            _gridKpi.Rows.Add("Primary KPI", result.Report.PrimaryMetric.ToString("P2"))
            _gridKpi.Rows.Add("Secondary KPI", result.Report.SecondaryMetric.ToString("P2"))
            _gridKpi.Rows.Add("Avg Return", result.Report.AverageReturnRate.ToString("P2"))
            _gridKpi.Rows.Add("Max Drawdown", result.Report.MaxDrawdownRate.ToString("P2"))
            _gridKpi.Rows.Add("Win Rate", result.Report.WinRate.ToString("P2"))
            _gridKpi.Rows.Add("Strength", result.Report.StrengthSummary)
            _gridKpi.Rows.Add("Weakness", result.Report.WeaknessSummary)
            If Not String.IsNullOrWhiteSpace(result.Report.FailedExampleSummary) Then
                _gridKpi.Rows.Add("Failed Example", result.Report.FailedExampleSummary)
            End If
        End Sub

        Private Sub RenderComparison(result As StrategyLabResult)
            _gridComparison.Rows.Clear()
            If result Is Nothing OrElse _baselineResult Is Nothing Then Return

            AddComparisonRow("Primary KPI", _baselineResult.Report.PrimaryMetric, result.Report.PrimaryMetric, "P2")
            AddComparisonRow("Secondary KPI", _baselineResult.Report.SecondaryMetric, result.Report.SecondaryMetric, "P2")
            AddComparisonRow("Avg Return", _baselineResult.Report.AverageReturnRate, result.Report.AverageReturnRate, "P2")
            AddComparisonRow("Max Drawdown", _baselineResult.Report.MaxDrawdownRate, result.Report.MaxDrawdownRate, "P2")
            AddComparisonRow("Win Rate", _baselineResult.Report.WinRate, result.Report.WinRate, "P2")
        End Sub

        Private Sub AddComparisonRow(metric As String, baselineValue As Double, currentValue As Double, fmt As String)
            _gridComparison.Rows.Add(metric,
                                     baselineValue.ToString(fmt),
                                     currentValue.ToString(fmt),
                                     (currentValue - baselineValue).ToString(fmt))
        End Sub

        Private Sub RenderDiagnosis(result As StrategyLabResult)
            _gridDiagnosis.Rows.Clear()
            If result Is Nothing OrElse result.Diagnosis Is Nothing Then Return

            _gridDiagnosis.Rows.Add("Summary", "", result.Diagnosis.Summary, "")

            For Each strength In result.Diagnosis.Strengths
                _gridDiagnosis.Rows.Add("Strength", "Info", strength, "유지 또는 확대 검증")
            Next

            For Each weakness In result.Diagnosis.Weaknesses
                _gridDiagnosis.Rows.Add("Weakness", "Warn", weakness, "개선안 생성 필요")
            Next

            For Each item In result.Diagnosis.Items
                _gridDiagnosis.Rows.Add(item.Category, item.Severity, item.Observation, item.Recommendation)
            Next
        End Sub

        Private Sub RenderSuggestions(result As StrategyLabResult)
            _gridSuggestions.Rows.Clear()
            If result Is Nothing OrElse result.ImprovementPlan Is Nothing Then Return

            _gridSuggestions.Rows.Add("", "Summary", "", result.ImprovementPlan.Summary, "", "")
            For Each suggestion In result.ImprovementPlan.Suggestions
                Dim rowIndex = _gridSuggestions.Rows.Add(suggestion.Priority,
                                                         suggestion.Title,
                                                         suggestion.TemplateName,
                                                         suggestion.ExpectedEffect,
                                                         suggestion.Action,
                                                         suggestion.PromptHint)
                _gridSuggestions.Rows(rowIndex).Tag = suggestion
            Next
        End Sub

        Private Sub RenderTrades(report As StrategyBaselineReport)
            _gridTrades.Rows.Clear()
            For Each trade In report.Trades
                _gridTrades.Rows.Add(trade.Symbol,
                                     trade.EntryTime.ToString("yyyy-MM-dd HH:mm"),
                                     trade.ExitTime.ToString("yyyy-MM-dd HH:mm"),
                                     trade.NetReturnRate.ToString("P2"),
                                      If(trade.HitTargetProfit, "Y", "N"),
                                     trade.ExitReason,
                                     trade.Notes)
            Next
        End Sub

        Private Function BuildChartSignals(report As StrategyBaselineReport) As IEnumerable(Of StrategySignal)
            Dim signals As New List(Of StrategySignal)()
            If report Is Nothing OrElse report.Trades Is Nothing Then Return signals

            Dim strategyName = If(_lastResult?.Definition?.Name, "StrategyLab")
            For Each trade In report.Trades
                If trade Is Nothing Then Continue For
                signals.Add(New StrategySignal With {
                    .StockCode = report.Symbol,
                    .StrategyName = strategyName,
                    .SignalType = SignalType.Buy,
                    .Price = CSng(trade.EntryPrice),
                    .Reason = String.Join(" + ", trade.EntryReasons),
                    .Timestamp = trade.EntryTime,
                    .Confidence = Math.Min(1.0F, CSng(Math.Max(0.1R, trade.EntryScore / 5.0R)))
                })
                signals.Add(New StrategySignal With {
                    .StockCode = report.Symbol,
                    .StrategyName = strategyName,
                    .SignalType = SignalType.Sell,
                    .Price = CSng(trade.ExitPrice),
                    .Reason = trade.ExitReason,
                    .Timestamp = trade.ExitTime,
                    .Confidence = 1.0F
                })
            Next

            Return signals
        End Function

        Private Sub RenderCandidateSummary()
            _gridCandidates.Rows.Clear()
            If _candidateRecords.Count = 0 Then Return

            Dim baselineSecondary = If(_baselineResult IsNot Nothing, _baselineResult.Report.SecondaryMetric, 0R)
            Dim ordered = _candidateRecords _
                .OrderByDescending(Function(item) item.Result.Report.PrimaryMetric) _
                .ThenByDescending(Function(item) item.Result.Report.SecondaryMetric) _
                .ThenByDescending(Function(item) item.Result.Report.MaxDrawdownRate) _
                .ToList()
            Dim recommendationPool = ordered.Where(Function(item) item IsNot Nothing AndAlso item.VersionType <> StrategyVersionType.Base).ToList()

            _recommendedCandidateId = If(recommendationPool.Count > 0, recommendationPool(0).CandidateId, "")

            For Each record In ordered
                Dim isRecommended = String.Equals(record.CandidateId, _recommendedCandidateId, StringComparison.OrdinalIgnoreCase)
                Dim isPromotion = String.Equals(record.CandidateId, _promotionCandidateId, StringComparison.OrdinalIgnoreCase)
                Dim rowIndex = _gridCandidates.Rows.Add(
                    record.VersionTag,
                    record.VersionType.ToString(),
                    record.ParentCandidateId,
                    record.Result.Report.PrimaryMetric.ToString("P2"),
                    record.Result.Report.SecondaryMetric.ToString("P2"),
                    record.Result.Report.MaxDrawdownRate.ToString("P2"),
                    record.Result.Report.AverageReturnRate.ToString("P2"),
                    (record.Result.Report.SecondaryMetric - baselineSecondary).ToString("P2"),
                    If(isRecommended, "Yes", ""),
                    If(isPromotion, "Pinned", ""),
                    record.ChangeSummary,
                    record.SourcePrompt)
                _gridCandidates.Rows(rowIndex).Tag = record
                If isRecommended Then
                    _gridCandidates.Rows(rowIndex).DefaultCellStyle.BackColor = Color.FromArgb(54, 66, 38)
                End If
                If isPromotion Then
                    _gridCandidates.Rows(rowIndex).DefaultCellStyle.SelectionBackColor = Color.FromArgb(44, 92, 56)
                End If
            Next

            UpdateRecommendationLabel(recommendationPool.FirstOrDefault())
            UpdatePromotionLabel()
        End Sub

        Private Sub OnSavePackage(sender As Object, e As EventArgs)
            Dim promotionRecord = _candidateRecords.FirstOrDefault(Function(item) String.Equals(item.CandidateId, _promotionCandidateId, StringComparison.OrdinalIgnoreCase))
            If promotionRecord Is Nothing OrElse promotionRecord.Result Is Nothing Then
                MessageBox.Show(Me, "먼저 승격 후보를 고정하세요.", "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            _lastResult = CloneResult(promotionRecord.Result)
            If _lastResult Is Nothing Then
                MessageBox.Show(Me, "먼저 평가를 실행하세요.", "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim package = _packageBuilder.BuildPackage(_lastResult.Definition, _lastResult.Report, Environment.UserName)
            Dim errors As List(Of String) = Nothing
            If Not _packageValidator.Validate(package, errors) Then
                AppendHistory("System", "Package validation failed: " & String.Join(", ", errors))
                MessageBox.Show(Me, String.Join(Environment.NewLine, errors), "Package Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            Dim packageFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages")
            Dim fullPath = _packageBuilder.SavePackage(package, packageFolder)
            Dim manifest = _manifestBuilder.BuildManifest(package, _lastResult.Report, Environment.UserName, "Created from StrategyLabApp MVP")
            AppendHistory("System", $"Package saved: {Path.GetFileName(fullPath)} | Manifest hash={manifest.PackageHash}")
            MessageBox.Show(Me, $"패키지를 저장했습니다.{Environment.NewLine}{fullPath}", "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub OnExportBatchReport(sender As Object, e As EventArgs)
            Dim prompt = _txtPrompt.Text.Trim()
            If String.IsNullOrWhiteSpace(prompt) Then
                MessageBox.Show(Me, "프롬프트를 먼저 입력하세요.", "StrategyLab", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim stocks = StockInfoManager.I.GetAll().
                OrderBy(Function(item) item.Code, StringComparer.OrdinalIgnoreCase).
                ToList()
            If stocks.Count = 0 Then
                MessageBox.Show(Me, "현재 종목정보 리스트가 비어 있습니다.", "StrategyLab", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim mode = DirectCast(_cboMode.SelectedItem, TradeMode)
            Dim fromDate = _dtFrom.Value.Date
            Dim targetRate = CDbl(_numTarget.Value) / 100.0R

            Using dlg As New SaveFileDialog()
                dlg.Title = "Save StrategyLab Batch Report"
                dlg.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
                dlg.FileName = $"StrategyLabBatch_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                dlg.InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reports")
                If dlg.ShowDialog(Me) <> DialogResult.OK Then Return

                Directory.CreateDirectory(Path.GetDirectoryName(dlg.FileName))

                Dim lines As New List(Of String) From {
                    BuildBatchReportHeader()
                }
                Dim outcomes As New List(Of BatchReportOutcome)()
                Dim successCount As Integer = 0
                Dim failCount As Integer = 0
                Dim startedAt = DateTime.Now
                Dim previousCursor = System.Windows.Forms.Cursor.Current
                System.Windows.Forms.Cursor.Current = Cursors.WaitCursor

                Try
                    AppendHistory("System", $"Batch evaluation started. Count={stocks.Count}, Prompt={prompt}")

                    For Each item In stocks
                        Dim result As StrategyLabResult = Nothing
                        Dim errorMessage As String = ""

                        Try
                            result = _labFacade.EvaluatePrompt(prompt, mode, item.Code, fromDate, targetRate, 5000, New CostModel())
                            successCount += 1
                        Catch ex As Exception
                            errorMessage = ex.Message
                            failCount += 1
                        End Try

                        outcomes.Add(New BatchReportOutcome With {
                            .Item = item,
                            .Prompt = prompt,
                            .Result = result,
                            .ErrorMessage = errorMessage
                        })
                        lines.Add(BuildBatchReportRow(item, prompt, result, errorMessage))
                        AppendHistory("System", $"Batch {item.Code} {If(errorMessage = "", "OK", "FAIL")} {If(errorMessage = "", "", "- " & errorMessage)}")
                        Application.DoEvents()
                    Next

                    File.WriteAllText(dlg.FileName, String.Join(Environment.NewLine, lines), New UTF8Encoding(True))
                    Dim summaryPath = Path.Combine(Path.GetDirectoryName(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName) & "_summary.csv")
                    File.WriteAllText(summaryPath, String.Join(Environment.NewLine, BuildBatchSummaryLines(outcomes)), New UTF8Encoding(True))

                    Dim elapsed = DateTime.Now - startedAt
                    AppendHistory("System", $"Batch report saved: {dlg.FileName} | Summary: {summaryPath} (Success={successCount}, Fail={failCount}, Elapsed={elapsed.TotalSeconds:N1}s)")
                    MessageBox.Show(Me,
                                    $"Batch report saved.{Environment.NewLine}Success: {successCount}{Environment.NewLine}Fail: {failCount}{Environment.NewLine}Detail: {dlg.FileName}{Environment.NewLine}Summary: {summaryPath}",
                                    "StrategyLab",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information)
                Finally
                    System.Windows.Forms.Cursor.Current = previousCursor
                End Try
            End Using
        End Sub

        Private Sub OnExportBatchPdfReport(sender As Object, e As EventArgs)
            Dim prompt = _txtPrompt.Text.Trim()
            If String.IsNullOrWhiteSpace(prompt) Then
                MessageBox.Show(Me, "프롬프트를 먼저 입력하세요.", "StrategyLab", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim stocks = StockInfoManager.I.GetAll().
                OrderBy(Function(item) item.Code, StringComparer.OrdinalIgnoreCase).
                ToList()
            If stocks.Count = 0 Then
                MessageBox.Show(Me, "현재 종목정보 리스트가 비어 있습니다.", "StrategyLab", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim mode = DirectCast(_cboMode.SelectedItem, TradeMode)
            Dim fromDate = _dtFrom.Value.Date
            Dim targetRate = CDbl(_numTarget.Value) / 100.0R

            Using dlg As New SaveFileDialog()
                dlg.Title = "Save StrategyLab Batch PDF Report"
                dlg.Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*"
                dlg.FileName = $"StrategyLabBatch_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
                dlg.InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reports")
                If dlg.ShowDialog(Me) <> DialogResult.OK Then Return

                Directory.CreateDirectory(Path.GetDirectoryName(dlg.FileName))
                Dim outcomes = RunBatchEvaluation(stocks, prompt, mode, fromDate, targetRate)
                Dim htmlPath = Path.Combine(Path.GetDirectoryName(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName) & ".html")
                Dim assetFolder = Path.Combine(Path.GetDirectoryName(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName) & "_assets")
                File.WriteAllText(htmlPath, BuildBatchHtmlReport(prompt, mode, fromDate, targetRate, outcomes, assetFolder), New UTF8Encoding(True))

                Dim pdfCreated = TryCreatePdfFromHtml(htmlPath, dlg.FileName)
                Dim successCount = outcomes.Where(Function(x) x.Result IsNot Nothing AndAlso x.Result.Report IsNot Nothing).Count()
                Dim failCount = outcomes.Where(Function(x) Not String.IsNullOrWhiteSpace(x.ErrorMessage)).Count()

                If pdfCreated Then
                    AppendHistory("System", $"Batch PDF report saved: {dlg.FileName} | Source HTML: {htmlPath} (Success={successCount}, Fail={failCount})")
                    MessageBox.Show(Me,
                                    $"Batch PDF report saved.{Environment.NewLine}Success: {successCount}{Environment.NewLine}Fail: {failCount}{Environment.NewLine}PDF: {dlg.FileName}{Environment.NewLine}HTML: {htmlPath}",
                                    "StrategyLab",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information)
                Else
                    AppendHistory("System", $"Batch HTML report saved: {htmlPath} (PDF conversion unavailable, Success={successCount}, Fail={failCount})")
                    MessageBox.Show(Me,
                                    $"HTML report was saved, but automatic PDF conversion was unavailable.{Environment.NewLine}Success: {successCount}{Environment.NewLine}Fail: {failCount}{Environment.NewLine}HTML: {htmlPath}",
                                    "StrategyLab",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information)
                End If
            End Using
        End Sub

        Private Function RunBatchEvaluation(stocks As IEnumerable(Of StockInfoItem),
                                            prompt As String,
                                            mode As TradeMode,
                                            fromDate As DateTime,
                                            targetRate As Double) As List(Of BatchReportOutcome)
            Dim outcomes As New List(Of BatchReportOutcome)()
            Dim stockList = stocks.ToList()
            Dim startedAt = DateTime.Now
            Dim previousCursor = System.Windows.Forms.Cursor.Current
            System.Windows.Forms.Cursor.Current = Cursors.WaitCursor

            Try
                AppendHistory("System", $"Batch evaluation started. Count={stockList.Count}, Prompt={prompt}")

                For Each item In stockList
                    Dim result As StrategyLabResult = Nothing
                    Dim errorMessage As String = ""

                    Try
                        result = _labFacade.EvaluatePrompt(prompt, mode, item.Code, fromDate, targetRate, 5000, New CostModel())
                    Catch ex As Exception
                        errorMessage = ex.Message
                    End Try

                    outcomes.Add(New BatchReportOutcome With {
                        .Item = item,
                        .Prompt = prompt,
                        .Result = result,
                        .ErrorMessage = errorMessage
                    })
                    AppendHistory("System", $"Batch {item.Code} {If(errorMessage = "", "OK", "FAIL")} {If(errorMessage = "", "", "- " & errorMessage)}")
                    Application.DoEvents()
                Next

                Dim successCount = outcomes.Where(Function(x) x.Result IsNot Nothing AndAlso x.Result.Report IsNot Nothing).Count()
                Dim failCount = outcomes.Where(Function(x) Not String.IsNullOrWhiteSpace(x.ErrorMessage)).Count()
                Dim elapsed = DateTime.Now - startedAt
                AppendHistory("System", $"Batch evaluation completed. Success={successCount}, Fail={failCount}, Elapsed={elapsed.TotalSeconds:N1}s")
                Return outcomes
            Finally
                System.Windows.Forms.Cursor.Current = previousCursor
            End Try
        End Function

        Private Function BuildBatchHtmlReport(prompt As String,
                                              mode As TradeMode,
                                              fromDate As DateTime,
                                              targetRate As Double,
                                              outcomes As IEnumerable(Of BatchReportOutcome),
                                              assetFolder As String) As String
            Dim rows = outcomes.ToList()
            Dim successful = rows.Where(Function(x) x.Result IsNot Nothing AndAlso x.Result.Report IsNot Nothing).ToList()
            Dim sb As New StringBuilder()
            Directory.CreateDirectory(assetFolder)
            sb.AppendLine("<!DOCTYPE html>")
            sb.AppendLine("<html><head><meta charset=""utf-8""/>")
            sb.AppendLine("<title>StrategyLab Batch Report</title>")
            sb.AppendLine("<style>")
            sb.AppendLine("body{font-family:'Malgun Gothic',sans-serif;margin:24px;color:#1f2937;background:#f8fafc;} h1,h2,h3{margin:0 0 12px;} .meta,.card{background:#fff;border:1px solid #dbe2ea;border-radius:10px;padding:16px;margin-bottom:16px;} .grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;} .metric{background:#f8fafc;border:1px solid #e5e7eb;border-radius:8px;padding:12px;} table{width:100%;border-collapse:collapse;margin-top:8px;} th,td{border:1px solid #dbe2ea;padding:8px;vertical-align:top;text-align:left;} th{background:#eef2f7;} .reinforce{color:#166534;font-weight:700;} .improve{color:#92400e;font-weight:700;} .rework{color:#991b1b;font-weight:700;} .small{color:#6b7280;font-size:12px;} .section-title{margin-top:24px;}")
            sb.AppendLine("</style></head><body>")
            sb.AppendLine("<h1>StrategyLab Batch Strategy Report</h1>")
            sb.AppendLine("<div class=""meta"">")
            sb.AppendLine($"<div><strong>Prompt:</strong> {HtmlEscape(prompt)}</div>")
            sb.AppendLine($"<div><strong>Mode:</strong> {HtmlEscape(mode.ToString())}</div>")
            Dim fromText = fromDate.ToString("yyyy-MM-dd")
            sb.AppendLine($"<div><strong>From:</strong> {HtmlEscape(fromText)}</div>")
            sb.AppendLine($"<div><strong>Target:</strong> {targetRate:P2}</div>")
            sb.AppendLine($"<div><strong>Generated:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>")
            sb.AppendLine("</div>")

            sb.AppendLine("<div class=""card"">")
            sb.AppendLine("<h2>System Midpoint Check</h2>")
            sb.AppendLine("<div class=""grid"">")
            sb.AppendLine(BuildMetricHtml("Evaluated Symbols", rows.Count.ToString()))
            sb.AppendLine(BuildMetricHtml("Successful", successful.Count.ToString()))
            sb.AppendLine(BuildMetricHtml("Mean Avg Return", If(successful.Count > 0, successful.Average(Function(x) x.Result.Report.AverageReturnRate).ToString("P2"), "n/a")))
            sb.AppendLine(BuildMetricHtml("Mean Target Hit", If(successful.Count > 0, successful.Average(Function(x) x.Result.Report.PrimaryMetric).ToString("P2"), "n/a")))
            sb.AppendLine("</div>")
            sb.AppendLine("</div>")

            sb.AppendLine("<div class=""card""><h2>Portfolio Summary</h2><table><thead><tr><th>Symbol</th><th>Name</th><th>Decision</th><th>Avg Return</th><th>Primary KPI</th><th>Drawdown</th><th>Decision Reason</th></tr></thead><tbody>")
            For Each outcome In rows
                If outcome.Result Is Nothing OrElse outcome.Result.Report Is Nothing Then
                    sb.AppendLine($"<tr><td>{HtmlEscape(outcome.Item.Code)}</td><td>{HtmlEscape(outcome.Item.Name)}</td><td class=""rework"">Improve</td><td>n/a</td><td>n/a</td><td>n/a</td><td>{HtmlEscape(outcome.ErrorMessage)}</td></tr>")
                Else
                    Dim decision = DetermineBatchDecision(outcome.Result)
                    Dim decisionClass = decision.ToLowerInvariant()
                    sb.AppendLine("<tr>")
                    sb.AppendLine($"<td>{HtmlEscape(outcome.Item.Code)}</td>")
                    sb.AppendLine($"<td>{HtmlEscape(outcome.Item.Name)}</td>")
                    sb.AppendLine($"<td class=""{decisionClass}"">{HtmlEscape(decision)}</td>")
                    sb.AppendLine($"<td>{outcome.Result.Report.AverageReturnRate:P2}</td>")
                    sb.AppendLine($"<td>{outcome.Result.Report.PrimaryMetric:P2}</td>")
                    sb.AppendLine($"<td>{outcome.Result.Report.MaxDrawdownRate:P2}</td>")
                    sb.AppendLine($"<td>{HtmlEscape(BuildBatchDecisionReason(outcome.Result))}</td>")
                    sb.AppendLine("</tr>")
                End If
            Next
            sb.AppendLine("</tbody></table></div>")

            For Each outcome In rows
                sb.AppendLine("<div class=""card"">")
                sb.AppendLine($"<h2 class=""section-title"">{HtmlEscape(outcome.Item.Code)} {HtmlEscape(outcome.Item.Name)}</h2>")
                sb.AppendLine($"<div class=""small"">Source: {HtmlEscape(outcome.Item.SourceText())} / {HtmlEscape(outcome.Item.SourceDetail)}</div>")
                If outcome.Result Is Nothing OrElse outcome.Result.Report Is Nothing OrElse outcome.Result.Definition Is Nothing Then
                    sb.AppendLine($"<p><strong>Evaluation failed:</strong> {HtmlEscape(outcome.ErrorMessage)}</p>")
                Else
                    Dim report = outcome.Result.Report
                    Dim suggestion = outcome.Result.ImprovementPlan?.Suggestions?.FirstOrDefault()
                    Dim chartImage = CaptureBatchChartSnapshot(outcome, assetFolder)
                    sb.AppendLine("<div class=""grid"">")
                    sb.AppendLine(BuildMetricHtml("Decision", DetermineBatchDecision(outcome.Result)))
                    sb.AppendLine(BuildMetricHtml("Avg Return", report.AverageReturnRate.ToString("P2")))
                    sb.AppendLine(BuildMetricHtml("Primary KPI", report.PrimaryMetric.ToString("P2")))
                    sb.AppendLine(BuildMetricHtml("Win Rate", report.WinRate.ToString("P2")))
                    sb.AppendLine("</div>")
                    If Not String.IsNullOrWhiteSpace(chartImage) Then
                        sb.AppendLine($"<div style=""margin:16px 0;""><img src=""{HtmlEscape(chartImage)}"" style=""width:100%;border:1px solid #dbe2ea;border-radius:8px;"" /></div>")
                    End If
                    sb.AppendLine("<table><tbody>")
                    sb.AppendLine($"<tr><th>Strategy</th><td>{HtmlEscape(outcome.Result.Definition.Name)}</td></tr>")
                    Dim indicatorText = String.Join(" | ", outcome.Result.Definition.Indicators.Select(Function(ind) ind.IndicatorType))
                    sb.AppendLine($"<tr><th>Indicators</th><td>{HtmlEscape(indicatorText)}</td></tr>")
                    sb.AppendLine($"<tr><th>Strength</th><td>{HtmlEscape(report.StrengthSummary)}</td></tr>")
                    sb.AppendLine($"<tr><th>Weakness</th><td>{HtmlEscape(report.WeaknessSummary)}</td></tr>")
                    sb.AppendLine($"<tr><th>Failed Example</th><td>{HtmlEscape(report.FailedExampleSummary)}</td></tr>")
                    sb.AppendLine($"<tr><th>Decision Reason</th><td>{HtmlEscape(BuildBatchDecisionReason(outcome.Result))}</td></tr>")
                    sb.AppendLine($"<tr><th>Top Suggestion</th><td>{HtmlEscape(If(suggestion?.Title, ""))}</td></tr>")
                    sb.AppendLine($"<tr><th>Suggestion Prompt</th><td>{HtmlEscape(If(suggestion?.PromptHint, ""))}</td></tr>")
                    sb.AppendLine("</tbody></table>")
                End If
                sb.AppendLine("</div>")
            Next

            sb.AppendLine("</body></html>")
            Return sb.ToString()
        End Function

        Private Shared Function BuildMetricHtml(title As String, value As String) As String
            Return $"<div class=""metric""><div class=""small"">{HtmlEscape(title)}</div><div><strong>{HtmlEscape(value)}</strong></div></div>"
        End Function

        Private Function CaptureBatchChartSnapshot(outcome As BatchReportOutcome, assetFolder As String) As String
            If outcome Is Nothing OrElse outcome.Result Is Nothing OrElse outcome.Result.Report Is Nothing Then
                Return ""
            End If

            Dim previousResult = _lastResult
            Dim previousSymbol = _txtSymbol.Text

            Try
                _lastResult = CloneResult(outcome.Result)
                _txtSymbol.Text = outcome.Item.Code
                RenderChart(_lastResult.Report)
                _fastChart.ShowAllCandles()
                _fastChart.Refresh()
                Application.DoEvents()
                Application.DoEvents()

                Dim width = Math.Max(1, _panelLabChart.ClientSize.Width)
                Dim height = Math.Max(1, _panelLabChart.ClientSize.Height)
                If width <= 1 OrElse height <= 1 Then
                    Return ""
                End If

                Dim fileName = $"{outcome.Item.Code}_{DateTime.Now:HHmmssfff}.png"
                Dim fullPath = Path.Combine(assetFolder, fileName)
                Using bmp As New Bitmap(width, height)
                    _panelLabChart.DrawToBitmap(bmp, New Rectangle(0, 0, width, height))
                    bmp.Save(fullPath, ImageFormat.Png)
                End Using
                Return Path.GetFileName(assetFolder) & "/" & fileName
            Finally
                _lastResult = previousResult
                _txtSymbol.Text = previousSymbol
                If _lastResult IsNot Nothing AndAlso _lastResult.Report IsNot Nothing Then
                    RenderChart(_lastResult.Report)
                End If
            End Try
        End Function

        Private Shared Function HtmlEscape(value As String) As String
            Dim text = If(value, "")
            Return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("""", "&quot;")
        End Function

        Private Shared Function TryCreatePdfFromHtml(htmlPath As String, pdfPath As String) As Boolean
            Dim edgeCandidates = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft\Edge\Application\msedge.exe")
            }

            Dim edgePath = edgeCandidates.FirstOrDefault(Function(path) File.Exists(path))
            If String.IsNullOrWhiteSpace(edgePath) Then
                Return False
            End If

            Dim psi As New ProcessStartInfo(edgePath) With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .Arguments = $"--headless --disable-gpu --print-to-pdf=""{pdfPath}"" ""{New Uri(htmlPath).AbsoluteUri}"""
            }

            Using proc = Process.Start(psi)
                If proc Is Nothing Then Return False
                proc.WaitForExit(30000)
            End Using

            Return File.Exists(pdfPath)
        End Function

        Private Shared Function BuildBatchReportHeader() As String
            Dim columns = {
                "EvaluatedAt",
                "Symbol",
                "Name",
                "Source",
                "SourceDetail",
                "Prompt",
                "Strategy",
                "Mode",
                "Timeframes",
                "Indicators",
                "TradeCount",
                "TargetHits",
                "MissedTargets",
                "PrimaryKPI",
                "SecondaryKPI",
                "AvgReturn",
                "MaxDrawdown",
                "WinRate",
                "Decision",
                "DecisionReason",
                "Strength",
                "Weakness",
                "FailedExample",
                "TopSuggestion",
                "SuggestionPrompt",
                "Error"
            }
            Return String.Join(",", columns.Select(AddressOf CsvEscape))
        End Function

        Private Shared Function BuildBatchReportRow(item As StockInfoItem,
                                                    prompt As String,
                                                    result As StrategyLabResult,
                                                    errorMessage As String) As String
            If result Is Nothing OrElse result.Report Is Nothing OrElse result.Definition Is Nothing Then
                Dim failedFields = {
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    If(item?.Code, ""),
                    If(item?.Name, ""),
                    If(item?.SourceText(), ""),
                    If(item?.SourceDetail, ""),
                    prompt,
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "Improve",
                    "Evaluation failed",
                    "",
                    "",
                    "",
                    "",
                    errorMessage
                }
                Return String.Join(",", failedFields.Select(AddressOf CsvEscape))
            End If

            Dim topSuggestion = result.ImprovementPlan?.Suggestions?.FirstOrDefault()
            Dim decision = DetermineBatchDecision(result)
            Dim decisionReason = BuildBatchDecisionReason(result)
            Dim fields = {
                result.Report.EvaluatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                item.Code,
                item.Name,
                item.SourceText(),
                item.SourceDetail,
                prompt,
                result.Definition.Name,
                result.Definition.TradeMode.ToString(),
                String.Join(" | ", result.Definition.Timeframes),
                String.Join(" | ", result.Definition.Indicators.Select(Function(ind) ind.IndicatorType)),
                result.Report.TradeCount.ToString(),
                $"{result.Report.TargetHitCount}/{Math.Max(0, result.Report.TradeCount)}",
                result.Report.MissedTargetCount.ToString(),
                result.Report.PrimaryMetric.ToString("P2"),
                result.Report.SecondaryMetric.ToString("P2"),
                result.Report.AverageReturnRate.ToString("P2"),
                result.Report.MaxDrawdownRate.ToString("P2"),
                result.Report.WinRate.ToString("P2"),
                decision,
                decisionReason,
                result.Report.StrengthSummary,
                result.Report.WeaknessSummary,
                result.Report.FailedExampleSummary,
                If(topSuggestion?.Title, ""),
                If(topSuggestion?.PromptHint, ""),
                errorMessage
            }
            Return String.Join(",", fields.Select(AddressOf CsvEscape))
        End Function

        Private Shared Function BuildBatchSummaryLines(outcomes As IEnumerable(Of BatchReportOutcome)) As List(Of String)
            Dim rows = outcomes.ToList()
            Dim lines As New List(Of String)()
            lines.Add(String.Join(",", {
                CsvEscape("Section"),
                CsvEscape("Key"),
                CsvEscape("Value"),
                CsvEscape("Notes")
            }))

            Dim successful = rows.Where(Function(x) x.Result IsNot Nothing AndAlso x.Result.Report IsNot Nothing).ToList()
            lines.Add(BuildSummaryRow("Overview", "EvaluatedSymbols", rows.Count.ToString(), ""))
            lines.Add(BuildSummaryRow("Overview", "SuccessfulSymbols", successful.Count.ToString(), ""))
            lines.Add(BuildSummaryRow("Overview", "FailedSymbols", rows.Where(Function(x) Not String.IsNullOrWhiteSpace(x.ErrorMessage)).Count().ToString(), ""))

            If successful.Count > 0 Then
                lines.Add(BuildSummaryRow("Metrics", "MeanPrimaryKPI", successful.Average(Function(x) x.Result.Report.PrimaryMetric).ToString("P2"), "Average target-hit ratio"))
                lines.Add(BuildSummaryRow("Metrics", "MeanAvgReturn", successful.Average(Function(x) x.Result.Report.AverageReturnRate).ToString("P2"), "Average net return"))
                lines.Add(BuildSummaryRow("Metrics", "MeanMaxDrawdown", successful.Average(Function(x) x.Result.Report.MaxDrawdownRate).ToString("P2"), "Average drawdown"))
                lines.Add(BuildSummaryRow("Metrics", "MeanWinRate", successful.Average(Function(x) x.Result.Report.WinRate).ToString("P2"), "Average win rate"))

                Dim best = successful.OrderByDescending(Function(x) x.Result.Report.AverageReturnRate).First()
                Dim worst = successful.OrderBy(Function(x) x.Result.Report.AverageReturnRate).First()
                lines.Add(BuildSummaryRow("BestSymbol", best.Item.Code, best.Result.Report.AverageReturnRate.ToString("P2"), best.Item.Name))
                lines.Add(BuildSummaryRow("WorstSymbol", worst.Item.Code, worst.Result.Report.AverageReturnRate.ToString("P2"), worst.Item.Name))

                For Each grp In successful.
                    GroupBy(Function(x) DetermineBatchDecision(x.Result)).
                    OrderBy(Function(g) g.Key, StringComparer.OrdinalIgnoreCase)
                    lines.Add(BuildSummaryRow("Decision", grp.Key, grp.Count().ToString(), ""))
                Next

                For Each grp In successful.
                    Select(Function(x) x.Result.ImprovementPlan?.Suggestions?.FirstOrDefault()).
                    Where(Function(x) x IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(x.Title)).
                    GroupBy(Function(x) x.Title).
                    OrderByDescending(Function(g) g.Count()).
                    Take(5)
                    lines.Add(BuildSummaryRow("CommonSuggestion", grp.Key, grp.Count().ToString(), grp.First().PromptHint))
                Next
            End If

            For Each failed In rows.Where(Function(x) Not String.IsNullOrWhiteSpace(x.ErrorMessage))
                lines.Add(BuildSummaryRow("Failure", failed.Item.Code, failed.ErrorMessage, failed.Item.Name))
            Next

            Return lines
        End Function

        Private Shared Function BuildSummaryRow(section As String, key As String, value As String, notes As String) As String
            Return String.Join(",", {
                CsvEscape(section),
                CsvEscape(key),
                CsvEscape(value),
                CsvEscape(notes)
            })
        End Function

        Private Shared Function DetermineBatchDecision(result As StrategyLabResult) As String
            If result Is Nothing OrElse result.Report Is Nothing Then
                Return "Improve"
            End If

            Dim report = result.Report
            If report.TradeCount = 0 Then
                Return "Improve"
            End If

            If report.PrimaryMetric >= 0.6R AndAlso report.AverageReturnRate > 0 AndAlso report.MaxDrawdownRate > -0.03R Then
                Return "Reinforce"
            End If

            If report.PrimaryMetric >= 0.35R AndAlso report.AverageReturnRate > 0 Then
                Return "Improve"
            End If

            Return "Rework"
        End Function

        Private Shared Function BuildBatchDecisionReason(result As StrategyLabResult) As String
            If result Is Nothing OrElse result.Report Is Nothing Then
                Return "No evaluation result"
            End If

            Dim report = result.Report
            If report.TradeCount = 0 Then
                Return "No trade generated in the evaluation range"
            End If

            If report.PrimaryMetric >= 0.6R AndAlso report.AverageReturnRate > 0 AndAlso report.MaxDrawdownRate > -0.03R Then
                Return $"Target hits {report.TargetHitCount}/{report.TradeCount}, avg {report.AverageReturnRate:P2}, drawdown {report.MaxDrawdownRate:P2}"
            End If

            If report.PrimaryMetric >= 0.35R AndAlso report.AverageReturnRate > 0 Then
                Return $"Profitable but target hits are limited: {report.TargetHitCount}/{report.TradeCount}, failed example: {report.FailedExampleSummary}"
            End If

            Return $"Weak target attainment or negative expectancy: avg {report.AverageReturnRate:P2}, drawdown {report.MaxDrawdownRate:P2}"
        End Function

        Private Shared Function CsvEscape(value As String) As String
            Dim text = If(value, "")
            text = text.Replace("""", """""")
            If text.Contains(",") OrElse text.Contains("""") OrElse text.Contains(vbCr) OrElse text.Contains(vbLf) Then
                Return $"""{text}"""
            End If
            Return text
        End Function

        Private Sub OnSaveSession(sender As Object, e As EventArgs)
            Dim session = BuildSession()
            Dim fullPath = _sessionService.SaveSession(session)
            AppendHistory("System", $"Session saved: {Path.GetFileName(fullPath)}")
        End Sub

        Private Sub OnLoadSession(sender As Object, e As EventArgs)
            Dim session = _sessionService.LoadLatestSession()
            If session Is Nothing Then
                MessageBox.Show(Me, "저장된 세션이 없습니다.", "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            ApplySession(session)
            AppendHistory("System", $"Session loaded: {session.Title}")
        End Sub

        Private Sub OnSetBaseline(sender As Object, e As EventArgs)
            If _lastResult Is Nothing Then
                MessageBox.Show(Me, "먼저 평가를 실행하세요.", "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            _baselineResult = CloneResult(_lastResult)
            Dim lineId = ResolveStrategyLineId(_baselineResult)
            _baselineResult.Definition.StrategyLineId = lineId
            _baselineResult.Definition.StrategyVersionId = Guid.NewGuid().ToString("N")
            _baselineResult.Definition.ParentVersionId = ""
            _baselineResult.Definition.Version = 1
            _baselineResult.Definition.VersionTag = "B1"
            _baselineResult.Definition.VersionType = StrategyVersionType.Base
            _baselineResult.Definition.ChangeSummary = "Frozen base version"
            _activeCandidateId = ""
            UpsertBaseRecord(_baselineResult)
            RefreshCandidateList()
            RenderCandidateSummary()
            RenderKpi(_lastResult)
            RenderComparison(_lastResult)
            AppendHistory("System", $"Base version saved: {_baselineResult.Definition.VersionTag} -> {_baselineResult.Definition.Name}")
            _lblStatus.Text = BuildStatusText(_baselineResult, "Base")
        End Sub

        Private Sub OnSaveCandidate(sender As Object, e As EventArgs)
            If _lastResult Is Nothing Then
                MessageBox.Show(Me, "먼저 평가를 실행하세요.", "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            If _baselineResult Is Nothing OrElse _baselineResult.Definition Is Nothing Then
                MessageBox.Show(Me, "먼저 Save Base Version으로 기준 베이스를 확정하세요.", "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim candidate = CloneResult(_lastResult)
            _candidateCounter += 1
            Dim parentId = If(String.IsNullOrWhiteSpace(_activeCandidateId), "baseline", _activeCandidateId)
            Dim parentVersionId = ResolveActiveParentVersionId()
            Dim lineId = ResolveStrategyLineId(_baselineResult)
            candidate.Definition.StrategyLineId = lineId
            candidate.Definition.StrategyVersionId = Guid.NewGuid().ToString("N")
            candidate.Definition.ParentVersionId = parentVersionId
            candidate.Definition.Version = _candidateCounter + 1
            candidate.Definition.VersionTag = $"C{_candidateCounter}"
            candidate.Definition.VersionType = StrategyVersionType.Derived
            candidate.Definition.ChangeSummary = BuildCandidateChangeSummary(parentId)
            Dim record As New StrategyLabCandidateRecord With {
                .StrategyLineId = candidate.Definition.StrategyLineId,
                .StrategyVersionId = candidate.Definition.StrategyVersionId,
                .ParentVersionId = candidate.Definition.ParentVersionId,
                .VersionTag = $"C{_candidateCounter}",
                .VersionType = StrategyVersionType.Derived,
                .ParentCandidateId = parentId,
                .SourcePrompt = _txtPrompt.Text.Trim(),
                .ChangeSummary = candidate.Definition.ChangeSummary,
                .AverageReturnRate = candidate.Report.AverageReturnRate,
                .Result = candidate
            }
            _candidateRecords.Add(record)
            _activeCandidateId = record.CandidateId
            RefreshCandidateList()
            RenderCandidateSummary()
            AppendHistory("System", $"Candidate saved: {record.VersionTag} -> {candidate.Definition.Name} (parent={parentId})")
        End Sub

        Private Sub OnCandidateSelected(sender As Object, e As EventArgs)
            Dim selected = TryCast(_lstCandidates.SelectedItem, CandidateListItem)
            If selected Is Nothing OrElse selected.Record Is Nothing OrElse selected.Record.Result Is Nothing Then Return

            _activeCandidateId = selected.Record.CandidateId
            _lastResult = CloneResult(selected.Record.Result)
            RenderChart(_lastResult.Report)
            RenderKpi(_lastResult)
            RenderComparison(_lastResult)
            RenderDiagnosis(_lastResult)
            RenderSuggestions(_lastResult)
            RenderTrades(_lastResult.Report)
            RenderCandidateSummary()
            _lblStatus.Text = BuildStatusText(_lastResult, selected.Record.VersionType.ToString())
            AppendHistory("System", $"Candidate loaded for review: {selected.Record.VersionTag} <- {selected.Record.ParentCandidateId}")
        End Sub

        Private Sub OnCandidateGridCellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then Return
            Dim record = TryCast(_gridCandidates.Rows(e.RowIndex).Tag, StrategyLabCandidateRecord)
            If record Is Nothing Then Return

            _activeCandidateId = record.CandidateId
            _lastResult = CloneResult(record.Result)
            RenderChart(_lastResult.Report)
            RenderKpi(_lastResult)
            RenderComparison(_lastResult)
            RenderDiagnosis(_lastResult)
            RenderSuggestions(_lastResult)
            RenderTrades(_lastResult.Report)
            RenderCandidateSummary()
            _lblStatus.Text = BuildStatusText(_lastResult, record.VersionType.ToString())
            AppendHistory("System", $"Candidate loaded from ranking grid: {record.VersionTag} <- {record.ParentCandidateId}")
        End Sub

        Private Sub OnApplyTopSuggestion(sender As Object, e As EventArgs)
            Dim suggestion = GetSelectedSuggestion()
            If suggestion Is Nothing AndAlso _lastResult IsNot Nothing AndAlso _lastResult.ImprovementPlan IsNot Nothing Then
                suggestion = _lastResult.ImprovementPlan.Suggestions.FirstOrDefault()
            End If

            If suggestion Is Nothing Then
                MessageBox.Show(Me, "No improvement suggestion is available.", "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            ApplySuggestionAndEvaluate(suggestion)
        End Sub

        Private Sub OnPinPromotionCandidate(sender As Object, e As EventArgs)
            Dim targetId = _activeCandidateId
            If Not String.IsNullOrWhiteSpace(targetId) Then
                Dim activeRecord = _candidateRecords.FirstOrDefault(Function(item) String.Equals(item.CandidateId, targetId, StringComparison.OrdinalIgnoreCase))
                If activeRecord IsNot Nothing AndAlso activeRecord.VersionType = StrategyVersionType.Base Then
                    targetId = ""
                End If
            End If
            If String.IsNullOrWhiteSpace(targetId) Then
                targetId = _recommendedCandidateId
            End If

            If String.IsNullOrWhiteSpace(targetId) Then
                MessageBox.Show(Me, "No candidate is available to pin for promotion.", "StrategyLabApp", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            _promotionCandidateId = targetId
            RenderCandidateSummary()
            AppendHistory("System", $"Promotion candidate pinned: {_promotionCandidateId}")
        End Sub

        Private Sub OnSuggestionCellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then Return
            Dim suggestion = TryCast(_gridSuggestions.Rows(e.RowIndex).Tag, StrategyImprovementSuggestion)
            If suggestion Is Nothing Then Return
            ApplySuggestionAndEvaluate(suggestion)
        End Sub

        Private Function BuildSession() As StrategyLabSession
            Return New StrategyLabSession With {
                .Title = If(_lastResult?.Definition?.Name, "strategy_lab_session"),
                .Symbol = _txtSymbol.Text.Trim(),
                .FromDate = _dtFrom.Value,
                .TradeMode = DirectCast(_cboMode.SelectedItem, TradeMode),
                .TargetPercent = CDbl(_numTarget.Value),
                .PromptText = _txtPrompt.Text,
                .HistoryText = _txtHistory.Text,
                .BaselineResult = _baselineResult,
                .CandidateRecords = _candidateRecords.ToList(),
                .LastResult = _lastResult,
                .ActiveCandidateId = _activeCandidateId,
                .RecommendedCandidateId = _recommendedCandidateId,
                .PromotionCandidateId = _promotionCandidateId
            }
        End Function

        Private Sub ApplySession(session As StrategyLabSession)
            If session Is Nothing Then Return

            _txtSymbol.Text = session.Symbol
            _dtFrom.Value = If(session.FromDate = DateTime.MinValue, DateTime.Today.AddDays(-1), session.FromDate)
            _cboMode.SelectedItem = session.TradeMode
            _numTarget.Value = CDec(Math.Max(CDbl(_numTarget.Minimum), Math.Min(CDbl(_numTarget.Maximum), session.TargetPercent)))
            _txtPrompt.Text = session.PromptText
            _txtHistory.Text = session.HistoryText
            _baselineResult = session.BaselineResult
            _candidateRecords.Clear()
            If session.CandidateRecords IsNot Nothing Then _candidateRecords.AddRange(session.CandidateRecords)
            _candidateCounter = _candidateRecords.Where(Function(item) item IsNot Nothing AndAlso item.VersionType <> StrategyVersionType.Base).Count()
            _activeCandidateId = session.ActiveCandidateId
            _recommendedCandidateId = session.RecommendedCandidateId
            _promotionCandidateId = session.PromotionCandidateId
            RefreshCandidateList()
            RenderCandidateSummary()
            _lastResult = session.LastResult

            If _lastResult IsNot Nothing Then
                RenderChart(_lastResult.Report)
                RenderKpi(_lastResult)
                RenderComparison(_lastResult)
                RenderDiagnosis(_lastResult)
                RenderSuggestions(_lastResult)
                RenderTrades(_lastResult.Report)
                _lblStatus.Text = BuildStatusText(_lastResult, "Loaded")
            End If
        End Sub

        Private Sub RefreshCandidateList()
            _lstCandidates.Items.Clear()
            For i = 0 To _candidateRecords.Count - 1
                _lstCandidates.Items.Add(New CandidateListItem With {
                    .Title = $"{_candidateRecords(i).VersionTag} [{_candidateRecords(i).VersionType}] {_candidateRecords(i).Result.Definition.Name} (Avg={_candidateRecords(i).Result.Report.AverageReturnRate:P2}) <- {_candidateRecords(i).ParentCandidateId}",
                    .Record = _candidateRecords(i)
                })
            Next
        End Sub

        Private Sub UpdateRecommendationLabel(record As StrategyLabCandidateRecord)
            If record Is Nothing OrElse record.Result Is Nothing Then
                _lblRecommendation.Text = "Recommended: none"
                _txtRecommendationReason.Text = "No candidate is available for recommendation."
                Return
            End If

            _lblRecommendation.Text = $"Recommended: {record.VersionTag} | P={record.Result.Report.PrimaryMetric:P0}, S={record.Result.Report.SecondaryMetric:P2}, Avg={record.Result.Report.AverageReturnRate:P2}, DD={record.Result.Report.MaxDrawdownRate:P2}"
            _txtRecommendationReason.Text = BuildRecommendationReason(record)
        End Sub

        Private Function BuildRecommendationReason(record As StrategyLabCandidateRecord) As String
            If record Is Nothing OrElse record.Result Is Nothing Then Return "No recommendation reason is available."

            Dim baselinePrimary = If(_baselineResult IsNot Nothing, _baselineResult.Report.PrimaryMetric, 0R)
            Dim baselineSecondary = If(_baselineResult IsNot Nothing, _baselineResult.Report.SecondaryMetric, 0R)
            Dim baselineDrawdown = If(_baselineResult IsNot Nothing, _baselineResult.Report.MaxDrawdownRate, 0R)
            Dim baselineAverageReturn = If(_baselineResult IsNot Nothing, _baselineResult.Report.AverageReturnRate, 0R)

            Dim primaryDelta = record.Result.Report.PrimaryMetric - baselinePrimary
            Dim secondaryDelta = record.Result.Report.SecondaryMetric - baselineSecondary
            Dim drawdownDelta = record.Result.Report.MaxDrawdownRate - baselineDrawdown
            Dim averageReturnDelta = record.Result.Report.AverageReturnRate - baselineAverageReturn
            Dim summary As String = $"Avg {record.Result.Report.AverageReturnRate:P2}"

            If averageReturnDelta > 0 Then
                summary &= $" | baseline 대비 평균수익률 {averageReturnDelta:P2} 개선"
            ElseIf Math.Abs(averageReturnDelta) < 0.0001R Then
                summary &= " | baseline 대비 평균수익률 동일"
            Else
                summary &= $" | baseline 대비 평균수익률 {Math.Abs(averageReturnDelta):P2} 감소"
            End If

            If primaryDelta > 0 Then
                summary &= " | 목표달성률 개선"
            ElseIf Math.Abs(primaryDelta) < 0.0001R Then
                summary &= " | 목표달성률 유지"
            End If

            If secondaryDelta > 0 Then
                summary &= " | 순수익 우위"
            End If

            If drawdownDelta < 0 Then
                summary &= " | 손실구간 확대"
            ElseIf drawdownDelta > 0 Then
                summary &= " | 손실구간 안정"
            End If

            Return summary
        End Function

        Private Function BuildStatusText(result As StrategyLabResult, contextLabel As String) As String
            If result Is Nothing OrElse result.Definition Is Nothing OrElse result.Report Is Nothing Then
                Return $"{contextLabel} | no result"
            End If

            Dim versionTag = If(String.IsNullOrWhiteSpace(result.Definition.VersionTag), "Draft", result.Definition.VersionTag)
            Dim versionType = result.Definition.VersionType.ToString()
            Dim avgText = result.Report.AverageReturnRate.ToString("P2")
            Dim secondaryText = result.Report.SecondaryMetric.ToString("P2")

            If _baselineResult IsNot Nothing AndAlso _baselineResult.Report IsNot Nothing AndAlso
               Not String.Equals(versionType, StrategyVersionType.Base.ToString(), StringComparison.OrdinalIgnoreCase) Then
                Dim avgDelta = result.Report.AverageReturnRate - _baselineResult.Report.AverageReturnRate
                Return $"{contextLabel} | {versionTag} [{versionType}] | Avg {avgText} ({avgDelta:+0.00%;-0.00%;0.00%}) | Net {secondaryText}"
            End If

            Return $"{contextLabel} | {versionTag} [{versionType}] | Avg {avgText} | Net {secondaryText}"
        End Function

        Private Sub ClearIndicatorOverlaySeries()
            Dim keepNames = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"Candles", "Trades", "BaselineTrades"}
            For i = _chart.Series.Count - 1 To 0 Step -1
                Dim name = _chart.Series(i).Name
                If keepNames.Contains(name) Then Continue For
                _chart.Series.RemoveAt(i)
            Next
        End Sub

        Private Sub RenderIndicatorOverlays(report As StrategyBaselineReport)
            Dim definition = _lastResult?.Definition
            If definition Is Nothing OrElse definition.Indicators Is Nothing OrElse definition.Indicators.Count = 0 Then
                _lblIndicatorContext.Text = "Indicators | none"
                Return
            End If

            Dim indicatorNames As New List(Of String)
            For Each indicator In definition.Indicators
                If indicator Is Nothing OrElse String.IsNullOrWhiteSpace(indicator.IndicatorType) Then Continue For
                indicatorNames.Add(indicator.IndicatorType)

                Select Case indicator.IndicatorType
                    Case "JMA"
                        AddLineOverlaySeries($"JMA_{indicatorNames.Count}", "Price", report, ComputeEma(report.Candles, GetParameter(indicator, "length", 14)))
                    Case "VWAP"
                        AddLineOverlaySeries($"VWAP_{indicatorNames.Count}", "Price", report, ComputeVwap(report.Candles))
                    Case "SuperTrend"
                        AddLineOverlaySeries($"SuperTrend_{indicatorNames.Count}", "Price", report, ComputeSuperTrendProxy(report.Candles, GetParameter(indicator, "atrPeriod", 10), GetParameter(indicator, "multiplier", 3)))
                    Case "MACD"
                        Dim macd = ComputeMacd(report.Candles,
                                               GetParameter(indicator, "fast", 12),
                                               GetParameter(indicator, "slow", 26),
                                               GetParameter(indicator, "signal", 9))
                        AddHistogramSeries($"MACDHist_{indicatorNames.Count}", "Oscillator", report, macd.Histogram, Color.FromArgb(90, 120, 200, 255))
                        AddLineOverlaySeries($"MACD_{indicatorNames.Count}", "Oscillator", report, macd.Macd)
                        AddLineOverlaySeries($"MACDSignal_{indicatorNames.Count}", "Oscillator", report, macd.Signal)
                    Case "RSI"
                        AddLineOverlaySeries($"RSI_{indicatorNames.Count}", "Oscillator", report, ComputeRsi(report.Candles, GetParameter(indicator, "period", 14)))
                    Case "Volume"
                        AddColumnSeries($"Volume_{indicatorNames.Count}", "Volume", report, report.Candles.Select(Function(c) c.Volume).ToList(), Color.FromArgb(90, 140, 140, 220))
                    Case "VolumeMA"
                        AddLineOverlaySeries($"VolumeMA_{indicatorNames.Count}", "Volume", report, ComputeSimpleMovingAverage(report.Candles.Select(Function(c) c.Volume).ToList(), GetParameter(indicator, "period", 20)))
                    Case "VolumeMASlope"
                        AddLineOverlaySeries($"VolumeSlope_{indicatorNames.Count}", "Oscillator", report, ComputeSlopeSeries(ComputeSimpleMovingAverage(report.Candles.Select(Function(c) c.Volume).ToList(), GetParameter(indicator, "period", 20)),
                                                                                                                              GetParameter(indicator, "slopeLookback", 3)))
                End Select
            Next

            _lblIndicatorContext.Text = $"Indicators | {String.Join(", ", indicatorNames.Distinct(StringComparer.OrdinalIgnoreCase))}"
        End Sub

        Private Sub AddLineOverlaySeries(seriesName As String, chartAreaName As String, report As StrategyBaselineReport, values As List(Of Double))
            If report Is Nothing OrElse report.Candles Is Nothing OrElse values Is Nothing Then Return
            If report.Candles.Count = 0 OrElse values.Count <> report.Candles.Count Then Return

            Dim series As New Series(seriesName) With {
                .ChartType = SeriesChartType.Line,
                .BorderWidth = 2,
                .XValueType = ChartValueType.DateTime,
                .ChartArea = chartAreaName
            }
            series.Color = ResolveOverlayColor(seriesName)

            For i = 0 To report.Candles.Count - 1
                series.Points.AddXY(report.Candles(i).Time, values(i))
            Next

            _chart.Series.Add(series)
        End Sub

        Private Sub AddHistogramSeries(seriesName As String, chartAreaName As String, report As StrategyBaselineReport, values As List(Of Double), color As Color)
            If report Is Nothing OrElse report.Candles Is Nothing OrElse values Is Nothing Then Return
            If report.Candles.Count = 0 OrElse values.Count <> report.Candles.Count Then Return

            Dim series As New Series(seriesName) With {
                .ChartType = SeriesChartType.Column,
                .XValueType = ChartValueType.DateTime,
                .ChartArea = chartAreaName,
                .Color = color
            }
            For i = 0 To report.Candles.Count - 1
                series.Points.AddXY(report.Candles(i).Time, values(i))
            Next
            _chart.Series.Add(series)
        End Sub

        Private Sub AddColumnSeries(seriesName As String, chartAreaName As String, report As StrategyBaselineReport, values As List(Of Double), color As Color)
            AddHistogramSeries(seriesName, chartAreaName, report, values, color)
        End Sub

        Private Shared Function ResolveOverlayColor(seriesName As String) As Color
            If seriesName.StartsWith("JMA", StringComparison.OrdinalIgnoreCase) Then Return Color.Orange
            If seriesName.StartsWith("VWAP", StringComparison.OrdinalIgnoreCase) Then Return Color.DeepSkyBlue
            If seriesName.StartsWith("SuperTrend", StringComparison.OrdinalIgnoreCase) Then Return Color.LimeGreen
            If seriesName.StartsWith("MACDSignal", StringComparison.OrdinalIgnoreCase) Then Return Color.Gold
            If seriesName.StartsWith("MACD", StringComparison.OrdinalIgnoreCase) Then Return Color.MediumPurple
            If seriesName.StartsWith("RSI", StringComparison.OrdinalIgnoreCase) Then Return Color.HotPink
            If seriesName.StartsWith("VolumeMA", StringComparison.OrdinalIgnoreCase) Then Return Color.WhiteSmoke
            If seriesName.StartsWith("VolumeSlope", StringComparison.OrdinalIgnoreCase) Then Return Color.LightGreen
            Return Color.Gainsboro
        End Function

        Private Shared Function GetParameter(indicator As StrategyIndicatorDefinition, key As String, defaultValue As Integer) As Integer
            If indicator Is Nothing OrElse indicator.Parameters Is Nothing OrElse Not indicator.Parameters.ContainsKey(key) Then
                Return defaultValue
            End If
            Return CInt(Math.Round(indicator.Parameters(key)))
        End Function

        Private Shared Function ComputeEma(candles As List(Of LabCandle), period As Integer) As List(Of Double)
            Dim results As New List(Of Double)
            If candles Is Nothing OrElse candles.Count = 0 Then Return results

            Dim safePeriod = Math.Max(1, period)
            Dim multiplier = 2.0R / (safePeriod + 1.0R)
            Dim ema = candles(0).Close
            For Each candle In candles
                ema = ((candle.Close - ema) * multiplier) + ema
                results.Add(ema)
            Next
            Return results
        End Function

        Private Shared Function ComputeVwap(candles As List(Of LabCandle)) As List(Of Double)
            Dim results As New List(Of Double)
            If candles Is Nothing OrElse candles.Count = 0 Then Return results

            Dim cumulativePriceVolume As Double = 0
            Dim cumulativeVolume As Double = 0
            For Each candle In candles
                Dim typicalPrice = (candle.High + candle.Low + candle.Close) / 3.0R
                cumulativePriceVolume += typicalPrice * Math.Max(1.0R, candle.Volume)
                cumulativeVolume += Math.Max(1.0R, candle.Volume)
                results.Add(cumulativePriceVolume / cumulativeVolume)
            Next
            Return results
        End Function

        Private Shared Function ComputeSimpleMovingAverage(values As List(Of Double), period As Integer) As List(Of Double)
            Dim results As New List(Of Double)
            If values Is Nothing OrElse values.Count = 0 Then Return results

            Dim safePeriod = Math.Max(1, period)
            Dim window As New Queue(Of Double)
            Dim sum As Double = 0
            For Each value In values
                window.Enqueue(value)
                sum += value
                If window.Count > safePeriod Then
                    sum -= window.Dequeue()
                End If
                results.Add(sum / window.Count)
            Next
            Return results
        End Function

        Private Shared Function ComputeSlopeSeries(values As List(Of Double), lookback As Integer) As List(Of Double)
            Dim results As New List(Of Double)
            If values Is Nothing OrElse values.Count = 0 Then Return results

            Dim safeLookback = Math.Max(1, lookback)
            For i = 0 To values.Count - 1
                Dim baseIndex = Math.Max(0, i - safeLookback)
                results.Add(values(i) - values(baseIndex))
            Next
            Return results
        End Function

        Private Shared Function ComputeMacd(candles As List(Of LabCandle), fastPeriod As Integer, slowPeriod As Integer, signalPeriod As Integer) As MacdOverlayData
            Dim closeValues = candles.Select(Function(c) c.Close).ToList()
            Dim fast = ComputeEma(candles, fastPeriod)
            Dim slow = ComputeEma(candles, slowPeriod)
            Dim macdLine As New List(Of Double)
            For i = 0 To closeValues.Count - 1
                macdLine.Add(fast(i) - slow(i))
            Next

            Dim signalLine = ComputeEma(macdLine.Select(Function(v) New LabCandle With {.Close = v}).ToList(), signalPeriod)
            Dim histogram As New List(Of Double)
            For i = 0 To macdLine.Count - 1
                histogram.Add(macdLine(i) - signalLine(i))
            Next

            Return New MacdOverlayData With {
                .Macd = macdLine,
                .Signal = signalLine,
                .Histogram = histogram
            }
        End Function

        Private Shared Function ComputeRsi(candles As List(Of LabCandle), period As Integer) As List(Of Double)
            Dim results As New List(Of Double)
            If candles Is Nothing OrElse candles.Count = 0 Then Return results

            Dim safePeriod = Math.Max(2, period)
            Dim gains As Double = 0
            Dim losses As Double = 0
            results.Add(50)

            For i = 1 To candles.Count - 1
                Dim changeValue = candles(i).Close - candles(i - 1).Close
                gains = ((gains * (safePeriod - 1)) + Math.Max(0, changeValue)) / safePeriod
                losses = ((losses * (safePeriod - 1)) + Math.Max(0, -changeValue)) / safePeriod
                If losses <= 0 Then
                    results.Add(100)
                Else
                    Dim rs = gains / losses
                    results.Add(100 - (100 / (1 + rs)))
                End If
            Next

            Return results
        End Function

        Private Shared Function ComputeSuperTrendProxy(candles As List(Of LabCandle), atrPeriod As Integer, multiplier As Integer) As List(Of Double)
            Dim results As New List(Of Double)
            If candles Is Nothing OrElse candles.Count = 0 Then Return results

            Dim emaValues = ComputeEma(candles, Math.Max(2, atrPeriod))
            For i = 0 To candles.Count - 1
                Dim bandOffset = Math.Max(1.0R, (candles(i).High - candles(i).Low) * multiplier * 0.25R)
                results.Add(emaValues(i) - bandOffset)
            Next
            Return results
        End Function

        Private Sub ApplyChartAxisRange(report As StrategyBaselineReport)
            If report Is Nothing OrElse report.Candles Is Nothing OrElse report.Candles.Count = 0 Then Return

            Dim area = _chart.ChartAreas("Price")
            If area Is Nothing Then Return

            Dim minTime = report.Candles.Min(Function(c) c.Time)
            Dim maxTime = report.Candles.Max(Function(c) c.Time)
            Dim minPrice = report.Candles.Min(Function(c) c.Low)
            Dim maxPrice = report.Candles.Max(Function(c) c.High)

            If maxPrice <= minPrice Then
                maxPrice = minPrice + 1
            End If

            Dim priceMargin = (maxPrice - minPrice) * 0.08R
            If priceMargin <= 0 Then priceMargin = Math.Max(1.0R, maxPrice * 0.01R)

            area.AxisY.Minimum = minPrice - priceMargin
            area.AxisY.Maximum = maxPrice + priceMargin

            Dim timeMarginHours = Math.Max(1.0R, (maxTime - minTime).TotalHours * 0.04R)
            area.AxisX.Minimum = minTime.AddHours(-timeMarginHours).ToOADate()
            area.AxisX.Maximum = maxTime.AddHours(timeMarginHours).ToOADate()
            area.RecalculateAxesScale()
        End Sub

        Private Sub OnChartMouseMove(sender As Object, e As MouseEventArgs)
            If _lastResult Is Nothing OrElse _lastResult.Report Is Nothing OrElse _lastResult.Report.Candles Is Nothing OrElse _lastResult.Report.Candles.Count = 0 Then
                _lblCrosshairInfo.Text = "Crosshair | no candle data"
                Return
            End If

            Dim area = _chart.ChartAreas("Price")
            If area Is Nothing Then Return

            Try
                Dim xValue = area.AxisX.PixelPositionToValue(e.X)
                Dim yValue = area.AxisY.PixelPositionToValue(e.Y)
                area.CursorX.SetCursorPosition(xValue)
                area.CursorY.SetCursorPosition(yValue)

                Dim nearest = GetNearestCandle(DateTime.FromOADate(xValue))
                If nearest Is Nothing Then
                    _lblCrosshairInfo.Text = "Crosshair | no candle data"
                    Return
                End If

                Dim indicatorText = BuildCrosshairIndicatorSnapshot(nearest.Time)
                _lblCrosshairInfo.Text = $"Crosshair | {nearest.Time:MM-dd HH:mm} | O {nearest.Open:N0} H {nearest.High:N0} L {nearest.Low:N0} C {nearest.Close:N0}{indicatorText}"
            Catch
                _lblCrosshairInfo.Text = "Crosshair | out of chart range"
            End Try
        End Sub

        Private Sub OnChartMouseLeave(sender As Object, e As EventArgs)
            Dim area = _chart.ChartAreas("Price")
            If area IsNot Nothing Then
                area.CursorX.Position = Double.NaN
                area.CursorY.Position = Double.NaN
            End If
            _lblCrosshairInfo.Text = "Crosshair | move mouse over chart"
        End Sub

        Private Function GetNearestCandle(targetTime As DateTime) As LabCandle
            Dim candles = _lastResult?.Report?.Candles
            If candles Is Nothing OrElse candles.Count = 0 Then Return Nothing

            Dim nearest As LabCandle = Nothing
            Dim nearestDistance As Double = Double.MaxValue
            For Each candle In candles
                Dim distance = Math.Abs((candle.Time - targetTime).TotalSeconds)
                If distance < nearestDistance Then
                    nearest = candle
                    nearestDistance = distance
                End If
            Next
            Return nearest
        End Function

        Private Function BuildCrosshairIndicatorSnapshot(targetTime As DateTime) As String
            Dim definition = _lastResult?.Definition
            Dim report = _lastResult?.Report
            If definition Is Nothing OrElse report Is Nothing OrElse report.Candles Is Nothing OrElse report.Candles.Count = 0 Then
                Return ""
            End If

            Dim index = report.Candles.FindIndex(Function(c) c.Time = targetTime)
            If index < 0 Then Return ""

            Dim parts As New List(Of String)
            For Each indicator In definition.Indicators
                If indicator Is Nothing OrElse String.IsNullOrWhiteSpace(indicator.IndicatorType) Then Continue For
                Select Case indicator.IndicatorType
                    Case "JMA"
                        Dim values = ComputeEma(report.Candles, GetParameter(indicator, "length", 14))
                        parts.Add($"JMA {values(index):N0}")
                    Case "VWAP"
                        Dim values = ComputeVwap(report.Candles)
                        parts.Add($"VWAP {values(index):N0}")
                    Case "SuperTrend"
                        Dim values = ComputeSuperTrendProxy(report.Candles, GetParameter(indicator, "atrPeriod", 10), GetParameter(indicator, "multiplier", 3))
                        parts.Add($"SuperTrend {values(index):N0}")
                    Case "MACD"
                        Dim values = ComputeMacd(report.Candles,
                                                 GetParameter(indicator, "fast", 12),
                                                 GetParameter(indicator, "slow", 26),
                                                 GetParameter(indicator, "signal", 9))
                        parts.Add($"MACD {values.Macd(index):N2}/{values.Signal(index):N2}")
                    Case "RSI"
                        Dim values = ComputeRsi(report.Candles, GetParameter(indicator, "period", 14))
                        parts.Add($"RSI {values(index):N1}")
                    Case "VolumeMA"
                        Dim values = ComputeSimpleMovingAverage(report.Candles.Select(Function(c) c.Volume).ToList(), GetParameter(indicator, "period", 20))
                        parts.Add($"VolMA {values(index):N0}")
                    Case "VolumeMASlope"
                        Dim values = ComputeSlopeSeries(ComputeSimpleMovingAverage(report.Candles.Select(Function(c) c.Volume).ToList(), GetParameter(indicator, "period", 20)),
                                                        GetParameter(indicator, "slopeLookback", 3))
                        parts.Add($"VolSlope {values(index):N0}")
                End Select
            Next

            If parts.Count = 0 Then Return ""
            Return " | " & String.Join(" | ", parts)
        End Function

        Private Sub UpdateChartContextLabel()
            If _lastResult Is Nothing OrElse _lastResult.Definition Is Nothing OrElse _lastResult.Report Is Nothing Then
                _lblChartContext.Text = "Chart | no active version"
                Return
            End If

            Dim versionTag = If(String.IsNullOrWhiteSpace(_lastResult.Definition.VersionTag), "Draft", _lastResult.Definition.VersionTag)
            Dim versionType = _lastResult.Definition.VersionType.ToString()
            Dim avgText = _lastResult.Report.AverageReturnRate.ToString("P2")

            If _baselineResult IsNot Nothing AndAlso _baselineResult.Report IsNot Nothing AndAlso
               Not String.Equals(versionType, StrategyVersionType.Base.ToString(), StringComparison.OrdinalIgnoreCase) Then
                Dim delta = _lastResult.Report.AverageReturnRate - _baselineResult.Report.AverageReturnRate
                _lblChartContext.Text = $"Chart | {versionTag} [{versionType}] | Avg {avgText} ({delta:+0.00%;-0.00%;0.00%}) vs Base"
                Return
            End If

            _lblChartContext.Text = $"Chart | {versionTag} [{versionType}] | Avg {avgText}"
        End Sub

        Private Sub UpdatePromotionLabel()
            Dim record = _candidateRecords.FirstOrDefault(Function(item) String.Equals(item.CandidateId, _promotionCandidateId, StringComparison.OrdinalIgnoreCase))
            If record Is Nothing OrElse record.Result Is Nothing Then
                _lblPromotionCandidate.Text = "Promotion candidate: none"
                Return
            End If

            _lblPromotionCandidate.Text = $"Promotion candidate: {record.VersionTag} | {record.Result.Definition.Name}"
        End Sub

        Private Function ResolveStrategyLineId(result As StrategyLabResult) As String
            If result Is Nothing OrElse result.Definition Is Nothing Then
                Return Guid.NewGuid().ToString("N")
            End If

            If Not String.IsNullOrWhiteSpace(result.Definition.StrategyLineId) Then
                Return result.Definition.StrategyLineId
            End If

            If _baselineResult IsNot Nothing AndAlso _baselineResult.Definition IsNot Nothing AndAlso
               Not String.IsNullOrWhiteSpace(_baselineResult.Definition.StrategyLineId) Then
                Return _baselineResult.Definition.StrategyLineId
            End If

            Return Guid.NewGuid().ToString("N")
        End Function

        Private Function ResolveActiveParentVersionId() As String
            If String.IsNullOrWhiteSpace(_activeCandidateId) Then
                Return If(_baselineResult?.Definition?.StrategyVersionId, "")
            End If

            Dim activeRecord = _candidateRecords.FirstOrDefault(Function(item) String.Equals(item.CandidateId, _activeCandidateId, StringComparison.OrdinalIgnoreCase))
            If activeRecord Is Nothing Then
                Return If(_baselineResult?.Definition?.StrategyVersionId, "")
            End If

            Return activeRecord.StrategyVersionId
        End Function

        Private Function BuildCandidateChangeSummary(parentCandidateId As String) As String
            If String.Equals(parentCandidateId, "baseline", StringComparison.OrdinalIgnoreCase) Then
                Return "Derived from baseline prompt refinement"
            End If

            Return $"Derived from candidate {parentCandidateId}"
        End Function

        Private Sub UpsertBaseRecord(result As StrategyLabResult)
            If result Is Nothing OrElse result.Definition Is Nothing OrElse result.Report Is Nothing Then Return

            Dim existing = _candidateRecords.FirstOrDefault(Function(item) item IsNot Nothing AndAlso item.VersionType = StrategyVersionType.Base)
            If existing Is Nothing Then
                existing = New StrategyLabCandidateRecord With {
                    .CandidateId = Guid.NewGuid().ToString("N"),
                    .VersionType = StrategyVersionType.Base
                }
                _candidateRecords.Insert(0, existing)
            End If

            existing.ParentCandidateId = ""
            existing.StrategyLineId = result.Definition.StrategyLineId
            existing.StrategyVersionId = result.Definition.StrategyVersionId
            existing.ParentVersionId = result.Definition.ParentVersionId
            existing.VersionTag = result.Definition.VersionTag
            existing.SourcePrompt = result.Definition.Prompt
            existing.ChangeSummary = result.Definition.ChangeSummary
            existing.AverageReturnRate = result.Report.AverageReturnRate
            existing.SavedAt = DateTime.Now
            existing.Result = CloneResult(result)
        End Sub

        Private Sub ApplySuggestionAndEvaluate(suggestion As StrategyImprovementSuggestion)
            Dim improvedPrompt = BuildImprovedPrompt(_txtPrompt.Text, suggestion)
            _txtPrompt.Text = improvedPrompt
            AppendHistory("System", $"Applied suggestion: {suggestion.Title}")
            AppendHistory("System", $"Prompt hint: {suggestion.PromptHint}")
            EvaluatePrompt(improvedPrompt, True)
            OnSaveCandidate(Me, EventArgs.Empty)
        End Sub

        Private Shared Function BuildImprovedPrompt(currentPrompt As String, suggestion As StrategyImprovementSuggestion) As String
            Dim basePrompt = If(currentPrompt, "").Trim()
            Dim addition = If(suggestion?.PromptHint, "").Trim()
            If addition.Length = 0 Then Return basePrompt
            If basePrompt.IndexOf(addition, StringComparison.OrdinalIgnoreCase) >= 0 Then Return basePrompt
            If basePrompt.Length = 0 Then Return addition
            Return $"{basePrompt} {addition}"
        End Function

        Private Function GetSelectedSuggestion() As StrategyImprovementSuggestion
            If _gridSuggestions.SelectedRows.Count = 0 Then Return Nothing
            Return TryCast(_gridSuggestions.SelectedRows(0).Tag, StrategyImprovementSuggestion)
        End Function

        Private Shared Function CloneResult(source As StrategyLabResult) As StrategyLabResult
            If source Is Nothing Then Return Nothing
            Dim json = Newtonsoft.Json.JsonConvert.SerializeObject(source)
            Return Newtonsoft.Json.JsonConvert.DeserializeObject(Of StrategyLabResult)(json)
        End Function

        Private Sub AppendHistory(role As String, message As String)
            If _txtHistory.TextLength > 0 Then
                _txtHistory.AppendText(Environment.NewLine & Environment.NewLine)
            End If
            _txtHistory.AppendText($"[{DateTime.Now:HH:mm:ss}] {role}" & Environment.NewLine & message)
        End Sub

        Private Class CandidateListItem
            Public Property Title As String = ""
            Public Property Record As StrategyLabCandidateRecord

            Public Overrides Function ToString() As String
                Return Title
            End Function
        End Class
    End Class
End Namespace
