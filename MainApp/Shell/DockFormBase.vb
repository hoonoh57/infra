' ═══════════════════════════════════════════════════════════════
' DockFormBase.vb — 모든 도킹 서브폼의 베이스 클래스
' ═══════════════════════════════════════════════════════════════
' 모든 서브폼은 이 클래스를 상속하여 도킹 가능하게 됨.
' ═══════════════════════════════════════════════════════════════

Imports WeifenLuo.WinFormsUI.Docking

Public Class DockFormBase
    Inherits DockContent

    ''' <summary>폼 고유 ID (레이아웃 저장/복원용)</summary>
    Public Overridable ReadOnly Property FormId As String
        Get
            Return Me.GetType().Name
        End Get
    End Property

    ''' <summary>기본 도킹 위치</summary>
    Public Overridable ReadOnly Property DefaultDockState As DockState
        Get
            Return DockState.Document
        End Get
    End Property

    ''' <summary>닫기 버튼 클릭 시 숨기기만 (실제 Dispose 방지)</summary>
    Protected Overrides Sub OnFormClosing(e As System.Windows.Forms.FormClosingEventArgs)
        If e.CloseReason = System.Windows.Forms.CloseReason.UserClosing Then
            e.Cancel = True
            Me.Hide()
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

    ''' <summary>Bus 구독 정리용 (Dispose 시 호출)</summary>
    Protected Overridable Sub UnsubscribeAll()
        ' 서브클래스에서 오버라이드
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then UnsubscribeAll()
        MyBase.Dispose(disposing)
    End Sub

End Class
