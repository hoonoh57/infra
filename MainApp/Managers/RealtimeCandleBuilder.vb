Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports [Shared]

Public NotInheritable Class RealtimeCandleBuilder

    Private Sub New()
    End Sub

    Public Shared Sub UpdateRowsFromTick(rows As List(Of Dictionary(Of String, String)),
                                         tickMsg As Msg,
                                         defaultTimeframe As String)
        If rows Is Nothing OrElse rows.Count = 0 OrElse tickMsg Is Nothing Then Return

        Dim price As Integer = Math.Abs(SharedUtil.SafeInt(tickMsg.Str("price", "0")))
        If price <= 0 Then Return

        Dim volume As Long = ResolveTickVolume(tickMsg)
        Dim tickTime As DateTime = ResolveTickTime(tickMsg)
        If tickTime = DateTime.MinValue Then tickTime = DateTime.Now

        Dim timeframe As String = RuntimeChartSettings.NormalizeMinuteTimeframe(defaultTimeframe)
        Dim minuteUnit As Integer = ResolveMinuteUnit(timeframe)
        Dim barTime As DateTime = AlignToMinuteBucket(tickTime, minuteUnit)
        Dim barTimeStr As String = barTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)

        SyncLock rows
            Dim lastRow As Dictionary(Of String, String) = rows(rows.Count - 1)
            Dim lastDt As String = ResolveRowDateTimeKey(lastRow)

            If lastDt = barTimeStr Then
                UpdateExistingBar(lastRow, price, volume, tickTime)
            ElseIf String.Compare(barTimeStr, lastDt, StringComparison.Ordinal) > 0 Then
                rows.Add(CreateNewBar(barTime, price, volume, tickTime))
            End If
        End SyncLock
    End Sub

    Private Shared Sub UpdateExistingBar(row As Dictionary(Of String, String),
                                         price As Integer,
                                         volume As Long,
                                         tickTime As DateTime)
        If row Is Nothing Then Return

        Dim curHigh As Integer = SharedUtil.SafeInt(ReadRow(row, "high", "고가"))
        Dim curLow As Integer = SharedUtil.SafeInt(ReadRow(row, "low", "저가"))
        Dim curVol As Long = 0
        Long.TryParse(ReadRow(row, "volume", "거래량"), curVol)

        If price > curHigh Then
            row("high") = price.ToString(CultureInfo.InvariantCulture)
            row("고가") = row("high")
        End If

        If curLow <= 0 OrElse price < curLow Then
            row("low") = price.ToString(CultureInfo.InvariantCulture)
            row("저가") = row("low")
        End If

        row("close") = price.ToString(CultureInfo.InvariantCulture)
        row("현재가") = row("close")
        row("volume") = Math.Max(0L, curVol + Math.Max(0L, volume)).ToString(CultureInfo.InvariantCulture)
        row("거래량") = row("volume")
        row("lastTickTime") = tickTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
    End Sub

    Private Shared Function CreateNewBar(barTime As DateTime,
                                         price As Integer,
                                         volume As Long,
                                         tickTime As DateTime) As Dictionary(Of String, String)
        Dim dateText As String = barTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
        Dim timeText As String = barTime.ToString("HHmm", CultureInfo.InvariantCulture)
        Dim dtText As String = barTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
        Dim priceText As String = price.ToString(CultureInfo.InvariantCulture)
        Dim volumeText As String = Math.Max(0L, volume).ToString(CultureInfo.InvariantCulture)

        Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        row("dt") = dtText
        row("date") = dateText
        row("time") = timeText
        row("open") = priceText
        row("high") = priceText
        row("low") = priceText
        row("close") = priceText
        row("volume") = volumeText
        row("시가") = priceText
        row("고가") = priceText
        row("저가") = priceText
        row("현재가") = priceText
        row("거래량") = volumeText
        row("lastTickTime") = tickTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
        Return row
    End Function

    Private Shared Function ResolveTickVolume(tickMsg As Msg) As Long
        Dim value As Double = 0.0R

        value = tickMsg.Dbl("tickVolume")
        If value = 0.0R Then value = tickMsg.Dbl("체결량")
        If value = 0.0R Then value = tickMsg.Dbl("volume")
        If value = 0.0R Then value = tickMsg.Dbl("거래량")

        Return Math.Abs(CLng(value))
    End Function

    Private Shared Function ResolveTickTime(tickMsg As Msg) As DateTime
        Dim candidates As String() = {
            tickMsg.Str("tradeTime", ""),
            tickMsg.Str("tickTime", ""),
            tickMsg.Str("timestamp", ""),
            tickMsg.Str("dt", ""),
            tickMsg.Str("체결시간", ""),
            tickMsg.Str("time", ""),
            tickMsg.Str("시간", "")
        }

        For Each candidate As String In candidates
            Dim parsed As DateTime = ParseTickTime(candidate)
            If parsed <> DateTime.MinValue Then Return parsed
        Next

        Return DateTime.MinValue
    End Function

    Private Shared Function ParseTickTime(raw As String) As DateTime
        If String.IsNullOrWhiteSpace(raw) Then Return DateTime.MinValue

        Dim text As String = raw.Trim()
        Dim parsed As DateTime = DateTime.MinValue
        If DateTime.TryParse(text, parsed) Then Return parsed

        Dim digits As String = New String(text.Where(Function(ch As Char) Char.IsDigit(ch)).ToArray())
        If digits.Length >= 14 Then
            If DateTime.TryParseExact(digits.Substring(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return parsed
        End If
        If digits.Length = 12 Then
            If DateTime.TryParseExact(digits, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return parsed
        End If
        If digits.Length = 8 Then
            If DateTime.TryParseExact(digits & "000000", "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return parsed
        End If

        If digits.Length >= 6 AndAlso digits.Length < 8 Then
            Dim hhmmss As String = NormalizeHHmmss(digits)
            Dim today As DateTime = DateTime.Today
            If DateTime.TryParseExact(today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) & hhmmss,
                                      "yyyyMMddHHmmss",
                                      CultureInfo.InvariantCulture,
                                      DateTimeStyles.None,
                                      parsed) Then
                Return parsed
            End If
        End If

        Return DateTime.MinValue
    End Function

    Private Shared Function NormalizeHHmmss(digits As String) As String
        If String.IsNullOrWhiteSpace(digits) Then Return "000000"
        If digits.Length <= 2 Then Return digits.PadLeft(2, "0"c) & "0000"
        If digits.Length <= 4 Then Return digits.PadLeft(4, "0"c) & "00"
        If digits.Length = 5 Then Return digits.PadLeft(6, "0"c)
        Return digits.Substring(0, 6)
    End Function

    Private Shared Function ResolveMinuteUnit(timeframe As String) As Integer
        Dim minuteUnit As Integer = 1
        Dim tf As String = If(timeframe, "").Trim().ToLowerInvariant()
        If tf.Length > 1 AndAlso tf.StartsWith("m", StringComparison.OrdinalIgnoreCase) Then
            Integer.TryParse(tf.Substring(1), minuteUnit)
        End If
        If minuteUnit <= 0 Then minuteUnit = 1
        Return minuteUnit
    End Function

    Private Shared Function AlignToMinuteBucket(sourceTime As DateTime, minuteUnit As Integer) As DateTime
        Dim safeUnit As Integer = Math.Max(1, minuteUnit)
        Dim bucketMinute As Integer = (sourceTime.Minute \ safeUnit) * safeUnit
        Return New DateTime(sourceTime.Year, sourceTime.Month, sourceTime.Day, sourceTime.Hour, bucketMinute, 0)
    End Function

    Private Shared Function ResolveRowDateTimeKey(row As Dictionary(Of String, String)) As String
        If row Is Nothing Then Return ""

        Dim dtText As String = ReadRow(row, "dt", "datetime")
        Dim parsed As DateTime = ParseTickTime(dtText)
        If parsed <> DateTime.MinValue Then Return parsed.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)

        Dim dateText As String = ReadRow(row, "date", "일자")
        Dim timeText As String = ReadRow(row, "time", "hhmm", "체결시간", "시간")
        parsed = ParseTickTime(dateText & NormalizeHHmmss(New String(If(timeText, "").Where(Function(ch As Char) Char.IsDigit(ch)).ToArray())))
        If parsed <> DateTime.MinValue Then Return parsed.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)

        Return ""
    End Function

    Private Shared Function ReadRow(row As Dictionary(Of String, String), ParamArray keys As String()) As String
        If row Is Nothing OrElse keys Is Nothing Then Return ""
        For Each key As String In keys
            If String.IsNullOrWhiteSpace(key) Then Continue For
            If row.ContainsKey(key) Then Return If(row(key), "")
        Next
        Return ""
    End Function

End Class
