' ═══════════════════════════════════════════════════════════════
' PipeServer.vb — Named Pipe 서버 (32‑bit 측)
' ═══════════════════════════════════════════════════════════════
' 수정 금지. 다중 클라이언트 지원, 자동 재대기.
' ═══════════════════════════════════════════════════════════════

Imports System.IO
Imports System.IO.Pipes
Imports System.Threading

Public Class PipeServer

    Public Event MessageReceived(msg As Msg)
    Public Event ClientConnected()
    Public Event ClientDisconnected()
    Public Event ErrorOccurred(message As String)

    Private ReadOnly _pipeName As String
    Private _running As Boolean = False
    Private _pipeStream As NamedPipeServerStream
    Private ReadOnly _writeLock As New Object()
    Private _listenThread As Thread

    Private Const MAX_MSG_SIZE As Integer = 50 * 1024 * 1024  ' 50 MB

    Public Sub New(pipeName As String)
        _pipeName = pipeName
    End Sub

    Public ReadOnly Property IsConnected As Boolean
        Get
            Return _pipeStream IsNot Nothing AndAlso _pipeStream.IsConnected
        End Get
    End Property

    Public Sub Start()
        _running = True
        _listenThread = New Thread(AddressOf ListenLoop)
        _listenThread.IsBackground = True
        _listenThread.Name = $"PipeServer_{_pipeName}"
        _listenThread.Start()
    End Sub

    Public Sub [Stop]()
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

    Private Sub ListenLoop()
        While _running
            Try
                _pipeStream = New NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous)

                _pipeStream.WaitForConnection()
                RaiseEvent ClientConnected()

                ReadLoop()

            Catch ex As Exception
                If _running Then
                    RaiseEvent ErrorOccurred($"Listen error: {ex.Message}")
                End If
            Finally
                RaiseEvent ClientDisconnected()
                Try : _pipeStream?.Close() : Catch : End Try
                _pipeStream = Nothing
            End Try

            If _running Then Thread.Sleep(500)
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
