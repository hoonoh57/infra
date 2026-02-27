Imports System.Text.RegularExpressions
Imports System.Collections.Generic
Imports System.Linq
Imports MainApp.Models

Namespace Services
    Public Class StrategyBridge
        Public Shared Function CreateFromNaturalLanguage(nlPrompt As String) As StrategyDefinition
            If String.IsNullOrWhiteSpace(nlPrompt) Then Return Nothing

            Dim text As String = NormalizeText(nlPrompt)
            Dim parts = SplitBuySell(text)

            Dim buyConditions = BuildConditions(parts.BuyText, True)
            Dim sellConditions = BuildConditions(parts.SellText, False)

            ' If user specified supertrend trend-follow but omitted sell part,
            ' provide a default exit to avoid never-ending position.
            If sellConditions.Count = 0 AndAlso MentionsSuperTrend(parts.BuyText) Then
                sellConditions.Add(New ConditionCell("S_AUTO_1", "Price CrossDown SuperTrend", "Price", ComparisonOperator.CrossDown, "SuperTrend"))
            End If

            If buyConditions.Count = 0 AndAlso sellConditions.Count = 0 Then Return Nothing

            Dim strategyName As String = "AI_Custom_" & DateTime.Now.ToString("HHmmss")
            Dim buyGate As New LogicGate("EntryGate", LogicalOperator.AND, buyConditions)
            Dim sellGate As New LogicGate("ExitGate", LogicalOperator.OR, sellConditions)

            Dim strategy As New StrategyDefinition(
                strategyName,
                "NL strategy: " & nlPrompt,
                New List(Of LogicGate) From {buyGate},
                New List(Of LogicGate) From {sellGate},
                nlPrompt)

            strategy.RequiredDataDays = 0
            Return strategy
        End Function

        Private Shared Function NormalizeText(input As String) As String
            Dim t As String = If(input, "")
            t = t.Replace(vbCr, " ").Replace(vbLf, " ").Replace("\t", " ")
            t = Regex.Replace(t, "\s+", " ")
            Return t.Trim()
        End Function

        Private Structure PromptParts
            Public BuyText As String
            Public SellText As String
        End Structure

        Private Shared Function SplitBuySell(text As String) As PromptParts
            Dim result As New PromptParts With {.BuyText = text, .SellText = ""}
            If String.IsNullOrWhiteSpace(text) Then Return result

            Dim sellKeys = New String() {"매도", "청산", "익절", "손절", "sell", "exit"}
            Dim pos As Integer = -1
            For Each k In sellKeys
                Dim p = text.IndexOf(k, StringComparison.OrdinalIgnoreCase)
                If p >= 0 AndAlso (pos = -1 OrElse p < pos) Then pos = p
            Next

            If pos > 0 Then
                result.BuyText = text.Substring(0, pos).Trim()
                result.SellText = text.Substring(pos).Trim()
            End If

            Return result
        End Function

        Private Shared Function BuildConditions(text As String, isBuy As Boolean) As List(Of ConditionCell)
            Dim conditions As New List(Of ConditionCell)
            If String.IsNullOrWhiteSpace(text) Then Return conditions

            Dim source As String = text.ToLowerInvariant()
            Dim seq As Integer = 1
            Dim prefix As String = If(isBuy, "B", "S")

            ' 1) SuperTrend templates
            ParseSuperTrend(source, isBuy, conditions, prefix, seq)

            ' 2) Tick intensity / trade strength threshold
            ParseThreshold(source, conditions, prefix, seq,
                           New String() {"틱강도", "체결강도", "tick intensity", "tick"},
                           "TICK_RAT")

            ' 3) Program net buy threshold
            ParseThreshold(source, conditions, prefix, seq,
                           New String() {"프로그램순매수", "프로그램 매매", "program net", "program"},
                           "PROGRAM_NET")

            ' 4) RSI threshold
            ParseRsi(source, conditions, prefix, seq)

            ' 5) JMA relation/cross
            ParseJma(source, isBuy, conditions, prefix, seq)

            ' 6) Risk management from sell side
            If Not isBuy Then
                ParseRisk(source, conditions, prefix, seq)
            End If

            Return Deduplicate(conditions)
        End Function

        Private Shared Sub ParseSuperTrend(source As String,
                                           isBuy As Boolean,
                                           conditions As List(Of ConditionCell),
                                           prefix As String,
                                           ByRef seq As Integer)
            If Not MentionsSuperTrend(source) Then Return

            Dim upWords = New String() {"상향", "상승", "위", "above", "long"}
            Dim downWords = New String() {"하향", "하락", "아래", "below", "short", "이탈"}
            Dim breakoutWords = New String() {"돌파", "크로스", "cross", "crossup", "crossdown"}

            Dim hasUp = upWords.Any(Function(w) source.Contains(w))
            Dim hasDown = downWords.Any(Function(w) source.Contains(w))
            Dim hasBreakout = breakoutWords.Any(Function(w) source.Contains(w))

            If isBuy Then
                Dim op = If(hasBreakout OrElse hasUp, ComparisonOperator.CrossUp, ComparisonOperator.Greater)
                AddCondition(conditions, prefix, seq,
                             "Price vs SuperTrend", "Price", op, "SuperTrend", Nothing)
            Else
                Dim op = If(hasBreakout OrElse hasDown, ComparisonOperator.CrossDown, ComparisonOperator.Less)
                AddCondition(conditions, prefix, seq,
                             "Price vs SuperTrend Exit", "Price", op, "SuperTrend", Nothing)
            End If
        End Sub

        Private Shared Function MentionsSuperTrend(source As String) As Boolean
            If String.IsNullOrWhiteSpace(source) Then Return False
            Dim s = source.ToLowerInvariant()
            Return s.Contains("supertrend") OrElse s.Contains("슈퍼트렌드") OrElse s.Contains("super trend")
        End Function

        Private Shared Sub ParseThreshold(source As String,
                                          conditions As List(Of ConditionCell),
                                          prefix As String,
                                          ByRef seq As Integer,
                                          keywords As String(),
                                          indicator As String)
            If keywords Is Nothing OrElse keywords.Length = 0 Then Return
            If Not keywords.Any(Function(k) source.Contains(k.ToLowerInvariant())) Then Return

            Dim m = Regex.Match(source, "(-?\d+(?:\.\d+)?)")
            If Not m.Success Then Return

            Dim value As Double = 0
            If Not Double.TryParse(m.Groups(1).Value, value) Then Return

            Dim op As ComparisonOperator = ComparisonOperator.GreaterEqual
            If source.Contains("이하") OrElse source.Contains("미만") OrElse source.Contains("under") OrElse source.Contains("below") Then
                op = ComparisonOperator.LessEqual
            ElseIf source.Contains("돌파") OrElse source.Contains("cross") Then
                op = ComparisonOperator.CrossUp
            End If

            AddCondition(conditions, prefix, seq,
                         indicator & " threshold", indicator, op, Nothing, value)
        End Sub

        Private Shared Sub ParseRsi(source As String,
                                    conditions As List(Of ConditionCell),
                                    prefix As String,
                                    ByRef seq As Integer)
            If Not source.Contains("rsi") Then Return

            Dim period As Integer = 14
            Dim p = Regex.Match(source, "rsi\s*(\d{1,3})")
            If p.Success Then Integer.TryParse(p.Groups(1).Value, period)

            Dim v = Regex.Match(source, "(-?\d+(?:\.\d+)?)")
            If Not v.Success Then Return

            Dim value As Double = 0
            If Not Double.TryParse(v.Groups(1).Value, value) Then Return

            Dim op As ComparisonOperator = ComparisonOperator.GreaterEqual
            If source.Contains("이하") OrElse source.Contains("under") OrElse source.Contains("below") Then
                op = ComparisonOperator.LessEqual
            ElseIf source.Contains("돌파") OrElse source.Contains("cross") Then
                op = ComparisonOperator.CrossUp
            End If

            AddCondition(conditions, prefix, seq,
                         "RSI threshold", "RSI_" & period.ToString(), op, Nothing, value)
        End Sub

        Private Shared Sub ParseJma(source As String,
                                    isBuy As Boolean,
                                    conditions As List(Of ConditionCell),
                                    prefix As String,
                                    ByRef seq As Integer)
            If Not source.Contains("jma") Then Return

            Dim period As Integer = 14
            Dim p = Regex.Match(source, "jma\s*(\d{1,3})")
            If p.Success Then Integer.TryParse(p.Groups(1).Value, period)

            Dim hasCross = source.Contains("돌파") OrElse source.Contains("크로스") OrElse source.Contains("cross")
            Dim op As ComparisonOperator

            If isBuy Then
                op = If(hasCross, ComparisonOperator.CrossUp, ComparisonOperator.Greater)
            Else
                op = If(hasCross OrElse source.Contains("이탈") OrElse source.Contains("하락"), ComparisonOperator.CrossDown, ComparisonOperator.Less)
            End If

            AddCondition(conditions, prefix, seq,
                         "Price vs JMA", "Price", op, "JMA_" & period.ToString(), Nothing)
        End Sub

        Private Shared Sub ParseRisk(source As String,
                                     conditions As List(Of ConditionCell),
                                     prefix As String,
                                     ByRef seq As Integer)
            Dim stopMatch = Regex.Match(source, "손절\s*(-?\d+(?:\.\d+)?)\s*%")
            If stopMatch.Success Then
                Dim v As Double = 0
                If Double.TryParse(stopMatch.Groups(1).Value, v) Then
                    If v > 0 Then v = -v
                    AddCondition(conditions, prefix, seq,
                                 "StopLoss", "PROFIT_PCT", ComparisonOperator.LessEqual, Nothing, v)
                End If
            End If

            Dim takeMatch = Regex.Match(source, "(익절|목표)\s*(\d+(?:\.\d+)?)\s*%")
            If takeMatch.Success Then
                Dim v As Double = 0
                If Double.TryParse(takeMatch.Groups(2).Value, v) Then
                    AddCondition(conditions, prefix, seq,
                                 "TakeProfit", "PROFIT_PCT", ComparisonOperator.GreaterEqual, Nothing, v)
                End If
            End If
        End Sub

        Private Shared Sub AddCondition(conditions As List(Of ConditionCell),
                                        prefix As String,
                                        ByRef seq As Integer,
                                        desc As String,
                                        indicatorA As String,
                                        op As ComparisonOperator,
                                        indicatorB As String,
                                        constantValue As Double?)
            Dim id As String = prefix & seq.ToString()
            seq += 1
            conditions.Add(New ConditionCell(id, desc, indicatorA, op, indicatorB, constantValue))
        End Sub

        Private Shared Function Deduplicate(conditions As List(Of ConditionCell)) As List(Of ConditionCell)
            Dim result As New List(Of ConditionCell)
            Dim keys As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each c In conditions
                If c Is Nothing Then Continue For
                Dim k As String = String.Join("|", {
                    If(c.IndicatorA, ""),
                    c.Operator.ToString(),
                    If(c.IndicatorB, ""),
                    If(c.ConstantValue.HasValue, c.ConstantValue.Value.ToString("0.####"), "")
                })
                If keys.Contains(k) Then Continue For
                keys.Add(k)
                result.Add(c)
            Next

            Return result
        End Function
    End Class
End Namespace
