Imports System.IO
Imports System.Collections.Generic

Public NotInheritable Class TradingCalendar

    Private Shared ReadOnly _holidaySet As HashSet(Of String) = LoadHolidaySet()

    Private Const MARKET_OPEN_HOUR As Integer = 9
    Private Const MARKET_OPEN_MINUTE As Integer = 0
    Private Const MARKET_CLOSE_HOUR As Integer = 15
    Private Const MARKET_CLOSE_MINUTE As Integer = 30

    Public Shared Function IsBusinessDay(d As DateTime) As Boolean
        Dim wd = d.DayOfWeek
        If wd = DayOfWeek.Saturday OrElse wd = DayOfWeek.Sunday Then Return False
        Return Not _holidaySet.Contains(d.ToString("yyyyMMdd"))
    End Function

    Public Shared Function PreviousBusinessDay(baseDate As DateTime) As DateTime
        Dim d = baseDate.Date
        Do
            d = d.AddDays(-1)
        Loop While Not IsBusinessDay(d)
        Return d
    End Function

    Public Shared Function NormalizeStopTime(candidate As DateTime) As DateTime
        Dim d = candidate.Date
        If Not IsBusinessDay(d) Then
            d = PreviousBusinessDay(d)
        End If

        Dim openTs = New TimeSpan(MARKET_OPEN_HOUR, MARKET_OPEN_MINUTE, 0)
        Dim closeTs = New TimeSpan(MARKET_CLOSE_HOUR, MARKET_CLOSE_MINUTE, 0)
        Dim t = candidate.TimeOfDay

        ' stopTime은 "해당 영업일의 시작 경계" 역할이므로
        ' 장시간 밖이면 장시작으로 보정한다.
        If t < openTs OrElse t > closeTs Then
            t = openTs
        End If

        Return d.Add(t)
    End Function

    Public Shared Function ResolveStopTime(candleTimes As IList(Of DateTime)) As DateTime
        If candleTimes IsNot Nothing Then
            For Each dt In candleTimes
                If dt = DateTime.MinValue Then Continue For
                Return NormalizeStopTime(dt)
            Next
        End If

        Dim now = DateTime.Now
        Dim d = If(IsBusinessDay(now), now.Date, PreviousBusinessDay(now))
        Return d.Add(New TimeSpan(MARKET_OPEN_HOUR, MARKET_OPEN_MINUTE, 0))
    End Function

    Private Shared Function LoadHolidaySet() As HashSet(Of String)
        Dim setVals As New HashSet(Of String)(StringComparer.Ordinal)
        Dim path = FindHolidayConfigPath()
        If String.IsNullOrWhiteSpace(path) Then Return setVals

        Try
            For Each raw In File.ReadAllLines(path)
                Dim line = If(raw, "").Trim()
                If line = "" Then Continue For
                If line.StartsWith("#") OrElse line.StartsWith(";") Then Continue For
                Dim digits = New String(line.Where(Function(ch) Char.IsDigit(ch)).ToArray())
                If digits.Length = 8 Then
                    setVals.Add(digits)
                End If
            Next
        Catch
        End Try
        Return setVals
    End Function

    Private Shared Function FindHolidayConfigPath() As String
        Const fileName As String = "krx_holidays.txt"
        Dim candidates As New List(Of String)
        candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName))
        candidates.Add(Path.Combine(Environment.CurrentDirectory, fileName))

        Try
            Dim dir = New DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
            Dim hop As Integer = 0
            While dir IsNot Nothing AndAlso hop < 8
                candidates.Add(Path.Combine(dir.FullName, fileName))
                dir = dir.Parent
                hop += 1
            End While
        Catch
        End Try

        For Each p In candidates
            If File.Exists(p) Then Return p
        Next
        Return ""
    End Function

End Class
