' ═══════════════════════════════════════════════════════════════
' StrategyPersistenceService.vb — 전략 데이터 저장/로드 서비스
' ═══════════════════════════════════════════════════════════════

Imports System.IO
Imports System.Runtime.Serialization.Json
Imports System.Collections.Generic
Imports MainApp.Models

Namespace Services
    Public Class StrategyPersistenceService
        Private Shared ReadOnly FilePath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strategies.json")

        Public Shared Sub SaveStrategies(strategies As List(Of StrategyDefinition))
            Try
                Dim serializer As New DataContractJsonSerializer(GetType(List(Of StrategyDefinition)))
                Using stream As New FileStream(FilePath, FileMode.Create)
                    serializer.WriteObject(stream, strategies)
                End Using
            Catch ex As Exception
                AppLogger.I.Error($"전략 저장 실패: {ex.Message}")
            End Try
        End Sub

        Public Shared Function LoadStrategies() As List(Of StrategyDefinition)
            If Not File.Exists(FilePath) Then Return New List(Of StrategyDefinition)()

            Try
                Dim serializer As New DataContractJsonSerializer(GetType(List(Of StrategyDefinition)))
                Using stream As New FileStream(FilePath, FileMode.Open)
                    Return DirectCast(serializer.ReadObject(stream), List(Of StrategyDefinition))
                End Using
            Catch
                Return New List(Of StrategyDefinition)()
            End Try
        End Function
    End Class
End Namespace
