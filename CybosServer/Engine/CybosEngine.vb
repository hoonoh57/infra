' ═══════════════════════════════════════════════════════════════
' CybosEngine.vb — CybosPlus 범용 데이터 다운로드 엔진
' ═══════════════════════════════════════════════════════════════

Imports CPSYSDIBLib
Imports CPUTILLib
Imports DSCBO1Lib
Imports System.Runtime.InteropServices
Imports System.Threading
Imports [Shared]

Public Class CybosEngine

    Private ReadOnly _limiter As New RequestLimiter()
    Private ReadOnly _programRtSync As New Object()
    Private ReadOnly _programRtSubs As New Dictionary(Of String, ProgramTradeRtSubscription)(StringComparer.OrdinalIgnoreCase)

    Public Event RealtimePublished(msg As Msg)

    Public Sub New()
        If Not CheckConnection() Then
            Throw New Exception("CybosPlus 연결 실패. CybosPlus를 먼저 실행하세요.")
        End If
    End Sub

    Private Function CheckConnection() As Boolean
        Try
            Dim cpCybos As New CpCybos()
            Return cpCybos.IsConnect = 1
        Catch
            Return False
        End Try
    End Function

    ' ════════════════════════════════════════
    ' 범용 실행
    ' ════════════════════════════════════════

    Public Sub Execute(msg As Msg, callback As Action(Of Msg))
        Dim funcName = msg.Str("func")

        Try
            Select Case funcName
                ' ── 차트 ──
                Case "분봉"
                    DoMinuteChart(msg, callback)
                Case "일봉", "주봉", "월봉"
                    DoDailyChart(msg, callback)
                Case "틱차트"
                    DoTickChart(msg, callback)
                Case "틱차트기간"
                    DoTickChartByStopTime(msg, callback)
                Case "분봉기간"
                    DoMinuteChartByStopTime(msg, callback)
                Case "기간캔들"
                    DoPeriodChart(msg, callback)

                ' ── 프로그램매매 ──
                Case "프로그램순매수"
                    DoProgramTrade(msg, callback)
                Case "프로그램순매수실시간등록"
                    DoProgramTradeRealtimeSubscribe(msg, callback)
                Case "프로그램순매수실시간해지"
                    DoProgramTradeRealtimeUnsubscribe(msg, callback)

                ' ── 투자자 ──
                Case "투자자매매"
                    DoInvestor(msg, callback)

                ' ── 재무/MarketEye ──
                Case "종목기본정보"
                    DoStockMst(msg, callback)
                Case "복수종목정보"
                    DoMarketEye(msg, callback)

                ' ── 섹터 ──
                Case "업종별종목"
                    DoSectorStocks(msg, callback)
                Case "테마별종목"
                    DoThemeStocks(msg, callback)

                ' ── 뉴스 ──
                Case "뉴스목록"
                    DoNewsList(msg, callback)
                Case "뉴스본문"
                    DoNewsBody(msg, callback)

                ' ── 조건검색 ──
                Case "조건검색목록"
                    DoConditionList(msg, callback)
                Case "조건검색실행"
                    DoConditionSearch(msg, callback)

                ' ── 유틸 ──
                Case "종목코드목록"
                    DoCodeList(msg, callback)
                Case "연결상태"
                    DoConnectionStatus(msg, callback)

                Case Else
                    callback(MakeError($"알 수 없는 함수: {funcName}"))
            End Select

        Catch ex As Exception
            callback(MakeError($"CybosEngine 오류: {ex.Message}"))
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' 분봉 다운로드
    ' ════════════════════════════════════════

    Private Sub DoMinuteChart(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))
        Dim interval = msg.Int("interval", 1)
        Dim count = msg.Int("count", 2000)

        Dim chart As New StockChart()
        chart.SetInputValue(0, code)
        chart.SetInputValue(1, CByte(AscW("2"c)))          ' 개수 요청
        chart.SetInputValue(4, Math.Min(count, 5000))
        chart.SetInputValue(5, New Object() {0, 1, 2, 3, 4, 5, 8})  ' 날짜,시간,시고저종,거래량
        chart.SetInputValue(6, CByte(AscW("m"c)))           ' 분봉
        chart.SetInputValue(7, interval)
        chart.SetInputValue(9, CByte(AscW("1"c)))           ' 수정주가

        Dim allCandles As New List(Of Dictionary(Of String, String))()
        Dim loopCount = 0
        Dim maxLoops = Math.Max(1, (count \ 5000) + 5)

        Do
            _limiter.WaitIfNeeded()
            chart.BlockRequest()

            Dim status = CInt(chart.GetDibStatus())
            If status <> 0 Then Exit Do

            Dim rows = CInt(chart.GetHeaderValue(3))
            If rows = 0 Then Exit Do

            For i = 0 To rows - 1
                Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                row("date") = CStr(chart.GetDataValue(0, i))
                row("time") = CInt(chart.GetDataValue(1, i)).ToString("0000")
                row("open") = CStr(CInt(chart.GetDataValue(2, i)))
                row("high") = CStr(CInt(chart.GetDataValue(3, i)))
                row("low") = CStr(CInt(chart.GetDataValue(4, i)))
                row("close") = CStr(CInt(chart.GetDataValue(5, i)))
                row("volume") = CStr(CLng(chart.GetDataValue(6, i)))
                allCandles.Add(row)
            Next

            If allCandles.Count >= count Then Exit Do
            If Not CBool(chart.Continue) Then Exit Do

            loopCount += 1
            If loopCount >= maxLoops Then Exit Do

            Thread.Sleep(200)
        Loop

        ' 시간 오름차순 정렬
        allCandles.Reverse()

        ' 요청 개수만큼 자르기
        If allCandles.Count > count Then
            allCandles = allCandles.GetRange(allCandles.Count - count, count)
        End If

        Dim result = MakeOk("분봉 다운로드 완료")
        result("code") = msg.Str("code")
        result("interval") = interval
        result("rows") = allCandles
        callback(result)
    End Sub

    ' ════════════════════════════════════════
    ' 일봉/주봉/월봉
    ' ════════════════════════════════════════

    Private Sub DoDailyChart(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))
        Dim funcName = msg.Str("func")
        Dim count = msg.Int("count", 500)

        Dim tfChar As Char
        Select Case funcName
            Case "일봉" : tfChar = "D"c
            Case "주봉" : tfChar = "W"c
            Case "월봉" : tfChar = "M"c
            Case Else : tfChar = "D"c
        End Select

        Dim chart As New StockChart()
        chart.SetInputValue(0, code)
        chart.SetInputValue(1, CByte(AscW("2"c)))
        chart.SetInputValue(4, Math.Min(count, 5000))
        chart.SetInputValue(5, New Object() {0, 2, 3, 4, 5, 8})
        chart.SetInputValue(6, CByte(AscW(tfChar)))
        chart.SetInputValue(9, CByte(AscW("1"c)))

        Dim allRows As New List(Of Dictionary(Of String, String))()

        _limiter.WaitIfNeeded()
        chart.BlockRequest()

        Dim rows = CInt(chart.GetHeaderValue(3))
        For i = 0 To rows - 1
            Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            row("date") = CStr(chart.GetDataValue(0, i))
            row("open") = CStr(CInt(chart.GetDataValue(1, i)))
            row("high") = CStr(CInt(chart.GetDataValue(2, i)))
            row("low") = CStr(CInt(chart.GetDataValue(3, i)))
            row("close") = CStr(CInt(chart.GetDataValue(4, i)))
            row("volume") = CStr(CLng(chart.GetDataValue(5, i)))
            allRows.Add(row)
        Next

        If allRows.Count > count Then
            allRows.RemoveRange(count, allRows.Count - count)
        End If
        allRows.Reverse()

        Dim result = MakeOk($"{funcName} 다운로드 완료")
        result("code") = msg.Str("code")
        result("timeframe") = funcName
        result("rows") = allRows
        callback(result)
    End Sub

    ' ════════════════════════════════════════
    ' 틱차트
    ' ════════════════════════════════════════

    Private Sub DoTickChart(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))
        Dim count = msg.Int("count", 500)
        If count <= 0 Then count = 1
        If count > 2000 Then count = 2000
        Dim tickUnit = RuntimeChartSettings.NormalizeTickUnit(msg.Int("tickUnit", RuntimeChartSettings.DefaultTickUnit))

        Dim chart As New StockChart()
        chart.SetInputValue(0, code)
        chart.SetInputValue(1, CByte(AscW("2"c)))
        chart.SetInputValue(4, 2000)
        chart.SetInputValue(5, New Object() {0, 1, 2, 3, 4, 5, 8})
        chart.SetInputValue(6, CByte(AscW("T"c)))
        chart.SetInputValue(7, tickUnit)
        chart.SetInputValue(9, CByte(AscW("1"c)))

        Dim allRows As New List(Of Dictionary(Of String, String))()
        _limiter.WaitIfNeeded()
        chart.BlockRequest()

        Dim rows = CInt(chart.GetHeaderValue(3))
        For i = 0 To rows - 1
            Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            row("date") = CStr(chart.GetDataValue(0, i))
            row("time") = CInt(chart.GetDataValue(1, i)).ToString("0000")
            row("open") = CStr(CInt(chart.GetDataValue(2, i)))
            row("high") = CStr(CInt(chart.GetDataValue(3, i)))
            row("low") = CStr(CInt(chart.GetDataValue(4, i)))
            row("close") = CStr(CInt(chart.GetDataValue(5, i)))
            row("volume") = CStr(CLng(chart.GetDataValue(6, i)))
            allRows.Add(row)
        Next

        If allRows.Count > count Then
            allRows.RemoveRange(count, allRows.Count - count)
        End If
        allRows.Reverse()

        Dim result = MakeOk("틱차트 다운로드 완료")
        result("code") = msg.Str("code")
        result("timeframe") = RuntimeChartSettings.TickTimeframe(tickUnit)
        result("tickUnit") = tickUnit
        result("requestedCount") = count
        result("rows") = allRows
        callback(result)
    End Sub

    Private Sub DoTickChartByStopTime(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))
        Dim tickUnit = RuntimeChartSettings.NormalizeTickUnit(msg.Int("tickUnit", RuntimeChartSettings.DefaultTickUnit))
        Dim stopTimeRaw = msg.Str("stopTime")
        Dim stopDt = ParseStopDateTime(stopTimeRaw)
        If stopDt = DateTime.MinValue Then
            callback(MakeError("틱차트기간 오류: stopTime(yyyyMMddHHmmss) 필요"))
            Return
        End If

        Dim allRows = DownloadChartRowsUntilStop(code, "T"c, tickUnit, stopDt)

        Dim result = MakeOk("틱차트 기간 다운로드 완료")
        result("code") = msg.Str("code")
        result("timeframe") = RuntimeChartSettings.TickTimeframe(tickUnit)
        result("tickUnit") = tickUnit
        result("stopTime") = stopTimeRaw
        result("rows") = allRows
        callback(result)
    End Sub

    Private Sub DoMinuteChartByStopTime(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))
        Dim interval = msg.Int("interval", 1)
        If interval <= 0 Then interval = 1
        Dim stopTimeRaw = msg.Str("stopTime")
        Dim stopDt = ParseStopDateTime(stopTimeRaw)
        If stopDt = DateTime.MinValue Then
            callback(MakeError("분봉기간 오류: stopTime(yyyyMMddHHmmss) 필요"))
            Return
        End If

        Dim allRows = DownloadChartRowsUntilStop(code, "m"c, interval, stopDt)

        Dim result = MakeOk("분봉 기간 다운로드 완료")
        result("code") = msg.Str("code")
        result("timeframe") = $"m{interval}"
        result("interval") = interval
        result("stopTime") = stopTimeRaw
        result("rows") = allRows
        callback(result)
    End Sub

    Private Function DownloadChartRowsUntilStop(code As String, tfChar As Char, interval As Integer, stopDt As DateTime) As List(Of Dictionary(Of String, String))
        Dim chart As New StockChart()
        chart.SetInputValue(0, code)
        chart.SetInputValue(1, CByte(AscW("2"c)))
        chart.SetInputValue(4, 2000)
        chart.SetInputValue(5, New Object() {0, 1, 2, 3, 4, 5, 8})
        chart.SetInputValue(6, CByte(AscW(tfChar)))
        If tfChar = "m"c OrElse tfChar = "T"c Then chart.SetInputValue(7, interval)
        chart.SetInputValue(9, CByte(AscW("1"c)))

        Dim allRows As New List(Of Dictionary(Of String, String))()
        Dim loopCount = 0

        Do
            _limiter.WaitIfNeeded()
            chart.BlockRequest()

            Dim rows = CInt(chart.GetHeaderValue(3))
            If rows = 0 Then Exit Do

            Dim reachedStop As Boolean = False
            For i = 0 To rows - 1
                Dim d = CStr(chart.GetDataValue(0, i))
                Dim t = CInt(chart.GetDataValue(1, i)).ToString("0000")
                Dim dt = ParseDateTime(d, t)
                If dt < stopDt Then
                    reachedStop = True
                    Exit For
                End If

                Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                row("date") = d
                row("time") = t
                row("open") = CStr(CInt(chart.GetDataValue(2, i)))
                row("high") = CStr(CInt(chart.GetDataValue(3, i)))
                row("low") = CStr(CInt(chart.GetDataValue(4, i)))
                row("close") = CStr(CInt(chart.GetDataValue(5, i)))
                row("volume") = CStr(CLng(chart.GetDataValue(6, i)))
                allRows.Add(row)
            Next

            If reachedStop Then Exit Do
            If Not CBool(chart.Continue) Then Exit Do

            loopCount += 1
            If loopCount >= 500 Then Exit Do
            Thread.Sleep(200)
        Loop

        allRows.Reverse()
        Return allRows
    End Function

    ' ════════════════════════════════════════
    ' 기간별 캔들 (from~to 날짜 지정)
    ' ════════════════════════════════════════

    Private Sub DoPeriodChart(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))
        Dim timeframe = msg.Str("timeframe", RuntimeChartSettings.DefaultCandleTimeframe)
        If String.IsNullOrWhiteSpace(timeframe) Then timeframe = RuntimeChartSettings.DefaultCandleTimeframe
        Dim fromDate = msg.Str("from")
        Dim toDate = msg.Str("to")

        ' timeframe 파싱
        timeframe = timeframe.Trim()
        Dim tfChar As Char = If(timeframe.Length > 0, Char.ToUpperInvariant(timeframe(0)), "M"c)
        Dim interval As Integer = 1
        If timeframe.Length > 1 Then Integer.TryParse(timeframe.Substring(1), interval)
        If interval <= 0 Then interval = 1
        If tfChar = "T"c Then
            interval = RuntimeChartSettings.NormalizeTickUnit(interval)
        ElseIf tfChar = "M"c Then
            tfChar = "m"c
        End If

        ' from/to → DateTime
        Dim fromDt = ParseDateString(fromDate, "0900")
        Dim toDt = ParseDateString(toDate, "1530")
        If toDt = DateTime.MinValue Then toDt = DateTime.Now

        Dim chart As New StockChart()
        chart.SetInputValue(0, code)
        chart.SetInputValue(1, CByte(AscW("2"c)))
        chart.SetInputValue(4, 5000)
        chart.SetInputValue(5, New Object() {0, 1, 2, 3, 4, 5, 8})
        chart.SetInputValue(6, CByte(AscW(tfChar)))
        If tfChar = "m"c OrElse tfChar = "T"c Then chart.SetInputValue(7, interval)
        chart.SetInputValue(9, CByte(AscW("1"c)))

        Dim allRows As New List(Of Dictionary(Of String, String))()
        Dim loopCount = 0

        Do
            _limiter.WaitIfNeeded()
            chart.BlockRequest()

            Dim rows = CInt(chart.GetHeaderValue(3))
            If rows = 0 Then Exit Do

            Dim reachedOldest = False

            For i = 0 To rows - 1
                Dim d = CStr(chart.GetDataValue(0, i))
                Dim t = CInt(chart.GetDataValue(1, i)).ToString("0000")
                Dim dt = ParseDateTime(d, t)

                If dt > toDt Then Continue For
                If dt < fromDt Then
                    reachedOldest = True
                    Exit For
                End If

                Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                row("date") = d
                row("time") = t
                row("open") = CStr(CInt(chart.GetDataValue(2, i)))
                row("high") = CStr(CInt(chart.GetDataValue(3, i)))
                row("low") = CStr(CInt(chart.GetDataValue(4, i)))
                row("close") = CStr(CInt(chart.GetDataValue(5, i)))
                row("volume") = CStr(CLng(chart.GetDataValue(6, i)))
                allRows.Add(row)
            Next

            If reachedOldest Then Exit Do
            If Not CBool(chart.Continue) Then Exit Do

            loopCount += 1
            If loopCount >= 200 Then Exit Do
            Thread.Sleep(200)
        Loop

        allRows.Reverse()

        Dim result = MakeOk("기간 캔들 다운로드 완료")
        result("code") = msg.Str("code")
        result("timeframe") = If(tfChar = "T"c, RuntimeChartSettings.TickTimeframe(interval), timeframe.ToLowerInvariant())
        result("from") = fromDate
        result("to") = toDate
        result("rows") = allRows
        callback(result)
    End Sub

    ' ════════════════════════════════════════
    ' 프로그램매매
    ' ════════════════════════════════════════

    Private Sub DoProgramTrade(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))
        Dim count = msg.Int("count", 100)
        If count <= 0 Then count = 1

        Dim baseDateRaw = msg.Str("baseDate", "")
        Dim stopTimeRaw = msg.Str("stopTime", "")
        Dim baseDate As DateTime = DateTime.Today
        If Not String.IsNullOrWhiteSpace(baseDateRaw) Then
            Dim parsedBase = SharedUtil.ToDateTime(baseDateRaw)
            If parsedBase <> DateTime.MinValue Then
                baseDate = parsedBase.Date
            End If
        End If
        If Not String.IsNullOrWhiteSpace(stopTimeRaw) Then
            Dim parsed = ParseStopDateTime(stopTimeRaw)
            If parsed <> DateTime.MinValue Then
                ' baseDate explicitly provided by caller has priority.
                If String.IsNullOrWhiteSpace(baseDateRaw) Then
                    baseDate = parsed.Date
                End If
            End If
        End If
        Dim stopDt As DateTime = ParseStopDateTime(stopTimeRaw)
        Dim hasStopDt As Boolean = (stopDt <> DateTime.MinValue)

        Try
            Dim candidates As New List(Of String)
            Dim cfg = msg.Str("programTradeObjects", "")
            If Not String.IsNullOrWhiteSpace(cfg) Then
                For Each t In cfg.Split(";"c)
                    Dim objName = t.Trim()
                    If objName <> "" AndAlso Not candidates.Contains(objName, StringComparer.OrdinalIgnoreCase) Then
                        candidates.Add(objName)
                    End If
                Next
            End If
            If candidates.Count = 0 Then
                candidates.Add("CpSvrNew8119Chart")
                candidates.Add("CpSysDib.CpSvr7326")
                candidates.Add("CpSysDib.CpSvr7238")
            End If

            Dim allRows As List(Of Dictionary(Of String, String)) = Nothing
            Dim selectedObject As String = ""
            Dim selectedIntraday As Boolean = False
            Dim selectedRawFirst As String = ""
            Dim selectedRawLast As String = ""
            Dim selectedErr As String = ""
            Dim probeTrace As New List(Of String)()

            For Each objName In candidates
                Dim attemptRows As New List(Of Dictionary(Of String, String))()
                Dim attemptIntraday As Boolean = False
                Dim attemptRawFirst As String = ""
                Dim attemptRawLast As String = ""
                Dim attemptErr As String = ""
                Dim attemptDiag As String = ""

                Try
                    attemptRows = DownloadProgramTradeRowsFromObject(objName, code, count, baseDate, hasStopDt, stopDt, attemptIntraday, attemptRawFirst, attemptRawLast, attemptDiag)
                Catch ex As Exception
                    attemptErr = ex.Message
                End Try

                probeTrace.Add($"{objName}:rows={If(attemptRows Is Nothing, 0, attemptRows.Count)},intraday={attemptIntraday},raw={attemptRawFirst}..{attemptRawLast},diag={attemptDiag},err={attemptErr}")

                If attemptRows IsNot Nothing AndAlso attemptRows.Count > 0 Then
                    If attemptIntraday Then
                        allRows = attemptRows
                        selectedObject = objName
                        selectedIntraday = True
                        selectedRawFirst = attemptRawFirst
                        selectedRawLast = attemptRawLast
                        selectedErr = ""
                        Exit For
                    End If
                End If

                If attemptErr <> "" Then
                    selectedErr = $"[{objName}] {attemptErr}"
                End If
            Next

            If allRows Is Nothing Then
                allRows = New List(Of Dictionary(Of String, String))()
                selectedObject = ""
                selectedIntraday = False
                selectedRawFirst = ""
                selectedRawLast = ""
                If selectedErr = "" Then
                    selectedErr = "시간별 프로그램매매 객체에서 유효한 intraday 데이터를 받지 못했습니다."
                End If
            End If

            Dim result = MakeOk("프로그램순매수 완료")
            result("code") = msg.Str("code")
            result("stopTime") = stopTimeRaw
            result("requestedCount") = count
            result("rows") = allRows
            result("providerObject") = selectedObject
            result("isIntraday") = selectedIntraday
            result("rawFirstToken") = selectedRawFirst
            result("rawLastToken") = selectedRawLast
            result("probeError") = selectedErr
            result("probeTrace") = String.Join(" | ", probeTrace)
            result("rawRowCount") = allRows.Count
            If allRows.Count > 0 Then
                Dim firstRow = allRows(0)
                Dim lastRow = allRows(allRows.Count - 1)
                result("rawFirstDt") = If(firstRow.ContainsKey("dt"), firstRow("dt"), "")
                result("rawFirstNet") = If(firstRow.ContainsKey("netBuy"), firstRow("netBuy"), If(firstRow.ContainsKey("net"), firstRow("net"), ""))
                result("rawLastDt") = If(lastRow.ContainsKey("dt"), lastRow("dt"), "")
                result("rawLastNet") = If(lastRow.ContainsKey("netBuy"), lastRow("netBuy"), If(lastRow.ContainsKey("net"), lastRow("net"), ""))
            End If
            callback(result)

        Catch ex As Exception
            callback(MakeError($"프로그램매매 오류: {ex.Message}"))
        End Try
    End Sub

    Private Sub DoProgramTradeRealtimeSubscribe(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))
        If String.IsNullOrWhiteSpace(code) Then
            callback(MakeError("프로그램순매수 실시간 등록 오류: code 누락"))
            Return
        End If

        SyncLock _programRtSync
            If _programRtSubs.ContainsKey(code) Then
                Dim okAlready = MakeOk("프로그램순매수 실시간 이미 등록")
                okAlready("code") = msg.Str("code")
                callback(okAlready)
                Return
            End If

            Dim subObj As New ProgramTradeRtSubscription(code, AddressOf OnProgramTradeRealtimeReceived)
            subObj.Start()
            _programRtSubs(code) = subObj
        End SyncLock

        Dim ok = MakeOk("프로그램순매수 실시간 등록 완료")
        ok("code") = msg.Str("code")
        callback(ok)
    End Sub

    Private Sub DoProgramTradeRealtimeUnsubscribe(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))

        SyncLock _programRtSync
            If String.IsNullOrWhiteSpace(code) Then
                For Each kv In _programRtSubs
                    Try
                        kv.Value.Stop()
                    Catch
                    End Try
                Next
                _programRtSubs.Clear()
            Else
                Dim subObj As ProgramTradeRtSubscription = Nothing
                If _programRtSubs.TryGetValue(code, subObj) Then
                    Try
                        subObj.Stop()
                    Catch
                    End Try
                    _programRtSubs.Remove(code)
                End If
            End If
        End SyncLock

        Dim ok = MakeOk("프로그램순매수 실시간 해지 완료")
        ok("code") = msg.Str("code")
        callback(ok)
    End Sub

    Private Sub OnProgramTradeRealtimeReceived(code As String,
                                               hhmmss As String,
                                               curPrice As Long,
                                               buyQty As Long,
                                               sellQty As Long,
                                               netQty As Long,
                                               buyAmt As Long,
                                               sellAmt As Long,
                                               netAmt As Long)
        Dim nowDate = DateTime.Now.Date
        Dim dt = CombineDateAndHHmmss(nowDate, NormalizeToHHmmss(hhmmss))

        Dim push As New Msg(Topics.PROGRAM_TRADE)
        push("code") = If(code.StartsWith("A"), code.Substring(1), code)
        push("stockCode") = push.Str("code")
        push("date") = dt.ToString("yyyyMMdd")
        push("time") = dt.ToString("HHmmss")
        push("dt") = dt.ToString("yyyy-MM-dd HH:mm:ss")
        push("price") = curPrice
        push("buyQty") = buyQty
        push("sellQty") = sellQty
        push("netBuy") = netQty
        push("net") = netQty
        push("buyAmt") = buyAmt
        push("sellAmt") = sellAmt
        push("netAmt") = netAmt
        push("providerObject") = "CpSysDib.CpSvr8119S"
        push("isRealtime") = True

        RaiseEvent RealtimePublished(push)
    End Sub

    Private Function DownloadProgramTradeRowsFromObject(objName As String,
                                                        code As String,
                                                        count As Integer,
                                                        baseDate As DateTime,
                                                        hasStopDt As Boolean,
                                                        stopDt As DateTime,
                                                        ByRef isIntraday As Boolean,
                                                        ByRef rawFirstToken As String,
                                                        ByRef rawLastToken As String,
                                                        ByRef diag As String) As List(Of Dictionary(Of String, String))
        Dim obj As Object
        Select Case objName.Trim().ToLowerInvariant()
            Case "cpsvrnew8119chart", "dscbo1lib.cpsvrnew8119chart", "cpsysdib.cpsvrnew8119chart"
                obj = New CpSvrNew8119Chart()
            Case Else
                obj = CreateObject(objName)
        End Select
        obj.SetInputValue(0, code)
        Dim is8119 As Boolean = objName.Trim().ToLowerInvariant().Contains("8119")

        Dim rowsOut As New List(Of Dictionary(Of String, String))()
        Dim loopCount As Integer = 0
        Dim currentYear As Integer = baseDate.Year
        Dim prevMonthDayToken As Integer = -1
        isIntraday = False
        rawFirstToken = ""
        rawLastToken = ""
        diag = ""

        Dim targetRawCount As Integer = count
        If is8119 Then
            targetRawCount = Math.Max(count * 20, count)
            targetRawCount = Math.Min(targetRawCount, 30000)
        End If

        Do
            _limiter.WaitIfNeeded()
            obj.BlockRequest()
            Try
                Dim st = CInt(obj.GetDibStatus())
                Dim msg = CStr(obj.GetDibMsg1())
                If st <> 0 Then
                    diag = $"status={st},msg={msg}"
                    Exit Do
                End If
            Catch
            End Try

            Dim rows As Integer = CInt(obj.GetHeaderValue(0))
            If rows <= 0 Then Exit Do

            For i = 0 To rows - 1
                If rowsOut.Count >= count Then Exit For

                Dim timeRaw = CStr(obj.GetDataValue(0, i))
                Dim digits = New String(If(timeRaw, "").Where(Function(ch) Char.IsDigit(ch)).ToArray())

                Dim rowDate As DateTime = baseDate.Date
                Dim hhmmss As String = "000000"

                Dim y As Integer = 0
                Dim m As Integer = 0
                Dim d As Integer = 0
                Dim monthDayToken As Integer = -1

                If TryParseYyyyMmDdHhMmSsDigits(digits, rowDate, hhmmss) Then
                    isIntraday = True
                ElseIf TryParseYyyyMmDdHhMmDigits(digits, rowDate, hhmmss) Then
                    isIntraday = True
                ElseIf TryParseYyyyMmDdDigits(digits, y, m, d) AndAlso TryBuildDate(y, m, d, rowDate) Then
                    hhmmss = "090000"
                ElseIf TryParseMonthDayDigits(digits, m, d, monthDayToken) Then
                    If prevMonthDayToken >= 0 AndAlso monthDayToken > prevMonthDayToken Then
                        currentYear -= 1
                    End If
                    prevMonthDayToken = monthDayToken
                    If Not TryBuildDate(currentYear, m, d, rowDate) Then
                        rowDate = baseDate.Date
                    End If
                    hhmmss = "090000"
                Else
                    hhmmss = NormalizeToHHmmss(timeRaw)
                End If

                If hhmmss <> "090000" Then
                    isIntraday = True
                End If

                Dim dt = CombineDateAndHHmmss(rowDate, hhmmss)
                If hasStopDt AndAlso dt < stopDt Then Continue For

                Dim sellStr As String
                Dim buyStr As String
                Dim netStr As String
                If is8119 Then
                    buyStr = CStr(obj.GetDataValue(1, i))
                    sellStr = CStr(obj.GetDataValue(2, i))
                    Dim buyVal = SharedUtil.SafeDouble(buyStr, True)
                    Dim sellVal = SharedUtil.SafeDouble(sellStr, True)
                    netStr = (buyVal - sellVal).ToString(System.Globalization.CultureInfo.InvariantCulture)
                Else
                    sellStr = CStr(obj.GetDataValue(1, i))
                    buyStr = CStr(obj.GetDataValue(2, i))
                    netStr = CStr(obj.GetDataValue(3, i))
                End If

                Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                row("date") = rowDate.ToString("yyyyMMdd")
                row("time") = hhmmss
                row("dt") = dt.ToString("yyyy-MM-dd HH:mm:ss")
                row("sell") = sellStr
                row("buy") = buyStr
                row("net") = netStr
                row("netBuy") = netStr
                row("raw0") = timeRaw

                rowsOut.Add(row)
            Next

            If rowsOut.Count >= targetRawCount Then Exit Do
            If Not CBool(obj.Continue) Then Exit Do

            loopCount += 1
            If loopCount >= 200 Then Exit Do
            Thread.Sleep(150)
        Loop

        If rowsOut.Count > 0 Then
            rawFirstToken = If(rowsOut(0).ContainsKey("raw0"), rowsOut(0)("raw0"), "")
            rawLastToken = If(rowsOut(rowsOut.Count - 1).ContainsKey("raw0"), rowsOut(rowsOut.Count - 1)("raw0"), "")
        End If

        If is8119 AndAlso rowsOut.Count > 0 Then
            rowsOut.Sort(Function(a, b)
                             Dim dta = SharedUtil.ToDateTime(If(a.ContainsKey("dt"), a("dt"), ""))
                             Dim dtb = SharedUtil.ToDateTime(If(b.ContainsKey("dt"), b("dt"), ""))
                             Return dta.CompareTo(dtb)
                         End Function)

            Dim minuteMap As New Dictionary(Of DateTime, Dictionary(Of String, String))()
            For Each row In rowsOut
                Dim dt = SharedUtil.ToDateTime(If(row.ContainsKey("dt"), row("dt"), ""))
                If dt = DateTime.MinValue Then Continue For
                Dim minuteDt = New DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0)
                minuteMap(minuteDt) = row
            Next

            Dim minuteKeys = minuteMap.Keys.OrderBy(Function(t) t).ToList()
            Dim minuteRows As New List(Of Dictionary(Of String, String))(minuteKeys.Count)
            For Each k In minuteKeys
                Dim row = minuteMap(k)
                row("time") = k.ToString("HHmmss")
                row("date") = k.ToString("yyyyMMdd")
                row("dt") = k.ToString("yyyy-MM-dd HH:mm:ss")
                minuteRows.Add(row)
            Next

            If minuteRows.Count > count Then
                minuteRows = minuteRows.Skip(minuteRows.Count - count).ToList()
            End If
            Return minuteRows
        End If

        If rowsOut.Count > count Then
            rowsOut = rowsOut.Take(count).ToList()
        End If
        Return rowsOut
    End Function

    ' ════════════════════════════════════════
    ' 투자자매매
    ' ════════════════════════════════════════

    Private Sub DoInvestor(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))
        Dim count = msg.Int("count", 20)

        Try
            Dim obj = CreateObject("CpSysDib.CpSvr7254")
            obj.SetInputValue(0, code)
            obj.SetInputValue(1, count)
            obj.SetInputValue(2, CChar("1"))
            obj.SetInputValue(3, CChar("1"))

            _limiter.WaitIfNeeded()
            obj.BlockRequest()

            Dim rows As Integer = CInt(obj.GetHeaderValue(0))
            Dim allRows As New List(Of Dictionary(Of String, String))()

            For i = 0 To rows - 1
                Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                row("date") = CStr(obj.GetDataValue(0, i))
                row("netBuy") = CStr(obj.GetDataValue(2, i))
                allRows.Add(row)
            Next

            Dim result = MakeOk("투자자매매 완료")
            result("code") = msg.Str("code")
            result("rows") = allRows
            callback(result)

        Catch ex As Exception
            callback(MakeError($"투자자매매 오류: {ex.Message}"))
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' 종목기본정보 (StockMst)
    ' ════════════════════════════════════════

    Private Sub DoStockMst(msg As Msg, callback As Action(Of Msg))
        Dim code = NormalizeCybosCode(msg.Str("code"))

        Try
            Dim obj As New StockMst()
            obj.SetInputValue(0, code)

            _limiter.WaitIfNeeded()
            obj.BlockRequest()

            Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            row("name") = CStr(obj.GetHeaderValue(1))
            row("price") = CStr(obj.GetHeaderValue(11))
            row("prevClose") = CStr(obj.GetHeaderValue(10))
            row("volume") = CStr(obj.GetHeaderValue(18))
            row("marketCap") = CStr(obj.GetHeaderValue(30))

            Dim result = MakeOk("종목기본정보 완료")
            result("code") = msg.Str("code")
            result("info") = row
            callback(result)

        Catch ex As Exception
            callback(MakeError($"StockMst 오류: {ex.Message}"))
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' MarketEye (복수 종목 일괄 조회)
    ' ════════════════════════════════════════

    Private Sub DoMarketEye(msg As Msg, callback As Action(Of Msg))
        Dim codesRaw = msg.Str("codes")
        If String.IsNullOrWhiteSpace(codesRaw) Then
            callback(MakeError("codes 필요"))
            Return
        End If

        Dim rawCodes = codesRaw.Split(";"c).Select(Function(c) c.Trim()).Where(Function(c) c <> "").ToArray()
        Dim cybCodes = rawCodes.Select(Function(c) If(c.StartsWith("A"), c, "A" & c)).ToArray()

        Try
            Dim mEye As New MarketEye()
            ' 필드: 0=종목코드, 4=현재가, 10=거래량, 12=등락률, 17=종목명
            mEye.SetInputValue(0, New Object() {0, 4, 10, 12, 17})
            mEye.SetInputValue(1, cybCodes)

            _limiter.WaitIfNeeded()
            mEye.BlockRequest()

            Dim cnt = CInt(mEye.GetHeaderValue(2))
            Dim allRows As New List(Of Dictionary(Of String, String))()

            For i = 0 To cnt - 1
                Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                Dim c = CStr(mEye.GetDataValue(0, i)).Trim()
                If c.StartsWith("A") AndAlso c.Length = 7 Then c = c.Substring(1)
                row("code") = c
                row("name") = CStr(mEye.GetDataValue(4, i)).Trim()
                row("price") = CStr(CInt(Math.Abs(CDbl(mEye.GetDataValue(1, i)))))
                row("volume") = CStr(CLng(mEye.GetDataValue(2, i)))
                row("changeRate") = CStr(CDbl(mEye.GetDataValue(3, i)))
                allRows.Add(row)
            Next

            Dim result = MakeOk("복수종목정보 완료")
            result("rows") = allRows
            callback(result)

        Catch ex As Exception
            callback(MakeError($"MarketEye 오류: {ex.Message}"))
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' 섹터/테마
    ' ════════════════════════════════════════

    Private Sub DoSectorStocks(msg As Msg, callback As Action(Of Msg))
        Try
            Dim sectorCode = msg.Str("sectorCode")
            Dim obj = CreateObject("CpSysDib.CpSvr7043")
            obj.SetInputValue(0, sectorCode)

            _limiter.WaitIfNeeded()
            obj.BlockRequest()

            Dim cnt As Integer = CInt(obj.GetHeaderValue(0))
            Dim allRows As New List(Of Dictionary(Of String, String))()

            For i = 0 To cnt - 1
                Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                row("code") = SharedUtil.NormalizeCode(CStr(obj.GetDataValue(0, i)))
                row("name") = CStr(obj.GetDataValue(1, i)).Trim()
                allRows.Add(row)
            Next

            Dim result = MakeOk("업종별종목 완료")
            result("rows") = allRows
            callback(result)

        Catch ex As Exception
            callback(MakeError($"업종별종목 오류: {ex.Message}"))
        End Try
    End Sub

    Private Sub DoThemeStocks(msg As Msg, callback As Action(Of Msg))
        Try
            Dim themeCode = msg.Str("themeCode")
            Dim obj = CreateObject("CpSysDib.CpSvr8081")
            obj.SetInputValue(0, themeCode)

            _limiter.WaitIfNeeded()
            obj.BlockRequest()

            Dim cnt As Integer = CInt(obj.GetHeaderValue(0))
            Dim allRows As New List(Of Dictionary(Of String, String))()

            For i = 0 To cnt - 1
                Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                row("code") = SharedUtil.NormalizeCode(CStr(obj.GetDataValue(0, i)))
                row("name") = CStr(obj.GetDataValue(1, i)).Trim()
                allRows.Add(row)
            Next

            Dim result = MakeOk("테마별종목 완료")
            result("rows") = allRows
            callback(result)

        Catch ex As Exception
            callback(MakeError($"테마별종목 오류: {ex.Message}"))
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' 뉴스
    ' ════════════════════════════════════════

    Private Sub DoNewsList(msg As Msg, callback As Action(Of Msg))
        Try
            Dim code = If(msg.Has("code"), NormalizeCybosCode(msg.Str("code")), "")
            Dim obj = CreateObject("CpSysDib.CpNews")
            If code <> "" Then obj.SetInputValue(0, code)

            _limiter.WaitIfNeeded()
            obj.BlockRequest()

            Dim cnt As Integer = CInt(obj.GetHeaderValue(0))
            Dim allRows As New List(Of Dictionary(Of String, String))()

            For i = 0 To Math.Min(cnt, 50) - 1
                Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                row("time") = CStr(obj.GetDataValue(0, i))
                row("title") = CStr(obj.GetDataValue(1, i)).Trim()
                row("code") = CStr(obj.GetDataValue(2, i)).Trim()
                row("newsCode") = CStr(obj.GetDataValue(3, i)).Trim()
                allRows.Add(row)
            Next

            Dim result = MakeOk("뉴스목록 완료")
            result("rows") = allRows
            callback(result)

        Catch ex As Exception
            callback(MakeError($"뉴스목록 오류: {ex.Message}"))
        End Try
    End Sub

    Private Sub DoNewsBody(msg As Msg, callback As Action(Of Msg))
        Try
            Dim newsCode = msg.Str("newsCode")
            Dim obj = CreateObject("CpSysDib.CpNewsBody")
            obj.SetInputValue(0, newsCode)

            _limiter.WaitIfNeeded()
            obj.BlockRequest()

            Dim body = CStr(obj.GetHeaderValue(0))

            Dim result = MakeOk("뉴스본문 완료")
            result("body") = body
            callback(result)

        Catch ex As Exception
            callback(MakeError($"뉴스본문 오류: {ex.Message}"))
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' 조건검색
    ' ════════════════════════════════════════

    Private Sub DoConditionList(msg As Msg, callback As Action(Of Msg))
        Try
            Dim condList = CreateObject("CpSysDib.CssStgList")
            condList.SetInputValue(0, CByte(AscW("1"c)))

            _limiter.WaitIfNeeded()
            condList.BlockRequest()

            Dim cnt = CInt(condList.GetHeaderValue(0))
            Dim allRows As New List(Of Dictionary(Of String, String))()

            For i = 0 To cnt - 1
                Dim row As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                row("name") = CStr(condList.GetDataValue(0, i)).Trim()
                row("id") = CStr(condList.GetDataValue(1, i)).Trim()
                allRows.Add(row)
            Next

            Dim result = MakeOk("조건검색 목록 완료")
            result("conditions") = allRows
            callback(result)

        Catch ex As Exception
            callback(MakeError($"조건검색목록 오류: {ex.Message}"))
        End Try
    End Sub

    Private Sub DoConditionSearch(msg As Msg, callback As Action(Of Msg))
        Try
            Dim condId = msg.Str("id")
            Dim condFind = CreateObject("CpSysDib.CssStgFind")
            condFind.SetInputValue(0, condId)
            condFind.SetInputValue(1, CByte(AscW("Y"c)))

            _limiter.WaitIfNeeded()
            condFind.BlockRequest()

            Dim cnt = CInt(condFind.GetHeaderValue(0))
            Dim codes As New List(Of String)()

            For i = 0 To cnt - 1
                Dim c = CStr(condFind.GetDataValue(0, i)).Trim()
                If c.StartsWith("A") Then c = c.Substring(1)
                codes.Add(c)
            Next

            Dim result = MakeOk("조건검색 실행 완료")
            result("codes") = codes.ToArray()
            callback(result)

        Catch ex As Exception
            callback(MakeError($"조건검색실행 오류: {ex.Message}"))
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' 유틸리티
    ' ════════════════════════════════════════

    Private Sub DoCodeList(msg As Msg, callback As Action(Of Msg))
        Try
            Dim codeMgr As New CpCodeMgr()
            Dim allCodes As New List(Of String)()

            ' KOSPI
            Dim kospi = codeMgr.GetStockListByMarket(CPE_MARKET_KIND.CPC_MARKET_KOSPI)
            For Each c As String In CType(kospi, Array)
                allCodes.Add(c)
            Next

            ' KOSDAQ
            Dim kosdaq = codeMgr.GetStockListByMarket(CPE_MARKET_KIND.CPC_MARKET_KOSDAQ)
            For Each c As String In CType(kosdaq, Array)
                allCodes.Add(c)
            Next

            Dim result = MakeOk("종목코드 목록 완료")
            result("codes") = allCodes.ToArray()
            callback(result)

        Catch ex As Exception
            callback(MakeError($"종목코드목록 오류: {ex.Message}"))
        End Try
    End Sub

    Private Sub DoConnectionStatus(msg As Msg, callback As Action(Of Msg))
        Try
            Dim cpCybos As New CpCybos()
            Dim result = MakeOk("연결상태")
            result("connected") = cpCybos.IsConnect = 1
            result("remainCount") = CInt(cpCybos.GetLimitRemainCount(1))
            result("remainTime") = CInt(cpCybos.GetLimitRemainTime(1))
            callback(result)
        Catch ex As Exception
            callback(MakeError($"연결상태 오류: {ex.Message}"))
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' 내부 헬퍼
    ' ════════════════════════════════════════

    Private Function NormalizeCybosCode(code As String) As String
        If String.IsNullOrWhiteSpace(code) Then Return ""
        code = code.Trim()
        If Not code.StartsWith("A") AndAlso Not code.StartsWith("U") AndAlso Not code.StartsWith("J") Then
            code = "A" & code
        End If
        Return code
    End Function

    Private Function ParseDateTime(dateStr As String, timeStr As String) As DateTime
        Try
            Dim d = dateStr.PadLeft(8, "0"c)
            Dim t = timeStr.PadLeft(4, "0"c)
            Return New DateTime(
                Integer.Parse(d.Substring(0, 4)),
                Integer.Parse(d.Substring(4, 2)),
                Integer.Parse(d.Substring(6, 2)),
                Integer.Parse(t.Substring(0, 2)),
                Integer.Parse(t.Substring(2, 2)), 0)
        Catch
            Return DateTime.MinValue
        End Try
    End Function

    Private Function ParseDateString(s As String, defaultTime As String) As DateTime
        If String.IsNullOrWhiteSpace(s) OrElse s.Length < 8 Then Return DateTime.MinValue
        Dim ymd = s.Substring(0, 8)
        Dim hhmm = If(s.Length >= 12, s.Substring(8, 4), defaultTime)
        Return ParseDateTime(ymd, hhmm)
    End Function

    Private Function ParseStopDateTime(s As String) As DateTime
        If String.IsNullOrWhiteSpace(s) Then Return DateTime.MinValue
        Dim digits = New String(s.Where(Function(ch) Char.IsDigit(ch)).ToArray())
        If digits.Length < 12 Then Return DateTime.MinValue
        If digits.Length > 14 Then digits = digits.Substring(0, 14)
        If digits.Length = 12 Then digits &= "00"
        If digits.Length <> 14 Then Return DateTime.MinValue
        Try
            Dim yyyy = Integer.Parse(digits.Substring(0, 4))
            Dim MM = Integer.Parse(digits.Substring(4, 2))
            Dim dd = Integer.Parse(digits.Substring(6, 2))
            Dim hh = Integer.Parse(digits.Substring(8, 2))
            Dim min = Integer.Parse(digits.Substring(10, 2))
            Dim ss = Integer.Parse(digits.Substring(12, 2))
            Return New DateTime(yyyy, MM, dd, hh, min, 0)
        Catch
            Return DateTime.MinValue
        End Try
    End Function

    Private Shared Function TryParseMonthDayDigits(digits As String,
                                                   ByRef mm As Integer,
                                                   ByRef dd As Integer,
                                                   ByRef token As Integer) As Boolean
        mm = 0
        dd = 0
        token = -1
        If String.IsNullOrWhiteSpace(digits) Then Return False

        Dim d = digits
        If d.Length > 4 Then Return False

        If d.Length = 3 Then
            If Not Integer.TryParse(d.Substring(0, 1), mm) Then Return False
            If Not Integer.TryParse(d.Substring(1, 2), dd) Then Return False
        ElseIf d.Length = 4 Then
            If Not Integer.TryParse(d.Substring(0, 2), mm) Then Return False
            If Not Integer.TryParse(d.Substring(2, 2), dd) Then Return False
        Else
            Return False
        End If

        If mm < 1 OrElse mm > 12 Then Return False
        If dd < 1 OrElse dd > 31 Then Return False
        token = mm * 100 + dd
        Return True
    End Function

    Private Shared Function TryParseYyyyMmDdDigits(digits As String,
                                                    ByRef yyyy As Integer,
                                                    ByRef mm As Integer,
                                                    ByRef dd As Integer) As Boolean
        yyyy = 0
        mm = 0
        dd = 0
        If String.IsNullOrWhiteSpace(digits) OrElse digits.Length <> 8 Then Return False
        If Not Integer.TryParse(digits.Substring(0, 4), yyyy) Then Return False
        If Not Integer.TryParse(digits.Substring(4, 2), mm) Then Return False
        If Not Integer.TryParse(digits.Substring(6, 2), dd) Then Return False
        Return (yyyy >= 1900 AndAlso yyyy <= 2099 AndAlso mm >= 1 AndAlso mm <= 12 AndAlso dd >= 1 AndAlso dd <= 31)
    End Function

    Private Shared Function TryParseYyyyMmDdHhMmDigits(digits As String,
                                                        ByRef dt As DateTime,
                                                        ByRef hhmmss As String) As Boolean
        dt = DateTime.MinValue
        hhmmss = "000000"
        If String.IsNullOrWhiteSpace(digits) OrElse digits.Length < 12 Then Return False
        Try
            Dim yyyy = Integer.Parse(digits.Substring(0, 4))
            Dim mm = Integer.Parse(digits.Substring(4, 2))
            Dim dd = Integer.Parse(digits.Substring(6, 2))
            Dim hh = Integer.Parse(digits.Substring(8, 2))
            Dim mi = Integer.Parse(digits.Substring(10, 2))
            dt = New DateTime(yyyy, mm, dd, hh, mi, 0)
            hhmmss = $"{hh:00}{mi:00}00"
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Shared Function TryParseYyyyMmDdHhMmSsDigits(digits As String,
                                                          ByRef dt As DateTime,
                                                          ByRef hhmmss As String) As Boolean
        dt = DateTime.MinValue
        hhmmss = "000000"
        If String.IsNullOrWhiteSpace(digits) OrElse digits.Length < 14 Then Return False
        Try
            Dim yyyy = Integer.Parse(digits.Substring(0, 4))
            Dim mm = Integer.Parse(digits.Substring(4, 2))
            Dim dd = Integer.Parse(digits.Substring(6, 2))
            Dim hh = Integer.Parse(digits.Substring(8, 2))
            Dim mi = Integer.Parse(digits.Substring(10, 2))
            Dim ss = Integer.Parse(digits.Substring(12, 2))
            dt = New DateTime(yyyy, mm, dd, hh, mi, ss)
            hhmmss = $"{hh:00}{mi:00}{ss:00}"
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Shared Function TryBuildDate(yyyy As Integer, mm As Integer, dd As Integer, ByRef dt As DateTime) As Boolean
        dt = DateTime.MinValue
        Try
            dt = New DateTime(yyyy, mm, dd)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Shared Function NormalizeToHHmmss(raw As String) As String
        Dim digits = New String(If(raw, "").Where(Function(ch) Char.IsDigit(ch)).ToArray())
        If digits.Length = 0 Then Return "000000"
        ' date+time combined payloads (e.g., yyyyMMddHHmm or yyyyMMddHHmmss)
        ' must extract time part from the tail, not head.
        If digits.Length = 14 Then Return digits.Substring(8, 6)
        If digits.Length = 12 Then Return digits.Substring(8, 4) & "00"
        If digits.Length = 10 Then Return digits.Substring(6, 4) & "00"
        If digits.Length = 8 Then Return digits.Substring(4, 4) & "00"
        If digits.Length <= 2 Then Return digits.PadLeft(2, "0"c) & "0000"
        If digits.Length = 3 OrElse digits.Length = 4 Then Return digits.PadLeft(4, "0"c) & "00"
        If digits.Length = 5 Then Return digits.PadLeft(6, "0"c)
        If digits.Length = 6 Then Return digits
        Return digits.Substring(digits.Length - 6, 6)
    End Function

    Private Shared Function CombineDateAndHHmmss(baseDate As DateTime, hhmmss As String) As DateTime
        Dim hh As Integer = 0
        Dim mm As Integer = 0
        Dim ss As Integer = 0
        Integer.TryParse(hhmmss.Substring(0, 2), hh)
        Integer.TryParse(hhmmss.Substring(2, 2), mm)
        Integer.TryParse(hhmmss.Substring(4, 2), ss)
        hh = Math.Max(0, Math.Min(23, hh))
        mm = Math.Max(0, Math.Min(59, mm))
        ss = Math.Max(0, Math.Min(59, ss))
        Return New DateTime(baseDate.Year, baseDate.Month, baseDate.Day, hh, mm, ss)
    End Function

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

Friend Class ProgramTradeRtSubscription
    Private ReadOnly _code As String
    Private ReadOnly _sink As Action(Of String, String, Long, Long, Long, Long, Long, Long, Long)
    Private WithEvents _sb As CpSvr8119S

    Public Sub New(code As String, sink As Action(Of String, String, Long, Long, Long, Long, Long, Long, Long))
        _code = code
        _sink = sink
        _sb = New CpSvr8119S()
    End Sub

    Public Sub Start()
        _sb.SetInputValue(0, _code)
        _sb.Subscribe()
    End Sub

    Public Sub [Stop]()
        Try
            _sb.Unsubscribe()
        Catch
        End Try
    End Sub

    Private Sub _sb_Received() Handles _sb.Received
        Try
            Dim code = CStr(_sb.GetHeaderValue(0))
            Dim tm = CStr(_sb.GetHeaderValue(1))
            Dim price = CLng(SharedUtil.SafeLong(CStr(_sb.GetHeaderValue(2))))
            Dim buyQty = CLng(SharedUtil.SafeLong(CStr(_sb.GetHeaderValue(7))))
            Dim sellQty = CLng(SharedUtil.SafeLong(CStr(_sb.GetHeaderValue(8))))
            Dim netQty = CLng(SharedUtil.SafeLong(CStr(_sb.GetHeaderValue(9))))
            Dim buyAmt = CLng(SharedUtil.SafeLong(CStr(_sb.GetHeaderValue(10))))
            Dim sellAmt = CLng(SharedUtil.SafeLong(CStr(_sb.GetHeaderValue(11))))
            Dim netAmt = CLng(SharedUtil.SafeLong(CStr(_sb.GetHeaderValue(12))))
            _sink?.Invoke(code, tm, price, buyQty, sellQty, netQty, buyAmt, sellAmt, netAmt)
        Catch
        End Try
    End Sub
End Class
