Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Newtonsoft.Json
Imports [Shared]
Imports StrategyCore.Models

Namespace StrategyCore.Services
    Public Class PipeMarketCandleProvider
        Implements ICandleDataProvider

        Private Const FixedProviderName As String = "cybos"

        Private Class CandleSnapshot
            Public Property code As String = ""
            Public Property provider As String = ""
            Public Property timeframe As String = ""
            Public Property savedAt As DateTime
            Public Property rows As New List(Of Dictionary(Of String, String))()
        End Class

        Public Function GetCandles(symbol As String,
                                   timeframe As String,
                                   fromDate As DateTime,
                                   barCount As Integer) As IReadOnlyList(Of LabCandle) Implements ICandleDataProvider.GetCandles
            Dim normalizedSymbol = SharedUtil.NormalizeChartCode(symbol)
            If String.IsNullOrWhiteSpace(normalizedSymbol) Then
                Throw New InvalidOperationException("A valid symbol is required for StrategyLab evaluation.")
            End If

            Dim normalizedTimeframe = NormalizeTimeframe(timeframe)
            Dim snapshotPath = BuildSnapshotPath(normalizedSymbol, normalizedTimeframe)
            If Not File.Exists(snapshotPath) Then
                Throw New InvalidOperationException($"No cached cybos candles were found for {normalizedSymbol} [{normalizedTimeframe}]. Open that symbol/timeframe in MainApp first.")
            End If

            Dim snapshot = JsonConvert.DeserializeObject(Of CandleSnapshot)(File.ReadAllText(snapshotPath))
            If snapshot Is Nothing OrElse snapshot.rows Is Nothing OrElse snapshot.rows.Count = 0 Then
                Throw New InvalidOperationException($"The cached cybos candle snapshot for {normalizedSymbol} [{normalizedTimeframe}] is empty.")
            End If

            Dim candles = ParseCandles(snapshot.rows).
                Where(Function(c) c.Time <> DateTime.MinValue).
                OrderBy(Function(c) c.Time).
                ToList()

            If candles.Count = 0 Then
                Throw New InvalidOperationException($"The cached cybos candle snapshot for {normalizedSymbol} [{normalizedTimeframe}] could not be parsed.")
            End If

            Dim businessDate = ResolveBusinessDate(fromDate)
            Dim filtered = candles.Where(Function(c) c.Time >= businessDate.Date).ToList()
            If filtered.Count = 0 Then
                filtered = candles
            End If

            Dim requestedCount = Math.Max(120, barCount)
            If filtered.Count > requestedCount Then
                filtered = filtered.Skip(filtered.Count - requestedCount).ToList()
            End If

            Return filtered
        End Function

        Private Shared Function BuildSnapshotPath(symbol As String, timeframe As String) As String
            Dim baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                       "Infra",
                                       "StrategyLab",
                                       "candles",
                                       FixedProviderName,
                                       timeframe)
            Return Path.Combine(baseDir, $"{symbol}.json")
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

        Private Shared Function NormalizeTimeframe(timeframe As String) As String
            Dim normalized = If(timeframe, "").Trim().ToLowerInvariant()
            If normalized = "" Then Return RuntimeChartSettings.DefaultCandleTimeframe
            If normalized = "daily" Then Return "d"
            If normalized = "weekly" Then Return "w"
            If normalized = "monthly" Then Return "m"
            If normalized.StartsWith("m", StringComparison.OrdinalIgnoreCase) Then Return RuntimeChartSettings.NormalizeMinuteTimeframe(normalized)
            Return normalized
        End Function

        Private Shared Function ResolveBusinessDate(fromDate As DateTime) As DateTime
            Dim businessDate = fromDate.Date
            If Not TradingCalendar.IsBusinessDay(businessDate) Then
                businessDate = TradingCalendar.PreviousBusinessDay(businessDate)
            End If
            Return businessDate
        End Function
    End Class
End Namespace
