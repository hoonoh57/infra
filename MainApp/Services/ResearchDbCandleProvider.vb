Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Newtonsoft.Json
Imports StrategyCore.Models
Imports StrategyCore.Services

Namespace Services
    Public Class ResearchDbCandleProvider
        Implements ICandleDataProvider
        Implements IStrategyIndicatorAuxDataProvider

        Private Const DbConfigFileName As String = "db.config"

        Private ReadOnly _config As ResearchDbMySqlConfig

        Public Sub New()
            _config = LoadDbConfig()
            If _config Is Nothing OrElse Not _config.Enabled Then
                Throw New InvalidOperationException("Research DB access requires an enabled db.config.")
            End If
        End Sub

        Public Function GetCandles(symbol As String,
                                   timeframe As String,
                                   fromDate As DateTime,
                                   barCount As Integer) As IReadOnlyList(Of LabCandle) Implements ICandleDataProvider.GetCandles
            Dim code As String = NormalizeCode(symbol)
            Dim normalizedTimeframe As String = NormalizeTimeframe(timeframe)

            If String.Equals(normalizedTimeframe, "d", StringComparison.OrdinalIgnoreCase) Then
                Return LoadDailyCandles(code, fromDate)
            End If

            Dim minuteUnit As Integer = ResolveMinuteUnit(normalizedTimeframe)
            If minuteUnit > 0 Then
                Dim minuteCandles As IReadOnlyList(Of LabCandle) = LoadMinuteCandles(code, fromDate)
                If minuteUnit = 1 Then Return minuteCandles
                Return ResampleMinuteCandles(minuteCandles, minuteUnit)
            End If

            Throw New InvalidOperationException($"Research DB candle provider does not support timeframe '{timeframe}'.")
        End Function

        Public Function GetTickTimestamps(symbol As String,
                                          timeframe As String,
                                          fromDate As DateTime,
                                          barCount As Integer) As IReadOnlyList(Of DateTime) Implements IStrategyIndicatorAuxDataProvider.GetTickTimestamps
            Dim code As String = NormalizeCode(symbol)
            Dim query As String =
                "SELECT DATE_FORMAT(candle_dt, '%Y-%m-%d %H:%i:%s') " &
                "FROM tick30_candles_k150 " &
                $"WHERE code = {Sql(code)} AND tick_unit = 30 AND candle_dt >= {Sql(fromDate.ToString("yyyy-MM-dd 00:00:00", CultureInfo.InvariantCulture))} " &
                "ORDER BY candle_dt ASC;"

            Return ExecuteQuery(query).
                Select(Function(line) ParseDateTime(line)).
                Where(Function(dt) dt <> DateTime.MinValue).
                ToList()
        End Function

        Private Function LoadDailyCandles(code As String, fromDate As DateTime) As IReadOnlyList(Of LabCandle)
            Dim query As String =
                "SELECT DATE_FORMAT(candle_date, '%Y-%m-%d'), open, high, low, close, volume " &
                "FROM daily_candles_k150 " &
                $"WHERE code = {Sql(code)} AND candle_date >= {Sql(fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))} " &
                "ORDER BY candle_date ASC;"

            Dim result As New List(Of LabCandle)()
            For Each line In ExecuteQuery(query)
                Dim cols As String() = SplitColumns(line, 6)
                If cols Is Nothing Then Continue For

                Dim dt As DateTime = ParseDateTime(cols(0))
                If dt = DateTime.MinValue Then Continue For

                result.Add(New LabCandle With {
                    .Time = dt,
                    .Open = SafeDouble(cols(1)),
                    .High = SafeDouble(cols(2)),
                    .Low = SafeDouble(cols(3)),
                    .Close = SafeDouble(cols(4)),
                    .Volume = SafeDouble(cols(5))
                })
            Next
            Return result
        End Function

        Private Function LoadMinuteCandles(code As String, fromDate As DateTime) As IReadOnlyList(Of LabCandle)
            Dim query As String =
                "SELECT DATE_FORMAT(candle_dt, '%Y-%m-%d %H:%i:%s'), open, high, low, close, volume " &
                "FROM minute_candles_k150 " &
                $"WHERE code = {Sql(code)} AND timeframe_min = 1 AND candle_dt >= {Sql(fromDate.ToString("yyyy-MM-dd 00:00:00", CultureInfo.InvariantCulture))} " &
                "ORDER BY candle_dt ASC;"

            Dim result As New List(Of LabCandle)()
            For Each line In ExecuteQuery(query)
                Dim cols As String() = SplitColumns(line, 6)
                If cols Is Nothing Then Continue For

                Dim dt As DateTime = ParseDateTime(cols(0))
                If dt = DateTime.MinValue Then Continue For

                result.Add(New LabCandle With {
                    .Time = dt,
                    .Open = SafeDouble(cols(1)),
                    .High = SafeDouble(cols(2)),
                    .Low = SafeDouble(cols(3)),
                    .Close = SafeDouble(cols(4)),
                    .Volume = SafeDouble(cols(5))
                })
            Next
            Return result
        End Function

        Private Shared Function ResampleMinuteCandles(source As IReadOnlyList(Of LabCandle), minuteUnit As Integer) As IReadOnlyList(Of LabCandle)
            If source Is Nothing OrElse source.Count = 0 OrElse minuteUnit <= 1 Then
                Return If(source, Array.Empty(Of LabCandle)())
            End If

            Dim result As New List(Of LabCandle)()
            Dim bucket As LabCandle = Nothing
            Dim bucketStart As DateTime = DateTime.MinValue

            For Each candle In source
                Dim currentBucket As New DateTime(candle.Time.Year,
                                                  candle.Time.Month,
                                                  candle.Time.Day,
                                                  candle.Time.Hour,
                                                  candle.Time.Minute - (candle.Time.Minute Mod minuteUnit),
                                                  0)

                If bucket Is Nothing OrElse currentBucket <> bucketStart Then
                    If bucket IsNot Nothing Then result.Add(bucket)
                    bucketStart = currentBucket
                    bucket = New LabCandle With {
                        .Time = bucketStart,
                        .Open = candle.Open,
                        .High = candle.High,
                        .Low = candle.Low,
                        .Close = candle.Close,
                        .Volume = candle.Volume
                    }
                Else
                    bucket.High = Math.Max(bucket.High, candle.High)
                    bucket.Low = Math.Min(bucket.Low, candle.Low)
                    bucket.Close = candle.Close
                    bucket.Volume += candle.Volume
                End If
            Next

            If bucket IsNot Nothing Then result.Add(bucket)
            Return result
        End Function

        Private Function ExecuteQuery(sql As String) As List(Of String)
            Dim psi As New ProcessStartInfo With {
                .FileName = _config.MySqlCliPath,
                .Arguments = BuildMysqlQueryArguments(_config, sql),
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8
            }

            Using process As Process = Process.Start(psi)
                If process Is Nothing Then
                    Throw New InvalidOperationException("mysql.exe could not be started for research DB query.")
                End If

                Dim stdOut As String = process.StandardOutput.ReadToEnd()
                Dim stdErr As String = process.StandardError.ReadToEnd()
                process.WaitForExit()

                If process.ExitCode <> 0 Then
                    Throw New InvalidOperationException("Research DB query failed: " & stdErr)
                End If

                Return stdOut.
                    Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries).
                    ToList()
            End Using
        End Function

        Private Shared Function BuildMysqlQueryArguments(config As ResearchDbMySqlConfig, sql As String) As String
            Dim args As New List(Of String) From {
                "--batch",
                "--raw",
                "--skip-column-names",
                $"--host={QuoteCli(config.Host)}",
                $"--port={config.Port.ToString(CultureInfo.InvariantCulture)}",
                $"--user={QuoteCli(config.UserName)}",
                $"--default-character-set={QuoteCli(config.Charset)}"
            }

            If Not String.IsNullOrWhiteSpace(config.Password) Then
                args.Add($"--password={QuoteCli(config.Password)}")
            End If

            args.Add(QuoteCli(config.DatabaseName))
            args.Add("-e")
            args.Add(QuoteCli(sql))
            Return String.Join(" ", args)
        End Function

        Private Shared Function LoadDbConfig() As ResearchDbMySqlConfig
            Dim configPath As String = ResolveDbConfigPath()
            If String.IsNullOrWhiteSpace(configPath) OrElse Not File.Exists(configPath) Then Return Nothing

            Dim json As String = File.ReadAllText(configPath, Encoding.UTF8)
            Dim config As ResearchDbMySqlConfig = JsonConvert.DeserializeObject(Of ResearchDbMySqlConfig)(json)
            If config Is Nothing Then Return Nothing

            If String.IsNullOrWhiteSpace(config.MySqlCliPath) Then config.MySqlCliPath = "mysql"
            If String.IsNullOrWhiteSpace(config.Host) Then config.Host = "127.0.0.1"
            If config.Port <= 0 Then config.Port = 3306
            If String.IsNullOrWhiteSpace(config.DatabaseName) Then config.DatabaseName = "strategy_research"
            If String.IsNullOrWhiteSpace(config.UserName) Then config.UserName = "root"
            If String.IsNullOrWhiteSpace(config.Charset) Then config.Charset = "utf8mb4"
            Return config
        End Function

        Private Shared Function ResolveDbConfigPath() As String
            Dim current = New DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
            For i As Integer = 0 To 5
                If current Is Nothing Then Exit For
                Dim candidate As String = Path.Combine(current.FullName, DbConfigFileName)
                If File.Exists(candidate) Then Return candidate
                current = current.Parent
            Next
            Return ""
        End Function

        Private Shared Function NormalizeTimeframe(timeframe As String) As String
            Dim normalized As String = If(timeframe, "").Trim().ToLowerInvariant()
            If normalized = "" Then Return "m1"
            If normalized = "daily" Then Return "d"
            Return normalized
        End Function

        Private Shared Function ResolveMinuteUnit(timeframe As String) As Integer
            Dim normalized = NormalizeTimeframe(timeframe)
            If normalized.StartsWith("m", StringComparison.OrdinalIgnoreCase) Then
                Dim value As Integer = 1
                If normalized.Length > 1 Then Integer.TryParse(normalized.Substring(1), value)
                Return Math.Max(1, value)
            End If
            Return 0
        End Function

        Private Shared Function NormalizeCode(symbol As String) As String
            Dim digits As String = New String(If(symbol, "").Where(Function(ch) Char.IsDigit(ch)).ToArray())
            If digits.Length > 6 Then digits = digits.Substring(digits.Length - 6)
            Return digits.PadLeft(6, "0"c)
        End Function

        Private Shared Function Sql(value As String) As String
            Return "'" & If(value, "").Replace("\", "\\").Replace("'", "''") & "'"
        End Function

        Private Shared Function QuoteCli(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return """"""
            Return """" & value.Replace("""", "\""") & """"
        End Function

        Private Shared Function SplitColumns(line As String, expectedCount As Integer) As String()
            Dim cols As String() = line.Split(ControlChars.Tab)
            If cols.Length < expectedCount Then Return Nothing
            Return cols
        End Function

        Private Shared Function ParseDateTime(value As String) As DateTime
            Dim dt As DateTime
            If DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Return dt
            If DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Return dt
            If DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Return dt
            Return DateTime.MinValue
        End Function

        Private Shared Function SafeDouble(value As String) As Double
            Dim parsed As Double
            If Double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, parsed) Then Return parsed
            If Double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, parsed) Then Return parsed
            Return 0
        End Function
    End Class
End Namespace
