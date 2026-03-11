Imports System.Collections.Generic

Public Class WatchlistStock
    Public Property Code As String = ""
    Public Property Comment As String = ""
End Class

Public Class WatchlistGroup
    Public Property Name As String = ""
    Public Property Stocks As New List(Of WatchlistStock)()
    Public Property Codes As New List(Of String)()
End Class

Public Class WatchlistData
    Public Property Groups As New List(Of WatchlistGroup)()
    Public Property LastModified As DateTime = DateTime.Now
End Class
