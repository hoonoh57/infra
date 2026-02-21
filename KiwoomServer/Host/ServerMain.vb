' ═══════════════════════════════════════════════════════════════
' ServerMain.vb — 키움 서버 메인 폼
' ═══════════════════════════════════════════════════════════════

Imports System.Windows.Forms
Imports [Shared]

Public Class ServerMain
    Inherits Form

    Private WithEvents _pipe As PipeServer
    Private _engine As KiwoomEngine
    Private _hostForm As KiwoomHostForm

    Public Sub New()
        Me.Text = "KiwoomServer"
        Me.WindowState = FormWindowState.Minimized
        Me.ShowInTaskbar = True
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)

        ' 키움 OCX 호스트 폼 (별도 폼에 ActiveX 배치)
        _hostForm = New KiwoomHostForm()
        _hostForm.Show()
        _hostForm.Visible = False

        ' 엔진 생성
        _engine = New KiwoomEngine(_hostForm.ApiControl)

        ' 실시간/체잔/조건 이벤트 → 파이프로 푸시
        AddHandler _engine.RealtimeReceived, Sub(m) _pipe?.Send(m)
        AddHandler _engine.ChejanReceived, Sub(m) _pipe?.Send(m)
        AddHandler _engine.ConditionHit, Sub(m) _pipe?.Send(m)

        ' 파이프 서버 시작
        _pipe = New PipeServer("KiwoomPipe")
        AddHandler _pipe.ClientConnected, Sub() Console.WriteLine("[Kiwoom] 클라이언트 연결됨")
        AddHandler _pipe.ClientDisconnected, Sub() Console.WriteLine("[Kiwoom] 클라이언트 연결 끊김")
        AddHandler _pipe.ErrorOccurred, Sub(msg) Console.WriteLine($"[Kiwoom] 파이프 오류: {msg}")
        _pipe.Start()

        Console.WriteLine("[KiwoomServer] 시작 완료. 파이프 대기 중...")
    End Sub

    Private Sub OnPipeMessage(msg As Msg) Handles _pipe.MessageReceived
        ' 모든 클라이언트 요청을 엔진에 위임
        _engine.Execute(msg, Sub(response)
                                 ' 원래 요청의 시퀀스를 응답에 복사 (클라이언트 콜백 매칭용)
                                 If msg.Has("_seq") Then response("_seq") = msg("_seq")
                                 _pipe.Send(response)
                             End Sub)
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _pipe?.Stop()
        MyBase.OnFormClosing(e)
    End Sub
End Class
