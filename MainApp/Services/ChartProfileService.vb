Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms
Imports Newtonsoft.Json
Imports [Shared]

Public Class ChartProfileService
    Private Shared _instance As ChartProfileService

    Public Shared ReadOnly Property I As ChartProfileService
        Get
            If _instance Is Nothing Then _instance = New ChartProfileService()
            Return _instance
        End Get
    End Property

    Private ReadOnly _filePath As String
    Private _profile As ChartProfileData

    Private Sub New()
        _filePath = Path.Combine(Application.StartupPath, "chart_profile.json")
        Load()
    End Sub

    Public Function GetProfile() As ChartProfileData
        If _profile Is Nothing Then Load()
        Return CloneProfile(_profile)
    End Function

    Public Sub SaveProfile(profile As ChartProfileData)
        Try
            _profile = NormalizeProfile(profile)
            _profile.LastModified = DateTime.Now

            Dim dirPath = Path.GetDirectoryName(_filePath)
            If Not String.IsNullOrWhiteSpace(dirPath) Then Directory.CreateDirectory(dirPath)

            Dim json = JsonConvert.SerializeObject(_profile, Formatting.Indented)
            File.WriteAllText(_filePath, json, New UTF8Encoding(False))
        Catch ex As Exception
            AppLogger.I.Error($"차트 프로필 저장 실패: {ex.Message}", "ChartProfile")
        End Try
    End Sub

    Public Sub Load()
        Try
            If File.Exists(_filePath) Then
                Dim json = File.ReadAllText(_filePath, Encoding.UTF8)
                _profile = NormalizeProfile(JsonConvert.DeserializeObject(Of ChartProfileData)(json))
            Else
                _profile = New ChartProfileData()
            End If
        Catch ex As Exception
            _profile = New ChartProfileData()
            AppLogger.I.Error($"차트 프로필 로드 실패: {ex.Message}", "ChartProfile")
        End Try
    End Sub

    Private Shared Function NormalizeProfile(profile As ChartProfileData) As ChartProfileData
        Dim result = If(profile, New ChartProfileData())
        If result.Indicators Is Nothing Then result.Indicators = New List(Of ChartProfileIndicatorItem)()
        If result.ContextOptions Is Nothing Then result.ContextOptions = New ChartProfileContextOptions()

        result.Indicators = result.Indicators.
            Where(Function(ind) ind IsNot Nothing).
            Select(Function(ind) NormalizeIndicator(ind)).
            Where(Function(ind) ind.IndicatorType <> "").
            OrderBy(Function(ind) ind.DisplayOrder).
            ThenBy(Function(ind) ind.IndicatorName, StringComparer.OrdinalIgnoreCase).
            ToList()

        result.ContextOptions = NormalizeContextOptions(result.ContextOptions)
        Return result
    End Function

    Private Shared Function NormalizeIndicator(indicator As ChartProfileIndicatorItem) As ChartProfileIndicatorItem
        Dim result = If(indicator, New ChartProfileIndicatorItem())
        result.IndicatorType = If(result.IndicatorType, "").Trim()
        result.IndicatorName = If(result.IndicatorName, "").Trim()
        If result.DisplayOrder < 0 Then result.DisplayOrder = 0
        If result.PanelIndex < 0 Then result.PanelIndex = 0

        Dim normalizedParams As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
        If result.Parameters IsNot Nothing Then
            For Each kv In result.Parameters
                Dim key = If(kv.Key, "").Trim()
                If key = "" Then Continue For
                normalizedParams(key) = kv.Value
            Next
        End If
        result.Parameters = normalizedParams
        Return result
    End Function

    Private Shared Function NormalizeContextOptions(options As ChartProfileContextOptions) As ChartProfileContextOptions
        Dim result = If(options, New ChartProfileContextOptions())
        If result.CandleWidth <= 0 Then result.CandleWidth = 8
        If result.Gap < 0 Then result.Gap = 2
        If result.VisibleCount <= 0 Then result.VisibleCount = 120
        If result.PanelHeightRatio <= 0 Then result.PanelHeightRatio = 0.18F
        If result.ManualMaxPrice < 0 Then result.ManualMaxPrice = 0
        If result.ManualMinPrice < 0 Then result.ManualMinPrice = 0
        Return result
    End Function

    Private Shared Function CloneProfile(profile As ChartProfileData) As ChartProfileData
        Dim normalized = NormalizeProfile(profile)
        Return New ChartProfileData With {
            .Indicators = normalized.Indicators.
                Select(Function(ind) New ChartProfileIndicatorItem With {
                    .IndicatorType = ind.IndicatorType,
                    .IndicatorName = ind.IndicatorName,
                    .DisplayOrder = ind.DisplayOrder,
                    .PanelIndex = ind.PanelIndex,
                    .Parameters = New Dictionary(Of String, Object)(ind.Parameters, StringComparer.OrdinalIgnoreCase)
                }).
                ToList(),
            .ContextOptions = New ChartProfileContextOptions With {
                .ShowCurrentPriceLine = normalized.ContextOptions.ShowCurrentPriceLine,
                .ShowPrevCloseLine = normalized.ContextOptions.ShowPrevCloseLine,
                .ShowViLine = normalized.ContextOptions.ShowViLine,
                .ShowDayChangeLines = normalized.ContextOptions.ShowDayChangeLines,
                .ShowCrosshair = normalized.ContextOptions.ShowCrosshair,
                .IsAutoScaleY = normalized.ContextOptions.IsAutoScaleY,
                .ManualMaxPrice = normalized.ContextOptions.ManualMaxPrice,
                .ManualMinPrice = normalized.ContextOptions.ManualMinPrice,
                .CandleWidth = normalized.ContextOptions.CandleWidth,
                .Gap = normalized.ContextOptions.Gap,
                .VisibleCount = normalized.ContextOptions.VisibleCount,
                .PanelHeightRatio = normalized.ContextOptions.PanelHeightRatio
            },
            .LastModified = normalized.LastModified
        }
    End Function
End Class
