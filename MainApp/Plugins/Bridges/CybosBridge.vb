' ═══════════════════════════════════════════════════════════════
' CybosBridge.vb — CybosClient ↔ MessageBus 양방향 브릿지
' ═══════════════════════════════════════════════════════════════
' 95% 불변. 새 토픽 매핑만 추가 가능.
' ═══════════════════════════════════════════════════════════════

Imports [Shared]

Public Class CybosBridge

    Private ReadOnly _client As New CybosClient()
    Public Sub Start()
        _client.연결()
        AddHandler _client.ProgramTradeRealtime,
            Sub(r)
                If r Is Nothing Then Return
                r.Topic = Topics.PROGRAM_TRADE
                r("provider") = "cybos"
                MessageBus.I.EmitOnUI(r)
            End Sub

        ' ─── 캔들 요청 분기 ───

        MessageBus.I.On(Topics.CANDLE_REQUEST, Sub(m)
                                                   Dim reqProvider = m.Str("provider", "")
                                                   If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                                                   If Not RuntimeChartSettings.IsMarketDataProvider("cybos") Then Return
                                                   Dim code = m.Str("code")
                                                   Dim tf = m.Str("timeframe", RuntimeChartSettings.DefaultCandleTimeframe).ToLower()
                                                   Dim count = m.Int("count", RuntimeChartSettings.DefaultCandleRequestCount)

                                                   Select Case True
                                                       Case tf.StartsWith("m")
                                                           Dim interval = 1
                                                           If tf.Length > 1 Then Integer.TryParse(tf.Substring(1), interval)
                                                           Dim stopTime = m.Str("stopTime", "")
                                                           Dim isIndexCode = code.StartsWith("U", StringComparison.OrdinalIgnoreCase)
                                                           If String.IsNullOrWhiteSpace(stopTime) Then
                                                               _client.분봉(code, interval, count, Sub(r)
                                                                                                     Dim rows = r.DictList("rows")
                                                                                                     If isIndexCode AndAlso (rows Is Nothing OrElse rows.Count = 0) Then
                                                                                                         _client.일봉(code, Math.Max(count, 120), Sub(d) EmitCandle(d, code, m.Str("consumer", "")))
                                                                                                         Return
                                                                                                     End If
                                                                                                     EmitCandle(r, code, m.Str("consumer", ""))
                                                                                                 End Sub)
                                                           Else
                                                               _client.분봉기간(code, interval, stopTime, Sub(r) EmitCandle(r, code, m.Str("consumer", "")))
                                                           End If

                                                       Case tf = "d" OrElse tf = "daily"
                                                           _client.일봉(code, count, Sub(r) EmitCandle(r, code, m.Str("consumer", "")))

                                                       Case tf = "w" OrElse tf = "weekly"
                                                           _client.주봉(code, count, Sub(r) EmitCandle(r, code, m.Str("consumer", "")))

                                                       Case tf = "mo" OrElse tf = "monthly"
                                                           _client.월봉(code, count, Sub(r) EmitCandle(r, code, m.Str("consumer", "")))

                                                       Case tf.StartsWith("t")
                                                           Dim tickUnit = RuntimeChartSettings.DefaultTickUnit
                                                           If tf.Length > 1 Then Integer.TryParse(tf.Substring(1), tickUnit)
                                                           tickUnit = RuntimeChartSettings.NormalizeTickUnit(tickUnit)
                                                           _client.틱차트(code, count, tickUnit, Sub(r)
                                                                                                     r.Topic = Topics.TICK_CANDLE_LOADED
                                                                                                     If Not r.Has("code") Then r("code") = code
                                                                                                     r("timeframe") = RuntimeChartSettings.TickTimeframe(tickUnit)
                                                                                                     r("requestedCount") = count
                                                                                                     MessageBus.I.EmitOnUI(r)
                                                                                                 End Sub)

                                                       Case Else
                                                           _client.분봉(code, 1, count, Sub(r) EmitCandle(r, code, m.Str("consumer", "")))
                                                   End Select
                                               End Sub)

        MessageBus.I.On(Topics.CANDLE_PERIOD_REQUEST, Sub(m)
                                                          Dim reqProvider = m.Str("provider", "")
                                                          If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                                                          If Not RuntimeChartSettings.IsMarketDataProvider("cybos") Then Return
                                                          _client.기간캔들(m.Str("code"), m.Str("timeframe"), m.Str("from"), m.Str("to"),
                                                              Sub(r)
                                                                  r.Topic = Topics.CANDLE_PERIOD_LOADED
                                                                  If Not r.Has("code") Then r("code") = m.Str("code")
                                                                  If m.Has("consumer") Then r("consumer") = m.Str("consumer")
                                                                  r("provider") = "cybos"
                                                                  MessageBus.I.EmitOnUI(r)
                                                              End Sub)
                                                      End Sub)
        MessageBus.I.On(Topics.RESEARCH_CANDLE_REQUEST, Sub(m)
                                                            Dim reqProvider = m.Str("provider", "")
                                                            If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                                                            _client.일봉(m.Str("code"), m.Int("count", 500), Sub(r)
                                                                                                                 r.Topic = Topics.RESEARCH_CANDLE_LOADED
                                                                                                                 If Not r.Has("code") Then r("code") = m.Str("code")
                                                                                                                 r("provider") = "cybos"
                                                                                                                 MessageBus.I.EmitOnUI(r)
                                                                                                             End Sub)
                                                        End Sub)
        MessageBus.I.On(Topics.RESEARCH_CANDLE_PERIOD_REQUEST, Sub(m)
                                                                   Dim reqProvider = m.Str("provider", "")
                                                                   If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                                                                   _client.기간캔들(m.Str("code"), m.Str("timeframe"), m.Str("from"), m.Str("to"),
                                                                       Sub(r)
                                                                           r.Topic = Topics.RESEARCH_CANDLE_PERIOD_LOADED
                                                                           If Not r.Has("code") Then r("code") = m.Str("code")
                                                                           r("provider") = "cybos"
                                                                           MessageBus.I.EmitOnUI(r)
                                                                       End Sub)
                                                               End Sub)
        MessageBus.I.On(Topics.RESEARCH_TICK_CANDLE_REQUEST, Sub(m)
                                                                 Dim reqProvider = m.Str("provider", "")
                                                                 If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                                                                 Dim code = m.Str("code")
                                                                 Dim tickUnit = RuntimeChartSettings.NormalizeTickUnit(m.Int("tickUnit", RuntimeChartSettings.DefaultTickUnit))
                                                                 Dim stopTime = m.Str("stopTime", "")
                                                                 If Not String.IsNullOrWhiteSpace(stopTime) Then
                                                                     _client.틱차트기간(code, tickUnit, stopTime, Sub(r)
                                                                                                                       r.Topic = Topics.RESEARCH_TICK_CANDLE_LOADED
                                                                                                                       If Not r.Has("code") Then r("code") = code
                                                                                                                       r("timeframe") = RuntimeChartSettings.TickTimeframe(tickUnit)
                                                                                                                       r("stopTime") = stopTime
                                                                                                                       r("provider") = "cybos"
                                                                                                                       MessageBus.I.EmitOnUI(r)
                                                                                                                   End Sub)
                                                                 Else
                                                                     Dim count = m.Int("count", RuntimeChartSettings.TickRequestMaxCount)
                                                                     _client.틱차트(code, count, tickUnit, Sub(r)
                                                                                                               r.Topic = Topics.RESEARCH_TICK_CANDLE_LOADED
                                                                                                               If Not r.Has("code") Then r("code") = code
                                                                                                               r("timeframe") = RuntimeChartSettings.TickTimeframe(tickUnit)
                                                                                                               r("requestedCount") = count
                                                                                                               r("provider") = "cybos"
                                                                                                               MessageBus.I.EmitOnUI(r)
                                                                                                           End Sub)
                                                                 End If
                                                             End Sub)

        MessageBus.I.On(Topics.TICK_CANDLE_REQUEST, Sub(m)
                                                        Dim reqProvider = m.Str("provider", "")
                                                        If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                                                        If Not RuntimeChartSettings.IsMarketDataProvider("cybos") Then Return
                                                        Dim code = m.Str("code")
                                                        Dim tickUnit = RuntimeChartSettings.NormalizeTickUnit(m.Int("tickUnit", RuntimeChartSettings.DefaultTickUnit))
                                                        Dim stopTime = m.Str("stopTime", "")
                                                        If Not String.IsNullOrWhiteSpace(stopTime) Then
                                                           _client.틱차트기간(code, tickUnit, stopTime, Sub(r)
                                                                                                  r.Topic = Topics.TICK_CANDLE_LOADED
                                                                                                  If Not r.Has("code") Then r("code") = code
                                                                                                   r("timeframe") = RuntimeChartSettings.TickTimeframe(tickUnit)
                                                                                                   r("stopTime") = stopTime
                                                                                                   If m.Has("consumer") Then r("consumer") = m.Str("consumer")
                                                                                                   r("provider") = "cybos"
                                                                                                  MessageBus.I.EmitOnUI(r)
                                                                                              End Sub)
                                                        Else
                                                            Dim count = m.Int("count", RuntimeChartSettings.TickRequestMaxCount)
                                                            _client.틱차트(code, count, tickUnit, Sub(r)
                                                                                      r.Topic = Topics.TICK_CANDLE_LOADED
                                                                                      If Not r.Has("code") Then r("code") = code
                                                                                       r("timeframe") = RuntimeChartSettings.TickTimeframe(tickUnit)
                                                                                       r("requestedCount") = count
                                                                                       If m.Has("consumer") Then r("consumer") = m.Str("consumer")
                                                                                       r("provider") = "cybos"
                                                                                      MessageBus.I.EmitOnUI(r)
                                                                                  End Sub)
                                                        End If
                                                    End Sub)

        MessageBus.I.On(Topics.DAILY_REQUEST, Sub(m) _client.일봉(m.Str("code"), m.Int("count", 500), Sub(r) EmitCandle(r, m.Str("code"), m.Str("consumer", ""))))
        MessageBus.I.On(Topics.WEEKLY_REQUEST, Sub(m) _client.주봉(m.Str("code"), m.Int("count", 200), Sub(r)
                                                                                                         r.Topic = Topics.WEEKLY_LOADED
                                                                                                         MessageBus.I.EmitOnUI(r)
                                                                                                     End Sub))
        MessageBus.I.On(Topics.MONTHLY_REQUEST, Sub(m) _client.월봉(m.Str("code"), m.Int("count", 100), Sub(r)
                                                                                                          r.Topic = Topics.MONTHLY_LOADED
                                                                                                          MessageBus.I.EmitOnUI(r)
                                                                                                      End Sub))

        ' ─── 기타 데이터 ───

        MessageBus.I.On(Topics.PROGRAM_TRADE_REQUEST, Sub(m)
                                                          Dim reqProvider = m.Str("provider", "")
                                                          If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                                                          _client.프로그램순매수(m.Str("code"), m.Int("count", 100), Sub(r)
                                                                                                                   Dim rows = r.DictList("rows")
                                                                                                                   Dim rowCount = If(rows Is Nothing, 0, rows.Count)
                                                                                                                   Dim fallbackTried = m.Bool("fallbackTried", False)
                                                                                                                   If rowCount <= 0 AndAlso Not fallbackTried Then
                                                                                                                       MessageBus.I.Emit(Topics.SYS_LOG, "text", $"[Data] 프로그램순매수 fallback: cybos -> kiwoom ({m.Str("code")})")
                                                                                                                       MessageBus.I.Emit(Topics.PROGRAM_TRADE_REQUEST,
                                                                                                                                         "code", m.Str("code"),
                                                                                                                                         "count", m.Int("count", 100),
                                                                                                                                         "stopTime", m.Str("stopTime", ""),
                                                                                                                                         "baseDate", m.Str("baseDate", ""),
                                                                                                                                         "provider", "kiwoom",
                                                                                                                                         "fallbackTried", True)
                                                                                                                       Return
                                                                                                                   End If
                                                                                                                   r.Topic = Topics.PROGRAM_TRADE_RESULT
                                                                                                                   r("provider") = "cybos"
                                                                                                                   MessageBus.I.EmitOnUI(r)
                                                                                                               End Sub, m.Str("stopTime", ""))
                                                      End Sub)
        MessageBus.I.On("program.trade.rt.subscribe", Sub(m)
                                                                Dim reqProvider = m.Str("provider", "")
                                                                If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                                                                _client.프로그램순매수실시간등록(m.Str("code"), Sub(r)
                                                                                                              r.Topic = Topics.SYS_LOG
                                                                                                              r("text") = $"[Data] 프로그램순매수 실시간 등록: {m.Str("code")} success:{r.Bool("success", False)} msg:{r.Str("message", "")}"
                                                                                                              MessageBus.I.EmitOnUI(r)
                                                                                                          End Sub)
                                                            End Sub)
        MessageBus.I.On("program.trade.rt.unsubscribe", Sub(m)
                                                                  Dim reqProvider = m.Str("provider", "")
                                                                  If reqProvider <> "" AndAlso Not String.Equals(reqProvider, "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                                                                  _client.프로그램순매수실시간해지(m.Str("code"), Sub(r)
                                                                                                            r.Topic = Topics.SYS_LOG
                                                                                                            r("text") = $"[Data] 프로그램순매수 실시간 해지: {m.Str("code")} success:{r.Bool("success", False)} msg:{r.Str("message", "")}"
                                                                                                            MessageBus.I.EmitOnUI(r)
                                                                                                        End Sub)
                                                              End Sub)
        MessageBus.I.On(Topics.INVESTOR_REQUEST, Sub(m) _client.투자자매매(m.Str("code"), m.Int("count", 20), Sub(r)
                                                                                                             r.Topic = Topics.INVESTOR_RESULT
                                                                                                             MessageBus.I.EmitOnUI(r)
                                                                                                         End Sub))
        MessageBus.I.On(Topics.STOCK_BASIC_REQUEST, Sub(m) _client.종목기본정보(m.Str("code"), Sub(r)
                                                                                             r.Topic = Topics.STOCK_BASIC_RESULT
                                                                                             MessageBus.I.EmitOnUI(r)
                                                                                         End Sub))
        MessageBus.I.On(Topics.STOCK_MULTI_INFO_REQUEST, Sub(m)
                                                              If Not RuntimeChartSettings.IsMarketDataProvider("cybos") Then Return
                                                              _client.복수종목정보(m.Str("codes"), Sub(r)
                                                                                            r.Topic = Topics.STOCK_MULTI_INFO_RESULT
                                                                                            r("provider") = "cybos"
                                                                                            MessageBus.I.EmitOnUI(r)
                                                                                        End Sub)
                                                          End Sub)
        MessageBus.I.On(Topics.SECTOR_LIST_REQUEST, Sub(m)
                                                        _client.호출("theme.top.rise", Sub(r)
                                                                                         r.Topic = Topics.SECTOR_LIST_RESULT
                                                                                         r("provider") = "cybos"
                                                                                         MessageBus.I.EmitOnUI(r)
                                                                                     End Sub,
                                                                                     "riseType", m.Str("riseType", "1"))
                                                    End Sub)
        MessageBus.I.On(Topics.SECTOR_STOCKS_REQUEST, Sub(m) _client.업종별종목(m.Str("sectorCode"), Sub(r)
                                                                                                    r.Topic = Topics.SECTOR_STOCKS_RESULT
                                                                                                    If Not r.Has("sectorCode") Then r("sectorCode") = m.Str("sectorCode")
                                                                                                    MessageBus.I.EmitOnUI(r)
                                                                                                End Sub))
        MessageBus.I.On(Topics.THEME_STOCKS_REQUEST, Sub(m) _client.테마별종목(m.Str("themeCode"), Sub(r)
                                                                                                  r.Topic = Topics.THEME_STOCKS_RESULT
                                                                                                  MessageBus.I.EmitOnUI(r)
                                                                                              End Sub))
        MessageBus.I.On(Topics.NEWS_LIST_REQUEST, Sub(m) _client.뉴스목록(Sub(r)
                                                                          r.Topic = Topics.NEWS_LIST_RESULT
                                                                          MessageBus.I.EmitOnUI(r)
                                                                      End Sub, m.Str("code")))
        MessageBus.I.On(Topics.NEWS_BODY_REQUEST, Sub(m) _client.뉴스본문(m.Str("newsCode"), Sub(r)
                                                                                             r.Topic = Topics.NEWS_BODY_RESULT
                                                                                             MessageBus.I.EmitOnUI(r)
                                                                                         End Sub))
        MessageBus.I.On(Topics.HOGA_REQUEST, Sub(m) _client.호가정보(m.Str("code"), Sub(r)
                                                                                    r.Topic = Topics.HOGA_RESULT
                                                                                    MessageBus.I.EmitOnUI(r)
                                                                                End Sub))

        MessageBus.I.On(Topics.STOCK_LIST_REQUEST, Sub(m) _client.종목코드목록(Sub(r)
                                                                             r.Topic = Topics.STOCK_LIST_RESULT
                                                                             MessageBus.I.EmitOnUI(r)
                                                                         End Sub))

        AddHandler _client.연결됨, Sub() MessageBus.I.Emit(Topics.SYS_LOG, "text", "[CybosBridge] 서버 연결됨")
        AddHandler _client.연결끊김, Sub() MessageBus.I.Emit(Topics.SYS_ERROR, "text", "[CybosBridge] 서버 연결 끊김")
    End Sub

    Private Sub EmitCandle(r As Msg, code As String, Optional consumer As String = "")
        r.Topic = Topics.CANDLE_LOADED
        If Not r.Has("code") Then r("code") = code
        If Not String.IsNullOrWhiteSpace(consumer) Then r("consumer") = consumer
        r("provider") = "cybos"
        MessageBus.I.EmitOnUI(r)
    End Sub

End Class
