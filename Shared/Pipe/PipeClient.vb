' ═══════════════════════════════════════════════════════════════
' PipeClient.vb — Named Pipe 클라이언트 (64‑bit 측)
' ═══════════════════════════════════════════════════════════════
' 수정 금지. 자동 재연결 포함.
' ═══════════════════════════════════════════════════════════════

Imports System.IO
Imports System.IO.Pipes
Imports System.Threading

Public Class PipeClient

    Public Event MessageReceived(msg As Msg)
    Public Event Connected()
    Public Event Disconnected()
    Public Event ErrorOccurred(message As String)

    Private ReadOnly _pipeName As String
    Private _pipeStream As NamedPipeClientStream
    Private _running As Boolean = False
    Private ReadOnly _writeLock As New Object()
    Private _connectThread As Thread
    Private _isConnected As Boolean = False

    Private Const RECONNECT_DELAY_MS As Integer = 3000
    Private Const CONNECT_TIMEOUT_MS As Integer = 5000
    Private Const MAX_MSG_SIZE As Integer = 50 * 1024 * 1024

    Public Sub New(pipeName As String)
        _pipeName = pipeName
    End Sub

    Public ReadOnly Property IsConnected As Boolean
        Get
            Return _isConnected
        End Get
    End Property

    Public Sub Connect()
        _running = True
        _connectThread = New Thread(AddressOf ConnectLoop)
        _connectThread.IsBackground = True
        _connectThread.Name = $"PipeClient_{_pipeName}"
        _connectThread.Start()
    End Sub

    Public Sub Disconnect()
        _running = False
        Try
            _pipeStream?.Close()
        Catch
        End Try
    End Sub

    Public Sub Send(msg As Msg)
        SyncLock _writeLock
            Try
                If _pipeStream IsNot Nothing AndAlso _pipeStream.IsConnected Then
                    Dim data = SimpleSerializer.Serialize(msg)
                    Dim lenBytes = BitConverter.GetBytes(data.Length)
                    _pipeStream.Write(lenBytes, 0, 4)
                    _pipeStream.Write(data, 0, data.Length)
                    _pipeStream.Flush()
                End If
            Catch ex As Exception
                RaiseEvent ErrorOccurred($"Send error: {ex.Message}")
            End Try
        End SyncLock
    End Sub

    Private Sub ConnectLoop()
        While _running
            Try
                _pipeStream = New NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
                _pipeStream.Connect(CONNECT_TIMEOUT_MS)
                _isConnected = True
                RaiseEvent Connected()

                ReadLoop()

            Catch ex As TimeoutException
                ' 서버가 아직 안 떴음 → 재시도
            Catch ex As Exception
                If _running Then
                    RaiseEvent ErrorOccurred($"Connect error: {ex.Message}")
                End If
            Finally
                _isConnected = False
                RaiseEvent Disconnected()
                Try : _pipeStream?.Close() : Catch : End Try
                _pipeStream = Nothing
            End Try

            If _running Then Thread.Sleep(RECONNECT_DELAY_MS)
        End While
    End Sub

    Private Sub ReadLoop()
        Dim lenBuf(3) As Byte

        While _running AndAlso _pipeStream.IsConnected
            Try
                Dim bytesRead = ReadExact(_pipeStream, lenBuf, 4)
                If bytesRead < 4 Then Exit While

                Dim dataLen = BitConverter.ToInt32(lenBuf, 0)
                If dataLen <= 0 OrElse dataLen > MAX_MSG_SIZE Then
                    RaiseEvent ErrorOccurred($"Invalid message length: {dataLen}")
                    Exit While
                End If

                Dim dataBuf(dataLen - 1) As Byte
                bytesRead = ReadExact(_pipeStream, dataBuf, dataLen)
                If bytesRead < dataLen Then Exit While

                Dim msg = SimpleSerializer.Deserialize(dataBuf)
                RaiseEvent MessageReceived(msg)

            Catch ex As IOException
                Exit While
            Catch ex As Exception
                RaiseEvent ErrorOccurred($"Read error: {ex.Message}")
                Exit While
            End Try
        End While
    End Sub

    Private Shared Function ReadExact(stream As Stream, buffer As Byte(), count As Integer) As Integer
        Dim total As Integer = 0
        While total < count
            Dim n = stream.Read(buffer, total, count - total)
            If n = 0 Then Return total
            total += n
        End While
        Return total
    End Function

End Class
