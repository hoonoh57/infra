Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

Public Class StrategyLabDockForm
    Inherits DockFormBase

    Private ReadOnly _infoLabel As New Label()
    Private ReadOnly _launchButton As New Button()
    Private _process As Process

    Public Sub New()
        Me.Text = "StrategyLab"
        BuildLayout()
    End Sub

    Private Sub BuildLayout()
        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .RowCount = 3,
            .Padding = New Padding(20)
        }
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 80.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 44.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        Me.Controls.Add(layout)

        _infoLabel.Dock = DockStyle.Fill
        _infoLabel.TextAlign = ContentAlignment.MiddleLeft
        _infoLabel.Text = "StrategyLab은 MainApp 내부에 소스 링크로 포함하지 않고 별도 x64 프로세스로 실행합니다." & vbCrLf &
                          "이 구조는 MainApp과 StrategyLabApp의 컴파일 결합도를 낮추고, 전략 실험 UI 오류가 메인 대시보드에 직접 전파되는 것을 줄입니다."
        layout.Controls.Add(_infoLabel, 0, 0)

        _launchButton.Dock = DockStyle.Left
        _launchButton.Width = 220
        _launchButton.Text = "StrategyLab 실행"
        AddHandler _launchButton.Click, AddressOf OnLaunchClicked
        layout.Controls.Add(_launchButton, 0, 1)
    End Sub

    Private Sub OnLaunchClicked(sender As Object, e As EventArgs)
        Dim exePath As String = FindStrategyLabExe()
        If String.IsNullOrWhiteSpace(exePath) OrElse Not File.Exists(exePath) Then
            MessageBox.Show(Me,
                            "StrategyLabApp.exe를 찾지 못했습니다." & vbCrLf & vbCrLf &
                            "먼저 StrategyLabApp 프로젝트를 x64 Debug 또는 Release로 빌드하세요.",
                            "StrategyLab",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information)
            Return
        End If

        Try
            If _process IsNot Nothing AndAlso Not _process.HasExited Then
                Return
            End If

            Dim psi As New ProcessStartInfo With {
                .FileName = exePath,
                .WorkingDirectory = Path.GetDirectoryName(exePath),
                .UseShellExecute = True
            }
            _process = Process.Start(psi)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "StrategyLab 실행 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Shared Function FindStrategyLabExe() As String
        Dim mainExeDir As String = AppDomain.CurrentDomain.BaseDirectory
        Dim solutionRoot As String = Path.GetFullPath(Path.Combine(mainExeDir, "..", "..", "..", ".."))
        Dim candidates As String() = {
            Path.Combine(mainExeDir, "StrategyLabApp.exe"),
            Path.Combine(mainExeDir, "apps", "StrategyLabApp", "StrategyLabApp.exe"),
            Path.Combine(solutionRoot, "StrategyLabApp", "bin", "Debug", "net481", "StrategyLabApp.exe"),
            Path.Combine(solutionRoot, "StrategyLabApp", "bin", "Release", "net481", "StrategyLabApp.exe")
        }

        For Each candidate As String In candidates
            If File.Exists(candidate) Then Return candidate
        Next

        Return ""
    End Function

    Public Overrides ReadOnly Property DefaultDockState As WeifenLuo.WinFormsUI.Docking.DockState
        Get
            Return WeifenLuo.WinFormsUI.Docking.DockState.Document
        End Get
    End Property

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            RemoveHandler _launchButton.Click, AddressOf OnLaunchClicked
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
