Imports System
Imports [Shared]

Namespace Services
    Public NotInheritable Class MarketTradingDayResolver
        Private Sub New()
        End Sub

        Public Shared Function ResolveLatestCompletedTradingDay(Optional reference As DateTime? = Nothing) As DateTime
            Dim now As DateTime = If(reference.HasValue, reference.Value, DateTime.Now)
            Dim candidate As DateTime = now.Date
            Dim marketClose As New TimeSpan(15, 30, 0)

            If now.TimeOfDay < marketClose Then
                candidate = candidate.AddDays(-1)
            End If

            Return ResolveTradingDayOnOrBefore(candidate)
        End Function

        Public Shared Function ResolveTradingDayOnOrBefore(candidate As DateTime) As DateTime
            Dim target As DateTime = candidate.Date
            If TradingCalendar.IsBusinessDay(target) Then
                Return target
            End If

            Return TradingCalendar.PreviousBusinessDay(target.AddDays(1))
        End Function

        Public Shared Function ResolvePreviousTradingDay(baseDate As DateTime) As DateTime
            Return ResolveTradingDayOnOrBefore(baseDate.Date.AddDays(-1))
        End Function
    End Class
End Namespace
