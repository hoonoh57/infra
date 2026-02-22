' ═══════════════════════════════════════════════════════════════
' CybosServer/ServerMain.vb — 사이보스 서버 메인 폼
' ═══════════════════════════════════════════════════════════════

Imports System.Windows.Forms
Imports [Shared]

Public Class CybosServerMain
    Inherits Form

    Private WithEvents _pipe As PipeServer
    Private _engine As CybosEngine

    Public Sub New()
        Me.Text = "CybosServer"
        Me.WindowState = FormWindowState.Minimized
        Me.ShowInTaskbar = True
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)

        Try
            _engine = New CybosEngine()
            AddHandler _engine.RealtimePublished,
                Sub(pushMsg)
                    Try
                        _pipe?.Send(pushMsg)
                    Catch
                    End Try
                End Sub
        Catch ex As Exception
            MessageBox.Show($"CybosPlus 연결 실패!{vbCrLf}{ex.Message}{vbCrLf}CybosPlus를 먼저 실행하세요.",
                            "Cybos 오류", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Application.Exit()
            Return
        End Try

        _pipe = New PipeServer("CybosPipe")
        AddHandler _pipe.ClientConnected, Sub() Console.WriteLine("[Cybos] 클라이언트 연결됨")
        AddHandler _pipe.ClientDisconnected, Sub() Console.WriteLine("[Cybos] 클라이언트 연결 끊김")
        AddHandler _pipe.ErrorOccurred, Sub(msg) Console.WriteLine($"[Cybos] 파이프 오류: {msg}")
        _pipe.Start()

        Console.WriteLine("[CybosServer] 시작 완료. 파이프 대기 중...")
    End Sub

    Private Sub OnPipeMessage(msg As Msg) Handles _pipe.MessageReceived
        ' 모든 요청을 엔진에 위임 (별도 스레드에서 실행 - 블로킹 COM 호출이므로)
        Threading.ThreadPool.QueueUserWorkItem(
            Sub(state)
                _engine.Execute(msg, Sub(response)
                                         ' 원래 요청의 시퀀스를 응답에 복사 (클라이언트 콜백 매칭용)
                                         If msg.Has("_seq") Then response("_seq") = msg("_seq")
                                         _pipe.Send(response)
                                     End Sub)
            End Sub)
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _pipe?.Stop()
        MyBase.OnFormClosing(e)
    End Sub
End Class
