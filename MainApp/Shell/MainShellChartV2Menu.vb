Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Windows.Forms
Imports [Shared]

Partial Public Class MainShell

    Private _chartV2MenuItem As ToolStripMenuItem

    Private Sub MainShellChartV2Menu_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        AddChartV2MenuItem()
    End Sub

    Private Sub AddChartV2MenuItem()
        If _chartV2MenuItem IsNot Nothing Then Return
        If mnuNewChart Is Nothing Then Return

        _chartV2MenuItem = New ToolStripMenuItem("Open Chart V2")
        _chartV2MenuItem.Name = "mnuOpenChartV2"
        AddHandler _chartV2MenuItem.Click, AddressOf OpenChartV2MenuItem_Click

        Dim ownerMenu As ToolStripMenuItem = TryCast(mnuNewChart.OwnerItem, ToolStripMenuItem)
        If ownerMenu IsNot Nothing Then
            ownerMenu.DropDownItems.Add(New ToolStripSeparator())
            ownerMenu.DropDownItems.Add(_chartV2MenuItem)
            Return
        End If

        Dim parentStrip As ToolStrip = mnuNewChart.GetCurrentParent()
        If parentStrip IsNot Nothing Then
            parentStrip.Items.Add(_chartV2MenuItem)
        End If
    End Sub

    Private Sub OpenChartV2MenuItem_Click(sender As Object, e As EventArgs)
        Dim code As String = Microsoft.VisualBasic.Interaction.InputBox("Code", "Chart V2", "005930")
        If String.IsNullOrWhiteSpace(code) Then Return

        code = SharedUtil.NormalizeChartCode(code)
        If String.IsNullOrWhiteSpace(code) Then Return

        Dim chartForm As New SafeChartHostForm(code, "minute", 300)
        chartForm.Show(Me)
    End Sub

End Class
