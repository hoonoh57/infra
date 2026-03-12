Imports System.Collections.Generic
Imports StrategyCore.Models

Namespace StrategyCore.Services
    Public Interface ICandleDataProvider
        Function GetCandles(symbol As String,
                            timeframe As String,
                            fromDate As DateTime,
                            barCount As Integer) As IReadOnlyList(Of LabCandle)
    End Interface
End Namespace
