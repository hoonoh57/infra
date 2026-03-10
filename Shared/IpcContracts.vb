Imports System.Runtime.InteropServices

Public Enum MessageType As Byte
    ' === 64비트 → 32비트 (명령) ===
    RequestCandleDownload = 1     ' 캔들 배치 다운로드 요청
    RequestProgramTrade = 2       ' 프로그램순매수 요청
    RequestTradeIntensity = 3     ' 체결강도 요청
    RequestOrderbook = 4          ' 호가 요청
    RequestFinancial = 5          ' 재무정보 요청
    RequestNews = 6               ' 뉴스 요청
    RequestSectorLeader = 7       ' 주도섹터 요청
    RequestMarketCapTop = 8       ' 시총 상위 종목 요청
    SubscribeRealtime = 10        ' 실시간 구독 요청
    UnsubscribeRealtime = 11      ' 실시간 구독 해제
    RequestConditionSearch = 12   ' 조건검색 요청
    SendOrder = 20                ' 주문 요청
    CancelOrder = 21              ' 주문 취소

    ' === 32비트 → 64비트 (응답/스트림) ===
    CandleDataReady = 101         ' 캔들 데이터 준비 완료
    RealtimeTick = 102            ' 실시간 체결
    RealtimeOrderbook = 103       ' 실시간 호가
    OrderResult = 104             ' 주문 결과
    ConditionResult = 105         ' 조건검색 결과
    BatchDataReady = 106          ' 범용 배치 데이터 준비 완료
    ErrorOccurred = 255           ' 에러
End Enum

<StructLayout(LayoutKind.Sequential, Pack:=1)>
Public Structure MessageHeader
    Public Type As MessageType          ' 1 byte
    Public Sequence As UInt32           ' 4 bytes (순서 번호)
    Public StockCode As Long            ' 8 bytes (종목코드를 숫자로)
    Public Timestamp As Long            ' 8 bytes (틱 단위 시각)
    Public PayloadSize As Integer       ' 4 bytes (뒤따르는 데이터 크기)
    Public Reserved As Byte             ' 1 byte
    Public Checksum As UInt16           ' 2 bytes
End Structure                           ' 합계: 28 bytes

<StructLayout(LayoutKind.Sequential, Pack:=1)>
Public Structure CandleRecord
    Public DateTime As Long     ' 8 bytes
    Public Open As Single       ' 4 bytes
    Public High As Single       ' 4 bytes
    Public Low As Single        ' 4 bytes
    Public Close As Single      ' 4 bytes
    Public Volume As Long       ' 8 bytes
    Public TickCount As Integer ' 4 bytes
    Public Reserved As Integer  ' 4 bytes
End Structure                   ' 합계: 40 bytes

<StructLayout(LayoutKind.Sequential, Pack:=1)>
Public Structure TickRecord
    Public StockCode As Long    ' 8 bytes
    Public Price As Single      ' 4 bytes
    Public Volume As Integer    ' 4 bytes
    Public Timestamp As Long    ' 8 bytes
End Structure                   ' 합계: 24 bytes

<StructLayout(LayoutKind.Sequential, Pack:=1)>
Public Structure OrderRequest
    Public StockCode As Long    ' 8 bytes
    Public Side As Byte         ' 1 byte (1=Buy, 2=Sell)
    Public OrderType As Byte    ' 1 byte (1=Market, 2=Limit)
    Public Quantity As Integer  ' 4 bytes
    Public Price As Single      ' 4 bytes
    Public Reserved As Long     ' 8 bytes
    Public Tag As Long          ' 8 bytes (주문 추적용)
End Structure
