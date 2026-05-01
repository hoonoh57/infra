Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Windows.Forms
Imports MainApp.Services

Public Class SafeChartHostForm
    Inherits Form

    Private ReadOnly _chart As SafeFastChartControl
    Private ReadOnly _stockCode As String
    Private ReadOnly _chartType As String
    Private ReadOnly _count As Integer

    Public Sub New(stockCode As String, Optional chartType As String = "minute", Optional count As Integer = 300)
        _stockCode = stockCode
        _chartType = chartType
        _count = count

        Me.Text = "Safe Chart V2 - " & stockCode
        Me.Width = 1200
        Me.Height = 800
        Me.StartPosition = FormStartPosition.CenterScreen

        _chart = New SafeFastChartControl()
        _chart.Dock = DockStyle.Fill
        Me.Controls.Add(_chart)

        AddHandler _chart.ChartProfileChanged, AddressOf OnChartProfileChanged
        AddHandler Me.Shown, AddressOf OnSafeChartHostShown
    End Sub

    Private Sub OnSafeChartHostShown(sender As Object, e As EventArgs)
        _chart.ApplyChartProfile(ChartProfileService.I.GetProfile())
        _chart.SetStock(_stockCode, _chartType, _count)
    End Sub

    Private Sub OnChartProfileChanged(sender As Object, e As EventArgs)
        ChartProfileService.I.SaveProfile(_chart.ExportChartProfile())
    End Sub
End Class
