' ═══════════════════════════════════════════════════════════════
' IndicatorEngine.vb — 지표 관리 및 일괄/증분 계산 엔진
' ═══════════════════════════════════════════════════════════════

''' <summary>
''' 등록된 IIndicator들을 관리하고, 캔들 리스트에 대해
''' 전체 계산 / 마지막 캔들 증분 계산을 수행한다.
''' </summary>
Public Class IndicatorEngine

    Private ReadOnly _indicators As New List(Of IIndicator)
    Private ReadOnly _results As New Dictionary(Of String, List(Of IndicatorResult))

    ''' <summary>지표별 계산 결과 딕셔너리 (읽기 전용 접근)</summary>
    Public ReadOnly Property Results As Dictionary(Of String, List(Of IndicatorResult))
        Get
            Return _results
        End Get
    End Property

    ''' <summary>지표 등록</summary>
    Public Sub Register(ind As IIndicator)
        If ind Is Nothing Then Return
        ' 동일 이름 중복 방지
        If _indicators.Any(Function(x) x.Name = ind.Name) Then Return
        _indicators.Add(ind)
    End Sub

    ''' <summary>지표 제거</summary>
    Public Sub Remove(name As String)
        Dim found = _indicators.FirstOrDefault(Function(x) x.Name = name)
        If found IsNot Nothing Then
            _indicators.Remove(found)
            _results.Remove(name)
        End If
    End Sub

    ''' <summary>등록된 전체 지표 반환</summary>
    Public Function GetAll() As List(Of IIndicator)
        Return _indicators.ToList()
    End Function

    ''' <summary>전체 캔들에 대해 모든 지표 계산</summary>
    Public Sub CalculateAll(candles As List(Of CandleItem))
        If candles Is Nothing OrElse candles.Count = 0 Then Return

        For Each ind In _indicators
            Try
                Dim result = ind.Calculate(candles)
                _results(ind.Name) = result
            Catch ex As Exception
                ' 지표 계산 오류 시 빈 결과
                _results(ind.Name) = New List(Of IndicatorResult)
            End Try
        Next
    End Sub

    ''' <summary>마지막 캔들만 증분 업데이트</summary>
    Public Sub UpdateLast(candles As List(Of CandleItem))
        If candles Is Nothing OrElse candles.Count = 0 Then Return

        For Each ind In _indicators
            Try
                Dim prevResults As List(Of IndicatorResult) = Nothing
                _results.TryGetValue(ind.Name, prevResults)

                Dim lastResult = ind.UpdateLast(candles, prevResults)

                If prevResults IsNot Nothing AndAlso prevResults.Count > 0 Then
                    ' 마지막 결과 교체
                    prevResults(prevResults.Count - 1) = lastResult
                ElseIf prevResults IsNot Nothing Then
                    prevResults.Add(lastResult)
                Else
                    _results(ind.Name) = New List(Of IndicatorResult) From {lastResult}
                End If
            Catch
                ' 증분 실패 시 무시
            End Try
        Next
    End Sub

    ''' <summary>전체 초기화</summary>
    Public Sub Clear()
        _indicators.Clear()
        _results.Clear()
    End Sub
End Class
