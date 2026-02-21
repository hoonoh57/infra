' ═══════════════════════════════════════════════════════════════
' KiwoomClient.vb — 64‑bit 측 키움 서버 클라이언트
' ═══════════════════════════════════════════════════════════════
' 99% 불변. 복사해서 바로 사용.
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Concurrent
Imports System.Threading
Imports [Shared]

Public Class KiwoomClient

    Private ReadOnly _pipe As PipeClient
    Private ReadOnly _callbacks As New ConcurrentDictionary(Of Integer, Action(Of Msg))()
    Private _seq As Integer = 0

    ' ─── 이벤트 (실시간 푸시) ───
    Public Event 체결수신(msg As Msg)
    Public Event 호가수신(msg As Msg)
    Public Event 프로그램매매수신(msg As Msg)
    Public Event 장상태수신(msg As Msg)
    Public Event 주문체결(msg As Msg)
    Public Event 잔고변경(msg As Msg)
    Public Event 조건편입(msg As Msg)
    Public Event 연결됨()
    Public Event 연결끊김()

    Public Sub New()
        _pipe = New PipeClient("KiwoomPipe")
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
    ' 인증
    ' ════════════════════════════════════════

    Public Sub 로그인(callback As Action(Of Msg))
        호출("login", callback)
    End Sub

    Public Sub 상태조회(callback As Action(Of Msg))
        호출("status", callback)
    End Sub

    ' ════════════════════════════════════════
    ' 시세 조회
    ' ════════════════════════════════════════

    Public Sub 주식기본정보(code As String, cb As Action(Of Msg))
        호출("주식기본정보", cb, "code", code)
    End Sub

    Public Sub 체결정보(code As String, cb As Action(Of Msg))
        호출("체결정보", cb, "code", code)
    End Sub

    Public Sub 호가요청(code As String, cb As Action(Of Msg))
        호출("주식호가요청", cb, "code", code)
    End Sub

    ' ════════════════════════════════════════
    ' 차트
    ' ════════════════════════════════════════

    Public Sub 분봉조회(code As String, 틱범위 As Integer, cb As Action(Of Msg))
        호출("분봉조회", cb, "code", code, "틱범위", 틱범위.ToString(), "수정주가구분", "1")
    End Sub

    Public Sub 일봉조회(code As String, 기준일자 As String, cb As Action(Of Msg))
        호출("일봉조회", cb, "code", code, "기준일자", 기준일자, "수정주가구분", "1")
    End Sub

    Public Sub 주봉조회(code As String, 기준일자 As String, cb As Action(Of Msg))
        호출("주봉조회", cb, "code", code, "기준일자", 기준일자, "수정주가구분", "1")
    End Sub

    Public Sub 월봉조회(code As String, 기준일자 As String, cb As Action(Of Msg))
        호출("월봉조회", cb, "code", code, "기준일자", 기준일자, "수정주가구분", "1")
    End Sub

    ' ════════════════════════════════════════
    ' 투자자
    ' ════════════════════════════════════════

    Public Sub 투자자조회(code As String, 시작 As String, 종료 As String, cb As Action(Of Msg))
        호출("투자자조회", cb, "code", code, "시작일자", 시작, "종료일자", 종료)
    End Sub

    Public Sub 프로그램매매(code As String, 시작 As String, 종료 As String, cb As Action(Of Msg))
        호출("프로그램매매", cb, "code", code, "시작일자", 시작, "종료일자", 종료)
    End Sub

    ' ════════════════════════════════════════
    ' 재무
    ' ════════════════════════════════════════

    Public Sub 재무정보(code As String, cb As Action(Of Msg))
        호출("주식재무정보", cb, "code", code)
    End Sub

    ' ════════════════════════════════════════
    ' 업종
    ' ════════════════════════════════════════

    Public Sub 업종현재가(업종코드 As String, cb As Action(Of Msg))
        호출("업종현재가", cb, "업종코드", 업종코드)
    End Sub

    Public Sub 업종별종목(업종코드 As String, cb As Action(Of Msg))
        호출("업종별종목", cb, "업종코드", 업종코드)
    End Sub

    ' ════════════════════════════════════════
    ' 순위
    ' ════════════════════════════════════════

    Public Sub 거래량상위(시장 As String, cb As Action(Of Msg))
        호출("거래량상위", cb, "시장구분", 시장, "정렬구분", "1")
    End Sub

    Public Sub 등락률상위(시장 As String, cb As Action(Of Msg))
        호출("등락률상위", cb, "시장구분", 시장, "정렬구분", "1")
    End Sub

    ' ════════════════════════════════════════
    ' 관심종목 일괄조회
    ' ════════════════════════════════════════

    Public Sub 관심종목정보(codes As String, cb As Action(Of Msg))
        호출("관심종목정보", cb, "codes", codes)
    End Sub

    ' ════════════════════════════════════════
    ' 계좌
    ' ════════════════════════════════════════

    Public Sub 계좌평가(accountNo As String, pass As String, cb As Action(Of Msg))
        호출("계좌평가현황", cb, "accountNo", accountNo, "pass", pass, "media", "00", "query", "2")
    End Sub

    Public Sub 미체결조회(accountNo As String, cb As Action(Of Msg))
        호출("미체결조회", cb, "accountNo", accountNo, "매매구분", "0", "code", "", "체결구분", "1")
    End Sub

    Public Sub 당일실현손익(accountNo As String, pass As String, cb As Action(Of Msg))
        Dim today = DateTime.Now.ToString("yyyyMMdd")
        호출("당일실현손익", cb, "accountNo", accountNo, "시작일자", today, "종료일자", today,
             "pass", pass, "media", "00", "query", "1")
    End Sub

    ' ════════════════════════════════════════
    ' 주문
    ' ════════════════════════════════════════

    Public Sub 매수_시장가(code As String, qty As Integer, cb As Action(Of Msg))
        호출("매수_시장가", cb, "code", code, "qty", qty, "price", 0)
    End Sub

    Public Sub 매수_지정가(code As String, qty As Integer, price As Integer, cb As Action(Of Msg))
        호출("매수_지정가", cb, "code", code, "qty", qty, "price", price)
    End Sub

    Public Sub 매도_시장가(code As String, qty As Integer, cb As Action(Of Msg))
        호출("매도_시장가", cb, "code", code, "qty", qty, "price", 0)
    End Sub

    Public Sub 매도_지정가(code As String, qty As Integer, price As Integer, cb As Action(Of Msg))
        호출("매도_지정가", cb, "code", code, "qty", qty, "price", price)
    End Sub

    Public Sub 주문정정(orgOrderNo As String, code As String, qty As Integer, price As Integer, cb As Action(Of Msg))
        호출("주문정정", cb, "code", code, "qty", qty, "price", price, "orgOrderNo", orgOrderNo)
    End Sub

    Public Sub 주문취소(orgOrderNo As String, code As String, qty As Integer, cb As Action(Of Msg))
        호출("주문취소", cb, "code", code, "qty", qty, "orgOrderNo", orgOrderNo)
    End Sub

    ' ════════════════════════════════════════
    ' 실시간
    ' ════════════════════════════════════════

    Public Sub 실시간_체결구독(codes As String)
        호출("실시간_체결", Nothing, "codes", codes)
    End Sub

    Public Sub 실시간_호가구독(codes As String)
        호출("실시간_호가", Nothing, "codes", codes)
    End Sub

    Public Sub 실시간_프로그램구독(codes As String)
        호출("실시간_프로그램", Nothing, "codes", codes)
    End Sub

    Public Sub 실시간_해제(Optional codes As String = "")
        호출("실시간_해제", Nothing, "codes", codes)
    End Sub

    ' ════════════════════════════════════════
    ' 조건검색
    ' ════════════════════════════════════════

    Public Sub 조건검색목록(cb As Action(Of Msg))
        호출("조건검색목록", cb)
    End Sub

    Public Sub 조건검색시작(name As String, index As Integer, cb As Action(Of Msg), Optional isRealtime As Boolean = True)
        호출("조건검색시작", cb, "name", name, "index", index, "realtime", If(isRealtime, 1, 0))
    End Sub

    Public Sub 조건검색중지(name As String, index As Integer)
        호출("조건검색중지", Nothing, "name", name, "index", index)
    End Sub

    ' ════════════════════════════════════════
    ' 유틸
    ' ════════════════════════════════════════

    Public Sub 종목코드목록(시장 As String, cb As Action(Of Msg))
        호출("종목코드목록", cb, "market", 시장)
    End Sub

    Public Sub 종목명(code As String, cb As Action(Of Msg))
        호출("종목명", cb, "code", code)
    End Sub

    ' ════════════════════════════════════════
    ' 메시지 수신 처리
    ' ════════════════════════════════════════

    Private Sub OnMessage(msg As Msg)
        ' 시퀀스 매칭 콜백
        If msg.Has("_seq") Then
            Dim s = msg.Int("_seq")
            Dim cb As Action(Of Msg) = Nothing
            If _callbacks.TryRemove(s, cb) AndAlso cb IsNot Nothing Then
                cb(msg)
                Return
            End If
        End If

        ' 실시간 타입별 이벤트 발화
        Select Case msg.Topic
            Case Topics.TICK : RaiseEvent 체결수신(msg)
            Case Topics.ORDERBOOK : RaiseEvent 호가수신(msg)
            Case Topics.PROGRAM_TRADE : RaiseEvent 프로그램매매수신(msg)
            Case Topics.MARKET_STATUS : RaiseEvent 장상태수신(msg)
            Case Topics.ORDER_EXECUTED : RaiseEvent 주문체결(msg)
            Case Topics.ORDER_BALANCE_CHANGED : RaiseEvent 잔고변경(msg)
            Case Topics.CONDITION_HIT : RaiseEvent 조건편입(msg)
        End Select
    End Sub

End Class
