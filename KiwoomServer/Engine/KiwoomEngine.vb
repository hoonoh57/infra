' ═══════════════════════════════════════════════════════════════
' KiwoomEngine.vb — 카탈로그 기반 범용 키움 API 실행기
' ═══════════════════════════════════════════════════════════════
' 99% 불변. 모든 TR/주문/실시간/조건검색을 카탈로그 기반으로 처리.
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Concurrent
Imports System.Threading
Imports AxKHOpenAPILib
Imports [Shared]

Public Class KiwoomEngine

    ' ─── 외부 참조 ───
    Private ReadOnly _api As AxKHOpenAPI
    Private ReadOnly _trQueue As New TrQueue()
    Private _screenCounter As Integer = 5000

    ' ─── 상태 ───
    Private _isLoggedIn As Boolean = False
    Private _accountNo As String = ""
    Private _accounts As String() = {}

    ' ─── 대기 중 요청 ───
    Private ReadOnly _pendingTr As New ConcurrentDictionary(Of String, PendingRequest)()
    Private _loginWaiter As ManualResetEventSlim
    Private _loginResult As Msg
    Private _condLoadWaiter As ManualResetEventSlim
    Private _condLoadResult As Msg
    Private _condSearchWaiter As ManualResetEventSlim
    Private _condSearchResult As Msg

    Private Class PendingRequest
        Public Property Callback As Action(Of Msg)
        Public Property FuncDef As KiwoomCatalog.FuncDef
        Public Property InputMsg As Msg
        Public Property AccumulatedRows As List(Of Dictionary(Of String, String))
        Public Property ScreenNo As String
        Public Sub New()
            AccumulatedRows = New List(Of Dictionary(Of String, String))()
        End Sub
    End Class

    ' ─── 이벤트: 서버→클라이언트 푸시 ───
    Public Event RealtimeReceived(msg As Msg)
    Public Event ChejanReceived(msg As Msg)
    Public Event ConditionHit(msg As Msg)

    Public Sub New(api As AxKHOpenAPI)
        _api = api
        AddHandler _api.OnEventConnect, AddressOf OnEventConnect
        AddHandler _api.OnReceiveTrData, AddressOf OnReceiveTrData
        AddHandler _api.OnReceiveRealData, AddressOf OnReceiveRealData
        AddHandler _api.OnReceiveChejanData, AddressOf OnReceiveChejanData
        AddHandler _api.OnReceiveConditionVer, AddressOf OnReceiveConditionVer
        AddHandler _api.OnReceiveTrCondition, AddressOf OnReceiveTrCondition
        AddHandler _api.OnReceiveRealCondition, AddressOf OnReceiveRealCondition
        AddHandler _api.OnReceiveMsg, AddressOf OnReceiveMsg
    End Sub

    Private Function NextScreen() As String
        Dim s = Interlocked.Increment(_screenCounter)
        If s > 9999 Then
            Interlocked.Exchange(_screenCounter, 5000)
            s = 5000
        End If
        Return s.ToString("0000")
    End Function

    ' ════════════════════════════════════════
    ' 범용 실행
    ' ════════════════════════════════════════

    Public Sub Execute(msg As Msg, callback As Action(Of Msg))
        Dim funcName = msg.Str("func")
        Dim def = KiwoomCatalog.Find(funcName)

        If def Is Nothing Then
            ' 카탈로그에 없으면 직접 토픽으로 분기
            Select Case funcName
                Case "login" : DoLogin(callback)
                Case "status" : DoStatus(callback)
                Case "종목코드목록" : DoCodeList(msg, callback)
                Case "종목명" : DoCodeName(msg, callback)
                Case Else
                    callback(MakeError($"알 수 없는 함수: {funcName}"))
            End Select
            Return
        End If

        Select Case def.Category
            Case KiwoomCatalog.FuncCategory.TrRequest
                DoTrRequest(def, msg, callback)
            Case KiwoomCatalog.FuncCategory.Order
                DoOrder(def, msg, callback)
            Case KiwoomCatalog.FuncCategory.RealtimeReg
                DoRealtimeReg(def, msg, callback)
            Case KiwoomCatalog.FuncCategory.RealtimeUnreg
                DoRealtimeUnreg(msg, callback)
            Case KiwoomCatalog.FuncCategory.Condition
                DoCondition(def, msg, callback)
            Case Else
                callback(MakeError($"미구현 카테고리: {def.Category}"))
        End Select
    End Sub

    ' ════════════════════════════════════════
    ' 로그인
    ' ════════════════════════════════════════

    Private Sub DoLogin(callback As Action(Of Msg))
        If _isLoggedIn Then
            callback(MakeOk("이미 로그인", "accountNo", _accountNo, "accounts", _accounts))
            Return
        End If

        _loginWaiter = New ManualResetEventSlim(False)
        UiInvoke(Sub() _api.CommConnect())

        If _loginWaiter.Wait(30000) Then
            callback(_loginResult)
        Else
            callback(MakeError("로그인 타임아웃"))
        End If
    End Sub

    Private Sub OnEventConnect(sender As Object, e As _DKHOpenAPIEvents_OnEventConnectEvent)
        If e.nErrCode = 0 Then
            _isLoggedIn = True
            Dim accRaw = _api.GetLoginInfo("ACCNO")
            _accounts = accRaw.Split({";"c}, StringSplitOptions.RemoveEmptyEntries)
            _accountNo = If(_accounts.Length > 0, _accounts(0).Trim(), "")
            _loginResult = MakeOk("로그인 성공", "accountNo", _accountNo, "accounts", _accounts)
        Else
            _isLoggedIn = False
            _loginResult = MakeError($"로그인 실패 코드: {e.nErrCode}")
        End If
        _loginWaiter?.Set()
    End Sub

    Private Sub DoStatus(callback As Action(Of Msg))
        Dim m = MakeOk("상태")
        m("isLoggedIn") = _isLoggedIn
        m("accountNo") = _accountNo
        m("accounts") = _accounts
        If _isLoggedIn Then
            m("serverName") = UiInvoke(Of String)(Function() _api.GetLoginInfo("SERVER_GUBUN"))
        End If
        callback(m)
    End Sub

    ' ════════════════════════════════════════
    ' TR 요청
    ' ════════════════════════════════════════

    Private Sub DoTrRequest(def As KiwoomCatalog.FuncDef, msg As Msg, callback As Action(Of Msg))
        If Not _isLoggedIn Then
            callback(MakeError("로그인 필요"))
            Return
        End If

        Dim rqName = $"{def.TrCode}_{Guid.NewGuid():N}"
        Dim scrNo = NextScreen()

        Dim pending As New PendingRequest With {
            .Callback = callback, .FuncDef = def, .InputMsg = msg, .ScreenNo = scrNo
        }
        _pendingTr(rqName) = pending

        ' OPTKWFID 특수 처리
        If def.TrCode = "OPTKWFID" Then
            _trQueue.Enqueue(Sub()
                                 UiInvoke(Sub()
                                              Dim codes = msg.Str("codes")
                                              Dim cnt = codes.Split(";"c).Length
                                              Dim ret = _api.CommKwRqData(codes, 0, cnt, 0, rqName, scrNo)
                                              If ret <> 0 Then
                                                  _pendingTr.TryRemove(rqName, Nothing)
                                                  callback(MakeError($"CommKwRqData 실패: {ret}"))
                                              End If
                                          End Sub)
                             End Sub, rqName)
            Return
        End If

        _trQueue.Enqueue(Sub()
                             UiInvoke(Sub()
                                         If String.Equals(def.TrCode, "OPT90008", StringComparison.OrdinalIgnoreCase) Then
                                             _api.SetInputValue("시간일자구분", "1")
                                         End If
                                         For Each field In def.Inputs
                                             Dim val = msg.Str(field.Name, "")
                                             If val <> "" Then _api.SetInputValue(field.KiwoomName, val)
                                         Next
                                          Dim ret = _api.CommRqData(rqName, def.TrCode, 0, scrNo)
                                          If ret <> 0 Then
                                              _pendingTr.TryRemove(rqName, Nothing)
                                              callback(MakeError($"CommRqData 실패: {ret}"))
                                          End If
                                      End Sub)
                         End Sub, rqName)
    End Sub

    Private Sub OnReceiveTrData(sender As Object, e As _DKHOpenAPIEvents_OnReceiveTrDataEvent)
        Dim pending As PendingRequest = Nothing
        If Not _pendingTr.TryGetValue(e.sRQName, pending) Then Return

        Dim def = pending.FuncDef
        Dim result As New Msg("tr.result")
        result("func") = def.Name
        result("trCode") = def.TrCode
        result("success") = True

        Try
            ' 단일 출력
            If def.Outputs.Count > 0 Then
                Dim summary As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                For Each field In def.Outputs
                    summary(field.Name) = _api.GetCommData(e.sTrCode, e.sRQName, 0, field.KiwoomName).Trim()
                Next
                result("summary") = summary
            End If

            ' 반복 출력
            If def.MultiOutputs.Count > 0 Then
                Dim cnt = _api.GetRepeatCnt(e.sTrCode, e.sRQName)
                For i = 0 To cnt - 1
                    Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                    For Each field In def.MultiOutputs
                        Dim raw = _api.GetCommData(e.sTrCode, e.sRQName, i, field.KiwoomName).Trim()
                        ' 종목코드 정규화
                        If field.Name = "종목코드" Then raw = SharedUtil.NormalizeCode(raw)
                        row(field.Name) = raw
                    Next
                    pending.AccumulatedRows.Add(row)
                Next

                ' 연속 조회
                If def.SupportsContinuation AndAlso e.sPrevNext = "2" Then
                    _trQueue.Enqueue(Sub()
                                         UiInvoke(Sub()
                                                     If String.Equals(def.TrCode, "OPT90008", StringComparison.OrdinalIgnoreCase) Then
                                                         _api.SetInputValue("시간일자구분", "1")
                                                     End If
                                                     For Each field In def.Inputs
                                                         Dim val = pending.InputMsg.Str(field.Name, "")
                                                         If val <> "" Then _api.SetInputValue(field.KiwoomName, val)
                                                     Next
                                                      _api.CommRqData(e.sRQName, def.TrCode, 2, pending.ScreenNo)
                                                  End Sub)
                                     End Sub, e.sRQName & "_cont")
                    Return  ' 콜백 아직 호출하지 않음
                End If

                result("rows") = pending.AccumulatedRows
            End If

            _pendingTr.TryRemove(e.sRQName, Nothing)
            pending.Callback?.Invoke(result)

        Catch ex As Exception
            _pendingTr.TryRemove(e.sRQName, Nothing)
            pending.Callback?.Invoke(MakeError($"TR 파싱 오류: {ex.Message}"))
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' 주문
    ' ════════════════════════════════════════

    Private Sub DoOrder(def As KiwoomCatalog.FuncDef, msg As Msg, callback As Action(Of Msg))
        If Not _isLoggedIn Then callback(MakeError("로그인 필요")) : Return

        Dim accountNo = msg.Str("accountNo", _accountNo)
        Dim code = msg.Str("code")
        Dim qty = msg.Int("qty")
        Dim price = msg.Int("price")
        Dim orgOrderNo = msg.Str("orgOrderNo", "")
        Dim scrNo = NextScreen()

        UiInvoke(Sub()
                     Dim ret = _api.SendOrder("주문", scrNo, accountNo, def.OrderType, code, qty, price, def.QuoteType, orgOrderNo)
                     If ret = 0 Then
                         callback(MakeOk("주문 요청 성공"))
                     Else
                         callback(MakeError($"SendOrder 실패: {ret}"))
                     End If
                 End Sub)
    End Sub

    ' ════════════════════════════════════════
    ' 실시간
    ' ════════════════════════════════════════

    Private Sub DoRealtimeReg(def As KiwoomCatalog.FuncDef, msg As Msg, callback As Action(Of Msg))
        Dim codes = msg.Str("codes")
        Dim scrNo = msg.Str("screenNo", "1000")

        UiInvoke(Sub()
                     _api.SetRealReg(scrNo, codes, def.FidList, "1")
                 End Sub)
        callback(MakeOk("실시간 등록 완료", "type", def.RealtimeType))
    End Sub

    Private Sub DoRealtimeUnreg(msg As Msg, callback As Action(Of Msg))
        Dim codes = msg.Str("codes", "")
        Dim scrNo = msg.Str("screenNo", "1000")

        UiInvoke(Sub()
                     If String.IsNullOrEmpty(codes) Then
                         _api.SetRealRemove(scrNo, "ALL")
                     Else
                         For Each code In codes.Split(";"c)
                             _api.SetRealRemove(scrNo, code.Trim())
                         Next
                     End If
                 End Sub)
        callback(MakeOk("실시간 해제 완료"))
    End Sub

    Private Sub OnReceiveRealData(sender As Object, e As _DKHOpenAPIEvents_OnReceiveRealDataEvent)
        Dim code = e.sRealKey
        Dim sType = e.sRealType

        If sType = "주식체결" Then
            Dim m As New Msg(Topics.TICK)
            m("code") = code
            m("time") = _api.GetCommRealData(sType, Fid.체결시간).Trim()
            m("price") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.현재가))
            m("change") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.전일대비), True)
            m("changeRate") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.등락율), True)
            m("volume") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.거래량))
            m("cumVolume") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.누적거래량))
            m("open") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.시가))
            m("high") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.고가))
            m("low") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.저가))
            m("ask1") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.매도호가))
            m("bid1") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.매수호가))
            m("strength") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.체결강도))
            RaiseEvent RealtimeReceived(m)

        ElseIf sType = "주식호가잔량" Then
            Dim m As New Msg(Topics.ORDERBOOK)
            m("code") = code
            m("time") = _api.GetCommRealData(sType, Fid.호가시간).Trim()
            m("totalAskVol") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.매도총잔량))
            m("totalBidVol") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.매수총잔량))
            m("netBidVol") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.순매수잔량))
            m("netAskVol") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.순매도잔량))

            Dim askPrices(9) As Double, askVols(9) As Double
            Dim bidPrices(9) As Double, bidVols(9) As Double
            For i = 0 To 9
                askPrices(i) = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.매도호가1 + i))
                askVols(i) = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.매도잔량1 + i))
                bidPrices(i) = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.매수호가1 + i))
                bidVols(i) = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.매수잔량1 + i))
            Next
            m("askPrices") = askPrices
            m("askVols") = askVols
            m("bidPrices") = bidPrices
            m("bidVols") = bidVols
            RaiseEvent RealtimeReceived(m)

        ElseIf sType = "주식프로그램매매" Then
            Dim m As New Msg(Topics.PROGRAM_TRADE)
            m("code") = code
            m("sell") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.프매도))
            m("buy") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.프매수))
            m("net") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.프순매수))
            m("sellCum") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.프매도누적))
            m("buyCum") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.프매수누적))
            m("netCum") = SharedUtil.SafeDouble(_api.GetCommRealData(sType, Fid.프순매수누적))
            RaiseEvent RealtimeReceived(m)

        ElseIf sType = "장시작시간" Then
            Dim m As New Msg(Topics.MARKET_STATUS)
            m("operation") = _api.GetCommRealData(sType, Fid.장운영구분).Trim()
            m("time") = _api.GetCommRealData(sType, Fid.체결시간).Trim()
            m("remainSec") = _api.GetCommRealData(sType, Fid.장시작예상잔여시간).Trim()
            RaiseEvent RealtimeReceived(m)
        End If
    End Sub

    ' ════════════════════════════════════════
    ' 체잔 (주문체결/잔고변경)
    ' ════════════════════════════════════════

    Private Sub OnReceiveChejanData(sender As Object, e As _DKHOpenAPIEvents_OnReceiveChejanDataEvent)
        If e.sGubun = "0" Then
            ' 주문체결
            Dim m As New Msg(Topics.ORDER_EXECUTED)
            m("주문번호") = _api.GetChejanData(Fid.CJ_주문번호).Trim()
            m("종목코드") = SharedUtil.NormalizeCode(_api.GetChejanData(Fid.CJ_종목코드))
            m("종목명") = _api.GetChejanData(Fid.CJ_종목명).Trim()
            m("주문상태") = _api.GetChejanData(Fid.CJ_주문상태).Trim()
            m("주문수량") = _api.GetChejanData(Fid.CJ_주문수량).Trim()
            m("주문가격") = _api.GetChejanData(Fid.CJ_주문가격).Trim()
            m("미체결수량") = _api.GetChejanData(Fid.CJ_미체결수량).Trim()
            m("체결가") = _api.GetChejanData(Fid.CJ_체결가).Trim()
            m("체결량") = _api.GetChejanData(Fid.CJ_체결량).Trim()
            m("주문구분") = _api.GetChejanData(Fid.CJ_주문구분).Trim()
            m("체결시간") = _api.GetChejanData(Fid.CJ_체결시간).Trim()
            RaiseEvent ChejanReceived(m)

        ElseIf e.sGubun = "1" Then
            ' 잔고변경
            Dim m As New Msg(Topics.ORDER_BALANCE_CHANGED)
            m("종목코드") = SharedUtil.NormalizeCode(_api.GetChejanData(Fid.CJ_종목코드))
            m("종목명") = _api.GetChejanData(Fid.CJ_종목명).Trim()
            m("보유수량") = _api.GetChejanData(Fid.CJ_보유수량).Trim()
            m("매입가") = _api.GetChejanData(Fid.CJ_매입가).Trim()
            m("현재가") = _api.GetChejanData(Fid.CJ_현재가).Trim()
            m("손익율") = _api.GetChejanData(Fid.CJ_손익율).Trim()
            RaiseEvent ChejanReceived(m)
        End If
    End Sub

    ' ════════════════════════════════════════
    ' 조건검색
    ' ════════════════════════════════════════

    Private Sub DoCondition(def As KiwoomCatalog.FuncDef, msg As Msg, callback As Action(Of Msg))
        Select Case def.Name
            Case "조건검색목록"
                _condLoadWaiter = New ManualResetEventSlim(False)
                UiInvoke(Sub() _api.GetConditionLoad())

                If _condLoadWaiter.Wait(10000) Then
                    callback(_condLoadResult)
                Else
                    callback(MakeError("조건검색 목록 타임아웃"))
                End If

            Case "조건검색시작"
                Dim name = msg.Str("name")
                Dim index = msg.Int("index")
                Dim isRealtime = msg.Int("realtime", 1)
                Dim scrNo = NextScreen()

                _condSearchWaiter = New ManualResetEventSlim(False)
                UiInvoke(Sub() _api.SendCondition(scrNo, name, index, isRealtime))

                If _condSearchWaiter.Wait(15000) Then
                    callback(_condSearchResult)
                Else
                    callback(MakeError("조건검색 타임아웃"))
                End If

            Case "조건검색중지"
                Dim name = msg.Str("name")
                Dim index = msg.Int("index")
                Dim scrNo = msg.Str("screenNo", "9000")
                UiInvoke(Sub() _api.SendConditionStop(scrNo, name, index))
                callback(MakeOk("조건검색 중지 완료"))
        End Select
    End Sub

    Private Sub OnReceiveConditionVer(sender As Object, e As _DKHOpenAPIEvents_OnReceiveConditionVerEvent)
        Dim raw = _api.GetConditionNameList()
        Dim list As New List(Of Dictionary(Of String, String))()
        If Not String.IsNullOrWhiteSpace(raw) Then
            For Each token In raw.Split({";"c}, StringSplitOptions.RemoveEmptyEntries)
                Dim parts = token.Split("^"c)
                If parts.Length >= 2 Then
                    Dim d As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                    d("index") = parts(0)
                    d("name") = parts(1)
                    list.Add(d)
                End If
            Next
        End If
        _condLoadResult = MakeOk("조건검색 목록")
        _condLoadResult("conditions") = list
        _condLoadWaiter?.Set()
    End Sub

    Private Sub OnReceiveTrCondition(sender As Object, e As _DKHOpenAPIEvents_OnReceiveTrConditionEvent)
        Dim codes As String() = {}
        If Not String.IsNullOrWhiteSpace(e.strCodeList) Then
            codes = e.strCodeList.Split({";"c}, StringSplitOptions.RemoveEmptyEntries).
                Select(Function(s) SharedUtil.NormalizeCode(s)).ToArray()
        End If
        _condSearchResult = MakeOk("조건검색 결과", "codes", codes, "condName", e.strConditionName)
        _condSearchWaiter?.Set()
    End Sub

    Private Sub OnReceiveRealCondition(sender As Object, e As _DKHOpenAPIEvents_OnReceiveRealConditionEvent)
        Dim m As New Msg(Topics.CONDITION_HIT)
        m("code") = SharedUtil.NormalizeCode(e.sTrCode)
        m("type") = e.strType  ' "I"=편입, "D"=이탈
        m("condName") = e.strConditionName
        m("condIndex") = e.strConditionIndex
        RaiseEvent ConditionHit(m)
    End Sub

    ' ════════════════════════════════════════
    ' 유틸리티
    ' ════════════════════════════════════════

    Private Sub DoCodeList(msg As Msg, callback As Action(Of Msg))
        Dim market = msg.Str("market", "0")  ' 0=코스피, 10=코스닥
        Dim raw = UiInvoke(Of String)(Function() _api.GetCodeListByMarket(market))
        Dim codes = raw.Split({";"c}, StringSplitOptions.RemoveEmptyEntries)
        callback(MakeOk("종목코드 목록", "codes", codes))
    End Sub

    Private Sub DoCodeName(msg As Msg, callback As Action(Of Msg))
        Dim code = msg.Str("code")
        Dim name = UiInvoke(Of String)(Function() _api.GetMasterCodeName(code))
        callback(MakeOk("종목명", "name", name.Trim()))
    End Sub

    Private Sub OnReceiveMsg(sender As Object, e As _DKHOpenAPIEvents_OnReceiveMsgEvent)
        ' 필요 시 로그 발행
    End Sub

    ' ════════════════════════════════════════
    ' UI 스레드 헬퍼
    ' ════════════════════════════════════════

    Private Sub UiInvoke(action As Action)
        If _api.InvokeRequired Then
            _api.Invoke(action)
        Else
            action()
        End If
    End Sub

    Private Function UiInvoke(Of T)(func As Func(Of T)) As T
        If _api.InvokeRequired Then
            Return CType(_api.Invoke(func), T)
        Else
            Return func()
        End If
    End Function

    ' ════════════════════════════════════════
    ' 메시지 헬퍼
    ' ════════════════════════════════════════

    Private Function MakeOk(message As String, ParamArray pairs() As Object) As Msg
        Dim m As New Msg("response", pairs)
        m("success") = True
        m("message") = message
        Return m
    End Function

    Private Function MakeError(message As String) As Msg
        Dim m As New Msg("response")
        m("success") = False
        m("message") = message
        Return m
    End Function

End Class
