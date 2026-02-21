' ═══════════════════════════════════════════════════════════════
' LogForm.vb — 시스템 로그 도킹 폼
' ═══════════════════════════════════════════════════════════════
' 모든 레벨의 로그를 실시간으로 표시.
' 필터링, 자동스크롤, 클리어 기능 포함.
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports [Shared]
Imports WeifenLuo.WinFormsUI.Docking

Public Class LogForm
    Inherits DockFormBase

    Private WithEvents _rtb As RichTextBox
    Private WithEvents _toolbar As ToolStrip
    Private _btnClear As ToolStripButton
    Private _btnAutoScroll As ToolStripButton
    Private _cboFilter As ToolStripComboBox
    Private _txtSearch As ToolStripTextBox
    Private _autoScroll As Boolean = True
    Private _filterLevel As String = "ALL"
    Private _maxLines As Integer = 5000

    ' ─── 로그 레벨별 색상 ───
    Private Shared ReadOnly _colors As New Dictionary(Of String, Color)(StringComparer.OrdinalIgnoreCase) From {
        {"DEBUG", Color.Gray},
        {"INFO", Color.White},
        {"WARN", Color.Orange},
        {"ERROR", Color.Red},
        {"TEST", Color.Cyan},
        {"TRADE", Color.LimeGreen},
        {"COMM", Color.MediumPurple}
    }

    Public Sub New()
        Me.Text = "시스템 로그"
        Me.DockAreas = DockAreas.DockBottom Or DockAreas.DockTop Or DockAreas.Float Or DockAreas.Document
        Me.ShowHint = DockState.DockBottom
        InitControls()
        SubscribeBus()
    End Sub

    Public Overrides ReadOnly Property DefaultDockState As DockState
        Get
            Return DockState.DockBottom
        End Get
    End Property

    Private Sub InitControls()
        ' ── 툴바 ──
        _toolbar = New ToolStrip()
        _toolbar.GripStyle = ToolStripGripStyle.Hidden

        _btnClear = New ToolStripButton("지우기")
        AddHandler _btnClear.Click, Sub(s, e) _rtb.Clear()

        _btnAutoScroll = New ToolStripButton("자동스크롤: ON")
        _btnAutoScroll.CheckOnClick = True
        _btnAutoScroll.Checked = True
        AddHandler _btnAutoScroll.CheckedChanged, Sub(s, e)
                                                      _autoScroll = _btnAutoScroll.Checked
                                                      _btnAutoScroll.Text = $"자동스크롤: {If(_autoScroll, "ON", "OFF")}"
                                                  End Sub

        _cboFilter = New ToolStripComboBox()
        _cboFilter.Items.AddRange({"ALL", "DEBUG", "INFO", "WARN", "ERROR", "TEST", "TRADE", "COMM"})
        _cboFilter.SelectedIndex = 0
        _cboFilter.DropDownStyle = ComboBoxStyle.DropDownList
        AddHandler _cboFilter.SelectedIndexChanged, Sub(s, e) _filterLevel = _cboFilter.Text

        _txtSearch = New ToolStripTextBox()
        _txtSearch.Width = 150
        _txtSearch.ToolTipText = "검색 (Enter)"
        AddHandler _txtSearch.KeyDown, Sub(s, e)
                                           If e.KeyCode = Keys.Enter Then
                                               SearchLog(_txtSearch.Text)
                                           End If
                                       End Sub

        _toolbar.Items.AddRange({_btnClear, New ToolStripSeparator(),
                                  _btnAutoScroll, New ToolStripSeparator(),
                                  New ToolStripLabel("필터:"), _cboFilter,
                                  New ToolStripSeparator(),
                                  New ToolStripLabel("검색:"), _txtSearch})

        ' ── RichTextBox ──
        _rtb = New RichTextBox()
        _rtb.Dock = DockStyle.Fill
        _rtb.ReadOnly = True
        _rtb.BackColor = Color.FromArgb(30, 30, 30)
        _rtb.ForeColor = Color.White
        _rtb.Font = New Font("Consolas", 9)
        _rtb.WordWrap = False
        _rtb.BorderStyle = BorderStyle.None

        Me.Controls.Add(_rtb)
        Me.Controls.Add(_toolbar)
    End Sub

    ' ─── Bus 구독 ───

    Private ReadOnly _handlers As New List(Of KeyValuePair(Of String, Action(Of Msg)))()

    Private Sub SubscribeBus()
        Dim topicsRet = {Topics.LOG_DEBUG, Topics.LOG_INFO, Topics.LOG_WARN, Topics.LOG_ERROR,
                      Topics.LOG_TEST, Topics.LOG_TRADE, Topics.LOG_COMM,
                      Topics.SYS_LOG, Topics.SYS_ERROR}

        For Each t In topicsRet
            Dim handler As Action(Of Msg) = Sub(m) SafeAppendLog(m)
            MessageBus.I.On(t, handler)
            _handlers.Add(New KeyValuePair(Of String, Action(Of Msg))(t, handler))
        Next
    End Sub

    Protected Overrides Sub UnsubscribeAll()
        For Each kv In _handlers
            MessageBus.I.Off(kv.Key, kv.Value)
        Next
        _handlers.Clear()
    End Sub

    ' ─── 로그 표시 ───

    Private Sub SafeAppendLog(m As Msg)
        If _rtb.IsDisposed Then Return

        If _rtb.InvokeRequired Then
            Try
                _rtb.BeginInvoke(Sub() AppendLog(m))
            Catch
            End Try
        Else
            AppendLog(m)
        End If
    End Sub

    Private Sub AppendLog(m As Msg)
        Dim level = m.Str("level", "INFO")

        ' 필터 확인
        If _filterLevel <> "ALL" AndAlso Not String.Equals(level, _filterLevel, StringComparison.OrdinalIgnoreCase) Then
            Return
        End If

        ' SYS_LOG/SYS_ERROR 호환 처리
        If m.Topic = Topics.SYS_LOG AndAlso Not m.Has("level") Then level = "INFO"
        If m.Topic = Topics.SYS_ERROR AndAlso Not m.Has("level") Then level = "ERROR"

        Dim line As String
        If m.Has("fullLine") Then
            line = m.Str("fullLine")
        Else
            Dim time = m.Str("time", DateTime.Now.ToString("HH:mm:ss.fff"))
            Dim src = m.Str("source", "")
            Dim srcStr = If(src <> "", $"[{src}] ", "")
            Dim text = m.Str("text", m.Str("message", ""))
            line = $"{time} [{level.PadRight(5)}] {srcStr}{text}"
        End If

        ' 색상
        Dim clr As Color = Color.White
        If _colors.ContainsKey(level) Then clr = _colors(level)

        ' 라인 수 제한
        If _rtb.Lines.Length > _maxLines Then
            _rtb.SelectionStart = 0
            _rtb.SelectionLength = _rtb.GetFirstCharIndexFromLine(_maxLines \ 2)
            _rtb.SelectedText = ""
        End If

        ' 추가
        _rtb.SelectionStart = _rtb.TextLength
        _rtb.SelectionLength = 0
        _rtb.SelectionColor = clr
        _rtb.AppendText(line & Environment.NewLine)

        ' 자동스크롤
        If _autoScroll Then
            _rtb.SelectionStart = _rtb.TextLength
            _rtb.ScrollToCaret()
        End If
    End Sub

    Private Sub SearchLog(keyword As String)
        If String.IsNullOrWhiteSpace(keyword) Then Return

        Dim startPos = _rtb.SelectionStart + _rtb.SelectionLength
        Dim idx = _rtb.Find(keyword, startPos, RichTextBoxFinds.None)
        If idx < 0 Then
            ' 처음부터 다시 검색
            idx = _rtb.Find(keyword, 0, RichTextBoxFinds.None)
        End If

        If idx >= 0 Then
            _rtb.SelectionStart = idx
            _rtb.SelectionLength = keyword.Length
            _rtb.SelectionBackColor = Color.Yellow
            _rtb.SelectionColor = Color.Black
            _rtb.ScrollToCaret()
        End If
    End Sub

End Class
