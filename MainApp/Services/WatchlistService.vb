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
            _data = NormalizeData(_data)
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
            Select(Function(g) CloneGroup(g)).
            ToList()
    End Function

    Public Function CreateGroup(groupName As String) As WatchlistGroup
        If _data Is Nothing Then Load()

        Dim normalizedGroup = NormalizeGroupName(groupName)
        If _data.Groups.Any(Function(g) String.Equals(g.Name, normalizedGroup, StringComparison.OrdinalIgnoreCase)) Then
            Throw New InvalidOperationException("동일한 그룹명이 이미 존재합니다.")
        End If

        Dim group As New WatchlistGroup With {.Name = normalizedGroup}
        _data.Groups.Add(group)
        SortGroups()
        Save()
        Return CloneGroup(group)
    End Function

    Public Sub RenameGroup(oldName As String, newName As String)
        If _data Is Nothing Then Load()

        Dim group = FindGroup(oldName)
        If group Is Nothing Then Throw New InvalidOperationException("수정할 그룹을 찾을 수 없습니다.")

        Dim normalizedNewName = NormalizeGroupName(newName)
        If _data.Groups.Any(Function(g) Not Object.ReferenceEquals(g, group) AndAlso String.Equals(g.Name, normalizedNewName, StringComparison.OrdinalIgnoreCase)) Then
            Throw New InvalidOperationException("동일한 그룹명이 이미 존재합니다.")
        End If

        group.Name = normalizedNewName
        SortGroups()
        Save()
    End Sub

    Public Sub DeleteGroup(groupName As String)
        If _data Is Nothing Then Load()

        Dim group = FindGroup(groupName)
        If group Is Nothing Then Return

        _data.Groups.Remove(group)
        Save()
    End Sub

    Public Sub UpsertStock(groupName As String, code As String, comment As String)
        If _data Is Nothing Then Load()

        Dim group = FindGroup(groupName)
        If group Is Nothing Then
            group = New WatchlistGroup With {.Name = NormalizeGroupName(groupName)}
            _data.Groups.Add(group)
        End If

        Dim normalizedCode = NormalizeCode(code)
        If normalizedCode = "" Then Throw New InvalidOperationException("종목코드를 입력하세요.")

        Dim stock = group.Stocks.FirstOrDefault(Function(s) String.Equals(s.Code, normalizedCode, StringComparison.OrdinalIgnoreCase))
        If stock Is Nothing Then
            stock = New WatchlistStock With {.Code = normalizedCode}
            group.Stocks.Add(stock)
        End If

        stock.Code = normalizedCode
        stock.Comment = NormalizeComment(comment)
        NormalizeGroup(group)
        SortGroups()
        Save()
    End Sub

    Public Sub RemoveStock(groupName As String, code As String)
        If _data Is Nothing Then Load()

        Dim group = FindGroup(groupName)
        If group Is Nothing Then Return

        Dim normalizedCode = NormalizeCode(code)
        If normalizedCode = "" Then Return

        group.Stocks = group.Stocks.
            Where(Function(s) Not String.Equals(s.Code, normalizedCode, StringComparison.OrdinalIgnoreCase)).
            ToList()
        NormalizeGroup(group)
        Save()
    End Sub

    Public Sub AddStock(groupName As String, code As String)
        UpsertStock(groupName, code, "")
    End Sub

    Private Function FindGroup(groupName As String) As WatchlistGroup
        Dim normalizedGroup = NormalizeGroupName(groupName)
        Return _data.Groups.FirstOrDefault(Function(g) String.Equals(g.Name, normalizedGroup, StringComparison.OrdinalIgnoreCase))
    End Function

    Private Shared Function NormalizeData(data As WatchlistData) As WatchlistData
        Dim result = If(data, New WatchlistData())
        If result.Groups Is Nothing Then result.Groups = New List(Of WatchlistGroup)()

        result.Groups = result.Groups.
            Where(Function(g) g IsNot Nothing).
            Select(Function(g)
                       NormalizeGroup(g)
                       Return g
                   End Function).
            Where(Function(g) g.Name <> "").
            OrderBy(Function(g) g.Name, StringComparer.OrdinalIgnoreCase).
            ToList()

        Return result
    End Function

    Private Shared Sub NormalizeGroup(group As WatchlistGroup)
        If group Is Nothing Then Return

        group.Name = NormalizeGroupName(group.Name)

        Dim stocks = New List(Of WatchlistStock)()
        If group.Stocks IsNot Nothing Then
            stocks.AddRange(group.Stocks)
        End If

        If stocks.Count = 0 AndAlso group.Codes IsNot Nothing Then
            stocks.AddRange(group.Codes.Select(Function(c) New WatchlistStock With {.Code = c}))
        End If

        group.Stocks = stocks.
            Where(Function(s) s IsNot Nothing).
            Select(Function(s) New WatchlistStock With {
                .Code = NormalizeCode(s.Code),
                .Comment = NormalizeComment(s.Comment)
            }).
            Where(Function(s) s.Code <> "").
            GroupBy(Function(s) s.Code, StringComparer.OrdinalIgnoreCase).
            Select(Function(g)
                       Dim first = g.First()
                       Dim comment = g.Select(Function(x) NormalizeComment(x.Comment)).FirstOrDefault(Function(text) text <> "")
                       If comment <> "" Then first.Comment = comment
                       Return first
                   End Function).
            OrderBy(Function(s) s.Code, StringComparer.OrdinalIgnoreCase).
            ToList()

        group.Codes = group.Stocks.Select(Function(s) s.Code).ToList()
    End Sub

    Private Sub SortGroups()
        _data.Groups = _data.Groups.
            OrderBy(Function(g) g.Name, StringComparer.OrdinalIgnoreCase).
            ToList()
    End Sub

    Private Shared Function CloneGroup(group As WatchlistGroup) As WatchlistGroup
        Dim clone As New WatchlistGroup With {
            .Name = If(If(group Is Nothing, Nothing, group.Name), "")
        }

        If group IsNot Nothing AndAlso group.Stocks IsNot Nothing Then
            clone.Stocks = group.Stocks.
                Select(Function(s) New WatchlistStock With {
                    .Code = If(s.Code, ""),
                    .Comment = If(s.Comment, "")
                }).
                ToList()
        End If

        clone.Codes = clone.Stocks.Select(Function(s) s.Code).ToList()
        Return clone
    End Function

    Private Shared Function NormalizeGroupName(groupName As String) As String
        Dim value = If(groupName, "").Trim()
        If value = "" Then value = "기본"
        Return value
    End Function

    Private Shared Function NormalizeCode(code As String) As String
        Return If(code, "").Trim()
    End Function

    Private Shared Function NormalizeComment(comment As String) As String
        Return If(comment, "").Trim()
    End Function
End Class
