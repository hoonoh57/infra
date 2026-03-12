Imports System
Imports StrategyCore.Models

Namespace StrategyLabApp
    Public Class StrategyLabCandidateRecord
        Public Property CandidateId As String = Guid.NewGuid().ToString("N")
        Public Property ParentCandidateId As String = ""
        Public Property StrategyLineId As String = ""
        Public Property StrategyVersionId As String = ""
        Public Property ParentVersionId As String = ""
        Public Property VersionTag As String = ""
        Public Property VersionType As StrategyVersionType = StrategyVersionType.Derived
        Public Property SourcePrompt As String = ""
        Public Property ChangeSummary As String = ""
        Public Property AverageReturnRate As Double
        Public Property SavedAt As DateTime = DateTime.Now
        Public Property Result As StrategyLabResult
    End Class

    Public Class StrategyLabSession
        Public Property SessionId As String = Guid.NewGuid().ToString("N")
        Public Property Title As String = ""
        Public Property Symbol As String = ""
        Public Property FromDate As DateTime
        Public Property TradeMode As TradeMode = TradeMode.Intraday
        Public Property TargetPercent As Double = 2.0R
        Public Property PromptText As String = ""
        Public Property HistoryText As String = ""
        Public Property BaselineResult As StrategyLabResult
        Public Property CandidateRecords As New List(Of StrategyLabCandidateRecord)()
        Public Property LastResult As StrategyLabResult
        Public Property ActiveCandidateId As String = ""
        Public Property RecommendedCandidateId As String = ""
        Public Property PromotionCandidateId As String = ""
        Public Property SavedAt As DateTime = DateTime.Now
    End Class
End Namespace
