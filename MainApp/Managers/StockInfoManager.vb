' ═══════════════════════════════════════════════════════════════
' StockInfoManager.vb — 싱글톤 종목정보 관리자
' ═══════════════════════════════════════════════════════════════
' 모든 데이터소스에서 추출된 종목을 중앙 관리.
' 종목 추가 → 정보 조회 → 캔들 다운로드 → 실시간 구독 → DataReady
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Concurrent
Imports [Shared]

Public Class StockInfoManager

    ' ─── 싱글톤 ───
    Private Shared _instance As StockInfoManager
    Private Shared ReadOnly _lock As New Object()

    Public Shared ReadOnly Property I As StockInfoManager
        Get
            If _instance Is Nothing Then
                SyncLock _lock
                    If _instance Is Nothing Then _instance = New StockInfoManager()
                End SyncLock
            End If
            Return _instance
        End Get
    End Property

    ' ─── 저장소 ───
    Private ReadOnly _items As New ConcurrentDictionary(Of String, StockInfoItem)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _candleRowsCache As New ConcurrentDictionary(Of String, List(Of Dictionary(Of String, String)))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _candleRequested As New ConcurrentDictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _realtimeRequested As New ConcurrentDictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
    Private _isInitialized As Boolean = False

    Private Sub New()
        ' Bus 구독: 실시간 틱 → 종목 업데이트
        MessageBus.I.On(Topics.TICK, AddressOf OnTick)

        ' Bus 구독: 캔들 로드 완료
        MessageBus.I.On(Topics.CANDLE_LOADED, AddressOf OnCandleLoaded)

        ' Bus 구독: 종목 추가 요청
        MessageBus.I.On(Topics.STOCKINFO_ADD_REQUEST, AddressOf OnAddRequest)

        _isInitialized = True
        AppLogger.I.Info("StockInfoManager 초기화 완료", "Manager")
    End Sub

    ' ════════════════════════════════════════
    ' 종목 추가/제거
    ' ════════════════════════════════════════

    ''' <summary>종목 추가 (이미 있으면 소스만 추가)</summary>
    Public Function AddStock(code As String, source As DataSourceType, Optional sourceDetail As String = "") As StockInfoItem
        code = SharedUtil.NormalizeChartCode(code.Trim())
        If String.IsNullOrEmpty(code) Then Return Nothing

        Dim item = _items.GetOrAdd(code, Function(k)
                                             Dim newItem As New StockInfoItem()
                                             newItem.Code = k
                                             Dim knownName = SharedUtil.GetKnownIndexName(k)
                                             If knownName <> "" Then newItem.Name = knownName
                                             Return newItem
                                         End Function)

        If String.IsNullOrWhiteSpace(item.Name) Then
            Dim knownName = SharedUtil.GetKnownIndexName(code)
            If knownName <> "" Then item.Name = knownName
        End If

        item.AddSource(source, sourceDetail)
        AppLogger.I.Debug($"종목 추가/업데이트: {code} [{source}] {sourceDetail}", "Manager")
        Return item
    End Function

    ''' <summary>복수 종목 일괄 추가</summary>
    Public Function AddStocks(codes As String(), source As DataSourceType, Optional sourceDetail As String = "") As List(Of StockInfoItem)
        Dim result As New List(Of StockInfoItem)()
        If codes Is Nothing Then Return result

        For Each code In codes
            Dim item = AddStock(code, source, sourceDetail)
            If item IsNot Nothing Then result.Add(item)
        Next

        AppLogger.I.Info($"종목 {result.Count}개 일괄 추가 [{source}] {sourceDetail}", "Manager")

        ' 정보 조회 요청
        If result.Count > 0 Then
            RequestStockInfo(result.Select(Function(x) x.Code).ToArray())
        End If

        ' 알림
        Dim m As New Msg(Topics.STOCKINFO_ADDED)
        m("codes") = result.Select(Function(x) x.Code).ToArray()
        m("source") = source.ToString()
        m("sourceDetail") = sourceDetail
        m("count") = result.Count
        MessageBus.I.EmitOnUI(m)

        Return result
    End Function

    ''' <summary>종목 제거</summary>
    Public Sub RemoveStock(code As String)
        Dim item As StockInfoItem = Nothing
        If _items.TryRemove(code, item) Then
            ' 실시간 해제
            If item.IsRealtimeSubscribed Then
                MessageBus.I.Emit(Topics.REALTIME_UNSUBSCRIBE, "codes", code)
            End If

            Dim m As New Msg(Topics.STOCKINFO_REMOVED)
            m("code") = code
            MessageBus.I.EmitOnUI(m)

            AppLogger.I.Info($"종목 제거: {code} {item.Name}", "Manager")
        End If
    End Sub

    ''' <summary>전체 초기화</summary>
    Public Sub Clear()
        ' 모든 실시간 해제
        MessageBus.I.Emit(Topics.REALTIME_UNSUBSCRIBE_ALL)

        _items.Clear()
        _candleRowsCache.Clear()
        _candleRequested.Clear()
        _realtimeRequested.Clear()
        MessageBus.I.EmitOnUI(New Msg(Topics.STOCKINFO_CLEAR))
        AppLogger.I.Info("종목 전체 초기화", "Manager")
    End Sub

    ' ════════════════════════════════════════
    ' 조회
    ' ════════════════════════════════════════

    Public Function GetItem(code As String) As StockInfoItem
        Dim item As StockInfoItem = Nothing
        _items.TryGetValue(code, item)
        Return item
    End Function

    Public Function GetAll() As List(Of StockInfoItem)
        Return _items.Values.ToList()
    End Function

    Public Function GetBySource(src As DataSourceType) As List(Of StockInfoItem)
        Return _items.Values.Where(Function(x) x.HasSource(src)).ToList()
    End Function

    Public Function GetReadyItems() As List(Of StockInfoItem)
        Return _items.Values.Where(Function(x) x.State = DataReadyState.Ready).ToList()
    End Function

    Public ReadOnly Property Count As Integer
        Get
            Return _items.Count
        End Get
    End Property

    Public ReadOnly Property ReadyCount As Integer
        Get
            Return _items.Values.Where(Function(x) x.State = DataReadyState.Ready).Count()
        End Get
    End Property

    ' ════════════════════════════════════════
    ' 정보 조회 파이프라인
    ' ════════════════════════════════════════

    ''' <summary>종목정보 일괄 조회 요청 (OPTKWFID 또는 MarketEye)</summary>
    Private Sub RequestStockInfo(codes As String())
        If codes Is Nothing OrElse codes.Length = 0 Then Return

        ' 99개씩 분할하여 조회
        Dim chunks = ChunkArray(codes, 99)
        For Each chunk In chunks
            Dim joined = String.Join(";", chunk)
            AppLogger.I.Comm($"종목정보 조회 요청: {chunk.Length}종목", "Manager")
            MessageBus.I.Emit(Topics.STOCK_MULTI_INFO_REQUEST, "codes", joined)
        Next

        ' 결과 수신 핸들러 (한 번만 등록)
        Static infoHandlerRegistered As Boolean = False
        If Not infoHandlerRegistered Then
            MessageBus.I.On(Topics.STOCK_MULTI_INFO_RESULT, AddressOf OnMultiInfoResult)
            infoHandlerRegistered = True
        End If
    End Sub

    Private Sub OnMultiInfoResult(m As Msg)
        If m.Has("provider") Then
            If Not RuntimeChartSettings.IsMarketDataProvider(m.Str("provider")) Then Return
        End If
        Dim rows = m.DictList("rows")
        If rows Is Nothing Then Return

        Dim updatedCodes As New List(Of String)()

        For Each row In rows
            Dim code = ""
            If row.ContainsKey("종목코드") Then code = row("종목코드")?.ToString()
            If row.ContainsKey("code") Then code = row("code")?.ToString()
            code = SharedUtil.NormalizeCode(code)

            If String.IsNullOrEmpty(code) Then Continue For

            Dim item As StockInfoItem = Nothing
            If _items.TryGetValue(code, item) Then
                Dim objRow = row.ToDictionary(Function(kv) kv.Key, Function(kv) CObj(kv.Value))
                item.UpdateFromInfo(objRow)
                updatedCodes.Add(code)
            End If
        Next

        If updatedCodes.Count > 0 Then
            AppLogger.I.Info($"종목정보 수신: {updatedCodes.Count}종목 업데이트", "Manager")

            ' 캔들 다운로드 시작
            RequestCandles(updatedCodes.ToArray())

            ' 실시간 구독 시작
            RequestRealtime(updatedCodes.ToArray())

            ' UI 업데이트 알림
            Dim um As New Msg(Topics.STOCKINFO_UPDATED)
            um("codes") = updatedCodes.ToArray()
            um("reason") = "info_loaded"
            MessageBus.I.EmitOnUI(um)
        End If
    End Sub

    ' ════════════════════════════════════════
    ' 캔들 다운로드
    ' ════════════════════════════════════════

    Private Sub RequestCandles(codes As String())
        Dim requested As Integer = 0

        For Each code In codes
            If _candleRowsCache.ContainsKey(code) Then Continue For
            If Not _candleRequested.TryAdd(code, True) Then Continue For
            MessageBus.I.Emit(Topics.CANDLE_REQUEST,
                              "code", code,
                              "provider", RuntimeChartSettings.MarketDataProvider,
                              "timeframe", RuntimeChartSettings.DefaultCandleTimeframe,
                              "count", RuntimeChartSettings.DefaultCandleRequestCount)
            requested += 1
        Next

        If requested > 0 Then
            AppLogger.I.Info($"캔들 다운로드 요청: {requested}종목", "Manager")
        End If
    End Sub

    Private Sub OnCandleLoaded(m As Msg)
        If m.Has("provider") Then
            If Not RuntimeChartSettings.IsMarketDataProvider(m.Str("provider")) Then Return
        End If
        Dim code = m.Str("code")
        If String.IsNullOrEmpty(code) Then Return

        Dim item As StockInfoItem = Nothing
        If Not _items.TryGetValue(code, item) Then Return

        Dim rows = m.DictList("rows")
        Dim cnt = If(rows IsNot Nothing, rows.Count, 0)
        item.CandleCount = cnt

        If rows IsNot Nothing AndAlso rows.Count > 0 Then
            Dim lastRow = rows(rows.Count - 1)
            Dim closePrice = SharedUtil.SafeInt(If(If(lastRow.ContainsKey("close"), lastRow("close"), ""), ""))
            If closePrice > 0 Then item.Price = closePrice
            Dim openPrice = SharedUtil.SafeInt(If(If(lastRow.ContainsKey("open"), lastRow("open"), ""), ""))
            If openPrice > 0 Then item.Open = openPrice
            Dim highPrice = SharedUtil.SafeInt(If(If(lastRow.ContainsKey("high"), lastRow("high"), ""), ""))
            If highPrice > 0 Then item.High = highPrice
            Dim lowPrice = SharedUtil.SafeInt(If(If(lastRow.ContainsKey("low"), lastRow("low"), ""), ""))
            If lowPrice > 0 Then item.Low = lowPrice
            If item.PrevClose = 0 AndAlso rows.Count >= 2 Then
                Dim prevRow = rows(rows.Count - 2)
                Dim prevClose = SharedUtil.SafeInt(If(If(prevRow.ContainsKey("close"), prevRow("close"), ""), ""))
                If prevClose > 0 Then item.PrevClose = prevClose
            End If

            _candleRowsCache.AddOrUpdate(code,
                                         Function(k) CloneRows(rows),
                                         Function(k, oldRows)
                                             If oldRows Is Nothing OrElse rows.Count >= oldRows.Count Then
                                                 Return CloneRows(rows)
                                             End If
                                             Return oldRows
                                         End Function)
        End If

        If item.State < DataReadyState.CandleLoaded Then
            item.State = DataReadyState.CandleLoaded
        End If

        AppLogger.I.Debug($"캔들 수신: {code} {item.Name} → {cnt}건", "Manager")

        ' 진행상황 알림
        Dim progress As New Msg(Topics.STOCKINFO_CANDLE_PROGRESS)
        progress("code") = code
        progress("count") = cnt
        progress("total") = _items.Count
        progress("completed") = _items.Values.Where(Function(x) x.State >= DataReadyState.CandleLoaded).Count()
        MessageBus.I.EmitOnUI(progress)

        ' 모든 종목의 캔들이 로드되었는지 확인
        CheckAllReady()
    End Sub

    ' ════════════════════════════════════════
    ' 실시간 구독
    ' ════════════════════════════════════════

    Private Sub RequestRealtime(codes As String())
        Dim onceCodes As New List(Of String)
        For Each code In codes
            If _realtimeRequested.TryAdd(code, True) Then
                onceCodes.Add(code)
            End If
        Next
        If onceCodes.Count = 0 Then Return

        Dim joined = String.Join(";", onceCodes)
        MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", joined)

        For Each code In onceCodes
            Dim item As StockInfoItem = Nothing
            If _items.TryGetValue(code, item) Then
                item.IsRealtimeSubscribed = True
                If item.State < DataReadyState.RealtimeOn Then
                    item.State = DataReadyState.RealtimeOn
                End If
            End If
        Next

        AppLogger.I.Info($"실시간 구독: {onceCodes.Count}종목", "Manager")
    End Sub

    Public Function TryEmitCachedCandles(code As String, Optional count As Integer = 300) As Boolean
        If String.IsNullOrWhiteSpace(code) Then Return False

        Dim rows As List(Of Dictionary(Of String, String)) = Nothing
        If Not _candleRowsCache.TryGetValue(code, rows) Then Return False
        If rows Is Nothing OrElse rows.Count = 0 Then Return False

        Dim emitRows As List(Of Dictionary(Of String, String))
        If count > 0 AndAlso rows.Count > count Then
            emitRows = rows.Skip(rows.Count - count).Select(Function(r) CloneRow(r)).ToList()
        Else
            emitRows = rows.Select(Function(r) CloneRow(r)).ToList()
        End If

        Dim m As New Msg(Topics.CANDLE_LOADED)
        m("code") = code
        m("rows") = emitRows
        Dim item = GetItem(code)
        If item IsNot Nothing AndAlso item.PrevClose > 0 Then
            m("prevClose") = CSng(item.PrevClose)
        End If
        MessageBus.I.EmitOnUI(m)
        Return True
    End Function

    Public Function IsCandleRequested(code As String) As Boolean
        If String.IsNullOrWhiteSpace(code) Then Return False
        Return _candleRequested.ContainsKey(code)
    End Function

    Public Sub MarkCandleRequested(code As String)
        If String.IsNullOrWhiteSpace(code) Then Return
        _candleRequested.TryAdd(code, True)
    End Sub

    Private Sub OnTick(m As Msg)
        Dim code = m.Str("code")
        If String.IsNullOrEmpty(code) Then Return

        Dim item As StockInfoItem = Nothing
        If Not _items.TryGetValue(code, item) Then Return

        item.UpdateFromTick(m)

        ' UI 업데이트 알림 (스로틀링: 100ms 이내 중복 무시)
        Static lastEmit As New Dictionary(Of String, DateTime)()
        Dim now = DateTime.Now
        If lastEmit.ContainsKey(code) AndAlso (now - lastEmit(code)).TotalMilliseconds < 100 Then Return
        lastEmit(code) = now

        Dim um As New Msg(Topics.STOCKINFO_UPDATED)
        um("code") = code
        um("reason") = "tick"
        MessageBus.I.Emit(um)
    End Sub

    ' ════════════════════════════════════════
    ' 필터링 + Data Ready
    ' ════════════════════════════════════════

    ''' <summary>기본 필터 적용 (가격, 거래량 등)</summary>
    Public Sub ApplyFilter(Optional minPrice As Integer = 1000,
                           Optional maxPrice As Integer = Integer.MaxValue,
                           Optional minVolume As Long = 10000)

        Dim passCount = 0
        Dim failCount = 0

        For Each item In _items.Values
            Dim passed = True
            Dim reason = ""

            If item.Price < minPrice Then
                passed = False : reason = $"가격 미달 ({item.Price} < {minPrice})"
            ElseIf item.Price > maxPrice Then
                passed = False : reason = $"가격 초과 ({item.Price} > {maxPrice})"
            ElseIf item.Volume < minVolume Then
                passed = False : reason = $"거래량 미달 ({item.Volume} < {minVolume})"
            End If

            item.FilterPassed = passed
            item.FilterReason = reason

            If passed Then
                If item.State >= DataReadyState.CandleLoaded Then
                    item.State = DataReadyState.Ready
                End If
                passCount += 1
            Else
                item.State = DataReadyState.Filtered
                failCount += 1
            End If
        Next

        AppLogger.I.Info($"필터 적용 완료: 통과 {passCount}, 제외 {failCount}", "Manager")

        Dim fm As New Msg(Topics.STOCKINFO_FILTER_APPLIED)
        fm("passed") = passCount
        fm("failed") = failCount
        MessageBus.I.EmitOnUI(fm)

        CheckAllReady()
    End Sub

    ''' <summary>Data Ready 상태 확인 및 알림</summary>
    Private Sub CheckAllReady()
        Dim ready = _items.Values.Where(Function(x) x.State = DataReadyState.Ready).Count()
        Dim total = _items.Count

        If ready > 0 Then
            Dim dm As New Msg(Topics.STOCKINFO_DATA_READY)
            dm("readyCount") = ready
            dm("totalCount") = total
            dm("isReady") = True
            MessageBus.I.EmitOnUI(dm)

            AppLogger.I.Info($"★ Data Ready: {ready}/{total} 종목 매매 가능", "Manager")
        End If
    End Sub

    ' ════════════════════════════════════════
    ' Bus 요청 핸들러
    ' ════════════════════════════════════════

    Private Sub OnAddRequest(m As Msg)
        Dim codes = m.Arr(Of String)("codes")
        Dim srcStr = m.Str("source", "수동추가")
        Dim detail = m.Str("sourceDetail", "")

        Dim src As DataSourceType = DataSourceType.수동추가
        [Enum].TryParse(srcStr, True, src)

        AddStocks(codes, src, detail)
    End Sub

    ' ════════════════════════════════════════
    ' 유틸
    ' ════════════════════════════════════════

    Private Shared Function ChunkArray(arr As String(), size As Integer) As List(Of String())
        Dim result As New List(Of String())()
        Dim i = 0
        While i < arr.Length
            Dim take = Math.Min(size, arr.Length - i)
            Dim chunk(take - 1) As String
            Array.Copy(arr, i, chunk, 0, take)
            result.Add(chunk)
            i += take
        End While
        Return result
    End Function

    Private Shared Function CloneRows(rows As List(Of Dictionary(Of String, String))) As List(Of Dictionary(Of String, String))
        If rows Is Nothing Then Return New List(Of Dictionary(Of String, String))()
        Return rows.Select(Function(r) CloneRow(r)).ToList()
    End Function

    Private Shared Function CloneRow(row As Dictionary(Of String, String)) As Dictionary(Of String, String)
        If row Is Nothing Then Return New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim copy As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each kv In row
            copy(kv.Key) = kv.Value
        Next
        Return copy
    End Function

End Class
