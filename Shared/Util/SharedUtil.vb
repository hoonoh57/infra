' ═══════════════════════════════════════════════════════════════
' SharedUtil.vb — 32/64비트 공용 유틸리티
' ═══════════════════════════════════════════════════════════════

Imports System.Globalization

Public Class SharedUtil

    ''' <summary>부호/공백 제거 후 Integer</summary>
    Public Shared Function SafeInt(raw As String) As Integer
        If String.IsNullOrWhiteSpace(raw) Then Return 0
        Dim s = raw.Trim().Replace("+", "").Replace(",", "")
        If s.StartsWith("-") Then
            Dim v As Integer = 0
            Integer.TryParse(s, v)
            Return v
        End If
        s = s.TrimStart("-"c)
        Dim r As Integer = 0
        Integer.TryParse(s, r)
        If raw.Trim().StartsWith("-") Then r = -r
        Return r
    End Function

    ''' <summary>부호/공백 제거 후 Long</summary>
    Public Shared Function SafeLong(raw As String) As Long
        If String.IsNullOrWhiteSpace(raw) Then Return 0
        Dim s = raw.Trim().Replace("+", "").Replace(",", "")
        Dim r As Long = 0
        Long.TryParse(s, r)
        Return r
    End Function

    ''' <summary>부호/공백 제거 후 Double (keepSign=True면 부호 보존)</summary>
    Public Shared Function SafeDouble(raw As String, Optional keepSign As Boolean = False) As Double
        If String.IsNullOrWhiteSpace(raw) Then Return 0
        Dim s = raw.Trim().Replace(",", "")
        If Not keepSign Then s = s.Replace("+", "").Replace("-", "")
        Dim r As Double = 0
        Double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, r)
        If Not keepSign AndAlso raw.Trim().StartsWith("-") Then r = -r
        Return r
    End Function

    ''' <summary>종목코드 정규화 (앞의 A 제거, 공백 제거)</summary>
    Public Shared Function NormalizeCode(raw As String) As String
        If String.IsNullOrWhiteSpace(raw) Then Return ""
        Dim s = raw.Trim()
        If s.StartsWith("A") AndAlso s.Length = 7 Then s = s.Substring(1)
        Return s
    End Function

    ''' <summary>HHmmss 문자열 → 타임스탬프 (오늘 기준)</summary>
    Public Shared Function TimeToTimestamp(hhmmss As String) As DateTime
        If String.IsNullOrWhiteSpace(hhmmss) OrElse hhmmss.Length < 4 Then Return DateTime.Now
        Dim s = hhmmss.Trim().PadRight(6, "0"c)
        Dim hh = Integer.Parse(s.Substring(0, 2))
        Dim mm = Integer.Parse(s.Substring(2, 2))
        Dim ss = If(s.Length >= 6, Integer.Parse(s.Substring(4, 2)), 0)
        Return New DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, hh, mm, ss)
    End Function

    ''' <summary>영업일 기준 조정 날짜 (주말→금요일)</summary>
    Public Shared Function GetAdjustedDate(Optional refDate As DateTime? = Nothing) As String
        Dim d = If(refDate, DateTime.Now)
        ' 장시작 전이면 전일
        If d.Hour < 9 Then d = d.AddDays(-1)
        ' 주말이면 금요일로
        While d.DayOfWeek = DayOfWeek.Saturday OrElse d.DayOfWeek = DayOfWeek.Sunday
            d = d.AddDays(-1)
        End While
        Return d.ToString("yyyyMMdd")
    End Function

    ''' <summary>이전 영업일</summary>
    Public Shared Function GetPreviousBusinessDay(d As DateTime) As DateTime
        d = d.AddDays(-1)
        While d.DayOfWeek = DayOfWeek.Saturday OrElse d.DayOfWeek = DayOfWeek.Sunday
            d = d.AddDays(-1)
        End While
        Return d
    End Function

End Class
