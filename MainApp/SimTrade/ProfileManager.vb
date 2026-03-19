' ═══════════════════════════════════════════════════════════════
' ProfileManager.vb — 전략 프로파일 관리 (원칙서 v4.0)
' ═══════════════════════════════════════════════════════════════
' ★ Profile A (공격형): 빠른 진입, 넓은 목표
' ★ Profile B (보수형): 보수적 진입, 좁은 손절
' ★ 시장 상황에 따른 자동 전환
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

    ''' <summary>프로파일 정의</summary>
    Public Class StrategyProfile
        Public Property Name As String = ""
        Public Property Description As String = ""
        Public Property TakeProfitRate As Single = 5.0F
        Public Property StopLossRate As Single = -3.0F
        Public Property TrailingStopRate As Single = -1.5F
        Public Property TightenedTrailingRate As Single = -1.0F
        Public Property TightenAfterBars As Integer = 30
        Public Property MinRiskReward As Single = 1.2F
        Public Property TickSumThreshold As Single = 5.0F
        Public Property RSI_MomentumLower As Single = 60.0F
        Public Property MaxPositionCount As Integer = 5
        Public Property PerStockMaxAmount As Long = 3000000

        Public Function Clone() As StrategyProfile
            Return DirectCast(Me.MemberwiseClone(), StrategyProfile)
        End Function

        Public Function ToSummary() As String
            Return $"{Name}: TP={TakeProfitRate:F1}%, SL={StopLossRate:F1}%, Trail={TrailingStopRate:F1}%, R/R≥{MinRiskReward:F1}, MaxPos={MaxPositionCount}"
        End Function
    End Class

    ''' <summary>프로파일 전환 판단 결과</summary>
    Public Class ProfileSwitchResult
        Public Property ShouldSwitch As Boolean = False
        Public Property FromProfile As String = ""
        Public Property ToProfile As String = ""
        Public Property Reason As String = ""
    End Class

    ''' <summary>전략 프로파일 관리자 — A/B 자동 전환</summary>
    Public Class ProfileManager

        Private ReadOnly _settings As SimTradeSettings
        Private ReadOnly _profiles As New Dictionary(Of String, StrategyProfile)(StringComparer.OrdinalIgnoreCase)
        Private _currentProfileName As String = "A"

        Public Event ProfileChanged(fromName As String, toName As String, reason As String)

        Public Sub New(settings As SimTradeSettings)
            _settings = settings
            InitDefaultProfiles()
        End Sub

        ''' <summary>현재 프로파일</summary>
        Public ReadOnly Property CurrentProfile As StrategyProfile
            Get
                If _profiles.ContainsKey(_currentProfileName) Then
                    Return _profiles(_currentProfileName)
                End If
                Return _profiles("A")
            End Get
        End Property

        ''' <summary>현재 프로파일 이름</summary>
        Public ReadOnly Property CurrentProfileName As String
            Get
                Return _currentProfileName
            End Get
        End Property

        ''' <summary>프로파일 수동 전환</summary>
        Public Sub SetProfile(name As String)
            If Not _profiles.ContainsKey(name) Then Return
            Dim old = _currentProfileName
            _currentProfileName = name
            If old <> name Then
                RaiseEvent ProfileChanged(old, name, "수동 전환")
            End If
        End Sub

        ''' <summary>시장 상황 기반 자동 전환 평가</summary>
        Public Function EvaluateSwitch(marketStats As MarketStats) As ProfileSwitchResult
            Dim result As New ProfileSwitchResult()
            result.FromProfile = _currentProfileName

            If _settings.ActiveProfileMode <> ProfileMode.Auto Then
                result.ShouldSwitch = False
                Return result
            End If

            ' 시장 약세 → Profile B (보수형)
            If marketStats.AdvanceDeclineRatio < 0.3F OrElse
               marketStats.AvgChangeRate < -1.5F Then
                If _currentProfileName <> "B" Then
                    result.ShouldSwitch = True
                    result.ToProfile = "B"
                    result.Reason = $"시장약세(AD비율={marketStats.AdvanceDeclineRatio:F2}, 평균등락={marketStats.AvgChangeRate:F1}%)"
                End If
                Return result
            End If

            ' 시장 강세 → Profile A (공격형)
            If marketStats.AdvanceDeclineRatio > 0.6F AndAlso
               marketStats.AvgChangeRate > 0.5F Then
                If _currentProfileName <> "A" Then
                    result.ShouldSwitch = True
                    result.ToProfile = "A"
                    result.Reason = $"시장강세(AD비율={marketStats.AdvanceDeclineRatio:F2}, 평균등락={marketStats.AvgChangeRate:F1}%)"
                End If
                Return result
            End If

            result.ShouldSwitch = False
            Return result
        End Function

        ''' <summary>자동 전환 실행</summary>
        Public Sub ApplySwitch(switchResult As ProfileSwitchResult)
            If Not switchResult.ShouldSwitch Then Return
            If Not _profiles.ContainsKey(switchResult.ToProfile) Then Return

            Dim old = _currentProfileName
            _currentProfileName = switchResult.ToProfile
            ApplyProfileToSettings()
            RaiseEvent ProfileChanged(old, _currentProfileName, switchResult.Reason)
        End Sub

        ''' <summary>현재 프로파일 값을 Settings에 반영</summary>
        Public Sub ApplyProfileToSettings()
            Dim p = CurrentProfile
            _settings.TakeProfitRate = p.TakeProfitRate
            _settings.StopLossRate = p.StopLossRate
            _settings.TrailingStopRate = p.TrailingStopRate
            _settings.TightenedTrailingRate = p.TightenedTrailingRate
            _settings.MaxHoldWithoutNewHigh = p.TightenAfterBars
            _settings.MinRiskReward = p.MinRiskReward
            _settings.TICKINT_Threshold = p.TickSumThreshold
            _settings.RSI_MomentumLower = p.RSI_MomentumLower
            _settings.MaxPositionCount = p.MaxPositionCount
            '_settings.PerStockMaxAmount = p.PerStockMaxAmount
        End Sub

        ''' <summary>프로파일 추가/수정</summary>
        Public Sub RegisterProfile(name As String, profile As StrategyProfile)
            _profiles(name) = profile
        End Sub

        ''' <summary>프로파일 조회</summary>
        Public Function GetProfile(name As String) As StrategyProfile
            If _profiles.ContainsKey(name) Then Return _profiles(name)
            Return Nothing
        End Function

        ''' <summary>전체 프로파일 목록</summary>
        Public Function GetAllProfiles() As Dictionary(Of String, StrategyProfile)
            Return New Dictionary(Of String, StrategyProfile)(_profiles, StringComparer.OrdinalIgnoreCase)
        End Function

        ' ─── 기본 프로파일 초기화 ───
        Private Sub InitDefaultProfiles()
            Dim profA As New StrategyProfile()
            profA.Name = "A"
            profA.Description = "공격형 — 빠른 진입, 넓은 목표"
            profA.TakeProfitRate = 5.0F
            profA.StopLossRate = -3.0F
            profA.TrailingStopRate = -1.5F
            profA.TightenedTrailingRate = -1.0F
            profA.TightenAfterBars = 30
            profA.MinRiskReward = 1.2F
            profA.TickSumThreshold = 5.0F
            profA.RSI_MomentumLower = 60.0F
            profA.MaxPositionCount = 5
            profA.PerStockMaxAmount = 3000000
            _profiles("A") = profA

            Dim profB As New StrategyProfile()
            profB.Name = "B"
            profB.Description = "보수형 — 보수적 진입, 좁은 손절"
            profB.TakeProfitRate = 3.0F
            profB.StopLossRate = -2.0F
            profB.TrailingStopRate = -1.0F
            profB.TightenedTrailingRate = -0.7F
            profB.TightenAfterBars = 20
            profB.MinRiskReward = 1.5F
            profB.TickSumThreshold = 7.0F
            profB.RSI_MomentumLower = 65.0F
            profB.MaxPositionCount = 3
            profB.PerStockMaxAmount = 2000000
            _profiles("B") = profB
        End Sub

    End Class

    ''' <summary>시장 통계 (ProfileManager 전환 판단용)</summary>
    Public Class MarketStats
        Public Property AdvanceDeclineRatio As Single = 0.5F
        Public Property AvgChangeRate As Single = 0.0F
        Public Property KospiChangeRate As Single = 0.0F
        Public Property TotalVolume As Long = 0
        Public Property StockCount As Integer = 0

        Public Sub Update(advCount As Integer, decCount As Integer, avgChg As Single)
            Dim total = advCount + decCount
            If total > 0 Then
                AdvanceDeclineRatio = CSng(advCount) / total
            End If
            AvgChangeRate = avgChg
            StockCount = total
        End Sub
    End Class

End Namespace
