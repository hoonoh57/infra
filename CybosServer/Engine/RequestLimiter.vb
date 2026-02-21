' ═══════════════════════════════════════════════════════════════
' RequestLimiter.vb — CybosPlus 요청 횟수 제한 관리
' ═══════════════════════════════════════════════════════════════

Imports CPUTILLib
Imports System.Threading

Public Class RequestLimiter

    Private ReadOnly _minIntervalMs As Integer = 200

    ''' <summary>남은 요청 횟수가 0이면 대기</summary>
    Public Sub WaitIfNeeded()
        Try
            Dim cpCybos As New CpCybos()
            Dim remain = CInt(cpCybos.GetLimitRemainCount(1))

            If remain <= 0 Then
                Dim waitMs = CInt(cpCybos.GetLimitRemainTime(1))
                If waitMs > 0 Then
                    Thread.Sleep(waitMs + 100)  ' 안전 마진
                Else
                    Thread.Sleep(1000)
                End If
            Else
                Thread.Sleep(_minIntervalMs)
            End If
        Catch
            Thread.Sleep(1000)
        End Try
    End Sub

    Public Function GetRemainCount() As Integer
        Try
            Dim cpCybos As New CpCybos()
            Return CInt(cpCybos.GetLimitRemainCount(1))
        Catch
            Return 0
        End Try
    End Function

End Class
