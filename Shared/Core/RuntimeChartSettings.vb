Imports System.IO
Imports System.Collections.Generic

Public NotInheritable Class RuntimeChartSettings

    Private Shared ReadOnly _values As Dictionary(Of String, String) = LoadIniValues()
    Private Shared ReadOnly _allowedTickUnits As HashSet(Of Integer) = ParseAllowedTickUnits(GetStr("tick.allowed_units", "1,3,5,10,15,30,60,120"))

    Public Shared ReadOnly Property DefaultCandleTimeframe As String
        Get
            Return NormalizeMinuteTimeframe(GetStr("candle.default_timeframe", "m1"))
        End Get
    End Property

    Public Shared ReadOnly Property DefaultCandleRequestCount As Integer
        Get
            Return GetInt("candle.default_count", 500, 1, 200000)
        End Get
    End Property

    Public Shared ReadOnly Property DefaultChartOpenCount As Integer
        Get
            Return GetInt("chart.open_count", 300, 1, 200000)
        End Get
    End Property

    Public Shared ReadOnly Property DefaultTickUnit As Integer
        Get
            Return NormalizeTickUnit(GetInt("tick.default_unit", 30, 1, 120))
        End Get
    End Property

    Public Shared ReadOnly Property TickRequestMinCount As Integer
        Get
            Return GetInt("tick.request_min_count", 1000, 1, 200000)
        End Get
    End Property

    Public Shared ReadOnly Property TickRequestMaxCount As Integer
        Get
            Return GetInt("tick.request_max_count", 30000, 1, 500000)
        End Get
    End Property

    Public Shared ReadOnly Property TickRequestMultiplier As Integer
        Get
            Return GetInt("tick.request_multiplier", 20, 1, 5000)
        End Get
    End Property

    Public Shared ReadOnly Property TickRetryDelayMs As Integer
        Get
            Return GetInt("tick.retry_delay_ms", 2500, 0, 600000)
        End Get
    End Property

    Public Shared ReadOnly Property TickRetryMax As Integer
        Get
            Return GetInt("tick.retry_max", 3, 0, 100)
        End Get
    End Property

    Public Shared ReadOnly Property ProgramTradeRequestCount As Integer
        Get
            Return GetInt("program_trade.request_count", 300, 1, 100000)
        End Get
    End Property

    Public Shared ReadOnly Property WatchlistFile As String
        Get
            Return GetStr("watchlist.file", "watchlist.json")
        End Get
    End Property

    Public Shared ReadOnly Property ProgramTopCount As Integer
        Get
            Return GetInt("program_top.count", 30, 1, 100000)
        End Get
    End Property

    Public Shared ReadOnly Property ProgramTopRefreshIntervalSeconds As Integer
        Get
            Return GetInt("program_top.refresh_interval_sec", 60, 1, 86400)
        End Get
    End Property

    Public Shared ReadOnly Property MarketFollowKospiCount As Integer
        Get
            Return GetInt("market_follow.kospi_count", 30, 1, 100000)
        End Get
    End Property

    Public Shared ReadOnly Property MarketFollowKosdaqCount As Integer
        Get
            Return GetInt("market_follow.kosdaq_count", 20, 1, 100000)
        End Get
    End Property

    Public Shared ReadOnly Property MarketDataProvider As String
        Get
            Dim p = GetStr("marketdata.provider", "cybos").Trim().ToLowerInvariant()
            If p <> "kiwoom" AndAlso p <> "cybos" Then p = "cybos"
            Return p
        End Get
    End Property

    Public Shared Function GetString(section As String, key As String, defaultValue As String) As String
        Return GetStr(BuildSectionKey(section, key), defaultValue)
    End Function

    Public Shared Function GetInt(section As String, key As String, defaultValue As Integer, minValue As Integer, maxValue As Integer) As Integer
        Return GetInt(BuildSectionKey(section, key), defaultValue, minValue, maxValue)
    End Function

    Public Shared Function GetInt(section As String, key As String, defaultValue As Integer) As Integer
        Return GetInt(BuildSectionKey(section, key), defaultValue, Integer.MinValue, Integer.MaxValue)
    End Function

    Public Shared Function IsMarketDataProvider(providerName As String) As Boolean
        If String.IsNullOrWhiteSpace(providerName) Then Return False
        Return String.Equals(MarketDataProvider, providerName.Trim(), StringComparison.OrdinalIgnoreCase)
    End Function

    Public Shared Function IsAllowedTickUnit(unit As Integer) As Boolean
        Return _allowedTickUnits.Contains(unit)
    End Function

    Public Shared Function NormalizeTickUnit(unit As Integer) As Integer
        If _allowedTickUnits.Contains(unit) Then Return unit
        Return 30
    End Function

    Public Shared Function TickTimeframe(unit As Integer) As String
        Return $"T{NormalizeTickUnit(unit)}"
    End Function

    Public Shared Function NormalizeMinuteTimeframe(tf As String) As String
        If String.IsNullOrWhiteSpace(tf) Then Return "m1"
        Dim t = tf.Trim().ToLowerInvariant()
        If Not t.StartsWith("m") Then Return "m1"

        Dim n As Integer = 1
        If t.Length > 1 Then
            Integer.TryParse(t.Substring(1), n)
        End If
        If n <= 0 Then n = 1
        Return $"m{n}"
    End Function

    Private Shared Function LoadIniValues() As Dictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim path = FindConfigPath("chart.runtime.ini")
        If String.IsNullOrWhiteSpace(path) Then Return result

        Try
            Dim currentSection As String = ""
            For Each raw In File.ReadAllLines(path)
                If raw Is Nothing Then Continue For
                Dim line = raw.Trim()
                If line = "" Then Continue For
                If line.StartsWith("#") OrElse line.StartsWith(";") Then Continue For
                If line.StartsWith("[") AndAlso line.EndsWith("]") AndAlso line.Length > 2 Then
                    currentSection = line.Substring(1, line.Length - 2).Trim()
                    Continue For
                End If
                Dim p = line.IndexOf("="c)
                If p <= 0 Then Continue For
                Dim key = line.Substring(0, p).Trim()
                Dim value = line.Substring(p + 1).Trim()
                If key = "" Then Continue For
                If currentSection <> "" AndAlso key.IndexOf("."c) < 0 Then
                    result($"{currentSection}.{key}") = value
                End If
                result(key) = value
            Next
        Catch
        End Try

        Return result
    End Function

    Private Shared Function FindConfigPath(fileName As String) As String
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

    Private Shared Function GetStr(key As String, defaultValue As String) As String
        Dim v As String = Nothing
        If _values.TryGetValue(key, v) Then
            If Not String.IsNullOrWhiteSpace(v) Then Return v.Trim()
        End If
        Return defaultValue
    End Function

    Private Shared Function BuildSectionKey(section As String, key As String) As String
        Dim sectionName = If(section, "").Trim()
        Dim keyName = If(key, "").Trim()
        If sectionName = "" Then Return keyName
        If keyName = "" Then Return sectionName
        Return $"{sectionName}.{keyName}"
    End Function

    Private Shared Function GetInt(key As String, defaultValue As Integer, minValue As Integer, maxValue As Integer) As Integer
        Dim s = GetStr(key, defaultValue.ToString())
        Dim v As Integer
        If Not Integer.TryParse(s, v) Then v = defaultValue
        If v < minValue Then v = minValue
        If v > maxValue Then v = maxValue
        Return v
    End Function

    Private Shared Function ParseAllowedTickUnits(csv As String) As HashSet(Of Integer)
        Dim setVals As New HashSet(Of Integer)
        If Not String.IsNullOrWhiteSpace(csv) Then
            Dim parts = csv.Split(","c)
            For Each p In parts
                Dim n As Integer
                If Integer.TryParse(p.Trim(), n) AndAlso n > 0 Then
                    setVals.Add(n)
                End If
            Next
        End If

        If setVals.Count = 0 Then
            setVals.Add(1)
            setVals.Add(3)
            setVals.Add(5)
            setVals.Add(10)
            setVals.Add(15)
            setVals.Add(30)
            setVals.Add(60)
            setVals.Add(120)
        End If
        Return setVals
    End Function

End Class
