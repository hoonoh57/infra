Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports Newtonsoft.Json
Imports StrategyCore.Models

Namespace StrategyCore.Services
    Public Class StrategyPackageBuilder
        Public Function BuildPackage(definition As StrategyDefinition,
                                     report As StrategyBaselineReport,
                                     promotedBy As String) As StrategyPackage
            Dim pkg As New StrategyPackage With {
                .StrategyId = definition.StrategyId,
                .Name = definition.Name,
                .Version = definition.Version,
                .StrategyType = definition.TradeMode.ToString(),
                .CreatedAt = DateTime.Now,
                .PromotedAt = DateTime.Now,
                .PromotedBy = promotedBy,
                .Prompt = definition.Prompt,
                .EntrySummary = definition.EntrySummary,
                .ExitSummary = definition.ExitSummary,
                .CostModel = definition.CostModel,
                .ExecutionConstraints = definition.Constraints,
                .ValidationSummary = New StrategyValidationSummary With {
                    .AverageReturnRate = report.AverageReturnRate,
                    .PrimaryMetricValue = report.PrimaryMetric,
                    .SecondaryMetricValue = report.SecondaryMetric,
                    .SampleCount = report.SampleCount,
                    .ValidatedFrom = If(report.Candles.Count > 0, report.Candles(0).Time, DateTime.MinValue),
                    .ValidatedTo = If(report.Candles.Count > 0, report.Candles(report.Candles.Count - 1).Time, DateTime.MinValue)
                }
            }

            pkg.Timeframes.AddRange(definition.Timeframes)
            pkg.IndicatorSet.AddRange(definition.Indicators)
            pkg.Hash = ComputeHash(pkg)
            Return pkg
        End Function

        Public Function SavePackage(pkg As StrategyPackage, folderPath As String) As String
            Directory.CreateDirectory(folderPath)
            Dim fileName = $"{SanitizeFileName(pkg.Name)}_v{pkg.Version}.strategy.json"
            Dim fullPath = Path.Combine(folderPath, fileName)
            File.WriteAllText(fullPath, JsonConvert.SerializeObject(pkg, Formatting.Indented), Encoding.UTF8)
            Return fullPath
        End Function

        Private Shared Function SanitizeFileName(name As String) As String
            Dim result = If(name, "strategy")
            For Each ch In Path.GetInvalidFileNameChars()
                result = result.Replace(ch, "_"c)
            Next
            Return result.Replace(" "c, "_"c)
        End Function

        Private Shared Function ComputeHash(pkg As StrategyPackage) As String
            Dim clone = JsonConvert.DeserializeObject(Of StrategyPackage)(JsonConvert.SerializeObject(pkg))
            clone.Hash = ""
            Dim json = JsonConvert.SerializeObject(clone, Formatting.None)
            Using sha = SHA256.Create()
                Dim bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json))
                Return BitConverter.ToString(bytes).Replace("-", "")
            End Using
        End Function
    End Class

    Public Class StrategyPackageValidator
        Public Function Validate(pkg As StrategyPackage, ByRef errors As List(Of String)) As Boolean
            errors = New List(Of String)()
            If pkg Is Nothing Then
                errors.Add("패키지가 비어 있습니다.")
                Return False
            End If

            If String.IsNullOrWhiteSpace(pkg.PackageVersion) Then errors.Add("PackageVersion 누락")
            If String.IsNullOrWhiteSpace(pkg.StrategyId) Then errors.Add("StrategyId 누락")
            If String.IsNullOrWhiteSpace(pkg.Name) Then errors.Add("Name 누락")
            If pkg.ExecutionConstraints Is Nothing OrElse Not pkg.ExecutionConstraints.SingleTradeOnly Then errors.Add("SingleTradeOnly 제약 필요")
            If pkg.Timeframes Is Nothing OrElse pkg.Timeframes.Count = 0 Then errors.Add("Timeframes 누락")
            If pkg.ValidationSummary Is Nothing Then errors.Add("ValidationSummary 누락")

            For Each timeframe In If(pkg.Timeframes, New List(Of String)())
                If Not IsAllowedTimeframe(pkg.StrategyType, timeframe) Then
                    errors.Add($"허용되지 않은 타임프레임: {timeframe}")
                End If
            Next

            Return errors.Count = 0
        End Function

        Private Shared Function IsAllowedTimeframe(strategyType As String, timeframe As String) As Boolean
            Dim intraday = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"m1", "m3", "m5", "T30", "T60", "T120"}
            Dim swing = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"m15", "m30", "m60"}
            If String.Equals(strategyType, TradeMode.Swing.ToString(), StringComparison.OrdinalIgnoreCase) Then
                Return swing.Contains(timeframe)
            End If
            Return intraday.Contains(timeframe)
        End Function
    End Class

    Public Class PromotionManifestBuilder
        Public Function BuildManifest(pkg As StrategyPackage, report As StrategyBaselineReport, approvedBy As String, notes As String) As PromotionManifest
            Return New PromotionManifest With {
                .StrategyId = pkg.StrategyId,
                .Version = pkg.Version,
                .ApprovedBy = approvedBy,
                .ApprovedAt = DateTime.Now,
                .PrimaryMetric = report.PrimaryMetric,
                .SecondaryMetric = report.SecondaryMetric,
                .Notes = notes,
                .PackageHash = pkg.Hash
            }
        End Function
    End Class

    Public Class StrategyLabFacade
        Private ReadOnly _compiler As New StrategyPromptCompiler()
        Private ReadOnly _validator As New StrategyPromptValidationService()
        Private ReadOnly _diagnoser As New StrategyDiagnosisService()
        Private ReadOnly _improver As New StrategyImprovementSuggestionService()
        Private ReadOnly _evaluator As BaselineEvaluationService

        Public Sub New(Optional candleProvider As ICandleDataProvider = Nothing,
                       Optional auxDataProvider As IStrategyIndicatorAuxDataProvider = Nothing)
            _evaluator = New BaselineEvaluationService(candleProvider, auxDataProvider)
        End Sub

        Public Function EvaluatePrompt(prompt As String,
                                       mode As TradeMode,
                                       symbol As String,
                                       fromDate As DateTime,
                                       targetProfitRate As Double,
                                       barCount As Integer,
                                       costModel As CostModel) As StrategyLabResult
            Dim definition = _compiler.Compile(prompt, mode, targetProfitRate, costModel)
            Dim draft = _compiler.CreateDraft(prompt, mode, targetProfitRate)
            Dim validation = _validator.Validate(prompt, mode, targetProfitRate)
            Dim report = _evaluator.Evaluate(definition, symbol, fromDate, barCount)
            draft.StrategyId = definition.StrategyId
            draft.Name = definition.Name

            Dim diagnosis = _diagnoser.Diagnose(definition, report)
            Return New StrategyLabResult With {
                .Draft = draft,
                .Definition = definition,
                .Report = report,
                .PromptValidation = validation,
                .Diagnosis = diagnosis,
                .ImprovementPlan = _improver.BuildPlan(definition, report, diagnosis)
            }
        End Function

        Public Function ValidatePrompt(prompt As String, mode As TradeMode, targetProfitRate As Double) As PromptValidationReport
            Return _validator.Validate(prompt, mode, targetProfitRate)
        End Function
    End Class
End Namespace
