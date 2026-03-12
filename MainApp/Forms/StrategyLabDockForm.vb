Imports System.Windows.Forms
Imports MainApp.Services
Imports StrategyCore.Services

Public Class StrategyLabDockForm
    Inherits DockFormBase

    Private ReadOnly _labForm As MainApp.StrategyLabApp.StrategyLabForm

    Public Sub New()
        Me.Text = "StrategyLab"

        Dim candleProvider As New StrategyLabCybosCandleProvider()
        Dim facade As New StrategyLabFacade(candleProvider)

        _labForm = New MainApp.StrategyLabApp.StrategyLabForm(facade, embeddedMode:=True) With {
            .TopLevel = False,
            .FormBorderStyle = FormBorderStyle.None,
            .Dock = DockStyle.Fill
        }

        Me.Controls.Add(_labForm)
        _labForm.Show()
    End Sub

    Public Overrides ReadOnly Property DefaultDockState As WeifenLuo.WinFormsUI.Docking.DockState
        Get
            Return WeifenLuo.WinFormsUI.Docking.DockState.Document
        End Get
    End Property

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _labForm?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
