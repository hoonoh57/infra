' ═══════════════════════════════════════════════════════════════
' KiwoomBridge.vb — KiwoomClient ↔ MessageBus 양방향 브릿지
' ═══════════════════════════════════════════════════════════════
' 95% 불변. 새 토픽 매핑만 추가 가능.
' ═══════════════════════════════════════════════════════════════

Imports [Shared]
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text.RegularExpressions

Public Class KiwoomBridge

    Private ReadOnly _client As New KiwoomClient()

    Public Sub Start()
        _client.연결()

        ' ─── Bus → 서버 ───

        MessageBus.I.On(Topics.AUTH_LOGIN_REQUEST, Sub(m) _client.로그인(Sub(r) MessageBus.I.EmitOnUI(WithTopic(r, Topics.AUTH_LOGIN_RESULT))))
        MessageBus.I.On(Topics.AUTH_STATUS_REQUEST, Sub(m) _client.상태조회(Sub(r) MessageBus.I.EmitOnUI(WithTopic(r, Topics.AUTH_STATUS_RESULT))))

        MessageBus.I.On(Topics.STOCK_BASIC_REQUEST, Sub(m) _client.주식기본정보(m.Str("code"), Sub(r) BusResult(r, Topics.STOCK_BASIC_RESULT, m)))
        MessageBus.I.On(Topics.HOGA_REQUEST, Sub(m) _client.호가요청(m.Str("code"), Sub(r) BusResult(r, Topics.HOGA_RESULT, m)))

        ' ─── 캔들 요청 ───
        MessageBus.I.On(Topics.CANDLE_REQUEST, Sub(m)
                                                   Dim reqProvider = m.Str("provider", "")
                                                   If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "kiwoom", StringComparison.OrdinalIgnoreCase) Then Return
                                                   If Not RuntimeChartSettings.IsMarketDataProvider("kiwoom") Then Return
                                                   Dim code = m.Str("code")
                                                   Dim tf = m.Str("timeframe", RuntimeChartSettings.DefaultCandleTimeframe).ToLower()
                                                   Dim count = m.Int("count", RuntimeChartSettings.DefaultCandleRequestCount)
                                                   If tf.StartsWith("m") Then
                                                       Dim interval = 1
                                                       If tf.Length > 1 Then Integer.TryParse(tf.Substring(1), interval)
                                                       _client.분봉조회(code, interval, Sub(r) EmitCandle(r, code))
                                                   ElseIf tf = "d" OrElse tf = "daily" Then
                                                       _client.일봉조회(code, DateTime.Now.ToString("yyyyMMdd"), Sub(r) EmitCandle(r, code))
                                                   Else
                                                       _client.분봉조회(code, 1, Sub(r) EmitCandle(r, code))
                                                   End If
                                               End Sub)

        MessageBus.I.On(Topics.DAILY_REQUEST, Sub(m)
                                                  If Not RuntimeChartSettings.IsMarketDataProvider("kiwoom") Then Return
                                                  _client.일봉조회(m.Str("code"), DateTime.Now.ToString("yyyyMMdd"), Sub(r) EmitCandle(r, m.Str("code")))
                                              End Sub)
        MessageBus.I.On(Topics.WEEKLY_REQUEST, Sub(m)
                                                   If Not RuntimeChartSettings.IsMarketDataProvider("kiwoom") Then Return
                                                   _client.주봉조회(m.Str("code"), DateTime.Now.ToString("yyyyMMdd"), Sub(r) EmitCandle(r, m.Str("code")))
                                               End Sub)
        MessageBus.I.On(Topics.MONTHLY_REQUEST, Sub(m)
                                                    If Not RuntimeChartSettings.IsMarketDataProvider("kiwoom") Then Return
                                                    _client.월봉조회(m.Str("code"), DateTime.Now.ToString("yyyyMMdd"), Sub(r) EmitCandle(r, m.Str("code")))
                                                End Sub)
        MessageBus.I.On(Topics.TICK_CANDLE_REQUEST, Sub(m)
                                                        Dim reqProvider = m.Str("provider", "")
                                                        If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "kiwoom", StringComparison.OrdinalIgnoreCase) Then Return
                                                        If Not RuntimeChartSettings.IsMarketDataProvider("kiwoom") Then Return
                                                        Dim tickUnit = RuntimeChartSettings.NormalizeTickUnit(m.Int("tickUnit", RuntimeChartSettings.DefaultTickUnit))
                                                        Dim r As New Msg(Topics.TICK_CANDLE_LOADED)
                                                        r("code") = m.Str("code")
                                                        r("rows") = New List(Of Dictionary(Of String, String))()
                                                        r("timeframe") = RuntimeChartSettings.TickTimeframe(tickUnit)
                                                        r("requestedCount") = m.Int("count", 0)
                                                        r("stopTime") = m.Str("stopTime", "")
                                                        r("success") = False
                                                        r("provider") = "kiwoom"
                                                        r("message") = "tick candle not supported on kiwoom bridge"
                                                        MessageBus.I.EmitOnUI(r)
                                                    End Sub)

        MessageBus.I.On(Topics.PROGRAM_TRADE_REQUEST, Sub(m)
                                                          Dim reqProvider = m.Str("provider", "")
                                                          If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "kiwoom", StringComparison.OrdinalIgnoreCase) Then Return
                                                          Dim explicitProvider As Boolean = (reqProvider <> "")
                                                          If (Not explicitProvider) AndAlso (Not RuntimeChartSettings.IsMarketDataProvider("kiwoom")) Then Return

                                                          Dim code = m.Str("code")
                                                          Dim baseDateStr = m.Str("baseDate", "")
                                                          Dim stopTime = m.Str("stopTime", "")
                                                          Dim baseDate As DateTime = DateTime.Today.Date
                                                          If Not String.IsNullOrWhiteSpace(baseDateStr) AndAlso baseDateStr.Length >= 8 Then
                                                              DateTime.TryParseExact(baseDateStr.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, baseDate)
                                                          ElseIf Not String.IsNullOrWhiteSpace(stopTime) AndAlso stopTime.Length >= 8 Then
                                                              DateTime.TryParseExact(stopTime.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, baseDate)
                                                          End If
                                                          If Not TradingCalendar.IsBusinessDay(baseDate) Then
                                                              baseDate = TradingCalendar.PreviousBusinessDay(baseDate)
                                                          End If

                                                          Dim requestByDate As Action(Of DateTime, Integer) = Nothing
                                                          requestByDate =
                                                              Sub(targetDate As DateTime, remainingBackoff As Integer)
                                                                  Dim reqDate = targetDate.ToString("yyyyMMdd")
                                                                  MessageBus.I.Emit(Topics.SYS_LOG, "text", $"[Data] 프로그램순매수 kiwoom 요청일: {code} {reqDate}")
                                                                  _client.프로그램매매(code, reqDate, "2", Sub(r)
                                                                                                                 Dim rows = r.DictList("rows")
                                                                                                                 Dim rowCount = If(rows Is Nothing, 0, rows.Count)

                                                                                                                 If rowCount <= 0 AndAlso remainingBackoff > 0 Then
                                                                                                                     Dim prevBiz = TradingCalendar.PreviousBusinessDay(targetDate)
                                                                                                                     MessageBus.I.Emit(Topics.SYS_LOG, "text", $"[Data] 프로그램순매수 재요청(이전영업일): {code} {reqDate}->{prevBiz:yyyyMMdd}")
                                                                                                                     requestByDate(prevBiz, remainingBackoff - 1)
                                                                                                                     Return
                                                                                                                 End If

                                                                                                                 r("rows") = NormalizeProgramTradeRows(rows, reqDate)
                                                                                                                 If Not r.Has("code") Then r("code") = code
                                                                                                                 r.Topic = Topics.PROGRAM_TRADE_RESULT
                                                                                                                 r("provider") = "kiwoom"
                                                                                                                 MessageBus.I.EmitOnUI(r)
                                                                                                             End Sub)
                                                              End Sub

                                                          requestByDate(baseDate, 5)
                                                      End Sub)

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
        MessageBus.I.On(Topics.STOCK_MULTI_INFO_REQUEST, Sub(m)
                                                              If Not RuntimeChartSettings.IsMarketDataProvider("kiwoom") Then Return
                                                              _client.관심종목정보(m.Str("codes"), Sub(r)
                                                                                              r("provider") = "kiwoom"
                                                                                              BusResult(r, Topics.STOCK_MULTI_INFO_RESULT, m)
                                                                                          End Sub)
                                                          End Sub)
        MessageBus.I.On(Topics.SECTOR_STOCKS_REQUEST, Sub(m) _client.업종별종목(m.Str("sectorCode"), Sub(r)
                                                                                                    r.Topic = Topics.SECTOR_STOCKS_RESULT
                                                                                                    If Not r.Has("sectorCode") Then r("sectorCode") = m.Str("sectorCode")
                                                                                                    MessageBus.I.EmitOnUI(r)
                                                                                                End Sub))

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

    Private Sub EmitCandle(r As Msg, code As String)
        r.Topic = Topics.CANDLE_LOADED
        If Not r.Has("code") Then r("code") = code
        r("provider") = "kiwoom"
        MessageBus.I.EmitOnUI(r)
    End Sub

    Private Shared Function NormalizeProgramTradeRows(rows As List(Of Dictionary(Of String, String)), reqDate As String) As List(Of Dictionary(Of String, String))
        Dim normalized As New List(Of Dictionary(Of String, String))
        If rows Is Nothing Then Return normalized

        For Each row In rows
            If row Is Nothing Then Continue For

            Dim dateToken As String = ""
            Dim timeToken As String = ""
            Dim netBuy As Double = Double.NaN
            Dim delta As Double = Double.NaN

            For Each kv In row
                Dim v = If(kv.Value, "").Trim()
                If v = "" Then Continue For

                If dateToken = "" AndAlso Regex.IsMatch(v, "^\d{8}$") Then
                    dateToken = v
                End If
                If timeToken = "" AndAlso Regex.IsMatch(v, "^\d{6}$") Then
                    timeToken = v
                End If

                Dim n As Double
                If Double.TryParse(v.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, n) Then
                    If kv.Key.Equals("netBuy", StringComparison.OrdinalIgnoreCase) Then netBuy = n
                    If kv.Key.Equals("delta", StringComparison.OrdinalIgnoreCase) Then delta = n
                End If
            Next

            If dateToken = "" Then dateToken = reqDate
            If timeToken = "" Then timeToken = "090000"

            If Double.IsNaN(netBuy) Then
                Dim parsed As Double
                If row.ContainsKey("순매수") AndAlso Double.TryParse(row("순매수").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, parsed) Then
                    netBuy = parsed
                End If
            End If
            If Double.IsNaN(netBuy) Then Continue For

            Dim outRow As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            outRow("date") = dateToken
            outRow("time") = timeToken
            outRow("netBuy") = netBuy.ToString(CultureInfo.InvariantCulture)
            outRow("net") = outRow("netBuy")
            outRow("value") = outRow("netBuy")
            If Not Double.IsNaN(delta) Then outRow("delta") = delta.ToString(CultureInfo.InvariantCulture)
            normalized.Add(outRow)
        Next

        Return normalized
    End Function

End Class
