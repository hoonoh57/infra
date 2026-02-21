' ═══════════════════════════════════════════════════════════════
' AppLogger.vb — 체계적 로깅 (파일 + UI + MessageBus 통합)
' ═══════════════════════════════════════════════════════════════
' 모든 로그는 이 클래스를 통해 기록됨.
' ① 콘솔 출력
' ② 파일 기록 (일별 자동 분리)
' ③ MessageBus 발행 → LogForm에서 실시간 표시
' ═══════════════════════════════════════════════════════════════

Imports System.IO
Imports [Shared]

Public Class AppLogger

    ' ─── 싱글톤 ───
    Private Shared _instance As AppLogger
    Private Shared ReadOnly _singletonLock As New Object()

    Public Shared ReadOnly Property I As AppLogger
        Get
            If _instance Is Nothing Then
                SyncLock _singletonLock
                    If _instance Is Nothing Then
                        _instance = New AppLogger()
                    End If
                End SyncLock
            End If
            Return _instance
        End Get
    End Property

    ' ─── 설정 ───
    Private ReadOnly _logDir As String
    Private ReadOnly _fileLock As New Object()

    Public Enum LogLevel
        DEBUG = 0
        INFO = 1
        WARN = 2
        [ERROR] = 3
        TEST = 10
        TRADE = 11
        COMM = 12
    End Enum

    Private Sub New()
        _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs")
        If Not Directory.Exists(_logDir) Then Directory.CreateDirectory(_logDir)
    End Sub

    ' ─── 공개 메서드 ───

    Public Sub Debug(message As String, Optional source As String = "")
        Write(LogLevel.DEBUG, message, source)
    End Sub

    Public Sub Info(message As String, Optional source As String = "")
        Write(LogLevel.INFO, message, source)
    End Sub

    Public Sub Warn(message As String, Optional source As String = "")
        Write(LogLevel.WARN, message, source)
    End Sub

    Public Sub [Error](message As String, Optional source As String = "")
        Write(LogLevel.ERROR, message, source)
    End Sub

    Public Sub Test(message As String, Optional source As String = "")
        Write(LogLevel.TEST, message, source)
    End Sub

    Public Sub Trade(message As String, Optional source As String = "")
        Write(LogLevel.TRADE, message, source)
    End Sub

    Public Sub Comm(message As String, Optional source As String = "")
        Write(LogLevel.COMM, message, source)
    End Sub

    ' ─── 핵심 기록 ───

    Private Sub Write(level As LogLevel, message As String, source As String)
        Dim timestamp = DateTime.Now
        Dim levelStr = level.ToString().PadRight(5)
        Dim srcStr = If(String.IsNullOrEmpty(source), "", $"[{source}] ")
        Dim line = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} [{levelStr}] {srcStr}{message}"

        ' ① 콘솔
        Dim prevColor = Console.ForegroundColor
        Console.ForegroundColor = GetColor(level)
        Console.WriteLine(line)
        Console.ForegroundColor = prevColor

        ' ② 파일 (일별)
        Try
            SyncLock _fileLock
                Dim filePath = Path.Combine(_logDir, $"{timestamp:yyyyMMdd}.log")
                File.AppendAllText(filePath, line & Environment.NewLine)
            End SyncLock
        Catch
            ' 파일 기록 실패는 무시 (UI는 계속 동작)
        End Try

        ' ③ MessageBus → LogForm
        Try
            Dim topic = LevelToTopic(level)
            Dim m As New Msg(topic)
            m("time") = timestamp.ToString("HH:mm:ss.fff")
            m("level") = level.ToString()
            m("source") = source
            m("text") = message
            m("fullLine") = line
            MessageBus.I.Emit(m)
        Catch
        End Try
    End Sub

    Private Shared Function GetColor(level As LogLevel) As ConsoleColor
        Select Case level
            Case LogLevel.ERROR : Return ConsoleColor.Red
            Case LogLevel.WARN : Return ConsoleColor.Yellow
            Case LogLevel.TEST : Return ConsoleColor.Cyan
            Case LogLevel.TRADE : Return ConsoleColor.Green
            Case LogLevel.COMM : Return ConsoleColor.Magenta
            Case LogLevel.DEBUG : Return ConsoleColor.Gray
            Case Else : Return ConsoleColor.White
        End Select
    End Function

    Private Shared Function LevelToTopic(level As LogLevel) As String
        Select Case level
            Case LogLevel.DEBUG : Return Topics.LOG_DEBUG
            Case LogLevel.INFO : Return Topics.LOG_INFO
            Case LogLevel.WARN : Return Topics.LOG_WARN
            Case LogLevel.ERROR : Return Topics.LOG_ERROR
            Case LogLevel.TEST : Return Topics.LOG_TEST
            Case LogLevel.TRADE : Return Topics.LOG_TRADE
            Case LogLevel.COMM : Return Topics.LOG_COMM
            Case Else : Return Topics.LOG_INFO
        End Select
    End Function

End Class
