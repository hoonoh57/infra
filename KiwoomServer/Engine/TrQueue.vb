' ═══════════════════════════════════════════════════════════════
' TrQueue.vb — 키움 TR 요청 제한 큐 (1초 5회 제한 준수)
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Concurrent
Imports System.Threading

Public Class TrQueue

    Public Class QueueItem
        Public Property Action As Action
        Public Property Description As String = ""
    End Class

    Private ReadOnly _queue As New ConcurrentQueue(Of QueueItem)()
    Private ReadOnly _timer As Timer
    Private ReadOnly _recentSends As New List(Of Long)()
    Private ReadOnly _lock As New Object()

    Private Const MAX_PER_SECOND As Integer = 4  ' 안전 마진: 5회 중 4회만 사용
    Private Const INTERVAL_MS As Integer = 250   ' 250ms 간격 체크

    Public Sub New()
        _timer = New Timer(AddressOf OnTimer, Nothing, INTERVAL_MS, INTERVAL_MS)
    End Sub

    Public Sub Enqueue(action As Action, Optional description As String = "")
        _queue.Enqueue(New QueueItem With {.Action = action, .Description = description})
    End Sub

    Public ReadOnly Property PendingCount As Integer
        Get
            Return _queue.Count
        End Get
    End Property

    Private Sub OnTimer(state As Object)
        SyncLock _lock
            ' 1초 이전 기록 제거
            Dim now = DateTime.Now.Ticks
            Dim oneSecAgo = now - TimeSpan.TicksPerSecond
            _recentSends.RemoveAll(Function(t) t < oneSecAgo)

            ' 여유가 있으면 큐에서 꺼내서 실행
            While _recentSends.Count < MAX_PER_SECOND
                Dim item As QueueItem = Nothing
                If Not _queue.TryDequeue(item) Then Exit While

                _recentSends.Add(now)
                Try
                    item.Action?.Invoke()
                Catch ex As Exception
                    System.Diagnostics.Debug.Print($"[TrQueue] Error: {ex.Message} ({item.Description})")
                End Try
            End While
        End SyncLock
    End Sub

    Public Sub Dispose()
        _timer?.Dispose()
    End Sub

End Class
