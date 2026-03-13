Imports System
Imports System.Collections.Generic

Namespace Services
    Public Enum ResearchDbRunMode
        FullRebuild = 0
        RangeUpdate = 1
        DateUpdate = 2
        AutoUpdate = 3
    End Enum

    Public Class ResearchDbMySqlConfig
        Public Property Enabled As Boolean = False
        Public Property MySqlCliPath As String = "mysql"
        Public Property Host As String = "127.0.0.1"
        Public Property Port As Integer = 3306
        Public Property DatabaseName As String = "strategy_research"
        Public Property UserName As String = "root"
        Public Property Password As String = ""
        Public Property Charset As String = "utf8mb4"
    End Class

    Public Class ResearchDbJobSettings
        Public Property UniverseSourcePath As String = "e:\2026\infra\out\kosdaq150_upsert_20260313.sql"
        Public Property OutputRootPath As String = "e:\2026\infra\out\research_db"
        Public Property AutoRunEnabled As Boolean = False
        Public Property AutoRunTime As String = "15:50"
        Public Property ExportDailyCandles As Boolean = True
        Public Property ExportMinuteCandles As Boolean = True
        Public Property ExportTick30Candles As Boolean = True
        Public Property ExportMarketIndexes As Boolean = True
        Public Property LastAutoRunDate As String = ""
        Public Property BackfillStartDate As String = ""
        Public Property BackfillEndDate As String = ""
    End Class

    Public Class ResearchDbJobResult
        Public Property JobName As String = ""
        Public Property OutputPath As String = ""
        Public Property RowsWritten As Integer
        Public Property FailedCount As Integer
        Public Property Success As Boolean
        Public Property Message As String = ""
    End Class

    Public Class ResearchDbCheckpointEntry
        Public Property TradingDate As String = ""
        Public Property Stage As String = ""
        Public Property Status As String = "pending"
        Public Property Mode As String = ""
        Public Property LastCode As String = ""
        Public Property TotalCodes As Integer
        Public Property CompletedCodes As Integer
        Public Property FailedCodes As Integer
        Public Property UpdatedAt As String = ""
    End Class

    Public Class ResearchDbCheckpointState
        Public Property Entries As New List(Of ResearchDbCheckpointEntry)
    End Class
End Namespace
