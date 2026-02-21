' ═══════════════════════════════════════════════════════════════
' StockInfoItem.vb — 표준화된 종목정보 모델
' ═══════════════════════════════════════════════════════════════
' 모든 데이터소스에서 추출된 종목은 이 하나의 모델로 통일.
' ═══════════════════════════════════════════════════════════════

Imports [Shared]

Public Enum DataSourceType
    조건검색 = 0
    주도섹터 = 1
    프로그램순매수 = 2
    관심종목 = 3
    코스피추종 = 4
    코스닥추종 = 5
    수동추가 = 6
    테마 = 7
End Enum

Public Enum DataReadyState
    None = 0            ' 코드만 있음
    InfoLoaded = 1      ' 기본정보 로드됨
    CandleLoaded = 2    ' 캔들 다운로드 완료
    RealtimeOn = 3      ' 실시간 구독 중
    Ready = 4           ' 필터 통과, 매매 가능
    Filtered = 5        ' 필터에서 제외됨
End Enum

Public Class StockInfoItem

    ' ─── 식별 ───
    Public Property Code As String = ""
    Public Property Name As String = ""

    ' ─── 데이터소스 ───
    Public Property Sources As New HashSet(Of DataSourceType)()
    Public Property SourceDetail As String = ""     ' 조건식명, 섹터명 등
    Public Property AddedTime As DateTime = DateTime.Now

    ' ─── 시세 (실시간 업데이트) ───
    Public Property Price As Integer = 0
    Public Property PrevClose As Integer = 0
    Public Property Change As Integer = 0
    Public Property ChangeRate As Double = 0
    Public Property Volume As Long = 0
    Public Property Open As Integer = 0
    Public Property High As Integer = 0
    Public Property Low As Integer = 0
    Public Property Ask1 As Integer = 0
    Public Property Bid1 As Integer = 0
    Public Property Strength As Double = 0          ' 체결강도

    ' ─── 기본정보 ───
    Public Property MarketCap As Long = 0           ' 시가총액
    Public Property PER As Double = 0
    Public Property ListedShares As Long = 0

    ' ─── 상태 ───
    Public Property State As DataReadyState = DataReadyState.None
    Public Property CandleCount As Integer = 0
    Public Property LastTickTime As DateTime = DateTime.MinValue
    Public Property IsRealtimeSubscribed As Boolean = False

    ' ─── 필터 결과 ───
    Public Property FilterPassed As Boolean = True
    Public Property FilterReason As String = ""

    ' ─── 소스 관리 ───

    Public Sub AddSource(src As DataSourceType, Optional detail As String = "")
        Sources.Add(src)
        If detail <> "" Then
            If SourceDetail = "" Then
                SourceDetail = detail
            ElseIf Not SourceDetail.Contains(detail) Then
                SourceDetail &= "," & detail
            End If
        End If
    End Sub

    Public Function HasSource(src As DataSourceType) As Boolean
        Return Sources.Contains(src)
    End Function

    Public Function SourceText() As String
        Return String.Join("/", Sources.Select(Function(s) s.ToString()))
    End Function

    ''' <summary>실시간 틱 데이터로 업데이트</summary>
    Public Sub UpdateFromTick(m As Msg)
        Dim p = CInt(m.Dbl("price"))
        If p > 0 Then
            Price = p
            If PrevClose > 0 Then
                Change = Price - PrevClose
                ChangeRate = (Change / CDbl(PrevClose)) * 100
            End If
        End If

        Dim v = m.Dbl("volume")
        If v > 0 Then Volume += CLng(v)

        Dim o = CInt(m.Dbl("open")) : If o > 0 Then Open = o
        Dim h = CInt(m.Dbl("high")) : If h > 0 AndAlso h > High Then High = h
        Dim l = CInt(m.Dbl("low")) : If l > 0 AndAlso (Low = 0 OrElse l < Low) Then Low = l
        Dim a = CInt(m.Dbl("ask1")) : If a > 0 Then Ask1 = a
        Dim b = CInt(m.Dbl("bid1")) : If b > 0 Then Bid1 = b
        Dim st = m.Dbl("strength") : If st > 0 Then Strength = st

        Dim cv = m.Dbl("cumVolume") : If cv > 0 Then Volume = CLng(cv)

        LastTickTime = DateTime.Now
    End Sub

    ''' <summary>OPTKWFID / MarketEye 결과로 업데이트</summary>
    Public Sub UpdateFromInfo(row As Dictionary(Of String, Object))
        If row Is Nothing Then Return

        If row.ContainsKey("종목명") Then Name = row("종목명").ToString()
        If row.ContainsKey("name") Then Name = row("name").ToString()

        Dim p = SafeInt(row, "현재가", "price") : If p > 0 Then Price = p
        Dim v = SafeLong(row, "거래량", "volume") : If v > 0 Then Volume = v
        Dim cr = SafeDbl(row, "등락율", "changeRate") : ChangeRate = cr
        Dim ch = SafeInt(row, "전일대비", "change") : Change = ch
        Dim hi = SafeInt(row, "고가", "high") : If hi > 0 Then High = hi
        Dim lo = SafeInt(row, "저가", "low") : If lo > 0 Then Low = lo
        Dim op = SafeInt(row, "시가", "open") : If op > 0 Then Open = op

        If Price > 0 AndAlso Change <> 0 Then
            PrevClose = Price - Change
        End If

        State = DataReadyState.InfoLoaded
    End Sub

    Private Shared Function SafeInt(d As Dictionary(Of String, Object), ParamArray keys() As String) As Integer
        For Each k In keys
            If d.ContainsKey(k) Then
                Dim valStr = d(k)?.ToString()
                If Not String.IsNullOrEmpty(valStr) Then
                    Dim v As Integer = 0
                    If Integer.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
                        Return Math.Abs(v)
                    End If
                End If
            End If
        Next
        Return 0
    End Function

    Private Shared Function SafeLong(d As Dictionary(Of String, Object), ParamArray keys() As String) As Long
        For Each k In keys
            If d.ContainsKey(k) Then
                Dim valStr = d(k)?.ToString()
                If Not String.IsNullOrEmpty(valStr) Then
                    Dim v As Long = 0
                    If Long.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
                        Return Math.Abs(v)
                    End If
                End If
            End If
        Next
        Return 0
    End Function

    Private Shared Function SafeDbl(d As Dictionary(Of String, Object), ParamArray keys() As String) As Double
        For Each k In keys
            If d.ContainsKey(k) Then
                Dim valStr = d(k)?.ToString()
                If Not String.IsNullOrEmpty(valStr) Then
                    Dim v As Double = 0
                    If Double.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
                        Return v
                    End If
                End If
            End If
        Next
        Return 0
    End Function

End Class
