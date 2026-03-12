Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports [Shared]
Imports StrategyCore.Models
Imports StrategyCore.Services

Namespace Services
    Public Class StrategyLabCybosCandleProvider
        Implements ICandleDataProvider

        Private Const RequestTimeoutMs As Integer = 20000

        Public Function GetCandles(symbol As String,
                                   timeframe As String,
                                   fromDate As DateTime,
                                   barCount As Integer) As IReadOnlyList(Of LabCandle) Implements ICandleDataProvider.GetCandles
            Dim normalizedSymbol = SharedUtil.NormalizeChartCode(symbol)
            If String.IsNullOrWhiteSpace(normalizedSymbol) Then
                Throw New InvalidOperationException("A valid symbol is required for StrategyLab evaluation.")
            End If

            Dim normalizedTimeframe = NormalizeTimeframe(timeframe)
            Dim requestFrom = ResolveRequestFromDate(fromDate)
            Dim requestTo = DateTime.Today
            Dim response As Msg = Nothing
            Dim responseError As Exception = Nothing
            Dim completed As Boolean = False

            Dim handler As Action(Of Msg) =
                Sub(m As Msg)
                    If m Is Nothing Then Return
                    If Not String.Equals(SharedUtil.NormalizeChartCode(m.Str("code")), normalizedSymbol, StringComparison.OrdinalIgnoreCase) Then Return
                    If Not String.Equals(NormalizeTimeframe(m.Str("timeframe")), normalizedTimeframe, StringComparison.OrdinalIgnoreCase) Then Return
                    response = m.Clone()
                    completed = True
                End Sub

            MessageBus.I.On(Topics.CANDLE_PERIOD_LOADED, handler)
            Try
                MessageBus.I.Emit(Topics.CANDLE_PERIOD_REQUEST,
                                  "code", normalizedSymbol,
                                  "provider", "cybos",
                                  "timeframe", normalizedTimeframe,
                                  "from", requestFrom.ToString("yyyyMMdd"),
                                  "to", requestTo.ToString("yyyyMMdd"))

                Dim startedAt = Environment.TickCount
                While Not completed AndAlso Environment.TickCount - startedAt < RequestTimeoutMs
                    Application.DoEvents()
                    Thread.Sleep(20)
                End While

                If Not completed Then
                    Throw New TimeoutException($"Timed out loading cybos candles for {normalizedSymbol} [{normalizedTimeframe}] from {requestFrom:yyyy-MM-dd}.")
                End If

                Dim rows = response.DictList("rows")
                If rows Is Nothing OrElse rows.Count = 0 Then
                    Throw New InvalidOperationException($"No cybos candles were returned for {normalizedSymbol} [{normalizedTimeframe}] from {requestFrom:yyyy-MM-dd}.")
                End If

                Dim candles = ParseCandles(rows).
                    Where(Function(c) c.Time >= requestFrom).
                    OrderBy(Function(c) c.Time).
                    ToList()

                If candles.Count = 0 Then
                    Throw New InvalidOperationException($"No candles remained after filtering from {requestFrom:yyyy-MM-dd} for {normalizedSymbol} [{normalizedTimeframe}].")
                End If

                Return candles
            Catch ex As Exception
                responseError = ex
                Throw
            Finally
                MessageBus.I.Off(Topics.CANDLE_PERIOD_LOADED, handler)
                If responseError IsNot Nothing Then
                    AppLogger.I.Warn($"StrategyLab candle load failed: {responseError.Message}", "StrategyLab")
                End If
            End Try
        End Function

        Private Shared Function ResolveRequestFromDate(fromDate As DateTime) As DateTime
            Dim businessDate = fromDate.Date
            If businessDate = DateTime.MinValue.Date Then businessDate = DateTime.Today
            If Not TradingCalendar.IsBusinessDay(businessDate) Then
                businessDate = TradingCalendar.PreviousBusinessDay(businessDate)
            End If
            Return businessDate
        End Function

        Private Shared Function NormalizeTimeframe(timeframe As String) As String
            Dim normalized = If(timeframe, "").Trim().ToLowerInvariant()
            If normalized = "" Then Return RuntimeChartSettings.DefaultCandleTimeframe
            If normalized = "daily" Then Return "d"
            If normalized = "weekly" Then Return "w"
            If normalized = "monthly" Then Return "mo"
            If normalized = "m" Then Return "mo"
            If normalized.StartsWith("m", StringComparison.OrdinalIgnoreCase) Then
                Return RuntimeChartSettings.NormalizeMinuteTimeframe(normalized)
            End If
            If normalized.StartsWith("t", StringComparison.OrdinalIgnoreCase) Then
                Dim tickUnit = RuntimeChartSettings.DefaultTickUnit
                If normalized.Length > 1 Then Integer.TryParse(normalized.Substring(1), tickUnit)
                Return RuntimeChartSettings.TickTimeframe(tickUnit)
            End If
            Return normalized
        End Function

        Private Shared Function ParseCandles(rows As List(Of Dictionary(Of String, String))) As List(Of LabCandle)
            Dim candles As New List(Of LabCandle)()
            If rows Is Nothing Then Return candles

            For Each row In rows
                If row Is Nothing Then Continue For

                Dim candleTime = ParseCandleDateTime(row)
                If candleTime = DateTime.MinValue Then Continue For

                candles.Add(New LabCandle With {
                    .Time = candleTime,
                    .Open = RowNum(row, "open", "시가"),
                    .High = RowNum(row, "high", "고가"),
                    .Low = RowNum(row, "low", "저가"),
                    .Close = RowNum(row, "close", "현재가"),
                    .Volume = RowNum(row, "volume", "거래량")
                })
            Next

            Return candles
        End Function

        Private Shared Function ParseCandleDateTime(row As Dictionary(Of String, String)) As DateTime
            If row Is Nothing Then Return DateTime.MinValue

            Dim dt As DateTime = DateTime.MinValue
            If row.ContainsKey("dt") Then dt = SharedUtil.ToDateTime(row("dt"))
            If dt = DateTime.MinValue AndAlso row.ContainsKey("date") Then dt = SharedUtil.ToDateTime(row("date"))
            If dt = DateTime.MinValue AndAlso row.ContainsKey("datetime") Then dt = SharedUtil.ToDateTime(row("datetime"))
            If dt = DateTime.MinValue AndAlso row.ContainsKey("일자") Then dt = SharedUtil.ToDateTime(row("일자"))

            Dim tm As String = ""
            If row.ContainsKey("time") Then tm = row("time")
            If tm = "" AndAlso row.ContainsKey("hhmm") Then tm = row("hhmm")
            If tm = "" AndAlso row.ContainsKey("체결시간") Then tm = row("체결시간")
            If tm = "" AndAlso row.ContainsKey("시간") Then tm = row("시간")

            If dt <> DateTime.MinValue AndAlso dt.TimeOfDay.TotalSeconds > 0 Then Return dt
            If String.IsNullOrWhiteSpace(tm) Then Return dt

            Dim digits = NormalizeHHmmssDigits(tm)
            If digits.Length < 6 Then Return dt

            Dim hh As Integer
            Dim mm As Integer
            Dim ss As Integer
            If Not Integer.TryParse(digits.Substring(0, 2), hh) Then Return dt
            If Not Integer.TryParse(digits.Substring(2, 2), mm) Then Return dt
            If Not Integer.TryParse(digits.Substring(4, 2), ss) Then Return dt

            Dim baseDate = If(dt = DateTime.MinValue, DateTime.Today, dt.Date)
            Return New DateTime(baseDate.Year,
                                baseDate.Month,
                                baseDate.Day,
                                Math.Max(0, Math.Min(23, hh)),
                                Math.Max(0, Math.Min(59, mm)),
                                Math.Max(0, Math.Min(59, ss)))
        End Function

        Private Shared Function NormalizeHHmmssDigits(raw As String) As String
            Dim digits = New String(If(raw, "").Where(Function(ch) Char.IsDigit(ch)).ToArray())
            If digits.Length = 0 Then Return ""
            If digits.Length <= 2 Then Return digits.PadLeft(2, "0"c) & "0000"
            If digits.Length = 3 OrElse digits.Length = 4 Then Return digits.PadLeft(4, "0"c) & "00"
            If digits.Length = 5 Then Return digits.PadLeft(6, "0"c)
            Return digits.Substring(0, 6)
        End Function

        Private Shared Function RowNum(row As Dictionary(Of String, String), ParamArray keys As String()) As Double
            If row Is Nothing OrElse keys Is Nothing Then Return 0
            For Each key In keys
                If String.IsNullOrWhiteSpace(key) OrElse Not row.ContainsKey(key) Then Continue For
                Dim raw = row(key)
                If String.IsNullOrWhiteSpace(raw) Then Continue For
                Return SharedUtil.SafeDouble(raw, True)
            Next
            Return 0
        End Function
    End Class
End Namespace
