' ═══════════════════════════════════════════════════════════════
' MessageBus.vb — 싱글톤 Pub/Sub 허브
' ═══════════════════════════════════════════════════════════════
' 수정 금지. 동일 프로세스 내 모든 모듈이 이 버스를 통해 통신.
' ═══════════════════════════════════════════════════════════════

Imports System.Threading

Public Class MessageBus

    ' ─── 싱글톤 ───
    Private Shared _instance As MessageBus
    Private Shared ReadOnly _singletonLock As New Object()

    Public Shared ReadOnly Property I As MessageBus
        Get
            If _instance Is Nothing Then
                SyncLock _singletonLock
                    If _instance Is Nothing Then
                        _instance = New MessageBus()
                    End If
                End SyncLock
            End If
            Return _instance
        End Get
    End Property

    ' ─── 내부 저장소 ───
    Private ReadOnly _subs As New Dictionary(Of String, List(Of Action(Of Msg)))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _allSubs As New List(Of Action(Of Msg))()
    Private ReadOnly _lock As New ReaderWriterLockSlim()
    Private _uiContext As SynchronizationContext

    Private Sub New()
    End Sub

    ''' <summary>UI 스레드 컨텍스트 설정 (Application.Run 전에 호출)</summary>
    Public Sub SetUIContext(ctx As SynchronizationContext)
        _uiContext = ctx
    End Sub

    ''' <summary>토픽 구독</summary>
    Public Sub [On](topic As String, handler As Action(Of Msg))
        _lock.EnterWriteLock()
        Try
            If Not _subs.ContainsKey(topic) Then
                _subs(topic) = New List(Of Action(Of Msg))()
            End If
            _subs(topic).Add(handler)
        Finally
            _lock.ExitWriteLock()
        End Try
    End Sub

    ''' <summary>토픽 구독 해제</summary>
    Public Sub Off(topic As String, handler As Action(Of Msg))
        _lock.EnterWriteLock()
        Try
            If _subs.ContainsKey(topic) Then
                _subs(topic).Remove(handler)
            End If
        Finally
            _lock.ExitWriteLock()
        End Try
    End Sub

    ''' <summary>모든 토픽 수신 (디버깅용)</summary>
    Public Sub OnAll(handler As Action(Of Msg))
        _lock.EnterWriteLock()
        Try
            _allSubs.Add(handler)
        Finally
            _lock.ExitWriteLock()
        End Try
    End Sub

    ''' <summary>메시지 발행</summary>
    Public Sub Emit(msg As Msg)
        Dim handlers As List(Of Action(Of Msg)) = Nothing
        Dim allHandlers As Action(Of Msg)() = Nothing

        _lock.EnterReadLock()
        Try
            If _subs.ContainsKey(msg.Topic) Then
                handlers = New List(Of Action(Of Msg))(_subs(msg.Topic))
            End If
            If _allSubs.Count > 0 Then
                allHandlers = _allSubs.ToArray()
            End If
        Finally
            _lock.ExitReadLock()
        End Try

        If handlers IsNot Nothing Then
            For Each h In handlers
                Try
                    h(msg)
                Catch ex As Exception
                    EmitError($"[Bus] Handler error on '{msg.Topic}': {ex.Message}")
                End Try
            Next
        End If

        If allHandlers IsNot Nothing Then
            For Each h In allHandlers
                Try
                    h(msg)
                Catch
                End Try
            Next
        End If
    End Sub

    ''' <summary>간편 발행 (토픽 + 키/값 쌍)</summary>
    Public Sub Emit(topic As String, ParamArray pairs() As Object)
        Emit(New Msg(topic, pairs))
    End Sub

    ''' <summary>UI 스레드에서 발행</summary>
    Public Sub EmitOnUI(msg As Msg)
        If _uiContext IsNot Nothing Then
            _uiContext.Post(Sub(state) Emit(msg), Nothing)
        Else
            Emit(msg)
        End If
    End Sub

    ''' <summary>UI 스레드에서 간편 발행</summary>
    Public Sub EmitOnUI(topic As String, ParamArray pairs() As Object)
        EmitOnUI(New Msg(topic, pairs))
    End Sub

    Private Sub EmitError(text As String)
        Try
            Dim m As New Msg(Topics.SYS_ERROR)
            m("text") = text
            ' 재귀 방지: 에러 토픽 핸들러에서 다시 에러가 나면 무시
            Dim handlers As List(Of Action(Of Msg)) = Nothing
            _lock.EnterReadLock()
            Try
                If _subs.ContainsKey(Topics.SYS_ERROR) Then
                    handlers = New List(Of Action(Of Msg))(_subs(Topics.SYS_ERROR))
                End If
            Finally
                _lock.ExitReadLock()
            End Try
            If handlers IsNot Nothing Then
                For Each h In handlers
                    Try : h(m) : Catch : End Try
                Next
            End If
        Catch
        End Try
    End Sub
End Class
