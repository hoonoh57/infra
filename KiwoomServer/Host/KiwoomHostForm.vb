Imports System.Windows.Forms
Imports AxKHOpenAPILib

Public Class KiwoomHostForm
    Inherits Form

    Public ReadOnly Property ApiControl As AxKHOpenAPI

    Public Sub New()
        ' ActiveX 컨트롤 인스턴스화
        ApiControl = New AxKHOpenAPI()
        
        ' 컴포넌트 초기화
        CType(ApiControl, System.ComponentModel.ISupportInitialize).BeginInit()
        
        Me.SuspendLayout()
        Me.Controls.Add(ApiControl)
        Me.Name = "KiwoomHostForm"
        Me.Text = "Kiwoom OpenAPI Host"
        Me.ResumeLayout(False)
        
        CType(ApiControl, System.ComponentModel.ISupportInitialize).EndInit()
    End Sub
End Class
