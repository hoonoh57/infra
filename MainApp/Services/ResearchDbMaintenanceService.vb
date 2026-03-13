Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Diagnostics
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Newtonsoft.Json
Imports [Shared]

Namespace Services
    Public NotInheritable Class ResearchDbMaintenanceService
        Private Const RequestTimeoutMs As Integer = 30000
        Private Const IndexKospi As String = "U001"
        Private Const IndexKosdaq As String = "U201"

        Private Shared ReadOnly _instance As New ResearchDbMaintenanceService()

        Private ReadOnly _settingsPath As String
        Private ReadOnly _dbConfigFileName As String = "db.config"
        Private ReadOnly _timer As Windows.Forms.Timer
        Private _settings As ResearchDbJobSettings
        Private _started As Boolean
        Private _isRunning As Integer

        Public Shared ReadOnly Property Instance As ResearchDbMaintenanceService
            Get
                Return _instance
            End Get
        End Property

        Public Event LogReceived As Action(Of String)
        Public Event SettingsChanged As Action(Of ResearchDbJobSettings)

        Private Sub New()
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "research_db_jobs.json")
            _settings = LoadSettingsInternal()
            _timer = New Windows.Forms.Timer() With {.Interval = 60000}
            AddHandler _timer.Tick, AddressOf OnTimerTick
        End Sub

        Public Sub Start()
            If _started Then Return
            _started = True
            _timer.Start()
            Log($"Research DB service started. AutoRun={_settings.AutoRunEnabled}, Time={_settings.AutoRunTime}")
        End Sub

        Public Function GetSettings() As ResearchDbJobSettings
            Return CloneSettings(_settings)
        End Function

        Public Sub UpdateSettings(settings As ResearchDbJobSettings)
            If settings Is Nothing Then Throw New ArgumentNullException(NameOf(settings))
            _settings = CloneSettings(settings)
            SaveSettingsInternal(_settings)
            RaiseEvent SettingsChanged(GetSettings())
            Log("Research DB settings saved.")
        End Sub

        Public Sub RunSelectedExportsAsync(tradingDate As DateTime,
                                           exportDaily As Boolean,
                                           exportMinute As Boolean,
                                           exportTick30 As Boolean,
                                           exportIndexes As Boolean)
            If Interlocked.Exchange(_isRunning, 1) = 1 Then
                Log("A research DB export job is already running.")
                Return
            End If

            ThreadPool.QueueUserWorkItem(
                Sub()
                    Try
                        RunSelectedExports(tradingDate.Date, exportDaily, exportMinute, exportTick30, exportIndexes)
                    Catch ex As Exception
                        Log($"Research DB export failed: {ex.Message}")
                        AppLogger.I.Error($"Research DB export failed: {ex}", "ResearchDb")
                    Finally
                        Interlocked.Exchange(_isRunning, 0)
                    End Try
                End Sub)
        End Sub

        Public Sub RunDateRangeExportsAsync(dateFrom As DateTime,
                                            dateTo As DateTime,
                                            exportDaily As Boolean,
                                            exportMinute As Boolean,
                                            exportTick30 As Boolean,
                                            exportIndexes As Boolean)
            If Interlocked.Exchange(_isRunning, 1) = 1 Then
                Log("A research DB export job is already running.")
                Return
            End If

            ThreadPool.QueueUserWorkItem(
                Sub()
                    Try
                        RunDateRangeExports(dateFrom.Date, dateTo.Date, exportDaily, exportMinute, exportTick30, exportIndexes)
                    Catch ex As Exception
                        Log($"Research DB range export failed: {ex.Message}")
                        AppLogger.I.Error($"Research DB range export failed: {ex}", "ResearchDb")
                    Finally
                        Interlocked.Exchange(_isRunning, 0)
                    End Try
                End Sub)
        End Sub

        Public Sub RunFullRebuildAsync(dateFrom As DateTime,
                                       dateTo As DateTime,
                                       exportDaily As Boolean,
                                       exportMinute As Boolean,
                                       exportTick30 As Boolean,
                                       exportIndexes As Boolean)
            If Interlocked.Exchange(_isRunning, 1) = 1 Then
                Log("A research DB export job is already running.")
                Return
            End If

            ThreadPool.QueueUserWorkItem(
                Sub()
                    Try
                        RunFullRebuild(dateFrom.Date, dateTo.Date, exportDaily, exportMinute, exportTick30, exportIndexes)
                    Catch ex As Exception
                        Log($"Research DB full rebuild failed: {ex.Message}")
                        AppLogger.I.Error($"Research DB full rebuild failed: {ex}", "ResearchDb")
                    Finally
                        Interlocked.Exchange(_isRunning, 0)
                    End Try
                End Sub)
        End Sub

        Public Sub RunDateUpdateAsync(tradingDate As DateTime,
                                      exportDaily As Boolean,
                                      exportMinute As Boolean,
                                      exportTick30 As Boolean,
                                      exportIndexes As Boolean)
            RunSelectedExportsAsync(tradingDate, exportDaily, exportMinute, exportTick30, exportIndexes)
        End Sub

        Public Sub RunDateRangeUpdateAsync(dateFrom As DateTime,
                                           dateTo As DateTime,
                                           exportDaily As Boolean,
                                           exportMinute As Boolean,
                                           exportTick30 As Boolean,
                                           exportIndexes As Boolean)
            RunDateRangeExportsAsync(dateFrom, dateTo, exportDaily, exportMinute, exportTick30, exportIndexes)
        End Sub

        Public Sub RunAutoUpdateAsync(exportDaily As Boolean,
                                      exportMinute As Boolean,
                                      exportTick30 As Boolean,
                                      exportIndexes As Boolean)
            If Interlocked.Exchange(_isRunning, 1) = 1 Then
                Log("A research DB export job is already running.")
                Return
            End If

            ThreadPool.QueueUserWorkItem(
                Sub()
                    Try
                        RunAutoUpdate(exportDaily, exportMinute, exportTick30, exportIndexes)
                    Catch ex As Exception
                        Log($"Research DB auto update failed: {ex.Message}")
                        AppLogger.I.Error($"Research DB auto update failed: {ex}", "ResearchDb")
                    Finally
                        Interlocked.Exchange(_isRunning, 0)
                    End Try
                End Sub)
        End Sub

        Private Sub OnTimerTick(sender As Object, e As EventArgs)
            If Not _settings.AutoRunEnabled Then Return
            If Interlocked.CompareExchange(_isRunning, 0, 0) = 1 Then Return

            Dim parsedTime As DateTime
            If Not DateTime.TryParseExact(_settings.AutoRunTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, parsedTime) Then
                Return
            End If

            Dim now = DateTime.Now
            If now.Hour < parsedTime.Hour OrElse (now.Hour = parsedTime.Hour AndAlso now.Minute < parsedTime.Minute) Then
                Return
            End If

            Dim todayKey = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            If String.Equals(_settings.LastAutoRunDate, todayKey, StringComparison.Ordinal) Then Return

            Dim snapshot = GetSettings()
            RunSelectedExportsAsync(now.Date, snapshot.ExportDailyCandles, snapshot.ExportMinuteCandles, snapshot.ExportTick30Candles, snapshot.ExportMarketIndexes)
            snapshot.LastAutoRunDate = todayKey
            UpdateSettings(snapshot)
        End Sub

        Private Sub RunSelectedExports(tradingDate As DateTime,
                                       exportDaily As Boolean,
                                       exportMinute As Boolean,
                                       exportTick30 As Boolean,
                                       exportIndexes As Boolean)
            Dim universe = LoadUniverseCodes(_settings.UniverseSourcePath)
            If universe.Count = 0 Then Throw New InvalidOperationException("No universe codes could be loaded from the configured source file.")

            Dim runFolder = Path.Combine(_settings.OutputRootPath, tradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            Directory.CreateDirectory(runFolder)
            Log($"Starting research DB export for {tradingDate:yyyy-MM-dd}. Universe={universe.Count} codes.")

            If exportDaily AndAlso Not IsStageCompleted(tradingDate, "daily") Then
                MarkCheckpoint(tradingDate, "daily", "running", universe.Count, 0, 0, "")
                Log($"[Stage] Daily candles 500 backfill start for {tradingDate:yyyy-MM-dd}.")
                Dim result = ExportDailyCandles(universe, Path.Combine(runFolder, $"daily_candles_k150_{tradingDate:yyyyMMdd}.sql"))
                ImportSqlIfConfigured(result.OutputPath)
                MarkCheckpoint(tradingDate, "daily", If(result.FailedCount > 0, "partial", "completed"), universe.Count, universe.Count - result.FailedCount, result.FailedCount, "")
                Log($"{result.JobName}: {result.RowsWritten} rows -> {result.OutputPath}")
            End If

            If exportMinute AndAlso Not IsStageCompleted(tradingDate, "minute") Then
                MarkCheckpoint(tradingDate, "minute", "running", universe.Count, 0, 0, "")
                Log($"[Stage] Minute candles start for {tradingDate:yyyy-MM-dd}.")
                Dim result = ExportMinuteCandles(universe, tradingDate, Path.Combine(runFolder, $"minute_candles_k150_{tradingDate:yyyyMMdd}.sql"))
                ImportSqlIfConfigured(result.OutputPath)
                MarkCheckpoint(tradingDate, "minute", If(result.FailedCount > 0, "partial", "completed"), universe.Count, universe.Count - result.FailedCount, result.FailedCount, "")
                Log($"{result.JobName}: {result.RowsWritten} rows -> {result.OutputPath}")
            End If

            If exportTick30 AndAlso Not IsStageCompleted(tradingDate, "tick30") Then
                MarkCheckpoint(tradingDate, "tick30", "running", universe.Count, 0, 0, "")
                Log($"[Stage] Tick30 candles start for {tradingDate:yyyy-MM-dd}.")
                Dim result = ExportTick30Candles(universe, tradingDate, Path.Combine(runFolder, $"tick30_candles_k150_{tradingDate:yyyyMMdd}.sql"))
                ImportSqlIfConfigured(result.OutputPath)
                MarkCheckpoint(tradingDate, "tick30", If(result.FailedCount > 0, "partial", "completed"), universe.Count, universe.Count - result.FailedCount, result.FailedCount, "")
                Log($"{result.JobName}: {result.RowsWritten} rows -> {result.OutputPath}")
            End If

            If exportIndexes AndAlso Not IsStageCompleted(tradingDate, "index") Then
                MarkCheckpoint(tradingDate, "index", "running", 2, 0, 0, "")
                Log($"[Stage] Market index minute start for {tradingDate:yyyy-MM-dd}.")
                Dim result = ExportMarketIndexes(tradingDate, Path.Combine(runFolder, $"market_index_minute_{tradingDate:yyyyMMdd}.sql"))
                ImportSqlIfConfigured(result.OutputPath)
                MarkCheckpoint(tradingDate, "index", "completed", 2, 2, 0, "")
                Log($"{result.JobName}: {result.RowsWritten} rows -> {result.OutputPath}")
            End If

            Log("Research DB export completed.")
        End Sub

        Private Sub RunFullRebuild(dateFrom As DateTime,
                                   dateTo As DateTime,
                                   exportDaily As Boolean,
                                   exportMinute As Boolean,
                                   exportTick30 As Boolean,
                                   exportIndexes As Boolean)
            Dim startDate = If(dateFrom <= dateTo, dateFrom, dateTo)
            Dim endDate = If(dateFrom <= dateTo, dateTo, dateFrom)
            Log($"Starting research DB full rebuild: {startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}.")
            ResetCheckpointState()
            ResetResearchTables(exportDaily, exportMinute, exportTick30, exportIndexes)
            RunDateRangeExports(startDate, endDate, exportDaily, exportMinute, exportTick30, exportIndexes)
        End Sub

        Private Sub RunAutoUpdate(exportDaily As Boolean,
                                  exportMinute As Boolean,
                                  exportTick30 As Boolean,
                                  exportIndexes As Boolean)
            Dim checkpoint = LoadCheckpointState()
            Dim today = DateTime.Today
            Dim pendingDates = checkpoint.Entries.
                Where(Function(x) IsStageEnabled(x.Stage, exportDaily, exportMinute, exportTick30, exportIndexes)).
                Where(Function(x) Not String.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase)).
                Select(Function(x) SafeParseDate(x.TradingDate)).
                Where(Function(x) x <> DateTime.MinValue).
                OrderBy(Function(x) x).
                ToList()

            If pendingDates.Count > 0 Then
                Dim startDate = pendingDates.First()
                Log($"Auto update resuming from checkpoint: {startDate:yyyy-MM-dd} ~ {today:yyyy-MM-dd}.")
                RunDateRangeExports(startDate, today, exportDaily, exportMinute, exportTick30, exportIndexes)
                Return
            End If

            Dim latestCompleted = checkpoint.Entries.
                Where(Function(x) String.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase)).
                Select(Function(x) SafeParseDate(x.TradingDate)).
                Where(Function(x) x <> DateTime.MinValue).
                DefaultIfEmpty(today.AddDays(-1)).
                Max()

            Dim nextDate = latestCompleted.AddDays(1)
            If nextDate > today Then
                Log("Auto update: no pending dates. Running today's update only. This mode ignores the selected range.")
                RunSelectedExports(today, exportDaily, exportMinute, exportTick30, exportIndexes)
            Else
                Log($"Auto update continuing from {nextDate:yyyy-MM-dd} ~ {today:yyyy-MM-dd}. This mode ignores the selected range.")
                RunDateRangeExports(nextDate, today, exportDaily, exportMinute, exportTick30, exportIndexes)
            End If
        End Sub

        Private Sub RunDateRangeExports(dateFrom As DateTime,
                                        dateTo As DateTime,
                                        exportDaily As Boolean,
                                        exportMinute As Boolean,
                                        exportTick30 As Boolean,
                                        exportIndexes As Boolean)
            Dim startDate = If(dateFrom <= dateTo, dateFrom, dateTo)
            Dim endDate = If(dateFrom <= dateTo, dateTo, dateFrom)
            Dim universe = LoadUniverseCodes(_settings.UniverseSourcePath)
            If universe.Count = 0 Then Throw New InvalidOperationException("No universe codes could be loaded from the configured source file.")

            Log($"Starting research DB range export: {startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}. Universe={universe.Count} codes.")

            If exportDaily Then
                Dim dailyFolder = Path.Combine(_settings.OutputRootPath, endDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
                Directory.CreateDirectory(dailyFolder)
                Log($"[Range] Daily candles 500 backfill start using as-of {endDate:yyyy-MM-dd}.")
                Dim dailyResult = ExportDailyCandles(universe, Path.Combine(dailyFolder, $"daily_candles_k150_{endDate:yyyyMMdd}.sql"))
                ImportSqlIfConfigured(dailyResult.OutputPath)
                Log($"{dailyResult.JobName}: {dailyResult.RowsWritten} rows -> {dailyResult.OutputPath}")
            End If

            Dim current = startDate
            While current <= endDate
                If current.DayOfWeek <> DayOfWeek.Saturday AndAlso current.DayOfWeek <> DayOfWeek.Sunday Then
                    Log($"[Range] Processing {current:yyyy-MM-dd}.")
                    RunSelectedExports(current, False, exportMinute, exportTick30, exportIndexes)
                End If
                current = current.AddDays(1)
            End While

            Log("Research DB range export completed.")
        End Sub

        Private Function ExportDailyCandles(universe As IReadOnlyList(Of String), outputPath As String) As ResearchDbJobResult
            Dim sb As New StringBuilder()
            Dim rowsWritten = 0
            Dim processed = 0
            Dim failed = 0

            For Each code In universe
                processed += 1
                Dim rows As List(Of Dictionary(Of String, String)) = Nothing
                Try
                    rows = RequestCandleRows(code, "d", 500)
                Catch ex As Exception
                    failed += 1
                    Log($"[Daily] {code} skipped: {ex.Message}")
                    If processed = 1 OrElse processed Mod 25 = 0 OrElse processed = universe.Count Then
                        Log($"[Daily] {processed}/{universe.Count} codes processed.")
                    End If
                    Continue For
                End Try
                For Each row In rows
                    Dim candleDate = ParseDateValue(row, "date", "일자", "dt")
                    If candleDate = DateTime.MinValue Then Continue For
                    rowsWritten += 1
                    sb.AppendLine(
                        "INSERT INTO daily_candles_k150(code, candle_date, open, high, low, close, volume, tr_amount, change_pct, source) VALUES (" &
                        $"{Sql(code)}, {Sql(candleDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}, {SqlInt(row, "open", "시가")}, {SqlInt(row, "high", "고가")}, {SqlInt(row, "low", "저가")}, {SqlInt(row, "close", "종가", "현재가")}, {SqlLong(row, "volume", "거래량")}, {SqlLong(row, "tr_amount", "거래대금")}, {SqlDecimal(row, "change_pct", "등락률")}, 'cybos')" &
                        " ON DUPLICATE KEY UPDATE open=VALUES(open), high=VALUES(high), low=VALUES(low), close=VALUES(close), volume=VALUES(volume), tr_amount=VALUES(tr_amount), change_pct=VALUES(change_pct), source=VALUES(source);")
                Next
                If processed = 1 OrElse processed Mod 25 = 0 OrElse processed = universe.Count Then
                    Log($"[Daily] {processed}/{universe.Count} codes processed.")
                End If
            Next

            WriteSql(outputPath, sb)
            Return New ResearchDbJobResult With {.JobName = "Daily Candles", .OutputPath = outputPath, .RowsWritten = rowsWritten, .FailedCount = failed, .Success = True}
        End Function

        Private Function ExportMinuteCandles(universe As IReadOnlyList(Of String), tradingDate As DateTime, outputPath As String) As ResearchDbJobResult
            Dim sb As New StringBuilder()
            Dim rowsWritten = 0
            Dim tradingDay = tradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            Dim processed = 0
            Dim failed = 0

            For Each code In universe
                Dim rows As List(Of Dictionary(Of String, String)) = Nothing
                Try
                    rows = RequestPeriodRows(code, "m1", tradingDay, tradingDay)
                Catch ex As Exception
                    failed += 1
                    Log($"[Minute {tradingDate:yyyy-MM-dd}] {code} skipped: {ex.Message}")
                    processed += 1
                    If processed = 1 OrElse processed Mod 25 = 0 OrElse processed = universe.Count Then
                        Log($"[Minute {tradingDate:yyyy-MM-dd}] {processed}/{universe.Count} codes processed.")
                    End If
                    Continue For
                End Try
                processed += 1
                For Each row In rows
                    Dim candleDt = ParseDateTimeValue(row)
                    If candleDt = DateTime.MinValue Then Continue For
                    rowsWritten += 1
                    sb.AppendLine(
                        "INSERT INTO minute_candles_k150(code, timeframe_min, candle_dt, open, high, low, close, volume, tr_amount, source) VALUES (" &
                        $"{Sql(code)}, 1, {Sql(candleDt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}, {SqlInt(row, "open", "시가")}, {SqlInt(row, "high", "고가")}, {SqlInt(row, "low", "저가")}, {SqlInt(row, "close", "종가", "현재가")}, {SqlLong(row, "volume", "거래량")}, {SqlLong(row, "tr_amount", "거래대금")}, 'cybos')" &
                        " ON DUPLICATE KEY UPDATE open=VALUES(open), high=VALUES(high), low=VALUES(low), close=VALUES(close), volume=VALUES(volume), tr_amount=VALUES(tr_amount), source=VALUES(source);")
                Next
                If processed = 1 OrElse processed Mod 25 = 0 OrElse processed = universe.Count Then
                    Log($"[Minute {tradingDate:yyyy-MM-dd}] {processed}/{universe.Count} codes processed.")
                End If
            Next

            WriteSql(outputPath, sb)
            Return New ResearchDbJobResult With {.JobName = "Minute Candles", .OutputPath = outputPath, .RowsWritten = rowsWritten, .FailedCount = failed, .Success = True}
        End Function

        Private Function ExportTick30Candles(universe As IReadOnlyList(Of String), tradingDate As DateTime, outputPath As String) As ResearchDbJobResult
            Dim sb As New StringBuilder()
            Dim rowsWritten = 0
            Dim stopTime = tradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) & "153000"
            Dim processed = 0
            Dim failed = 0

            For Each code In universe
                Dim rows As List(Of Dictionary(Of String, String)) = Nothing
                Try
                    rows = RequestTickRows(code, RuntimeChartSettings.DefaultTickUnit, stopTime)
                Catch ex As Exception
                    failed += 1
                    Log($"[Tick30 {tradingDate:yyyy-MM-dd}] {code} skipped: {ex.Message}")
                    processed += 1
                    If processed = 1 OrElse processed Mod 25 = 0 OrElse processed = universe.Count Then
                        Log($"[Tick30 {tradingDate:yyyy-MM-dd}] {processed}/{universe.Count} codes processed.")
                    End If
                    Continue For
                End Try
                processed += 1
                For Each row In rows
                    Dim candleDt = ParseDateTimeValue(row)
                    If candleDt = DateTime.MinValue OrElse candleDt.Date <> tradingDate.Date Then Continue For
                    rowsWritten += 1
                    sb.AppendLine(
                        "INSERT INTO tick30_candles_k150(code, candle_dt, tick_unit, open, high, low, close, volume, source) VALUES (" &
                        $"{Sql(code)}, {Sql(candleDt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}, {RuntimeChartSettings.DefaultTickUnit}, {SqlInt(row, "open", "시가")}, {SqlInt(row, "high", "고가")}, {SqlInt(row, "low", "저가")}, {SqlInt(row, "close", "종가", "현재가")}, {SqlLong(row, "volume", "거래량")}, 'cybos')" &
                        " ON DUPLICATE KEY UPDATE open=VALUES(open), high=VALUES(high), low=VALUES(low), close=VALUES(close), volume=VALUES(volume), source=VALUES(source);")
                Next
                If processed = 1 OrElse processed Mod 25 = 0 OrElse processed = universe.Count Then
                    Log($"[Tick30 {tradingDate:yyyy-MM-dd}] {processed}/{universe.Count} codes processed.")
                End If
            Next

            WriteSql(outputPath, sb)
            Return New ResearchDbJobResult With {.JobName = "Tick30 Candles", .OutputPath = outputPath, .RowsWritten = rowsWritten, .FailedCount = failed, .Success = True}
        End Function

        Private Function ExportMarketIndexes(tradingDate As DateTime, outputPath As String) As ResearchDbJobResult
            Dim sb As New StringBuilder()
            Dim rowsWritten = 0
            Dim tradingDay = tradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)

            For Each indexCode In New String() {IndexKospi, IndexKosdaq}
                Log($"[Indexes {tradingDate:yyyy-MM-dd}] Requesting {indexCode}.")
                Dim rows = RequestPeriodRows(indexCode, "m1", tradingDay, tradingDay)
                For Each row In rows
                    Dim candleDt = ParseDateTimeValue(row)
                    If candleDt = DateTime.MinValue Then Continue For
                    rowsWritten += 1
                    sb.AppendLine(
                        "INSERT INTO market_index_minute(index_code, timeframe_min, candle_dt, open, high, low, close, volume, source) VALUES (" &
                        $"{Sql(indexCode)}, 1, {Sql(candleDt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}, {SqlDecimal(row, "open", "시가")}, {SqlDecimal(row, "high", "고가")}, {SqlDecimal(row, "low", "저가")}, {SqlDecimal(row, "close", "종가", "현재가")}, {SqlLong(row, "volume", "거래량")}, 'cybos')" &
                        " ON DUPLICATE KEY UPDATE open=VALUES(open), high=VALUES(high), low=VALUES(low), close=VALUES(close), volume=VALUES(volume), source=VALUES(source);")
                Next
            Next

            WriteSql(outputPath, sb)
            Return New ResearchDbJobResult With {.JobName = "Market Index Minute", .OutputPath = outputPath, .RowsWritten = rowsWritten, .Success = True}
        End Function

        Private Function RequestCandleRows(code As String, timeframe As String, count As Integer) As List(Of Dictionary(Of String, String))
            Dim normalizedCode = SharedUtil.NormalizeChartCode(code)
            Dim response As Msg = Nothing
            Dim completed As Boolean = False

            Dim handler As Action(Of Msg) =
                Sub(m As Msg)
                    If m Is Nothing Then Return
                    If Not String.Equals(SharedUtil.NormalizeChartCode(m.Str("code")), normalizedCode, StringComparison.OrdinalIgnoreCase) Then Return
                    If Not String.Equals(m.Str("provider", "cybos"), "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                    Dim rows = m.DictList("rows")
                    If rows Is Nothing OrElse rows.Count = 0 Then Return
                    response = m.Clone()
                    completed = True
                End Sub

            MessageBus.I.On(Topics.RESEARCH_CANDLE_LOADED, handler)
            Try
                MessageBus.I.Emit(Topics.RESEARCH_CANDLE_REQUEST, "code", normalizedCode, "provider", "cybos", "timeframe", timeframe, "count", count)
                WaitForCompletion(completed)
                If response Is Nothing Then Throw New TimeoutException($"No candle response for {normalizedCode} [{timeframe}].")
                Return response.DictList("rows")
            Finally
                MessageBus.I.Off(Topics.RESEARCH_CANDLE_LOADED, handler)
            End Try
        End Function

        Private Function RequestPeriodRows(code As String, timeframe As String, fromDay As String, toDay As String) As List(Of Dictionary(Of String, String))
            Dim normalizedCode = SharedUtil.NormalizeChartCode(code)
            Dim response As Msg = Nothing
            Dim completed As Boolean = False

            Dim handler As Action(Of Msg) =
                Sub(m As Msg)
                    If m Is Nothing Then Return
                    If Not String.Equals(SharedUtil.NormalizeChartCode(m.Str("code")), normalizedCode, StringComparison.OrdinalIgnoreCase) Then Return
                    If Not String.Equals(m.Str("provider", "cybos"), "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                    If Not String.Equals(NormalizeTimeframe(m.Str("timeframe", timeframe)), NormalizeTimeframe(timeframe), StringComparison.OrdinalIgnoreCase) Then Return
                    response = m.Clone()
                    completed = True
                End Sub

            MessageBus.I.On(Topics.RESEARCH_CANDLE_PERIOD_LOADED, handler)
            Try
                MessageBus.I.Emit(Topics.RESEARCH_CANDLE_PERIOD_REQUEST, "code", normalizedCode, "provider", "cybos", "timeframe", timeframe, "from", fromDay, "to", toDay)
                WaitForCompletion(completed)
                If response Is Nothing Then Throw New TimeoutException($"No period response for {normalizedCode} [{timeframe}] {fromDay}-{toDay}.")
                Return response.DictList("rows")
            Finally
                MessageBus.I.Off(Topics.RESEARCH_CANDLE_PERIOD_LOADED, handler)
            End Try
        End Function

        Private Function RequestTickRows(code As String, tickUnit As Integer, stopTime As String) As List(Of Dictionary(Of String, String))
            Dim normalizedCode = SharedUtil.NormalizeChartCode(code)
            Dim response As Msg = Nothing
            Dim completed As Boolean = False
            Dim normalizedTf = RuntimeChartSettings.TickTimeframe(RuntimeChartSettings.NormalizeTickUnit(tickUnit))

            Dim handler As Action(Of Msg) =
                Sub(m As Msg)
                    If m Is Nothing Then Return
                    If Not String.Equals(SharedUtil.NormalizeChartCode(m.Str("code")), normalizedCode, StringComparison.OrdinalIgnoreCase) Then Return
                    If Not String.Equals(m.Str("provider", "cybos"), "cybos", StringComparison.OrdinalIgnoreCase) Then Return
                    If Not String.Equals(m.Str("timeframe", normalizedTf), normalizedTf, StringComparison.OrdinalIgnoreCase) Then Return
                    response = m.Clone()
                    completed = True
                End Sub

            MessageBus.I.On(Topics.RESEARCH_TICK_CANDLE_LOADED, handler)
            Try
                MessageBus.I.Emit(Topics.RESEARCH_TICK_CANDLE_REQUEST, "code", normalizedCode, "provider", "cybos", "tickUnit", tickUnit, "timeframe", normalizedTf, "stopTime", stopTime)
                WaitForCompletion(completed)
                If response Is Nothing Then Throw New TimeoutException($"No tick response for {normalizedCode} [{normalizedTf}] {stopTime}.")
                Return response.DictList("rows")
            Finally
                MessageBus.I.Off(Topics.RESEARCH_TICK_CANDLE_LOADED, handler)
            End Try
        End Function

        Private Shared Sub WaitForCompletion(ByRef completed As Boolean)
            Dim startedAt = Environment.TickCount
            While Not completed AndAlso Environment.TickCount - startedAt < RequestTimeoutMs
                Application.DoEvents()
                Thread.Sleep(20)
            End While
            If Not completed Then Throw New TimeoutException("Timed out waiting for research DB data response.")
        End Sub

        Private Shared Sub WriteSql(outputPath As String, sb As StringBuilder)
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath))
            File.WriteAllText(outputPath, sb.ToString(), New UTF8Encoding(False))
        End Sub

        Private Sub ImportSqlIfConfigured(sqlPath As String)
            Dim config = LoadDbConfig()
            If config Is Nothing OrElse Not config.Enabled Then Return
            If String.IsNullOrWhiteSpace(sqlPath) OrElse Not File.Exists(sqlPath) Then Return

            Dim psi As New ProcessStartInfo() With {
                .FileName = config.MySqlCliPath,
                .Arguments = BuildMysqlArguments(config),
                .UseShellExecute = False,
                .RedirectStandardInput = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .CreateNoWindow = True
            }

            Using proc = Process.Start(psi)
                If proc Is Nothing Then
                    Throw New InvalidOperationException("mysql.exe could not be started.")
                End If

                Using writer = proc.StandardInput
                    writer.Write(File.ReadAllText(sqlPath, Encoding.UTF8))
                End Using

                Dim stdOut = proc.StandardOutput.ReadToEnd()
                Dim stdErr = proc.StandardError.ReadToEnd()
                proc.WaitForExit()

                If proc.ExitCode <> 0 Then
                    Throw New InvalidOperationException($"MySQL import failed for {Path.GetFileName(sqlPath)}: {stdErr}".Trim())
                End If

                If Not String.IsNullOrWhiteSpace(stdOut) Then
                    Log($"MySQL import output: {stdOut.Trim()}")
                End If
                Log($"MySQL import completed: {Path.GetFileName(sqlPath)}")
            End Using
        End Sub

        Private Function GetCheckpointPath() As String
            Return Path.Combine(_settings.OutputRootPath, "research_db_checkpoint.json")
        End Function

        Private Function LoadCheckpointState() As ResearchDbCheckpointState
            Try
                Dim path = GetCheckpointPath()
                If File.Exists(path) Then
                    Dim json = File.ReadAllText(path, Encoding.UTF8)
                    Dim loaded = JsonConvert.DeserializeObject(Of ResearchDbCheckpointState)(json)
                    If loaded IsNot Nothing Then Return loaded
                End If
            Catch
            End Try
            Return New ResearchDbCheckpointState()
        End Function

        Private Sub SaveCheckpointState(state As ResearchDbCheckpointState)
            Dim checkpointPath = GetCheckpointPath()
            Dim checkpointDirectory = System.IO.Path.GetDirectoryName(checkpointPath)
            If Not String.IsNullOrWhiteSpace(checkpointDirectory) Then
                Directory.CreateDirectory(checkpointDirectory)
            End If
            File.WriteAllText(checkpointPath, JsonConvert.SerializeObject(state, Formatting.Indented), Encoding.UTF8)
        End Sub

        Private Sub ResetCheckpointState()
            Dim path = GetCheckpointPath()
            If File.Exists(path) Then File.Delete(path)
        End Sub

        Private Function IsStageCompleted(tradingDate As DateTime, stage As String) As Boolean
            Dim state = LoadCheckpointState()
            Dim key = tradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Dim found = state.Entries.FirstOrDefault(Function(x) x.TradingDate = key AndAlso String.Equals(x.Stage, stage, StringComparison.OrdinalIgnoreCase))
            Return found IsNot Nothing AndAlso String.Equals(found.Status, "completed", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Sub MarkCheckpoint(tradingDate As DateTime,
                                   stage As String,
                                   status As String,
                                   totalCodes As Integer,
                                   completedCodes As Integer,
                                   failedCodes As Integer,
                                   lastCode As String)
            Dim state = LoadCheckpointState()
            Dim key = tradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Dim found = state.Entries.FirstOrDefault(Function(x) x.TradingDate = key AndAlso String.Equals(x.Stage, stage, StringComparison.OrdinalIgnoreCase))
            If found Is Nothing Then
                found = New ResearchDbCheckpointEntry With {
                    .TradingDate = key,
                    .Stage = stage
                }
                state.Entries.Add(found)
            End If

            found.Mode = "research_db"
            found.Status = status
            found.TotalCodes = totalCodes
            found.CompletedCodes = completedCodes
            found.FailedCodes = failedCodes
            found.LastCode = lastCode
            found.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            SaveCheckpointState(state)
        End Sub

        Private Sub ResetResearchTables(exportDaily As Boolean,
                                        exportMinute As Boolean,
                                        exportTick30 As Boolean,
                                        exportIndexes As Boolean)
            Dim statements As New List(Of String)()
            If exportDaily Then statements.Add("TRUNCATE TABLE daily_candles_k150;")
            If exportMinute Then statements.Add("TRUNCATE TABLE minute_candles_k150;")
            If exportTick30 Then statements.Add("TRUNCATE TABLE tick30_candles_k150;")
            If exportIndexes Then statements.Add("TRUNCATE TABLE market_index_minute;")
            If statements.Count = 0 Then Return

            Dim resetPath = Path.Combine(_settings.OutputRootPath, $"reset_research_tables_{DateTime.Now:yyyyMMdd_HHmmss}.sql")
            WriteSql(resetPath, New StringBuilder(String.Join(Environment.NewLine, statements)))
            ImportSqlIfConfigured(resetPath)
            Log($"Research DB tables reset: {Path.GetFileName(resetPath)}")
        End Sub

        Private Shared Function IsStageEnabled(stage As String,
                                               exportDaily As Boolean,
                                               exportMinute As Boolean,
                                               exportTick30 As Boolean,
                                               exportIndexes As Boolean) As Boolean
            Select Case stage.ToLowerInvariant()
                Case "daily" : Return exportDaily
                Case "minute" : Return exportMinute
                Case "tick30" : Return exportTick30
                Case "index" : Return exportIndexes
                Case Else : Return False
            End Select
        End Function

        Private Shared Function SafeParseDate(value As String) As DateTime
            Dim parsed As DateTime
            If DateTime.TryParse(value, parsed) Then Return parsed.Date
            Return DateTime.MinValue
        End Function

        Private Shared Function LoadUniverseCodes(sourcePath As String) As List(Of String)
            If String.IsNullOrWhiteSpace(sourcePath) OrElse Not File.Exists(sourcePath) Then
                Throw New FileNotFoundException("Universe source file was not found.", sourcePath)
            End If

            Dim ext = Path.GetExtension(sourcePath).ToLowerInvariant()
            Dim lines = File.ReadAllLines(sourcePath, Encoding.UTF8)
            Dim codes As New List(Of String)()

            If ext = ".sql" Then
                Dim rx As New Regex("'(?<code>\d{6})'", RegexOptions.Compiled)
                For Each line In lines
                    Dim match = rx.Match(line)
                    If match.Success Then codes.Add(match.Groups("code").Value)
                Next
            Else
                For Each line In lines
                    Dim match = Regex.Match(line, "\b\d{6}\b")
                    If match.Success Then codes.Add(match.Value)
                Next
            End If

            Return codes.Select(Function(code) SharedUtil.NormalizeChartCode(code)).
                Where(Function(code) Not String.IsNullOrWhiteSpace(code)).
                Distinct(StringComparer.OrdinalIgnoreCase).
                OrderBy(Function(code) code).
                ToList()
        End Function

        Private Shared Function ParseDateValue(row As Dictionary(Of String, String), ParamArray keys As String()) As DateTime
            For Each key In keys
                If row.ContainsKey(key) Then
                    Dim dt = SharedUtil.ToDateTime(row(key))
                    If dt <> DateTime.MinValue Then Return dt.Date
                End If
            Next
            Return DateTime.MinValue
        End Function

        Private Shared Function ParseDateTimeValue(row As Dictionary(Of String, String)) As DateTime
            If row Is Nothing Then Return DateTime.MinValue
            Dim dt = DateTime.MinValue
            If row.ContainsKey("dt") Then dt = SharedUtil.ToDateTime(row("dt"))
            If dt = DateTime.MinValue AndAlso row.ContainsKey("datetime") Then dt = SharedUtil.ToDateTime(row("datetime"))
            If dt = DateTime.MinValue AndAlso row.ContainsKey("date") Then dt = SharedUtil.ToDateTime(row("date"))
            If dt = DateTime.MinValue AndAlso row.ContainsKey("일자") Then dt = SharedUtil.ToDateTime(row("일자"))
            If dt <> DateTime.MinValue AndAlso dt.TimeOfDay.TotalSeconds > 0 Then Return dt

            Dim tm = ""
            If row.ContainsKey("time") Then tm = row("time")
            If tm = "" AndAlso row.ContainsKey("hhmm") Then tm = row("hhmm")
            If tm = "" AndAlso row.ContainsKey("체결시간") Then tm = row("체결시간")
            If tm = "" AndAlso row.ContainsKey("시간") Then tm = row("시간")
            If String.IsNullOrWhiteSpace(tm) OrElse dt = DateTime.MinValue Then Return dt

            Dim digits = NormalizeHHmmssDigits(tm)
            If digits.Length < 6 Then Return dt

            Dim hh As Integer
            Dim mm As Integer
            Dim ss As Integer
            If Not Integer.TryParse(digits.Substring(0, 2), hh) Then Return dt
            If Not Integer.TryParse(digits.Substring(2, 2), mm) Then Return dt
            If Not Integer.TryParse(digits.Substring(4, 2), ss) Then Return dt
            Return New DateTime(dt.Year, dt.Month, dt.Day, hh, mm, ss)
        End Function

        Private Shared Function NormalizeHHmmssDigits(raw As String) As String
            Dim digits = New String(If(raw, "").Where(Function(ch) Char.IsDigit(ch)).ToArray())
            If digits.Length = 0 Then Return ""
            If digits.Length <= 2 Then Return digits.PadLeft(2, "0"c) & "0000"
            If digits.Length = 3 OrElse digits.Length = 4 Then Return digits.PadLeft(4, "0"c) & "00"
            If digits.Length = 5 Then Return digits.PadLeft(6, "0"c)
            Return digits.Substring(0, 6)
        End Function

        Private Shared Function Sql(value As String) As String
            Return $"'{value.Replace("'", "''")}'"
        End Function

        Private Shared Function SqlInt(row As Dictionary(Of String, String), ParamArray keys As String()) As String
            Return CInt(Math.Round(RowNum(row, keys))).ToString(CultureInfo.InvariantCulture)
        End Function

        Private Shared Function SqlLong(row As Dictionary(Of String, String), ParamArray keys As String()) As String
            Return CLng(Math.Round(RowNum(row, keys))).ToString(CultureInfo.InvariantCulture)
        End Function

        Private Shared Function SqlDecimal(row As Dictionary(Of String, String), ParamArray keys As String()) As String
            Return RowNum(row, keys).ToString("0.####", CultureInfo.InvariantCulture)
        End Function

        Private Shared Function RowNum(row As Dictionary(Of String, String), ParamArray keys As String()) As Double
            If row Is Nothing Then Return 0
            For Each key In keys
                If String.IsNullOrWhiteSpace(key) OrElse Not row.ContainsKey(key) Then Continue For
                Dim raw = row(key)
                If String.IsNullOrWhiteSpace(raw) Then Continue For
                Return SharedUtil.SafeDouble(raw, True)
            Next
            Return 0
        End Function

        Private Shared Function NormalizeTimeframe(timeframe As String) As String
            Dim normalized = If(timeframe, "").Trim().ToLowerInvariant()
            If normalized = "" Then Return RuntimeChartSettings.DefaultCandleTimeframe
            If normalized = "daily" Then Return "d"
            If normalized = "weekly" Then Return "w"
            If normalized = "monthly" Then Return "mo"
            Return normalized
        End Function

        Private Function LoadSettingsInternal() As ResearchDbJobSettings
            Try
                If File.Exists(_settingsPath) Then
                    Dim json = File.ReadAllText(_settingsPath, Encoding.UTF8)
                    Dim loaded = JsonConvert.DeserializeObject(Of ResearchDbJobSettings)(json)
                    If loaded IsNot Nothing Then Return loaded
                End If
            Catch ex As Exception
                AppLogger.I.Warn($"Failed to load research DB settings: {ex.Message}", "ResearchDb")
            End Try
            Return New ResearchDbJobSettings()
        End Function

        Private Sub SaveSettingsInternal(settings As ResearchDbJobSettings)
            Dim json = JsonConvert.SerializeObject(settings, Formatting.Indented)
            File.WriteAllText(_settingsPath, json, New UTF8Encoding(False))
        End Sub

        Private Function LoadDbConfig() As ResearchDbMySqlConfig
            Dim configPath = ResolveDbConfigPath()
            If String.IsNullOrWhiteSpace(configPath) OrElse Not File.Exists(configPath) Then Return Nothing

            Dim json = File.ReadAllText(configPath, Encoding.UTF8)
            Dim config = JsonConvert.DeserializeObject(Of ResearchDbMySqlConfig)(json)
            If config Is Nothing Then Return Nothing

            If String.IsNullOrWhiteSpace(config.MySqlCliPath) Then config.MySqlCliPath = "mysql"
            If String.IsNullOrWhiteSpace(config.Host) Then config.Host = "127.0.0.1"
            If config.Port <= 0 Then config.Port = 3306
            If String.IsNullOrWhiteSpace(config.DatabaseName) Then config.DatabaseName = "strategy_research"
            If String.IsNullOrWhiteSpace(config.UserName) Then config.UserName = "root"
            If String.IsNullOrWhiteSpace(config.Charset) Then config.Charset = "utf8mb4"
            Return config
        End Function

        Private Function ResolveDbConfigPath() As String
            Dim current = New DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
            For i = 0 To 5
                If current Is Nothing Then Exit For
                Dim candidate = Path.Combine(current.FullName, _dbConfigFileName)
                If File.Exists(candidate) Then Return candidate
                current = current.Parent
            Next
            Return ""
        End Function

        Private Shared Function BuildMysqlArguments(config As ResearchDbMySqlConfig) As String
            Dim args As New List(Of String) From {
                $"--host={QuoteCli(config.Host)}",
                $"--port={config.Port.ToString(CultureInfo.InvariantCulture)}",
                $"--user={QuoteCli(config.UserName)}",
                $"--default-character-set={QuoteCli(config.Charset)}"
            }

            If Not String.IsNullOrWhiteSpace(config.Password) Then
                args.Add($"--password={QuoteCli(config.Password)}")
            End If

            args.Add(QuoteCli(config.DatabaseName))
            Return String.Join(" ", args)
        End Function

        Private Shared Function QuoteCli(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return """"""
            Return """" & value.Replace("""", "\""") & """"
        End Function

        Private Shared Function CloneSettings(settings As ResearchDbJobSettings) As ResearchDbJobSettings
            Return JsonConvert.DeserializeObject(Of ResearchDbJobSettings)(JsonConvert.SerializeObject(settings))
        End Function

        Private Sub Log(message As String)
            RaiseEvent LogReceived($"[{DateTime.Now:HH:mm:ss}] {message}")
            AppLogger.I.Info(message, "ResearchDb")
        End Sub
    End Class
End Namespace
