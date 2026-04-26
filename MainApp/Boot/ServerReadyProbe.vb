Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.IO
Imports System.IO.Pipes
Imports [Shared]

Public NotInheritable Class ServerReadyProbe

    Private Const MaxMessageSize As Integer = 50 * 1024 * 1024

    Private Sub New()
    End Sub

    Public Shared Function WaitForReady(pipeName As String,
                                        readyFuncName As String,
                                        serverName As String,
                                        Optional timeoutMs As Integer = 10000,
                                        Optional retryDelayMs As Integer = 250) As Boolean
        Dim startedAt As DateTime = DateTime.Now
        Dim lastError As String = ""

        Do While (DateTime.Now - startedAt).TotalMilliseconds < timeoutMs
            Try
                Dim response As Msg = ProbeOnce(pipeName, readyFuncName, Math.Min(2000, timeoutMs))
                If response IsNot Nothing Then
                    Dim success As Boolean = response.Bool("success", True)
                    If success OrElse response.Has("connected") OrElse response.Has("isConnected") OrElse response.Has("status") Then
                        AppLogger.I.Info($"[{serverName}] READY 확인됨 ({pipeName}/{readyFuncName})", "Boot")
                        Return True
                    End If
                    lastError = response.Str("message", "READY 응답은 수신했지만 success=False")
                End If
            Catch ex As Exception
                lastError = ex.Message
            End Try

            System.Threading.Thread.Sleep(Math.Max(50, retryDelayMs))
        Loop

        AppLogger.I.Error($"[{serverName}] READY 확인 실패: {lastError}", "Boot")
        Return False
    End Function

    Private Shared Function ProbeOnce(pipeName As String,
                                      readyFuncName As String,
                                      connectTimeoutMs As Integer) As Msg
        Using stream As New NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None)
            stream.Connect(connectTimeoutMs)

            Dim request As New Msg("CALL")
            request("func") = readyFuncName
            request("_seq") = 1
            request("probe") = "ready"
            request("sentAt") = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")

            Dim data As Byte() = SimpleSerializer.Serialize(request)
            Dim lengthBytes As Byte() = BitConverter.GetBytes(data.Length)
            stream.Write(lengthBytes, 0, lengthBytes.Length)
            stream.Write(data, 0, data.Length)
            stream.Flush()

            Dim responseLengthBytes(3) As Byte
            If ReadExact(stream, responseLengthBytes, 4) < 4 Then
                Throw New IOException("READY 응답 길이를 읽지 못했습니다.")
            End If

            Dim responseLength As Integer = BitConverter.ToInt32(responseLengthBytes, 0)
            If responseLength <= 0 OrElse responseLength > MaxMessageSize Then
                Throw New IOException($"READY 응답 길이 오류: {responseLength}")
            End If

            Dim responseBytes(responseLength - 1) As Byte
            If ReadExact(stream, responseBytes, responseLength) < responseLength Then
                Throw New IOException("READY 응답 본문을 읽지 못했습니다.")
            End If

            Return SimpleSerializer.Deserialize(responseBytes)
        End Using
    End Function

    Private Shared Function ReadExact(stream As Stream, buffer As Byte(), count As Integer) As Integer
        Dim total As Integer = 0
        While total < count
            Dim readCount As Integer = stream.Read(buffer, total, count - total)
            If readCount <= 0 Then Return total
            total += readCount
        End While
        Return total
    End Function

End Class
