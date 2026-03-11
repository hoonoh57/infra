' ═══════════════════════════════════════════════════════════════
' SharedUtil.vb — 32/64비트 공용 유틸리티
' ═══════════════════════════════════════════════════════════════

Imports System.Globalization

Public Class SharedUtil

    ''' <summary>가격/수량용 Integer 파서 (부호 제거)</summary>
    Public Shared Function SafeInt(raw As String) As Integer
        If String.IsNullOrWhiteSpace(raw) Then Return 0
        Dim s = raw.Trim().Replace("+", "").Replace("-", "").Replace(",", "")
        Dim r As Integer = 0
        Integer.TryParse(s, r)
        Return r
    End Function

    ''' <summary>부호를 보존하는 Integer 파서</summary>
    Public Shared Function SafeIntSigned(raw As String) As Integer
        If String.IsNullOrWhiteSpace(raw) Then Return 0
        Dim s = raw.Trim().Replace("+", "").Replace(",", "")
        Dim r As Integer = 0
        Integer.TryParse(s, r)
        Return r
    End Function

    ''' <summary>가격/수량용 Long 파서 (부호 제거)</summary>
    Public Shared Function SafeLong(raw As String) As Long
        If String.IsNullOrWhiteSpace(raw) Then Return 0
        Dim s = raw.Trim().Replace("+", "").Replace("-", "").Replace(",", "")
        Dim r As Long = 0
        Long.TryParse(s, r)
        Return r
    End Function

    ''' <summary>부호를 보존하는 Long 파서</summary>
    Public Shared Function SafeLongSigned(raw As String) As Long
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
        Return r
    End Function

    ''' <summary>종목코드 정규화 (앞의 A 제거, 공백 제거)</summary>
    Public Shared Function NormalizeCode(raw As String) As String
        If String.IsNullOrWhiteSpace(raw) Then Return ""
        Dim s = raw.Trim()
        If s.StartsWith("A") AndAlso s.Length = 7 Then s = s.Substring(1)
        Return s
    End Function

    ''' <summary>차트/시세 요청용 코드 정규화 (지수 001 → U001)</summary>
    Public Shared Function NormalizeChartCode(raw As String) As String
        Dim s = NormalizeCode(raw)
        If s = "" Then Return ""

        If s.StartsWith("U", StringComparison.OrdinalIgnoreCase) AndAlso s.Length = 4 Then
            Return "U" & s.Substring(1)
        End If

        If s.Length = 3 AndAlso s.All(Function(ch) Char.IsDigit(ch)) Then
            Return "U" & s
        End If

        Return s
    End Function

    Public Shared Function GetKnownIndexName(raw As String) As String
        Select Case NormalizeChartCode(raw)
            Case "U001"
                Return "코스피"
            Case "U201"
                Return "코스닥"
            Case Else
                Return ""
        End Select
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

    ''' <summary>다양한 형식의 문자열/객체를 DateTime으로 변환</summary>
    Public Shared Function ToDateTime(obj As Object) As DateTime
        If obj Is Nothing OrElse IsDBNull(obj) Then Return DateTime.MinValue
        If TypeOf obj Is DateTime Then Return DirectCast(obj, DateTime)

        ' 숫자인 경우 (Double/Long) 과학적 표기법 방지
        Dim rawStr As String
        If TypeOf obj Is Double OrElse TypeOf obj Is Single OrElse TypeOf obj Is Decimal Then
            rawStr = CDbl(obj).ToString("F0", CultureInfo.InvariantCulture)
        Else
            rawStr = obj.ToString()
        End If

        Dim s = rawStr.Trim().Replace("-", "").Replace(":", "").Replace(" ", "").Replace("/", "").Replace(".", "")
        If String.IsNullOrWhiteSpace(s) Then Return DateTime.MinValue

        Try
            ' yyyyMMddHHmmssfff (17자리)
            If s.Length >= 17 Then
                Return DateTime.ParseExact(s.Substring(0, 17), "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
            End If
            ' yyyyMMddHHmmss (14자리)
            If s.Length = 14 Then
                Return DateTime.ParseExact(s, "yyyyMMddHHmmss", CultureInfo.InvariantCulture)
            End If
            ' yyyyMMddHHmm (12자리)
            If s.Length = 12 Then
                Return DateTime.ParseExact(s, "yyyyMMddHHmm", CultureInfo.InvariantCulture)
            End If
            ' yyMMddHHmm (10자리 - 키움/사이보스 일부 데이터용)
            If s.Length = 10 Then
                Return DateTime.ParseExact(s, "yyMMddHHmm", CultureInfo.InvariantCulture)
            End If
            ' yyyyMMdd (8자리)
            If s.Length = 8 Then
                Return DateTime.ParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture)
            End If
            ' HHmmss (6자리)
            If s.Length = 6 Then
                Return TimeToTimestamp(s)
            End If

            ' 최후의 수단으로 일반 파싱 시도 (단, 예외 방지)
            Dim res As DateTime
            If DateTime.TryParse(rawStr, CultureInfo.InvariantCulture, DateTimeStyles.None, res) Then
                Return res
            End If
            Return DateTime.MinValue
        Catch
            Return DateTime.MinValue
        End Try
    End Function

End Class
