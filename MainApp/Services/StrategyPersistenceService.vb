Imports System.IO
Imports System.Runtime.Serialization.Json
Imports System.Collections.Generic
Imports System.Linq
Imports MainApp.Models

Namespace Services
    Public Class StrategyPersistenceService
        Private Shared ReadOnly FilePath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strategies.json")

        Public Shared Sub SaveStore(store As StrategyStore)
            Try
                If store Is Nothing Then store = New StrategyStore()
                Dim serializer As New DataContractJsonSerializer(GetType(StrategyStore))
                Using stream As New FileStream(FilePath, FileMode.Create)
                    serializer.WriteObject(stream, store)
                End Using
            Catch ex As Exception
                AppLogger.I.Error($"전략 저장 실패: {ex.Message}")
            End Try
        End Sub

        Public Shared Function LoadStore() As StrategyStore
            If Not File.Exists(FilePath) Then Return New StrategyStore()

            Try
                Dim serializer As New DataContractJsonSerializer(GetType(StrategyStore))
                Using stream As New FileStream(FilePath, FileMode.Open)
                    Dim store = DirectCast(serializer.ReadObject(stream), StrategyStore)
                    If store Is Nothing Then store = New StrategyStore()
                    If store.Groups Is Nothing Then store.Groups = New List(Of StrategyGroup)()
                    If store.Strategies Is Nothing Then store.Strategies = New List(Of StrategyDefinition)()
                    Return NormalizeStore(store)
                End Using
            Catch
                Try
                    Dim oldSerializer As New DataContractJsonSerializer(GetType(List(Of StrategyDefinition)))
                    Using stream As New FileStream(FilePath, FileMode.Open)
                        Dim oldList = DirectCast(oldSerializer.ReadObject(stream), List(Of StrategyDefinition))
                        Dim migrated As New StrategyStore With {
                            .Groups = New List(Of StrategyGroup) From {
                                New StrategyGroup With {.GroupId = "default", .GroupName = "Default", .Description = "Migrated", .DisplayOrder = 0}
                            },
                            .Strategies = If(oldList, New List(Of StrategyDefinition)())
                        }
                        For i = 0 To migrated.Strategies.Count - 1
                            Dim s = migrated.Strategies(i)
                            If String.IsNullOrWhiteSpace(s.StrategyId) Then s.StrategyId = Guid.NewGuid().ToString("N")
                            If String.IsNullOrWhiteSpace(s.GroupId) Then s.GroupId = "default"
                            If s.DisplayOrder <= 0 Then s.DisplayOrder = i + 1
                            If s.Version <= 0 Then s.Version = 1
                        Next
                        Return NormalizeStore(migrated)
                    End Using
                Catch
                    Return New StrategyStore()
                End Try
            End Try
        End Function

        Public Shared Sub SaveStrategies(strategies As List(Of StrategyDefinition))
            Dim store = LoadStore()
            store.Strategies = If(strategies, New List(Of StrategyDefinition)())
            SaveStore(NormalizeStore(store))
        End Sub

        Public Shared Function LoadStrategies() As List(Of StrategyDefinition)
            Return LoadStore().Strategies
        End Function

        Private Shared Function NormalizeStore(store As StrategyStore) As StrategyStore
            If store Is Nothing Then store = New StrategyStore()
            If store.Groups Is Nothing Then store.Groups = New List(Of StrategyGroup)()
            If store.Strategies Is Nothing Then store.Strategies = New List(Of StrategyDefinition)()

            If store.Groups.Count = 0 Then
                store.Groups.Add(New StrategyGroup With {.GroupId = "default", .GroupName = "Default", .DisplayOrder = 0})
            End If

            Dim groupSet = New HashSet(Of String)(store.Groups.Select(Function(g) g.GroupId))
            For i = 0 To store.Strategies.Count - 1
                Dim s = store.Strategies(i)
                If String.IsNullOrWhiteSpace(s.StrategyId) Then s.StrategyId = Guid.NewGuid().ToString("N")
                If String.IsNullOrWhiteSpace(s.GroupId) OrElse Not groupSet.Contains(s.GroupId) Then s.GroupId = store.Groups(0).GroupId
                If s.Version <= 0 Then s.Version = 1
                If s.DisplayOrder <= 0 Then s.DisplayOrder = i + 1
            Next

            Return store
        End Function
    End Class
End Namespace
