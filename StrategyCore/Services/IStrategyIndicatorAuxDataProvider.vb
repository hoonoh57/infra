Imports System
Imports System.Collections.Generic

Namespace StrategyCore.Services
    Public Interface IStrategyIndicatorAuxDataProvider
        Function GetTickTimestamps(symbol As String,
                                   timeframe As String,
                                   fromDate As DateTime,
                                   barCount As Integer) As IReadOnlyList(Of DateTime)
    End Interface
End Namespace
