Imports System.Collections.Generic

Public Class WatchlistGroup
    Public Property Name As String = ""
    Public Property Codes As New List(Of String)()
End Class

Public Class WatchlistData
    Public Property Groups As New List(Of WatchlistGroup)()
    Public Property LastModified As DateTime = DateTime.Now
End Class
