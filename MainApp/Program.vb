' ═══════════════════════════════════════════════════════════════
' Program.vb — 진입점
' ═══════════════════════════════════════════════════════════════

Imports System.Windows.Forms
Imports System.Diagnostics
Imports System.IO

Module Program

    Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        ' ── 로거 초기화 ──
        AppLogger.I.Info("═══════════════════════════════════════")
        AppLogger.I.Info("  프로세스 시작")
        AppLogger.I.Info("═══════════════════════════════════════")

        ' ── 0) CybosPlus 로그인 확인 + CybosServer 실행 + READY 확인 ──
        If Not LaunchAndVerifyCybosServer() Then
            MessageBox.Show(
                "CybosPlus(Cybos5)를 먼저 로그인하세요!" & vbCrLf & vbCrLf &
                "CybosPlus가 관리자 권한으로 실행되어 있고," & vbCrLf &
                "정상적으로 로그인된 상태여야 합니다.",
                "Cybos 연결 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error)
            Return
        End If

        ' ── 1) KiwoomServer 실행 + READY 확인 ──
        If Not LaunchAndVerifyKiwoomServer() Then
            MessageBox.Show(
                "KiwoomServer 준비 확인에 실패했습니다." & vbCrLf & vbCrLf &
                "Kiwoom OpenAPI 서버 프로세스와 파이프 연결 상태를 확인하세요.",
                "Kiwoom 연결 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning)
            Return
        End If

        ' ── 2) 브릿지 시작 (READY 확인 후 연결) ──
        AppLogger.I.Info("브릿지 시작 중...", "Boot")

        Dim cybosBridge As New CybosBridge()
        cybosBridge.Start()
        AppLogger.I.Info("CybosBridge 시작됨", "Boot")

        Dim kiwoomBridge As New KiwoomBridge()
        kiwoomBridge.Start()
        AppLogger.I.Info("KiwoomBridge 시작됨", "Boot")

        ' ── 3) 종목정보 관리자 설정
        Dim mgr = StockInfoManager.I
        AppLogger.I.Info($"StockInfoManager 준비 완료", "Boot")

        ' ── 3-b) 중앙 매매관리자 (싱글톤) ──
        Dim tradeMgr = TradeManager.I
        AppLogger.I.Info("TradeManager 준비 완료", "Boot")

        ' ── 4) 지표 플러그인 ──
        ' (★3 단계에서 추가)
        AppLogger.I.Info("지표 플러그인: 미구현 (★3 단계)", "Boot")

        ' ── 5) 전략 플러그인 ──
        ' (★4 단계에서 추가)
        AppLogger.I.Info("전략 플러그인: 미구현 (★4 단계)", "Boot")

        ' ── 6) 주문 플러그인 ──
        ' (★5 단계에서 추가)
        AppLogger.I.Info("주문 플러그인: 미구현 (★5 단계)", "Boot")

        ' ── 7) 메인 셸 실행 ──
        AppLogger.I.Info("MainShell 실행...", "Boot")
        Dim shell As New MainShell()
        Application.Run(shell)

    End Sub

    ''' <summary>
    ''' CybosServer를 시작하고 NamedPipe READY 응답까지 확인한다.
    ''' </summary>
    Private Function LaunchAndVerifyCybosServer() As Boolean
        Try
            Dim proc As Process = Nothing
            Dim existing = Process.GetProcessesByName("CybosServer")
            If existing.Length > 0 Then
                proc = existing(0)
                AppLogger.I.Info($"[CybosServer] 이미 실행 중 (PID: {proc.Id})", "Boot")
            Else
                Dim serverExe = FindServerExe("CybosServer")
                If String.IsNullOrEmpty(serverExe) Then
                    AppLogger.I.Error("[CybosServer] 서버 EXE를 찾을 수 없습니다.", "Boot")
                    Return False
                End If

                Dim psi As New ProcessStartInfo With {
                    .FileName = serverExe,
                    .WorkingDirectory = Path.GetDirectoryName(serverExe),
                    .UseShellExecute = True,
                .Verb = "runas",
                    .WindowStyle = ProcessWindowStyle.Minimized
                }
                proc = Process.Start(psi)
                AppLogger.I.Info($"[CybosServer] 시작 중... ({serverExe})", "Boot")
            End If

            If Not WaitProcessAlive(proc, "CybosServer", 2000) Then Return False
            If Not ServerReadyProbe.WaitForReady("CybosPipe", "연결상태", "CybosServer", 12000, 250) Then Return False

            AppLogger.I.Info("[CybosServer] 서버 READY 확인 완료 ✓", "Boot")
            Return True

        Catch ex As Exception
            AppLogger.I.Error($"[CybosServer] 확인 실패: {ex.Message}", "Boot")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' KiwoomServer를 시작하고 NamedPipe READY 응답까지 확인한다.
    ''' </summary>
    Private Function LaunchAndVerifyKiwoomServer() As Boolean
        Try
            Dim proc As Process = LaunchServerIfNotRunning("KiwoomServer")
            If proc IsNot Nothing AndAlso Not WaitProcessAlive(proc, "KiwoomServer", 2000) Then Return False
            If Not ServerReadyProbe.WaitForReady("KiwoomPipe", "status", "KiwoomServer", 12000, 250) Then Return False

            AppLogger.I.Info("[KiwoomServer] 서버 READY 확인 완료 ✓", "Boot")
            Return True

        Catch ex As Exception
            AppLogger.I.Error($"[KiwoomServer] 확인 실패: {ex.Message}", "Boot")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 지정 서버 프로세스가 실행되지 않았으면 자동 실행하고 Process를 반환한다.
    ''' 이미 실행 중이면 기존 Process를 반환한다.
    ''' </summary>
    Private Function LaunchServerIfNotRunning(serverName As String) As Process
        Try
            Dim existing = Process.GetProcessesByName(serverName)
            If existing.Length > 0 Then
                AppLogger.I.Info($"[{serverName}] 이미 실행 중 (PID: {existing(0).Id})", "Boot")
                Return existing(0)
            End If

            Dim serverExe = FindServerExe(serverName)
            If String.IsNullOrEmpty(serverExe) Then
                AppLogger.I.Warn($"[{serverName}] 서버 EXE 없음", "Boot")
                Return Nothing
            End If

            Dim psi As New ProcessStartInfo With {
                .FileName = serverExe,
                .WorkingDirectory = Path.GetDirectoryName(serverExe),
                .UseShellExecute = True,
                .WindowStyle = ProcessWindowStyle.Minimized
            }
            Dim proc As Process = Process.Start(psi)
            AppLogger.I.Info($"[{serverName}] 서버 프로세스 시작됨", "Boot")
            Return proc

        Catch ex As Exception
            AppLogger.I.Error($"[{serverName}] 서버 시작 실패: {ex.Message}", "Boot")
            Return Nothing
        End Try
    End Function

    Private Function WaitProcessAlive(proc As Process, serverName As String, waitMs As Integer) As Boolean
        If proc Is Nothing Then Return True

        Dim elapsed As Integer = 0
        Do While elapsed < waitMs
            System.Threading.Thread.Sleep(250)
            elapsed += 250
            Try
                If proc.HasExited Then
                    AppLogger.I.Error($"[{serverName}] 서버 프로세스가 초기화 중 종료됨", "Boot")
                    Return False
                End If
            Catch ex As Exception
                AppLogger.I.Warn($"[{serverName}] 프로세스 생존 확인 실패: {ex.Message}", "Boot")
                Return True
            End Try
        Loop

        Return True
    End Function

    ''' <summary>
    ''' 서버 EXE 경로를 찾는다. 배포/Debug/Release 경로를 순서대로 탐색한다.
    ''' </summary>
    Private Function FindServerExe(serverName As String) As String
        Dim mainExeDir = AppDomain.CurrentDomain.BaseDirectory
        Dim solutionRoot = Path.GetFullPath(Path.Combine(mainExeDir, "..", "..", "..", ".."))
        Dim candidates As String() = {
            Path.Combine(solutionRoot, serverName, "bin", "x86", "Debug", "net481", $"{serverName}.exe"),
            Path.Combine(solutionRoot, serverName, "bin", "x86", "Release", "net481", $"{serverName}.exe"),
            Path.Combine(mainExeDir, "servers", serverName, $"{serverName}.exe"),
            Path.Combine(mainExeDir, $"{serverName}.exe"),
            Path.Combine(solutionRoot, serverName, "bin", "Debug", "net481", $"{serverName}.exe"),
            Path.Combine(solutionRoot, serverName, "bin", "Release", "net481", $"{serverName}.exe")
        }

        For Each candidate As String In candidates
            If File.Exists(candidate) Then
                AppLogger.I.Info($"[{serverName}] EXE 경로 확인: {candidate}", "Boot")
                Return candidate
            End If
        Next

        Return Nothing
    End Function

End Module


