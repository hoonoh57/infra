' ═══════════════════════════════════════════════════════════════
' ExclusionEngine.vb — 종목 제외 엔진 (원칙서 v4.0)
' ═══════════════════════════════════════════════════════════════
' ★ 정적 제외 (S1: 관리종목, S2: 투자경고)
' ★ 동적 제외 (D1x~D5x: 당일 조건 기반)
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

    ''' <summary>제외 검사 결과</summary>
    Public Class ExclusionResult
        Public Property IsExcluded As Boolean = False
        Public Property Reason As String = ""
        Public Property RuleId As String = ""
        Public Property Details As New List(Of String)

        Public Function ToSummary() As String
            If Not IsExcluded Then Return "통과"
            Return $"[제외:{RuleId}] {Reason}"
        End Function
    End Class

    ''' <summary>종목 제외 엔진 — 정적(S) + 동적(D) 규칙</summary>
    Public Class ExclusionEngine

        Private ReadOnly _settings As SimTradeSettings

        ' 정적 제외 목록 (관리종목, 투자경고 등 — 외부에서 로드)
        Private ReadOnly _staticBlacklist As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Public Sub New(settings As SimTradeSettings)
            _settings = settings
        End Sub

        ''' <summary>정적 블랙리스트에 종목 추가</summary>
        Public Sub AddToBlacklist(code As String, Optional reason As String = "")
            _staticBlacklist.Add(code)
        End Sub

        ''' <summary>정적 블랙리스트에서 종목 제거</summary>
        Public Sub RemoveFromBlacklist(code As String)
            _staticBlacklist.Remove(code)
        End Sub

        ''' <summary>블랙리스트 일괄 설정</summary>
        Public Sub SetBlacklist(codes As IEnumerable(Of String))
            _staticBlacklist.Clear()
            For Each c In codes
                _staticBlacklist.Add(c)
            Next
        End Sub

        ''' <summary>전체 제외 검사 실행</summary>
        Public Function Evaluate(state As StockState) As ExclusionResult
            Dim result As New ExclusionResult()

            ' ── S1: 정적 블랙리스트 ──
            If _staticBlacklist.Contains(state.Code) Then
                result.IsExcluded = True
                result.RuleId = "S1"
                result.Reason = "정적 블랙리스트(관리/경고)"
                Return result
            End If

            ' ── S2: 이름 기반 제외 (스팩, 리츠, ETN 등) ──
            If state.Name IsNot Nothing Then
                Dim nm = state.Name
                If nm.Contains("스팩") OrElse nm.Contains("SPAC") OrElse
                   nm.Contains("리츠") OrElse nm.Contains("REIT") OrElse
                   nm.EndsWith("ETN") OrElse nm.Contains("인버스") OrElse
                   nm.Contains("레버리지") Then
                    result.IsExcluded = True
                    result.RuleId = "S2"
                    result.Reason = $"종목명 제외패턴: {nm}"
                    Return result
                End If
            End If

            ' ── D1x: 당일 등락률 과대 ──
            If _settings.Exclude_MaxDayGain > 0 AndAlso state.ChangeRate > _settings.Exclude_MaxDayGain Then
                result.IsExcluded = True
                result.RuleId = "D1"
                result.Reason = $"등락률 {state.ChangeRate:F1}% > 한도 {_settings.Exclude_MaxDayGain:F1}%"
                result.Details.Add($"ChangeRate={state.ChangeRate:F1}")
                Return result
            End If

            ' ── D2x: 가격 범위 제외 (현재 설정에 Min/MaxPrice 없으므로 생략) ──
            ' 향후 SimTradeSettings에 Exclude_MinPrice, Exclude_MaxPrice 추가 시 활성화

            ' ── D3x: 거래량 부족 ──
            If state.DayVolume > 0 AndAlso state.DayVolume < 10000 Then
                result.IsExcluded = True
                result.RuleId = "D3"
                result.Reason = $"당일거래량 {state.DayVolume:N0} < 최소 10,000"
                Return result
            End If

            ' ── D4x: 거래대금 부족 ──
            If _settings.Exclude_MinAvgDailyAmount > 0 Then
                Dim tradeAmt = CLng(state.CurrentPrice) * state.DayVolume
                If tradeAmt > 0 AndAlso tradeAmt < _settings.Exclude_MinAvgDailyAmount Then
                    result.IsExcluded = True
                    result.RuleId = "D4"
                    result.Reason = $"거래대금 {tradeAmt:N0} < 최소 {_settings.Exclude_MinAvgDailyAmount:N0}"
                    Return result
                End If
            End If


            ' ── D5x: 시가총액 제외 (향후 확장) ──
            ' 현재 StockState에 시가총액 필드 없음 — 추후 추가 시 활성화

            result.IsExcluded = False
            result.Reason = "통과"
            Return result
        End Function

        ''' <summary>블랙리스트 수량</summary>
        Public Function BlacklistCount() As Integer
            Return _staticBlacklist.Count
        End Function

        ''' <summary>블랙리스트 목록</summary>
        Public Function GetBlacklist() As List(Of String)
            Return _staticBlacklist.ToList()
        End Function

    End Class

End Namespace
