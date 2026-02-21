' ═══════════════════════════════════════════════════════════════
' KiwoomBridge.vb — KiwoomClient ↔ MessageBus 양방향 브릿지
' ═══════════════════════════════════════════════════════════════
' 95% 불변. 새 토픽 매핑만 추가 가능.
' ═══════════════════════════════════════════════════════════════

Imports [Shared]

Public Class KiwoomBridge

    Private ReadOnly _client As New KiwoomClient()

    Public Sub Start()
        _client.연결()

        ' ─── Bus → 서버 ───

        MessageBus.I.On(Topics.AUTH_LOGIN_REQUEST, Sub(m) _client.로그인(Sub(r) MessageBus.I.EmitOnUI(WithTopic(r, Topics.AUTH_LOGIN_RESULT))))
        MessageBus.I.On(Topics.AUTH_STATUS_REQUEST, Sub(m) _client.상태조회(Sub(r) MessageBus.I.EmitOnUI(WithTopic(r, Topics.AUTH_STATUS_RESULT))))

        MessageBus.I.On(Topics.STOCK_BASIC_REQUEST, Sub(m) _client.주식기본정보(m.Str("code"), Sub(r) BusResult(r, Topics.STOCK_BASIC_RESULT, m)))
        MessageBus.I.On(Topics.HOGA_REQUEST, Sub(m) _client.호가요청(m.Str("code"), Sub(r) BusResult(r, Topics.HOGA_RESULT, m)))

        MessageBus.I.On(Topics.ACCOUNT_BALANCE_REQUEST, Sub(m) _client.계좌평가(m.Str("accountNo"), m.Str("pass"), Sub(r) BusResult(r, Topics.ACCOUNT_BALANCE_RESULT, m)))
        MessageBus.I.On(Topics.ACCOUNT_OPEN_ORDERS_REQUEST, Sub(m) _client.미체결조회(m.Str("accountNo"), Sub(r) BusResult(r, Topics.ACCOUNT_OPEN_ORDERS_RESULT, m)))
        MessageBus.I.On(Topics.ACCOUNT_TODAY_PNL_REQUEST, Sub(m) _client.당일실현손익(m.Str("accountNo"), m.Str("pass"), Sub(r) BusResult(r, Topics.ACCOUNT_TODAY_PNL_RESULT, m)))

        MessageBus.I.On(Topics.ORDER_BUY_MARKET, Sub(m) _client.매수_시장가(m.Str("code"), m.Int("qty"), Sub(r) BusResult(r, Topics.ORDER_EXECUTED, m)))
        MessageBus.I.On(Topics.ORDER_BUY_LIMIT, Sub(m) _client.매수_지정가(m.Str("code"), m.Int("qty"), m.Int("price"), Sub(r) BusResult(r, Topics.ORDER_EXECUTED, m)))
        MessageBus.I.On(Topics.ORDER_SELL_MARKET, Sub(m) _client.매도_시장가(m.Str("code"), m.Int("qty"), Sub(r) BusResult(r, Topics.ORDER_EXECUTED, m)))
        MessageBus.I.On(Topics.ORDER_SELL_LIMIT, Sub(m) _client.매도_지정가(m.Str("code"), m.Int("qty"), m.Int("price"), Sub(r) BusResult(r, Topics.ORDER_EXECUTED, m)))
        MessageBus.I.On(Topics.ORDER_MODIFY, Sub(m) _client.주문정정(m.Str("orgOrderNo"), m.Str("code"), m.Int("qty"), m.Int("price"), Sub(r) BusResult(r, Topics.ORDER_EXECUTED, m)))
        MessageBus.I.On(Topics.ORDER_CANCEL, Sub(m) _client.주문취소(m.Str("orgOrderNo"), m.Str("code"), m.Int("qty"), Sub(r) BusResult(r, Topics.ORDER_EXECUTED, m)))

        MessageBus.I.On(Topics.REALTIME_SUBSCRIBE, Sub(m)
                                                       Dim codes = m.Str("codes")
                                                       _client.실시간_체결구독(codes)
                                                       _client.실시간_호가구독(codes)
                                                   End Sub)
        MessageBus.I.On(Topics.REALTIME_UNSUBSCRIBE, Sub(m) _client.실시간_해제(m.Str("codes")))
        MessageBus.I.On(Topics.REALTIME_UNSUBSCRIBE_ALL, Sub(m) _client.실시간_해제())

        MessageBus.I.On(Topics.CONDITION_LIST_REQUEST, Sub(m) _client.조건검색목록(Sub(r) BusResult(r, Topics.CONDITION_LIST_RESULT, m)))
        MessageBus.I.On(Topics.CONDITION_START, Sub(m) _client.조건검색시작(m.Str("name"), m.Int("index"), Sub(r) BusResult(r, Topics.CONDITION_SEARCH_RESULT, m)))
        MessageBus.I.On(Topics.CONDITION_STOP, Sub(m) _client.조건검색중지(m.Str("name"), m.Int("index")))

        MessageBus.I.On(Topics.RANK_VOLUME_REQUEST, Sub(m) _client.거래량상위(m.Str("market"), Sub(r) BusResult(r, Topics.RANK_VOLUME_RESULT, m)))
        MessageBus.I.On(Topics.RANK_CHANGE_REQUEST, Sub(m) _client.등락률상위(m.Str("market"), Sub(r) BusResult(r, Topics.RANK_CHANGE_RESULT, m)))

        MessageBus.I.On(Topics.FINANCE_REQUEST, Sub(m) _client.재무정보(m.Str("code"), Sub(r) BusResult(r, Topics.FINANCE_RESULT, m)))
        MessageBus.I.On(Topics.STOCK_MULTI_INFO_REQUEST, Sub(m) _client.관심종목정보(m.Str("codes"), Sub(r) BusResult(r, Topics.STOCK_MULTI_INFO_RESULT, m)))

        ' ─── 서버 → Bus ───

        AddHandler _client.체결수신, Sub(m) MessageBus.I.Emit(m)
        AddHandler _client.호가수신, Sub(m) MessageBus.I.Emit(m)
        AddHandler _client.프로그램매매수신, Sub(m) MessageBus.I.Emit(m)
        AddHandler _client.장상태수신, Sub(m) MessageBus.I.Emit(m)
        AddHandler _client.주문체결, Sub(m) MessageBus.I.Emit(m)
        AddHandler _client.잔고변경, Sub(m) MessageBus.I.Emit(m)
        AddHandler _client.조건편입, Sub(m) MessageBus.I.Emit(m)

        AddHandler _client.연결됨, Sub() MessageBus.I.Emit(Topics.SYS_LOG, "text", "[KiwoomBridge] 서버 연결됨")
        AddHandler _client.연결끊김, Sub() MessageBus.I.Emit(Topics.SYS_ERROR, "text", "[KiwoomBridge] 서버 연결 끊김")
    End Sub

    Private Sub BusResult(response As Msg, resultTopic As String, originalRequest As Msg)
        response.Topic = resultTopic
        ' 원래 요청의 code를 응답에 전달
        If originalRequest.Has("code") AndAlso Not response.Has("code") Then
            response("code") = originalRequest.Str("code")
        End If
        MessageBus.I.EmitOnUI(response)
    End Sub

    Private Function WithTopic(m As Msg, topic As String) As Msg
        m.Topic = topic
        Return m
    End Function

End Class
