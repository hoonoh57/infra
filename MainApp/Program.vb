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

        ' ── 0) CybosPlus 로그인 확인 + CybosServer 실행 (필수, 가장 먼저) ──
        If Not LaunchAndVerifyCybosServer() Then
            MessageBox.Show(
                "CybosPlus(Cybos5)를 먼저 로그인하세요!" & vbCrLf & vbCrLf &
                "CybosPlus가 관리자 권한으로 실행되어 있고," & vbCrLf &
                "정상적으로 로그인된 상태여야 합니다.",
                "Cybos 연결 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error)
            Return  ' 프로그램 종료
        End If

        ' ── 1) KiwoomServer 실행 ──
        LaunchServerIfNotRunning("KiwoomServer")
        System.Threading.Thread.Sleep(1000) ' KiwoomServer 초기화 대기

        ' ── 2) 브릿지 시작 (32‑bit 서버 연결) ──
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
    ''' CybosServer를 시작하고, CybosPlus 연결 상태를 확인한다.
    ''' CybosServer가 시작 후 곧바로 죽으면 CybosPlus가 미로그인 상태이다.
    ''' </summary>
    Private Function LaunchAndVerifyCybosServer() As Boolean
        Try
            ' 이미 실행 중이면 OK
            Dim existing = Process.GetProcessesByName("CybosServer")
            If existing.Length > 0 Then
                AppLogger.I.Info($"[CybosServer] 이미 실행 중 (PID: {existing(0).Id})", "Boot")
                Return True
            End If

            ' 서버 EXE 경로 찾기
            Dim serverExe = FindServerExe("CybosServer")
            If String.IsNullOrEmpty(serverExe) Then
                AppLogger.I.Error("[CybosServer] 서버 EXE를 찾을 수 없습니다.", "Boot")
                Return False
            End If

            ' CybosServer 시작
            Dim psi As New ProcessStartInfo With {
                .FileName = serverExe,
                .WorkingDirectory = Path.GetDirectoryName(serverExe),
                .UseShellExecute = True,
                .WindowStyle = ProcessWindowStyle.Minimized
            }
            Dim proc = Process.Start(psi)
            AppLogger.I.Info($"[CybosServer] 시작 중... ({serverExe})", "Boot")

            ' 2초간 대기하면서 프로세스 생존 확인
            ' CybosPlus 미로그인이면 CybosEngine 생성자가 실패하고 서버가 즉시 종료됨
            For i = 0 To 7  ' 총 2초 (250ms × 8)
                System.Threading.Thread.Sleep(250)
                If proc.HasExited Then
                    AppLogger.I.Error("[CybosServer] CybosPlus 미연결로 서버 종료됨", "Boot")
                    Return False
                End If
            Next

            AppLogger.I.Info("[CybosServer] 서버 정상 실행 확인됨 ✓", "Boot")
            Return True

        Catch ex As Exception
            AppLogger.I.Error($"[CybosServer] 확인 실패: {ex.Message}", "Boot")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 32비트 서버 프로세스가 실행되지 않았으면 자동 실행한다.
    ''' </summary>
    Private Sub LaunchServerIfNotRunning(serverName As String)
        Try
            Dim existing = Process.GetProcessesByName(serverName)
            If existing.Length > 0 Then
                AppLogger.I.Info($"[{serverName}] 이미 실행 중 (PID: {existing(0).Id})", "Boot")
                Return
            End If

            Dim serverExe = FindServerExe(serverName)
            If String.IsNullOrEmpty(serverExe) Then
                AppLogger.I.Warn($"[{serverName}] 서버 EXE 없음", "Boot")
                Return
            End If

            Dim psi As New ProcessStartInfo With {
                .FileName = serverExe,
                .WorkingDirectory = Path.GetDirectoryName(serverExe),
                .UseShellExecute = True,
                .WindowStyle = ProcessWindowStyle.Minimized
            }
            Process.Start(psi)
            AppLogger.I.Info($"[{serverName}] 서버 프로세스 시작됨", "Boot")

        Catch ex As Exception
            AppLogger.I.Error($"[{serverName}] 서버 시작 실패: {ex.Message}", "Boot")
        End Try
    End Sub

    ''' <summary>
    ''' 솔루션 루트/{서버명}/bin/Debug/net481/{서버명}.exe 경로를 찾는다.
    ''' </summary>
    Private Function FindServerExe(serverName As String) As String
        Dim mainExeDir = AppDomain.CurrentDomain.BaseDirectory
        Dim solutionRoot = Path.GetFullPath(Path.Combine(mainExeDir, "..", "..", "..", ".."))
        Dim serverExe = Path.Combine(solutionRoot, serverName, "bin", "Debug", "net481", $"{serverName}.exe")
        If File.Exists(serverExe) Then Return serverExe
        Return Nothing
    End Function

End Module
