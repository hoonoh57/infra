' ═══════════════════════════════════════════════════════════════
' CybosClient.vb — 64‑bit 측 사이보스 서버 클라이언트
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Concurrent
Imports System.Threading
Imports [Shared]

Public Class CybosClient

    Private ReadOnly _pipe As PipeClient
    Private ReadOnly _callbacks As New ConcurrentDictionary(Of Integer, Action(Of Msg))()
    Private _seq As Integer = 0
    Public Event ProgramTradeRealtime(msg As Msg)

    Public Event 연결됨()
    Public Event 연결끊김()

    Public Sub New()
        _pipe = New PipeClient("CybosPipe")
        AddHandler _pipe.MessageReceived, AddressOf OnMessage
        AddHandler _pipe.Connected, Sub() RaiseEvent 연결됨()
        AddHandler _pipe.Disconnected, Sub() RaiseEvent 연결끊김()
    End Sub

    Public Sub 연결()
        _pipe.Connect()
    End Sub

    Public Sub 연결해제()
        _pipe.Disconnect()
    End Sub

    Public ReadOnly Property Is연결됨 As Boolean
        Get
            Return _pipe.IsConnected
        End Get
    End Property

    ' ════════════════════════════════════════
    ' 범용 호출
    ' ════════════════════════════════════════

    Public Sub 호출(funcName As String, callback As Action(Of Msg), ParamArray pairs() As Object)
        Dim s = Interlocked.Increment(_seq)
        Dim m As New Msg("CALL", pairs)
        m("func") = funcName
        m("_seq") = s
        If callback IsNot Nothing Then _callbacks(s) = callback
        _pipe.Send(m)
    End Sub

    ' ════════════════════════════════════════
    ' 차트
    ' ════════════════════════════════════════

    Public Sub 분봉(code As String, interval As Integer, count As Integer, cb As Action(Of Msg))
        호출("분봉", cb, "code", code, "interval", interval, "count", count)
    End Sub

    Public Sub 분봉기간(code As String, interval As Integer, stopTime As String, cb As Action(Of Msg))
        호출("분봉기간", cb, "code", code, "interval", interval, "timeframe", $"m{Math.Max(1, interval)}", "stopTime", stopTime)
    End Sub

    Public Sub 일봉(code As String, count As Integer, cb As Action(Of Msg))
        호출("일봉", cb, "code", code, "count", count)
    End Sub

    Public Sub 주봉(code As String, count As Integer, cb As Action(Of Msg))
        호출("주봉", cb, "code", code, "count", count)
    End Sub

    Public Sub 월봉(code As String, count As Integer, cb As Action(Of Msg))
        호출("월봉", cb, "code", code, "count", count)
    End Sub

    Public Sub 틱차트(code As String, count As Integer, tickUnit As Integer, cb As Action(Of Msg))
        Dim normalizedTickUnit = RuntimeChartSettings.NormalizeTickUnit(tickUnit)
        호출("틱차트", cb, "code", code, "count", count, "tickUnit", normalizedTickUnit, "timeframe", RuntimeChartSettings.TickTimeframe(normalizedTickUnit))
    End Sub

    Public Sub 틱차트기간(code As String, tickUnit As Integer, stopTime As String, cb As Action(Of Msg))
        Dim normalizedTickUnit = RuntimeChartSettings.NormalizeTickUnit(tickUnit)
        호출("틱차트기간", cb,
            "code", code,
            "tickUnit", normalizedTickUnit,
            "timeframe", RuntimeChartSettings.TickTimeframe(normalizedTickUnit),
            "stopTime", stopTime)
    End Sub

    Public Sub 기간캔들(code As String, timeframe As String, from As String, [to] As String, cb As Action(Of Msg))
        호출("기간캔들", cb, "code", code, "timeframe", timeframe, "from", from, "to", [to])
    End Sub

    ' ════════════════════════════════════════
    ' 프로그램매매
    ' ════════════════════════════════════════

    Public Sub 프로그램순매수(code As String, count As Integer, cb As Action(Of Msg), Optional stopTime As String = "")
        If String.IsNullOrWhiteSpace(stopTime) Then
            호출("프로그램순매수", cb, "code", code, "count", count)
        Else
            호출("프로그램순매수", cb, "code", code, "count", count, "stopTime", stopTime)
        End If
    End Sub

    Public Sub 프로그램순매수실시간등록(code As String, cb As Action(Of Msg))
        호출("프로그램순매수실시간등록", cb, "code", code)
    End Sub

    Public Sub 프로그램순매수실시간해지(code As String, cb As Action(Of Msg))
        호출("프로그램순매수실시간해지", cb, "code", code)
    End Sub

    ' ════════════════════════════════════════
    ' 투자자
    ' ════════════════════════════════════════

    Public Sub 투자자매매(code As String, count As Integer, cb As Action(Of Msg))
        호출("투자자매매", cb, "code", code, "count", count)
    End Sub

    ' ════════════════════════════════════════
    ' 재무/기본정보
    ' ════════════════════════════════════════

    Public Sub 종목기본정보(code As String, cb As Action(Of Msg))
        호출("종목기본정보", cb, "code", code)
    End Sub

    Public Sub 복수종목정보(codes As String, cb As Action(Of Msg))
        호출("복수종목정보", cb, "codes", codes)
    End Sub

    ' ════════════════════════════════════════
    ' 호가
    ' ════════════════════════════════════════

    Public Sub 호가정보(code As String, cb As Action(Of Msg))
        호출("호가정보", cb, "code", code)
    End Sub

    ' ════════════════════════════════════════
    ' 섹터/테마
    ' ════════════════════════════════════════

    Public Sub 업종별종목(sectorCode As String, cb As Action(Of Msg))
        호출("업종별종목", cb, "sectorCode", sectorCode)
    End Sub

    Public Sub 테마별종목(themeCode As String, cb As Action(Of Msg))
        호출("테마별종목", cb, "themeCode", themeCode)
    End Sub

    ' ════════════════════════════════════════
    ' 뉴스
    ' ════════════════════════════════════════

    Public Sub 뉴스목록(cb As Action(Of Msg), Optional code As String = "")
        호출("뉴스목록", cb, "code", code)
    End Sub

    Public Sub 뉴스본문(newsCode As String, cb As Action(Of Msg))
        호출("뉴스본문", cb, "newsCode", newsCode)
    End Sub

    ' ════════════════════════════════════════
    ' 조건검색
    ' ════════════════════════════════════════

    Public Sub 조건검색목록(cb As Action(Of Msg))
        호출("조건검색목록", cb)
    End Sub

    Public Sub 조건검색실행(condId As String, cb As Action(Of Msg))
        호출("조건검색실행", cb, "id", condId)
    End Sub

    ' ════════════════════════════════════════
    ' 유틸
    ' ════════════════════════════════════════

    Public Sub 종목코드목록(cb As Action(Of Msg))
        호출("종목코드목록", cb)
    End Sub

    Public Sub 연결상태(cb As Action(Of Msg))
        호출("연결상태", cb)
    End Sub

    ' ════════════════════════════════════════
    ' 수신 처리
    ' ════════════════════════════════════════

    Private Sub OnMessage(msg As Msg)
        If msg.Has("_seq") Then
            Dim s = msg.Int("_seq")
            Dim cb As Action(Of Msg) = Nothing
            If _callbacks.TryRemove(s, cb) AndAlso cb IsNot Nothing Then
                cb(msg)
                Return
            End If
        End If

        Select Case msg.Topic
            Case Topics.PROGRAM_TRADE
                RaiseEvent ProgramTradeRealtime(msg)
        End Select
    End Sub

End Class
