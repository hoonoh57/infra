Imports System
Imports System.IO
Imports Newtonsoft.Json

Namespace StrategyLabApp
    Public Class StrategyLabSessionService
        Private ReadOnly _sessionFolder As String

        Public Sub New()
            _sessionFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sessions")
            Directory.CreateDirectory(_sessionFolder)
        End Sub

        Public Function SaveSession(session As StrategyLabSession) As String
            If session Is Nothing Then Throw New ArgumentNullException(NameOf(session))
            session.SavedAt = DateTime.Now
            If String.IsNullOrWhiteSpace(session.Title) Then
                session.Title = $"session_{session.SavedAt:yyyyMMdd_HHmmss}"
            End If

            Dim fileName = SanitizeFileName(session.Title) & ".lab.json"
            Dim fullPath = Path.Combine(_sessionFolder, fileName)
            File.WriteAllText(fullPath, JsonConvert.SerializeObject(session, Formatting.Indented))
            Return fullPath
        End Function

        Public Function LoadLatestSession() As StrategyLabSession
            Dim latest = New DirectoryInfo(_sessionFolder).
                GetFiles("*.lab.json").
                OrderByDescending(Function(file) file.LastWriteTimeUtc).
                FirstOrDefault()
            If latest Is Nothing Then Return Nothing
            Return JsonConvert.DeserializeObject(Of StrategyLabSession)(File.ReadAllText(latest.FullName))
        End Function

        Private Shared Function SanitizeFileName(name As String) As String
            Dim result = If(name, "session")
            For Each ch In Path.GetInvalidFileNameChars()
                result = result.Replace(ch, "_"c)
            Next
            Return result.Replace(" "c, "_"c)
        End Function
    End Class
End Namespace
