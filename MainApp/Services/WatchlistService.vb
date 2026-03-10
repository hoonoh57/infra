Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms
Imports Newtonsoft.Json
Imports [Shared]

Public Class WatchlistService
    Private Shared _instance As WatchlistService

    Public Shared ReadOnly Property I As WatchlistService
        Get
            If _instance Is Nothing Then _instance = New WatchlistService()
            Return _instance
        End Get
    End Property

    Private _data As WatchlistData
    Private ReadOnly _filePath As String

    Private Sub New()
        _filePath = Path.Combine(Application.StartupPath, RuntimeChartSettings.GetString("watchlist", "file", "watchlist.json"))
        Load()
    End Sub

    Public Sub Load()
        Try
            If File.Exists(_filePath) Then
                Dim json = File.ReadAllText(_filePath, Encoding.UTF8)
                _data = NormalizeData(JsonConvert.DeserializeObject(Of WatchlistData)(json))
            Else
                _data = New WatchlistData()
                Save()
            End If
        Catch ex As Exception
            _data = New WatchlistData()
            AppLogger.I.Error($"관심종목 로드 실패: {ex.Message}", "Watchlist")
        End Try
    End Sub

    Public Sub Save()
        Try
            If _data Is Nothing Then _data = New WatchlistData()
            _data.LastModified = DateTime.Now

            Dim dirPath = Path.GetDirectoryName(_filePath)
            If Not String.IsNullOrWhiteSpace(dirPath) Then Directory.CreateDirectory(dirPath)

            Dim json = JsonConvert.SerializeObject(_data, Formatting.Indented)
            File.WriteAllText(_filePath, json, New UTF8Encoding(False))
        Catch ex As Exception
            AppLogger.I.Error($"관심종목 저장 실패: {ex.Message}", "Watchlist")
        End Try
    End Sub

    Public Function GetGroups() As List(Of WatchlistGroup)
        If _data Is Nothing Then Load()

        Return _data.Groups.
            Select(Function(g) New WatchlistGroup With {
                .Name = g.Name,
                .Codes = g.Codes.ToList()
            }).
            ToList()
    End Function

    Public Sub AddStock(groupName As String, code As String)
        If _data Is Nothing Then Load()

        Dim normalizedGroup = NormalizeGroupName(groupName)
        Dim normalizedCode = NormalizeCode(code)
        If normalizedCode = "" Then Return

        Dim group = _data.Groups.FirstOrDefault(Function(g) String.Equals(g.Name, normalizedGroup, StringComparison.OrdinalIgnoreCase))
        If group Is Nothing Then
            group = New WatchlistGroup With {.Name = normalizedGroup}
            _data.Groups.Add(group)
        End If

        If group.Codes.Any(Function(c) String.Equals(c, normalizedCode, StringComparison.OrdinalIgnoreCase)) Then Return

        group.Codes.Add(normalizedCode)
        group.Codes = group.Codes.
            Select(Function(c) NormalizeCode(c)).
            Where(Function(c) c <> "").
            Distinct(StringComparer.OrdinalIgnoreCase).
            OrderBy(Function(c) c).
            ToList()
        Save()
    End Sub

    Public Sub RemoveStock(groupName As String, code As String)
        If _data Is Nothing Then Load()

        Dim normalizedGroup = NormalizeGroupName(groupName)
        Dim normalizedCode = NormalizeCode(code)
        If normalizedCode = "" Then Return

        Dim group = _data.Groups.FirstOrDefault(Function(g) String.Equals(g.Name, normalizedGroup, StringComparison.OrdinalIgnoreCase))
        If group Is Nothing Then Return

        group.Codes = group.Codes.
            Select(Function(c) NormalizeCode(c)).
            Where(Function(c) c <> "" AndAlso Not String.Equals(c, normalizedCode, StringComparison.OrdinalIgnoreCase)).
            Distinct(StringComparer.OrdinalIgnoreCase).
            ToList()

        If group.Codes.Count = 0 Then
            _data.Groups.Remove(group)
        End If

        Save()
    End Sub

    Private Shared Function NormalizeData(data As WatchlistData) As WatchlistData
        Dim result = If(data, New WatchlistData())
        If result.Groups Is Nothing Then result.Groups = New List(Of WatchlistGroup)()

        result.Groups = result.Groups.
            Where(Function(g) g IsNot Nothing).
            Select(Function(g) New WatchlistGroup With {
                .Name = NormalizeGroupName(g.Name),
                .Codes = If(g.Codes, New List(Of String)()).
                    Select(Function(c) NormalizeCode(c)).
                    Where(Function(c) c <> "").
                    Distinct(StringComparer.OrdinalIgnoreCase).
                    OrderBy(Function(c) c).
                    ToList()
            }).
            Where(Function(g) g.Name <> "").
            ToList()

        Return result
    End Function

    Private Shared Function NormalizeGroupName(groupName As String) As String
        Dim value = If(groupName, "").Trim()
        If value = "" Then value = "기본"
        Return value
    End Function

    Private Shared Function NormalizeCode(code As String) As String
        Return If(code, "").Trim()
    End Function
End Class
