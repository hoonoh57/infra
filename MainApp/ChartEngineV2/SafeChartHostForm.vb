Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Reflection
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

        InstallOriginalStyleContextMenu()

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

    Private Sub InstallOriginalStyleContextMenu()
        Dim menu As ContextMenuStrip = BuildOriginalStyleContextMenu()
        _chart.ContextMenuStrip = menu

        Dim field As FieldInfo = GetType(SafeFastChartControl).GetField("_sk", BindingFlags.Instance Or BindingFlags.NonPublic)
        If field Is Nothing Then Return

        Dim skControl As Control = TryCast(field.GetValue(_chart), Control)
        If skControl IsNot Nothing Then
            skControl.ContextMenuStrip = menu
        End If
    End Sub

    Private Function BuildOriginalStyleContextMenu() As ContextMenuStrip
        Dim menu As New ContextMenuStrip()

        Dim mnuInsertIndicator As New ToolStripMenuItem("지표 삽입")
        AddIndicatorItem(mnuInsertIndicator, "이동평균(MA)", Function() New MA_Indicator(20, "SMA"))
        AddIndicatorItem(mnuInsertIndicator, "볼린 밴드", Function() New Bollinger_Indicator(20, 2.0F))
        AddIndicatorItem(mnuInsertIndicator, "거래량 지표", Function() New Volume_Indicator())
        AddIndicatorItem(mnuInsertIndicator, "MACD", Function() New MACD_Indicator())
        AddIndicatorItem(mnuInsertIndicator, "RSI", Function() New RSI_Indicator())
        AddIndicatorItem(mnuInsertIndicator, "SuperTrend", Function() New SuperTrend_Indicator())

        Dim mnuTech As New ToolStripMenuItem("기술 지표")
        AddIndicatorItem(mnuTech, "VWAP", Function() New VWAP_Indicator())
        AddIndicatorItem(mnuTech, "OBV", Function() New OBV_Indicator())
        AddIndicatorItem(mnuTech, "이격도", Function() New Disparity_Indicator())
        AddIndicatorItem(mnuTech, "JMA", Function() New JMA_Indicator())
        AddIndicatorItem(mnuTech, "TickIntensity", Function() New TickIntensity_Indicator())
        AddIndicatorItem(mnuTech, "체결강도", Function() New TradeStrength_Indicator())
        AddIndicatorItem(mnuTech, "프로그램 순매수", Function() New ProgramTrade_Indicator())
        AddIndicatorItem(mnuTech, "누적 거래대금", Function() New CumTradeAmount_Indicator())
        AddIndicatorItem(mnuTech, "섹터 리더", Function() New SectorLeader_Indicator())
        mnuInsertIndicator.DropDownItems.Add(New ToolStripSeparator())
        mnuInsertIndicator.DropDownItems.Add(mnuTech)
        menu.Items.Add(mnuInsertIndicator)

        Dim mnuAutoRoll As New ToolStripMenuItem("자동 롤링")
        mnuAutoRoll.Enabled = False
        menu.Items.Add(mnuAutoRoll)

        Dim mnuChartManage As New ToolStripMenuItem("차트 관리")
        Dim mnuLatest As New ToolStripMenuItem("최근봉 이동")
        AddHandler mnuLatest.Click, Sub() SendKeys.SendWait("{END}")
        mnuChartManage.DropDownItems.Add(mnuLatest)

        Dim mnuShowAll As New ToolStripMenuItem("전체 보기")
        AddHandler mnuShowAll.Click, Sub() ShowAllCandlesByMenu()
        mnuChartManage.DropDownItems.Add(mnuShowAll)

        Dim mnuResetY As New ToolStripMenuItem("Y축 자동 복귀")
        AddHandler mnuResetY.Click, Sub() ResetYAxisByMenu()
        mnuChartManage.DropDownItems.Add(mnuResetY)
        menu.Items.Add(mnuChartManage)

        Dim mnuStrategy As New ToolStripMenuItem("전략 삽입")
        AddHandler mnuStrategy.Click,
            Sub()
                Using f As New StrategyManagerForm()
                    f.ShowDialog(Me)
                End Using
            End Sub
        menu.Items.Add(mnuStrategy)

        Dim mnuCompare As New ToolStripMenuItem("비교 차트")
        mnuCompare.Enabled = False
        menu.Items.Add(mnuCompare)

        Dim mnuChartInit As New ToolStripMenuItem("차트 초기화")
        AddHandler mnuChartInit.Click,
            Sub()
                _chart.ApplyChartProfile(New ChartProfileData())
                _chart.ReCalculate()
                SaveProfileNow()
            End Sub
        menu.Items.Add(mnuChartInit)

        Dim mnuSettings As New ToolStripMenuItem("차트 설정")
        mnuSettings.Enabled = False
        menu.Items.Add(mnuSettings)

        Dim mnuSave As New ToolStripMenuItem("차트 상태 저장")
        AddHandler mnuSave.Click, Sub() SaveProfileNow()
        menu.Items.Add(mnuSave)

        menu.Items.Add(New ToolStripSeparator())

        Dim mnuClose As New ToolStripMenuItem("차트 닫기")
        AddHandler mnuClose.Click, Sub() Me.Close()
        menu.Items.Add(mnuClose)

        Return menu
    End Function

    Private Sub AddIndicatorItem(parent As ToolStripMenuItem, caption As String, factory As Func(Of IIndicator))
        Dim item As New ToolStripMenuItem(caption)
        AddHandler item.Click,
            Sub()
                Dim ind As IIndicator = factory.Invoke()
                If ind Is Nothing Then Return
                _chart.AddIndicator(ind)
                _chart.ReCalculate()
                SaveProfileNow()
            End Sub
        parent.DropDownItems.Add(item)
    End Sub

    Private Sub SaveProfileNow()
        ChartProfileService.I.SaveProfile(_chart.ExportChartProfile())
    End Sub

    Private Sub ShowAllCandlesByMenu()
        Dim stateField As FieldInfo = GetType(SafeFastChartControl).GetField("_state", BindingFlags.Instance Or BindingFlags.NonPublic)
        Dim bufferField As FieldInfo = GetType(SafeFastChartControl).GetField("_buffer", BindingFlags.Instance Or BindingFlags.NonPublic)
        Dim repaintMethod As MethodInfo = GetType(SafeFastChartControl).GetMethod("RequestRepaint", BindingFlags.Instance Or BindingFlags.NonPublic)

        Dim state As SafeChartState = TryCast(stateField.GetValue(_chart), SafeChartState)
        Dim buffer As SafeChartDataBuffer = TryCast(bufferField.GetValue(_chart), SafeChartDataBuffer)
        If state Is Nothing OrElse buffer Is Nothing Then Return
        If buffer.Count <= 0 Then Return

        state.StartIndex = 0
        state.VisibleCount = buffer.Count
        state.Clamp(buffer.Count)
        If repaintMethod IsNot Nothing Then repaintMethod.Invoke(_chart, Nothing)
        SaveProfileNow()
    End Sub

    Private Sub ResetYAxisByMenu()
        Dim stateField As FieldInfo = GetType(SafeFastChartControl).GetField("_state", BindingFlags.Instance Or BindingFlags.NonPublic)
        Dim repaintMethod As MethodInfo = GetType(SafeFastChartControl).GetMethod("RequestRepaint", BindingFlags.Instance Or BindingFlags.NonPublic)
        Dim state As SafeChartState = TryCast(stateField.GetValue(_chart), SafeChartState)
        If state Is Nothing Then Return

        state.ResetManualPriceScale()
        If repaintMethod IsNot Nothing Then repaintMethod.Invoke(_chart, Nothing)
        SaveProfileNow()
    End Sub
End Class
