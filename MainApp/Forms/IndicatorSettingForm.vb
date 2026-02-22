' ═══════════════════════════════════════════════════════════════
' IndicatorSettingForm.vb — 지표 설정 대화상자
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms

Public Class IndicatorSettingForm
    Inherits Form

    Private _indicator As IIndicator
    Private _controls As New Dictionary(Of String, Control)

    Public Sub New(ind As IIndicator)
        _indicator = ind
        InitializeComponent()
        SetupLayout()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = $"{_indicator.DisplayName} 설정"
        Me.Size = New Size(350, 450)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.BackColor = Color.FromArgb(30, 30, 35)
        Me.ForeColor = Color.White
    End Sub

    Private Sub SetupLayout()
        Dim mainLayout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .RowCount = _indicator.Parameters.Count + 1,
            .Padding = New Padding(20),
            .AutoScroll = True
        }
        mainLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40))
        mainLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 60))

        Dim rowIdx As Integer = 0
        For Each param In _indicator.Parameters
            Dim lbl As New Label With {
                .Text = param.Key,
                .Dock = DockStyle.Fill,
                .TextAlign = ContentAlignment.MiddleRight,
                .ForeColor = Color.LightGray
            }
            mainLayout.Controls.Add(lbl, 0, rowIdx)

            Dim ctrl As Control
            If TypeOf param.Value Is Integer OrElse TypeOf param.Value Is Decimal OrElse TypeOf param.Value Is Single Then
                Dim nud As New NumericUpDown With {
                    .Minimum = -999999999,
                    .Maximum = 999999999,
                    .DecimalPlaces = If(TypeOf param.Value Is Integer, 0, 2),
                    .Value = Convert.ToDecimal(param.Value),
                    .Dock = DockStyle.Fill,
                    .BackColor = Color.FromArgb(45, 45, 50),
                    .ForeColor = Color.White,
                    .BorderStyle = BorderStyle.FixedSingle
                }
                ctrl = nud
            Else
                Dim txt As New TextBox With {
                    .Text = param.Value.ToString(),
                    .Dock = DockStyle.Fill,
                    .BackColor = Color.FromArgb(45, 45, 50),
                    .ForeColor = Color.White,
                    .BorderStyle = BorderStyle.FixedSingle
                }
                ctrl = txt
            End If
            
            mainLayout.Controls.Add(ctrl, 1, rowIdx)
            _controls.Add(param.Key, ctrl)
            rowIdx += 1
        Next

        Dim btnPanel As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 50,
            .FlowDirection = FlowDirection.RightToLeft,
            .Padding = New Padding(10)
        }

        Dim btnCancel As New Button With {.Text = "취소", .DialogResult = DialogResult.Cancel, .FlatStyle = FlatStyle.Flat, .ForeColor = Color.White}
        Dim btnSave As New Button With {.Text = "저장", .FlatStyle = FlatStyle.Flat, .ForeColor = Color.White, .BackColor = Color.FromArgb(0, 122, 204)}
        
        AddHandler btnSave.Click, AddressOf OnSave
        
        btnPanel.Controls.Add(btnCancel)
        btnPanel.Controls.Add(btnSave)

        Me.Controls.Add(mainLayout)
        Me.Controls.Add(btnPanel)
        
        Me.AcceptButton = btnSave
        Me.CancelButton = btnCancel
    End Sub

    Private Sub OnSave(sender As Object, e As EventArgs)
        Try
            Dim newParams As New Dictionary(Of String, Object)
            For Each kv In _controls
                Dim originalValue = _indicator.Parameters(kv.Key)
                If TypeOf kv.Value Is NumericUpDown Then
                    Dim nud = DirectCast(kv.Value, NumericUpDown)
                    If TypeOf originalValue Is Integer Then
                        newParams(kv.Key) = CInt(nud.Value)
                    ElseIf TypeOf originalValue Is Single Then
                        newParams(kv.Key) = CSng(nud.Value)
                    ElseIf TypeOf originalValue Is Decimal Then
                        newParams(kv.Key) = nud.Value
                    Else
                        newParams(kv.Key) = CDbl(nud.Value)
                    End If
                Else
                    newParams(kv.Key) = kv.Value.Text
                End If
            Next
            
            _indicator.Parameters = newParams
            Me.DialogResult = DialogResult.OK
            Me.Close()
        Catch ex As Exception
            MessageBox.Show($"설정 저장 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub
End Class
