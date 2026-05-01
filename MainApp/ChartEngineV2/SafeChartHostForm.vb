Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Windows.Forms

Public Class SafeChartHostForm
    Inherits Form

    Private ReadOnly _chart As SafeFastChartControl

    Public Sub New(stockCode As String, Optional chartType As String = "minute", Optional count As Integer = 300)
        Me.Text = "Safe Chart V2 - " & stockCode
        Me.Width = 1200
        Me.Height = 800
        Me.StartPosition = FormStartPosition.CenterScreen

        _chart = New SafeFastChartControl()
        _chart.Dock = DockStyle.Fill
        Me.Controls.Add(_chart)

        AddHandler Me.Shown,
            Sub()
                _chart.SetStock(stockCode, chartType, count)
            End Sub
    End Sub
End Class
