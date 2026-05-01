Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Text
Imports System.Windows.Forms

Namespace SimTrade

    Public Class SimTradeStockDataDebugForm
        Inherits Form

        Private ReadOnly _state As StockState
        Private ReadOnly _tab As TabControl
        Private ReadOnly _btnRefresh As Button
        Private ReadOnly _btnCopy As Button
        Private ReadOnly _lblTitle As Label

        Private ReadOnly _gridSummary As DataGridView
        Private ReadOnly _gridMinuteCandles As DataGridView
        Private ReadOnly _gridRawTicks As DataGridView
        Private ReadOnly _gridTickMap As DataGridView
        Private ReadOnly _gridIndicators As DataGridView
        Private ReadOnly _gridTopN As DataGridView
        Private ReadOnly _gridTradeState As DataGridView
        Private ReadOnly _txtRawDump As TextBox

        Public Sub New(state As StockState)
            _state = state

            Me.Text = "종목 데이터 확인"
            If _state IsNot Nothing Then Me.Text = "종목 데이터 확인 - [" & _state.Code & "] " & _state.Name
            Me.Size = New Size(1280, 820)
            Me.StartPosition = FormStartPosition.CenterParent
            Me.BackColor = Color.FromArgb(30, 30, 30)
            Me.ForeColor = Color.White

            Dim topPanel As New Panel()
            topPanel.Dock = DockStyle.Top
            topPanel.Height = 44
            topPanel.BackColor = Color.FromArgb(45, 45, 48)

            _lblTitle = New Label()
            _lblTitle.Left = 10
            _lblTitle.Top = 11
            _lblTitle.Width = 780
            _lblTitle.Height = 22
            _lblTitle.ForeColor = Color.Cyan
            _lblTitle.Font = New Font("맑은 고딕", 9.0F, FontStyle.Bold)

            _btnRefresh = CreateButton("새로고침", 810, 8)
            AddHandler _btnRefresh.Click, AddressOf OnRefreshClick

            _btnCopy = CreateButton("Raw 복사", 910, 8)
            AddHandler _btnCopy.Click, AddressOf OnCopyClick

            topPanel.Controls.Add(_lblTitle)
            topPanel.Controls.Add(_btnRefresh)
            topPanel.Controls.Add(_btnCopy)

            _tab = New TabControl()
            _tab.Dock = DockStyle.Fill

            _gridSummary = CreateGrid()
            _gridMinuteCandles = CreateGrid()
            _gridRawTicks = CreateGrid()
            _gridTickMap = CreateGrid()
            _gridIndicators = CreateGrid()
            _gridTopN = CreateGrid()
            _gridTradeState = CreateGrid()

            _txtRawDump = New TextBox()
            _txtRawDump.Dock = DockStyle.Fill
            _txtRawDump.Multiline = True
            _txtRawDump.ScrollBars = ScrollBars.Both
            _txtRawDump.WordWrap = False
            _txtRawDump.ReadOnly = True
            _txtRawDump.BackColor = Color.Black
            _txtRawDump.ForeColor = Color.LightGray
            _txtRawDump.Font = New Font("Consolas", 9.0F)

            AddTab("요약", _gridSummary)
            AddTab("캔들(1분봉)", _gridMinuteCandles)
            AddTab("틱원본(30틱)", _gridRawTicks)
            AddTab("틱→1분봉 매핑", _gridTickMap)
            AddTab("지표", _gridIndicators)
            AddTab("TopN 점수", _gridTopN)
            AddTab("매매/신호", _gridTradeState)
            AddTab("Raw Dump", _txtRawDump)

            Me.Controls.Add(_tab)
            Me.Controls.Add(topPanel)

            BuildColumns()
            RefreshAll()
        End Sub

        Private Shared Function CreateButton(text As String, x As Integer, y As Integer) As Button
            Dim btn As New Button()
            btn.Text = text
            btn.Width = 90
            btn.Height = 28
            btn.Left = x
            btn.Top = y
            btn.FlatStyle = FlatStyle.Flat
            btn.BackColor = Color.FromArgb(60, 60, 65)
            btn.ForeColor = Color.White
            Return btn
        End Function

        Private Sub AddTab(title As String, ctrl As Control)
            Dim page As New TabPage(title)
            page.BackColor = Color.FromArgb(30, 30, 30)
            page.Controls.Add(ctrl)
            _tab.TabPages.Add(page)
        End Sub

        Private Sub BuildColumns()
            AddKeyValueColumns(_gridSummary)
            AddCandleColumns(_gridMinuteCandles)
            AddRawTickColumns(_gridRawTicks)
            AddTickMapColumns(_gridTickMap)
            AddIndicatorColumns(_gridIndicators)
            AddKeyValueColumns(_gridTopN)
            AddKeyValueColumns(_gridTradeState)
        End Sub

        Private Sub RefreshAll()
            If _state Is Nothing Then
                _lblTitle.Text = "선택 종목 상태가 없습니다."
                Return
            End If

            Dim rawTicks As List(Of DateTime) = GetRawTickTimestamps()

            _lblTitle.Text = "[" & _state.Code & "] " & _state.Name &
                             " | State=" & _state.State.ToString() &
                             " | 1분봉=" & _state.Candles.Count.ToString() &
                             " | TickBarCount=" & _state.TickBarCount.ToString() &
                             " | RawTicks=" & rawTicks.Count.ToString() &
                             " | LastSignal=" & _state.LastSignal

            RefreshSummary(rawTicks)
            RefreshMinuteCandles()
            RefreshRawTicks(rawTicks)
            RefreshTickMap(rawTicks)
            RefreshIndicators()
            RefreshTopN()
            RefreshTradeState()
            RefreshRawDump(rawTicks)
        End Sub

        Private Sub RefreshSummary(rawTicks As List(Of DateTime))
            _gridSummary.Rows.Clear()
            AddKV(_gridSummary, "Code", _state.Code)
            AddKV(_gridSummary, "Name", _state.Name)
            AddKV(_gridSummary, "State", _state.State.ToString())
            AddKV(_gridSummary, "CurrentPrice", _state.CurrentPrice.ToString("N0"))
            AddKV(_gridSummary, "ChangeRate", _state.ChangeRate.ToString("F2") & "%")
            AddKV(_gridSummary, "DayVolume", _state.DayVolume.ToString("N0"))
            AddKV(_gridSummary, "DayAmount", _state.DayAmount.ToString("N0"))
            AddKV(_gridSummary, "MinuteCandleCount", _state.Candles.Count.ToString())
            AddKV(_gridSummary, "LastMinuteCandleTime", GetLastCandleTimeText())
            AddKV(_gridSummary, "TickBarCount(State)", _state.TickBarCount.ToString())
            AddKV(_gridSummary, "RawTickTimestampCount", rawTicks.Count.ToString("N0"))
            AddKV(_gridSummary, "RawTickFirst", If(rawTicks.Count > 0, rawTicks(0).ToString("yyyy-MM-dd HH:mm:ss"), "-"))
            AddKV(_gridSummary, "RawTickLast", If(rawTicks.Count > 0, rawTicks(rawTicks.Count - 1).ToString("yyyy-MM-dd HH:mm:ss"), "-"))
            AddKV(_gridSummary, "MinuteCandle.TickCount.Sum", GetMinuteCandleTickCountSum().ToString("N0"))
            AddKV(_gridSummary, "MinuteCandle.NTS.SumAbs", GetMinuteCandleNormalizedTickAbsSum().ToString("F2"))
            AddKV(_gridSummary, "TickSourceDiagnosis", GetTickSourceDiagnosis(rawTicks))
            AddKV(_gridSummary, "TickSum_Normalized", FormatDouble(_state.TickSum_Normalized))
            AddKV(_gridSummary, "TickMA5_Normalized", FormatDouble(_state.TickMA5_Normalized))
            AddKV(_gridSummary, "TickMA20_Normalized", FormatDouble(_state.TickMA20_Normalized))
            AddKV(_gridSummary, "RSI_Value", FormatDouble(_state.RSI_Value))
            AddKV(_gridSummary, "ST_Direction", FormatDouble(_state.ST_Direction))
            AddKV(_gridSummary, "JMA_Direction", FormatDouble(_state.JMA_Direction))
            AddKV(_gridSummary, "OBV_Direction", FormatDouble(_state.OBV_Direction))
            AddKV(_gridSummary, "MACD_Histogram", FormatDouble(_state.MACD_Histogram))
            AddKV(_gridSummary, "Volume_Ratio", FormatDouble(_state.Volume_Ratio))
            AddKV(_gridSummary, "TopNRank", _state.TopNRank.ToString())
            AddKV(_gridSummary, "TopNScore", FormatDouble(_state.TopNScore))
            AddKV(_gridSummary, "TopTickScore", FormatDouble(_state.TopTickScore))
            AddKV(_gridSummary, "TopAmountScore", FormatDouble(_state.TopAmountScore))
            AddKV(_gridSummary, "TopTrendScore", FormatDouble(_state.TopTrendScore))
            AddKV(_gridSummary, "LastSignal", _state.LastSignal)
            AddKV(_gridSummary, "ExclusionReason", _state.ExclusionReason)
        End Sub

        Private Sub RefreshMinuteCandles()
            _gridMinuteCandles.Rows.Clear()
            If _state.Candles Is Nothing Then Return

            For i As Integer = 0 To _state.Candles.Count - 1
                Dim c As CandleItem = _state.Candles(i)
                If c Is Nothing Then Continue For
                _gridMinuteCandles.Rows.Add(i.ToString(), c.Dt.ToString("yyyy-MM-dd HH:mm:ss"),
                                            c.Open.ToString("N0"), c.High.ToString("N0"),
                                            c.Low.ToString("N0"), c.Close.ToString("N0"),
                                            c.Volume.ToString("N0"), c.TickCount.ToString(),
                                            c.NormalizedTickSum.ToString("F2"), DiagnoseMinuteCandleTick(c))
            Next
        End Sub

        Private Sub RefreshRawTicks(rawTicks As List(Of DateTime))
            _gridRawTicks.Rows.Clear()
            If rawTicks Is Nothing OrElse rawTicks.Count = 0 Then
                _gridRawTicks.Rows.Add("-", "-", "-", "TickIntensity_Indicator 내부 원본 tick timestamp 없음")
                Return
            End If

            For i As Integer = 0 To rawTicks.Count - 1
                Dim ts As DateTime = rawTicks(i)
                _gridRawTicks.Rows.Add(i.ToString(), ts.ToString("yyyy-MM-dd HH:mm:ss"), ts.TimeOfDay.ToString(), DiagnoseRawTickTimestamp(ts))
            Next
        End Sub

        Private Sub RefreshTickMap(rawTicks As List(Of DateTime))
            _gridTickMap.Rows.Clear()
            If _state.Candles Is Nothing Then Return

            Dim tickResults As List(Of IndicatorResult) = FindTickIntensityResults()

            For i As Integer = 0 To _state.Candles.Count - 1
                Dim c As CandleItem = _state.Candles(i)
                If c Is Nothing Then Continue For

                Dim rawMatched As Integer = CountRawTicksInMinute(rawTicks, c.Dt)
                Dim tickSumText As String = "-"
                Dim ma5Text As String = "-"
                Dim ma20Text As String = "-"
                Dim indicatorExists As Boolean = False

                If tickResults IsNot Nothing AndAlso i < tickResults.Count Then
                    Dim r As IndicatorResult = tickResults(i)
                    If r IsNot Nothing Then
                        indicatorExists = True
                        tickSumText = FormatSingle(r.Val("TickSum"))
                        ma5Text = FormatSingle(r.Val("MA5"))
                        ma20Text = FormatSingle(r.Val("MA20"))
                    End If
                End If

                _gridTickMap.Rows.Add(i.ToString(), c.Dt.ToString("yyyy-MM-dd HH:mm:ss"),
                                      rawMatched.ToString(), c.TickCount.ToString(), c.NormalizedTickSum.ToString("F2"),
                                      tickSumText, ma5Text, ma20Text, If(indicatorExists, "Y", "N"),
                                      DiagnoseTickMapRow(rawMatched, c, indicatorExists, tickSumText))
            Next
        End Sub

        Private Sub RefreshIndicators()
            _gridIndicators.Rows.Clear()
            If _state.Engine Is Nothing OrElse _state.Engine.Results Is Nothing Then Return

            For Each kv As KeyValuePair(Of String, List(Of IndicatorResult)) In _state.Engine.Results
                Dim indName As String = kv.Key
                Dim list As List(Of IndicatorResult) = kv.Value
                If list Is Nothing Then Continue For

                For i As Integer = 0 To list.Count - 1
                    Dim r As IndicatorResult = list(i)
                    If r Is Nothing Then Continue For
                    If r.Values Is Nothing OrElse r.Values.Count = 0 Then
                        _gridIndicators.Rows.Add(indName, r.Index.ToString(), r.PanelIndex.ToString(), "", "")
                    Else
                        For Each v As KeyValuePair(Of String, Single) In r.Values
                            _gridIndicators.Rows.Add(indName, r.Index.ToString(), r.PanelIndex.ToString(), v.Key, FormatSingle(v.Value))
                        Next
                    End If
                Next
            Next
        End Sub

        Private Sub RefreshTopN()
            _gridTopN.Rows.Clear()
            AddKV(_gridTopN, "TopNRank", _state.TopNRank.ToString())
            AddKV(_gridTopN, "TopNScore", FormatDouble(_state.TopNScore))
            AddKV(_gridTopN, "TopTickScore", FormatDouble(_state.TopTickScore))
            AddKV(_gridTopN, "TopAmountScore", FormatDouble(_state.TopAmountScore))
            AddKV(_gridTopN, "TopTrendScore", FormatDouble(_state.TopTrendScore))
            AddKV(_gridTopN, "TickSum_Normalized", FormatDouble(_state.TickSum_Normalized))
            AddKV(_gridTopN, "TickMA5_Normalized", FormatDouble(_state.TickMA5_Normalized))
            AddKV(_gridTopN, "TickMA20_Normalized", FormatDouble(_state.TickMA20_Normalized))
            AddKV(_gridTopN, "DayAmount", _state.DayAmount.ToString("N0"))
            AddKV(_gridTopN, "ST_Direction", FormatDouble(_state.ST_Direction))
            AddKV(_gridTopN, "JMA_Direction", FormatDouble(_state.JMA_Direction))
            AddKV(_gridTopN, "RSI_Value", FormatDouble(_state.RSI_Value))
            AddKV(_gridTopN, "ChangeRate", _state.ChangeRate.ToString("F2") & "%")
            AddKV(_gridTopN, "Volume_Ratio", FormatDouble(_state.Volume_Ratio))
        End Sub

        Private Sub RefreshTradeState()
            _gridTradeState.Rows.Clear()
            AddKV(_gridTradeState, "HasPosition", _state.HasPosition.ToString())
            AddKV(_gridTradeState, "BuyPrice", _state.BuyPrice.ToString("N0"))
            AddKV(_gridTradeState, "BuyQty", _state.BuyQty.ToString())
            AddKV(_gridTradeState, "BuyTime", If(_state.BuyTime = DateTime.MinValue, "-", _state.BuyTime.ToString("yyyy-MM-dd HH:mm:ss")))
            AddKV(_gridTradeState, "HighSinceBuy", _state.HighSinceBuy.ToString("N0"))
            AddKV(_gridTradeState, "CurrentPnLRate", _state.CurrentPnLRate.ToString("F2") & "%")
            AddKV(_gridTradeState, "LastSignal", _state.LastSignal)
            AddKV(_gridTradeState, "LastSignalTime", If(_state.LastSignalTime = DateTime.MinValue, "-", _state.LastSignalTime.ToString("yyyy-MM-dd HH:mm:ss")))
            AddKV(_gridTradeState, "LastBuyTime", If(_state.LastBuyTime = DateTime.MinValue, "-", _state.LastBuyTime.ToString("yyyy-MM-dd HH:mm:ss")))
            AddKV(_gridTradeState, "ExclusionReason", _state.ExclusionReason)

            If _state.FilterResults IsNot Nothing Then
                For Each kv As KeyValuePair(Of String, Boolean) In _state.FilterResults
                    AddKV(_gridTradeState, "Filter." & kv.Key, kv.Value.ToString())
                Next
            End If
        End Sub

        Private Sub RefreshRawDump(rawTicks As List(Of DateTime))
            Dim sb As New StringBuilder()
            If _state Is Nothing Then
                _txtRawDump.Text = "NO STATE"
                Return
            End If

            sb.AppendLine("=== StockState ===")
            sb.AppendLine("Code=" & _state.Code)
            sb.AppendLine("Name=" & _state.Name)
            sb.AppendLine("State=" & _state.State.ToString())
            sb.AppendLine("CurrentPrice=" & _state.CurrentPrice.ToString())
            sb.AppendLine("ChangeRate=" & _state.ChangeRate.ToString("F4"))
            sb.AppendLine("MinuteCandleCount=" & _state.Candles.Count.ToString())
            sb.AppendLine("LastMinuteCandleTime=" & GetLastCandleTimeText())
            sb.AppendLine("TickBarCount(State)=" & _state.TickBarCount.ToString())
            sb.AppendLine("RawTickTimestampCount=" & rawTicks.Count.ToString())
            If rawTicks.Count > 0 Then
                sb.AppendLine("RawTickFirst=" & rawTicks(0).ToString("yyyy-MM-dd HH:mm:ss"))
                sb.AppendLine("RawTickLast=" & rawTicks(rawTicks.Count - 1).ToString("yyyy-MM-dd HH:mm:ss"))
            End If
            sb.AppendLine("MinuteCandle.TickCount.Sum=" & GetMinuteCandleTickCountSum().ToString())
            sb.AppendLine("MinuteCandle.NTS.SumAbs=" & GetMinuteCandleNormalizedTickAbsSum().ToString("F2"))
            sb.AppendLine("TickSourceDiagnosis=" & GetTickSourceDiagnosis(rawTicks))
            sb.AppendLine("TickSum_Normalized=" & FormatDouble(_state.TickSum_Normalized))
            sb.AppendLine("TickMA5_Normalized=" & FormatDouble(_state.TickMA5_Normalized))
            sb.AppendLine("TickMA20_Normalized=" & FormatDouble(_state.TickMA20_Normalized))
            sb.AppendLine("TopNRank=" & _state.TopNRank.ToString())
            sb.AppendLine("TopNScore=" & FormatDouble(_state.TopNScore))
            sb.AppendLine("LastSignal=" & _state.LastSignal)
            sb.AppendLine()

            sb.AppendLine("=== Registered Indicators ===")
            If _state.Engine IsNot Nothing Then
                Dim indicators As List(Of IIndicator) = _state.Engine.GetAll()
                For i As Integer = 0 To indicators.Count - 1
                    Dim ind As IIndicator = indicators(i)
                    If ind IsNot Nothing Then sb.AppendLine(i.ToString() & ": " & ind.Name & " / " & ind.DisplayName & " / Panel=" & ind.PanelIndex.ToString())
                Next
            End If
            sb.AppendLine()

            sb.AppendLine("=== Indicator Results Summary ===")
            If _state.Engine IsNot Nothing AndAlso _state.Engine.Results IsNot Nothing Then
                For Each kv As KeyValuePair(Of String, List(Of IndicatorResult)) In _state.Engine.Results
                    Dim cnt As Integer = If(kv.Value Is Nothing, 0, kv.Value.Count)
                    sb.AppendLine(kv.Key & " => " & cnt.ToString() & " rows")
                    If kv.Value IsNot Nothing AndAlso kv.Value.Count > 0 Then sb.AppendLine("  Last: " & kv.Value(kv.Value.Count - 1).ToString())
                Next
            End If
            sb.AppendLine()

            sb.AppendLine("=== Recent Raw Tick Timestamps ===")
            Dim startRaw As Integer = Math.Max(0, rawTicks.Count - 50)
            For i As Integer = startRaw To rawTicks.Count - 1
                sb.AppendLine(i.ToString() & " " & rawTicks(i).ToString("yyyy-MM-dd HH:mm:ss"))
            Next
            sb.AppendLine()

            sb.AppendLine("=== Recent Minute Candles ===")
            If _state.Candles IsNot Nothing Then
                Dim startIndex As Integer = Math.Max(0, _state.Candles.Count - 30)
                For i As Integer = startIndex To _state.Candles.Count - 1
                    Dim c As CandleItem = _state.Candles(i)
                    sb.AppendLine(i.ToString() & " " & c.Dt.ToString("yyyy-MM-dd HH:mm:ss") &
                                  " O=" & c.Open.ToString("N0") & " H=" & c.High.ToString("N0") &
                                  " L=" & c.Low.ToString("N0") & " C=" & c.Close.ToString("N0") &
                                  " V=" & c.Volume.ToString() & " TC=" & c.TickCount.ToString() &
                                  " NTS=" & c.NormalizedTickSum.ToString("F2"))
                Next
            End If

            _txtRawDump.Text = sb.ToString()
        End Sub

        Private Function GetRawTickTimestamps() As List(Of DateTime)
            Dim ticks As New List(Of DateTime)()
            If _state Is Nothing OrElse _state.Engine Is Nothing Then Return ticks

            Dim indicators As List(Of IIndicator) = _state.Engine.GetAll()
            For i As Integer = 0 To indicators.Count - 1
                Dim tickInd As TickIntensity_Indicator = TryCast(indicators(i), TickIntensity_Indicator)
                If tickInd IsNot Nothing Then
                    ticks = tickInd.GetTickBarsSnapshot()
                    ticks.Sort()
                    Return ticks
                End If
            Next
            Return ticks
        End Function

        Private Function FindTickIntensityResults() As List(Of IndicatorResult)
            If _state Is Nothing OrElse _state.Engine Is Nothing OrElse _state.Engine.Results Is Nothing Then Return Nothing
            For Each kv As KeyValuePair(Of String, List(Of IndicatorResult)) In _state.Engine.Results
                If kv.Key IsNot Nothing AndAlso kv.Key.StartsWith("TICKINT_", StringComparison.OrdinalIgnoreCase) Then Return kv.Value
            Next
            Return Nothing
        End Function

        Private Function GetLastCandleTimeText() As String
            If _state Is Nothing OrElse _state.Candles Is Nothing OrElse _state.Candles.Count = 0 Then Return "-"
            Dim c As CandleItem = _state.Candles(_state.Candles.Count - 1)
            If c Is Nothing OrElse c.Dt = DateTime.MinValue Then Return "-"
            Return c.Dt.ToString("yyyy-MM-dd HH:mm:ss")
        End Function

        Private Function GetMinuteCandleTickCountSum() As Long
            If _state Is Nothing OrElse _state.Candles Is Nothing Then Return 0L
            Dim total As Long = 0L
            For Each c As CandleItem In _state.Candles
                If c IsNot Nothing Then total += CLng(c.TickCount)
            Next
            Return total
        End Function

        Private Function GetMinuteCandleNormalizedTickAbsSum() As Double
            If _state Is Nothing OrElse _state.Candles Is Nothing Then Return 0.0R
            Dim total As Double = 0.0R
            For Each c As CandleItem In _state.Candles
                If c IsNot Nothing Then total += Math.Abs(c.NormalizedTickSum)
            Next
            Return total
        End Function

        Private Function GetTickSourceDiagnosis(rawTicks As List(Of DateTime)) As String
            If _state Is Nothing Then Return "NO_STATE"
            If rawTicks IsNot Nothing AndAlso rawTicks.Count > 0 Then Return "원본 tick timestamp 로드됨"
            If _state.TickBarCount > 0 Then Return "State.TickBarCount 존재, 원본 조회 실패"
            If GetMinuteCandleTickCountSum() > 0L Then Return "1분봉 Candle.TickCount 존재"
            If GetMinuteCandleNormalizedTickAbsSum() > 0.0R Then Return "1분봉 NormalizedTickSum 존재"
            Return "틱/30틱 데이터 미주입 또는 미다운로드 의심"
        End Function

        Private Function CountRawTicksInMinute(rawTicks As List(Of DateTime), candleStart As DateTime) As Integer
            If rawTicks Is Nothing OrElse rawTicks.Count = 0 Then Return 0
            Dim startTod As TimeSpan = candleStart.TimeOfDay
            Dim endTod As TimeSpan = candleStart.AddMinutes(1).TimeOfDay
            Dim cnt As Integer = 0
            For i As Integer = 0 To rawTicks.Count - 1
                Dim tod As TimeSpan = rawTicks(i).TimeOfDay
                If tod >= startTod AndAlso tod < endTod Then cnt += 1
            Next
            Return cnt
        End Function

        Private Shared Function DiagnoseRawTickTimestamp(ts As DateTime) As String
            If ts = DateTime.MinValue Then Return "INVALID"
            Return "OK"
        End Function

        Private Shared Function DiagnoseMinuteCandleTick(c As CandleItem) As String
            If c Is Nothing Then Return "NO_CANDLE"
            If c.TickCount = 0 AndAlso Math.Abs(c.NormalizedTickSum) = 0.0R Then Return "틱데이터 없음"
            If c.TickCount = 0 AndAlso Math.Abs(c.NormalizedTickSum) > 0.0R Then Return "NTS만 존재"
            If c.TickCount > 0 AndAlso Math.Abs(c.NormalizedTickSum) = 0.0R Then Return "TickCount만 존재"
            Return "OK"
        End Function

        Private Shared Function DiagnoseTickMapRow(rawMatched As Integer, c As CandleItem, indicatorExists As Boolean, tickSumText As String) As String
            If c Is Nothing Then Return "NO_CANDLE"
            If Not indicatorExists Then Return "TickIntensity 결과 없음"
            If rawMatched > 0 AndAlso tickSumText = "0.00" Then Return "원본틱 있음 / 지표 TickSum=0"
            If rawMatched = 0 AndAlso tickSumText <> "0.00" AndAlso tickSumText <> "-" Then Return "원본틱 0 / 지표값 존재"
            If rawMatched = 0 Then Return "원본틱 매칭 없음"
            Return "OK"
        End Function

        Private Shared Function FormatDouble(v As Double) As String
            If Double.IsNaN(v) OrElse Double.IsInfinity(v) Then Return "-"
            Return v.ToString("F2")
        End Function

        Private Shared Function FormatSingle(v As Single) As String
            If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then Return "-"
            Return v.ToString("F2")
        End Function

        Private Shared Sub AddKV(grid As DataGridView, key As String, value As String)
            grid.Rows.Add(key, value)
        End Sub

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
            dgv.DefaultCellStyle.ForeColor = Color.White
            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 80, 120)
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(50, 50, 55)
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
            dgv.EnableHeadersVisualStyles = False
            dgv.RowHeadersVisible = False
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            Return dgv
        End Function

        Private Shared Sub AddKeyValueColumns(dgv As DataGridView)
            dgv.Columns.Add("항목", "항목")
            dgv.Columns.Add("값", "값")
            dgv.Columns(0).Width = 220
            dgv.Columns(1).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        End Sub

        Private Shared Sub AddCandleColumns(dgv As DataGridView)
            AddColumn(dgv, "Index", 60)
            AddColumn(dgv, "Dt", 145)
            AddColumn(dgv, "Open", 85)
            AddColumn(dgv, "High", 85)
            AddColumn(dgv, "Low", 85)
            AddColumn(dgv, "Close", 85)
            AddColumn(dgv, "Volume", 95)
            AddColumn(dgv, "TickCount", 80)
            AddColumn(dgv, "NormalizedTickSum", 130)
            AddColumn(dgv, "틱진단", 170, DataGridViewAutoSizeColumnMode.Fill)
        End Sub

        Private Shared Sub AddRawTickColumns(dgv As DataGridView)
            AddColumn(dgv, "Index", 70)
            AddColumn(dgv, "TickTimestamp", 180)
            AddColumn(dgv, "TimeOfDay", 120)
            AddColumn(dgv, "진단", 180, DataGridViewAutoSizeColumnMode.Fill)
        End Sub

        Private Shared Sub AddTickMapColumns(dgv As DataGridView)
            AddColumn(dgv, "Index", 60)
            AddColumn(dgv, "MinuteCandleTime", 150)
            AddColumn(dgv, "RawTickMatched", 110)
            AddColumn(dgv, "Candle.TickCount", 110)
            AddColumn(dgv, "Candle.NTS", 90)
            AddColumn(dgv, "Indicator.TickSum", 110)
            AddColumn(dgv, "Indicator.MA5", 100)
            AddColumn(dgv, "Indicator.MA20", 100)
            AddColumn(dgv, "IndicatorExists", 100)
            AddColumn(dgv, "진단", 220, DataGridViewAutoSizeColumnMode.Fill)
        End Sub

        Private Shared Sub AddIndicatorColumns(dgv As DataGridView)
            AddColumn(dgv, "Indicator", 150)
            AddColumn(dgv, "Index", 60)
            AddColumn(dgv, "Panel", 60)
            AddColumn(dgv, "Key", 120)
            AddColumn(dgv, "Value", 120, DataGridViewAutoSizeColumnMode.Fill)
        End Sub

        Private Shared Sub AddColumn(dgv As DataGridView, headerText As String, width As Integer, Optional autoSizeMode As DataGridViewAutoSizeColumnMode = DataGridViewAutoSizeColumnMode.None)
            Dim idx As Integer = dgv.Columns.Add(headerText, headerText)
            dgv.Columns(idx).Width = width
            dgv.Columns(idx).AutoSizeMode = autoSizeMode
        End Sub

        Private Sub OnRefreshClick(sender As Object, e As EventArgs)
            RefreshAll()
        End Sub

        Private Sub OnCopyClick(sender As Object, e As EventArgs)
            If String.IsNullOrEmpty(_txtRawDump.Text) Then Return
            Clipboard.SetText(_txtRawDump.Text)
        End Sub
    End Class
End Namespace
