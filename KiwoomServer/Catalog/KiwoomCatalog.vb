' ═══════════════════════════════════════════════════════════════
' KiwoomCatalog.vb — 키움 OpenAPI 전체 기능 카탈로그
' ═══════════════════════════════════════════════════════════════
' 99% 불변. 새 항목 추가만 가능, 기존 항목 수정/삭제 금지.
' ═══════════════════════════════════════════════════════════════

Public Class KiwoomCatalog

    Public Class FuncDef
        Public Property Name As String = ""
        Public Property TrCode As String = ""
        Public Property Category As FuncCategory = FuncCategory.TrRequest
        Public Property Inputs As List(Of FieldDef)
        Public Property Outputs As List(Of FieldDef)
        Public Property MultiOutputs As List(Of FieldDef)
        Public Property OrderType As Integer = 0        ' SendOrder 전용
        Public Property QuoteType As String = ""        ' "03"=시장가 등
        Public Property RealtimeType As String = ""     ' 실시간 타입명
        Public Property FidList As String = ""          ' 실시간 FID
        Public Property ScreenNo As String = ""
        Public Property SupportsContinuation As Boolean = False

        Public Sub New()
            Inputs = New List(Of FieldDef)()
            Outputs = New List(Of FieldDef)()
            MultiOutputs = New List(Of FieldDef)()
        End Sub
    End Class

    Public Class FieldDef
        Public Property Name As String = ""
        Public Property KiwoomName As String = ""       ' SetInputValue / GetCommData 키
        Public Property DataType As Type = GetType(String)
        Public Property Fid As Integer = -1
        Public Sub New()
        End Sub
        Public Sub New(n As String, kn As String, Optional dt As Type = Nothing, Optional fid As Integer = -1)
            Name = n : KiwoomName = kn : DataType = If(dt, GetType(String)) : Me.Fid = fid
        End Sub
    End Class

    Public Enum FuncCategory
        TrRequest = 0
        Order = 1
        RealtimeReg = 2
        RealtimeUnreg = 3
        Condition = 4
        Utility = 5
    End Enum

    ' ─── 전체 함수 정의 ───

    Private Shared _all As List(Of FuncDef)

    Public Shared Function GetAll() As List(Of FuncDef)
        If _all IsNot Nothing Then Return _all
        _all = New List(Of FuncDef)()

        ' ════════════════════════════════════════
        ' TR 조회
        ' ════════════════════════════════════════

        ' ── 시세/기본 ──
        _all.Add(New FuncDef With {
            .Name = "주식기본정보", .TrCode = "OPT10001", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {New FieldDef("code", "종목코드")},
            .Outputs = New List(Of FieldDef) From {
                New FieldDef("종목명", "종목명"), New FieldDef("현재가", "현재가"),
                New FieldDef("전일대비", "전일대비"), New FieldDef("거래량", "거래량"),
                New FieldDef("시가", "시가"), New FieldDef("고가", "고가"),
                New FieldDef("저가", "저가"), New FieldDef("상한가", "상한가"),
                New FieldDef("하한가", "하한가"), New FieldDef("시가총액", "시가총액"),
                New FieldDef("PER", "PER"), New FieldDef("EPS", "EPS"),
                New FieldDef("250최고", "250최고"), New FieldDef("250최저", "250최저")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "체결정보", .TrCode = "OPT10003", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {New FieldDef("code", "종목코드")},
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("체결시간", "체결시간"), New FieldDef("현재가", "현재가"),
                New FieldDef("전일대비", "전일대비"), New FieldDef("거래량", "거래량"),
                New FieldDef("누적거래량", "누적거래량")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "주식호가요청", .TrCode = "OPT10004", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {New FieldDef("code", "종목코드")},
            .Outputs = New List(Of FieldDef) From {
                New FieldDef("매도호가1", "매도최우선호가"), New FieldDef("매수호가1", "매수최우선호가"),
                New FieldDef("매도잔량1", "매도최우선잔량"), New FieldDef("매수잔량1", "매수최우선잔량")
            }
        })

        ' ── 차트 ──
        _all.Add(New FuncDef With {
            .Name = "분봉조회", .TrCode = "OPT10080", .Category = FuncCategory.TrRequest,
            .SupportsContinuation = True,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("code", "종목코드"), New FieldDef("틱범위", "틱범위"),
                New FieldDef("수정주가구분", "수정주가구분")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("체결시간", "체결시간"), New FieldDef("시가", "시가"),
                New FieldDef("고가", "고가"), New FieldDef("저가", "저가"),
                New FieldDef("현재가", "현재가"), New FieldDef("거래량", "거래량")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "일봉조회", .TrCode = "OPT10081", .Category = FuncCategory.TrRequest,
            .SupportsContinuation = True,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("code", "종목코드"), New FieldDef("기준일자", "기준일자"),
                New FieldDef("수정주가구분", "수정주가구분")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("일자", "일자"), New FieldDef("시가", "시가"),
                New FieldDef("고가", "고가"), New FieldDef("저가", "저가"),
                New FieldDef("현재가", "현재가"), New FieldDef("거래량", "거래량")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "주봉조회", .TrCode = "OPT10082", .Category = FuncCategory.TrRequest,
            .SupportsContinuation = True,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("code", "종목코드"), New FieldDef("기준일자", "기준일자"),
                New FieldDef("수정주가구분", "수정주가구분")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("일자", "일자"), New FieldDef("시가", "시가"),
                New FieldDef("고가", "고가"), New FieldDef("저가", "저가"),
                New FieldDef("현재가", "현재가"), New FieldDef("거래량", "거래량")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "월봉조회", .TrCode = "OPT10083", .Category = FuncCategory.TrRequest,
            .SupportsContinuation = True,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("code", "종목코드"), New FieldDef("기준일자", "기준일자"),
                New FieldDef("수정주가구분", "수정주가구분")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("일자", "일자"), New FieldDef("시가", "시가"),
                New FieldDef("고가", "고가"), New FieldDef("저가", "저가"),
                New FieldDef("현재가", "현재가"), New FieldDef("거래량", "거래량")
            }
        })

        ' ── 투자자 ──
        _all.Add(New FuncDef With {
            .Name = "투자자조회", .TrCode = "OPT10059", .Category = FuncCategory.TrRequest,
            .SupportsContinuation = True,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("code", "종목코드"), New FieldDef("시작일자", "일자"),
                New FieldDef("종료일자", "일자"), New FieldDef("외인구분", "금액수량구분")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("일자", "일자"), New FieldDef("기관", "기관계"),
                New FieldDef("외국인", "외국인"), New FieldDef("개인", "개인")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "프로그램매매", .TrCode = "OPT10060", .Category = FuncCategory.TrRequest,
            .SupportsContinuation = True,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("code", "종목코드"), New FieldDef("시작일자", "시작일자"),
                New FieldDef("종료일자", "종료일자")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("일자", "일자"), New FieldDef("매수", "프로그램매수"),
                New FieldDef("매도", "프로그램매도"), New FieldDef("순매수", "프로그램순매수")
            }
        })

        ' ── 재무 ──
        _all.Add(New FuncDef With {
            .Name = "주식재무정보", .TrCode = "OPT10012", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {New FieldDef("code", "종목코드")},
            .Outputs = New List(Of FieldDef) From {
                New FieldDef("PER", "PER"), New FieldDef("PBR", "PBR"),
                New FieldDef("ROE", "ROE"), New FieldDef("매출액", "매출액"),
                New FieldDef("영업이익", "영업이익"), New FieldDef("당기순이익", "당기순이익"),
                New FieldDef("유보율", "유보율"), New FieldDef("부채비율", "부채비율")
            }
        })

        ' ── 업종 ──
        _all.Add(New FuncDef With {
            .Name = "업종현재가", .TrCode = "OPT20001", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {New FieldDef("업종코드", "업종코드")},
            .Outputs = New List(Of FieldDef) From {
                New FieldDef("업종명", "업종명"), New FieldDef("현재가", "현재가"),
                New FieldDef("전일대비", "전일대비"), New FieldDef("거래량", "거래량")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "업종별종목", .TrCode = "OPT20002", .Category = FuncCategory.TrRequest,
            .SupportsContinuation = True,
            .Inputs = New List(Of FieldDef) From {New FieldDef("업종코드", "업종코드")},
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("종목코드", "종목코드"), New FieldDef("종목명", "종목명"),
                New FieldDef("현재가", "현재가"), New FieldDef("등락율", "등락율")
            }
        })

        ' ── 순위 ──
        _all.Add(New FuncDef With {
            .Name = "거래량상위", .TrCode = "OPT10027", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("시장구분", "시장구분"), New FieldDef("정렬구분", "정렬구분")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("종목코드", "종목코드"), New FieldDef("종목명", "종목명"),
                New FieldDef("현재가", "현재가"), New FieldDef("거래량", "거래량")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "등락률상위", .TrCode = "OPT10028", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("시장구분", "시장구분"), New FieldDef("정렬구분", "정렬구분")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("종목코드", "종목코드"), New FieldDef("종목명", "종목명"),
                New FieldDef("현재가", "현재가"), New FieldDef("등락률", "등락율")
            }
        })

        ' ── 관심종목 일괄조회 ──
        _all.Add(New FuncDef With {
            .Name = "관심종목정보", .TrCode = "OPTKWFID", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {New FieldDef("codes", "종목코드")},
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("종목코드", "종목코드"), New FieldDef("종목명", "종목명"),
                New FieldDef("현재가", "현재가"), New FieldDef("전일대비", "전일대비"),
                New FieldDef("등락율", "등락율"), New FieldDef("거래량", "거래량"),
                New FieldDef("고가", "고가"), New FieldDef("저가", "저가"),
                New FieldDef("시가", "시가"), New FieldDef("체결시간", "체결시간")
            }
        })

        ' ════════════════════════════════════════
        ' 계좌
        ' ════════════════════════════════════════

        _all.Add(New FuncDef With {
            .Name = "예수금상세현황", .TrCode = "OPW00001", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("accountNo", "계좌번호"), New FieldDef("pass", "비밀번호"),
                New FieldDef("media", "비밀번호입력매체구분")
            },
            .Outputs = New List(Of FieldDef) From {
                New FieldDef("주문가능금액", "주문가능금액"), New FieldDef("예수금", "예수금"),
                New FieldDef("D+2추정예수금", "d+2추정예수금")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "계좌평가현황", .TrCode = "OPW00018", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("accountNo", "계좌번호"), New FieldDef("pass", "비밀번호"),
                New FieldDef("media", "비밀번호입력매체구분"), New FieldDef("query", "조회구분")
            },
            .Outputs = New List(Of FieldDef) From {
                New FieldDef("총매입금액", "총매입금액"), New FieldDef("총평가금액", "총평가금액"),
                New FieldDef("총평가손익금액", "총평가손익금액"),
                New FieldDef("총수익률", "총수익률(%)"), New FieldDef("추정예탁자산", "추정예탁자산")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("종목코드", "종목번호"), New FieldDef("종목명", "종목명"),
                New FieldDef("보유수량", "보유수량"), New FieldDef("매입가", "매입가"),
                New FieldDef("평가금액", "평가금액"), New FieldDef("손익률", "수익률(%)"),
                New FieldDef("현재가", "현재가"), New FieldDef("평가손익", "평가손익"),
                New FieldDef("매매가능수량", "매매가능수량")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "미체결조회", .TrCode = "OPT10075", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("accountNo", "계좌번호"), New FieldDef("매매구분", "매매구분"),
                New FieldDef("code", "종목코드"), New FieldDef("체결구분", "체결구분")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("종목코드", "종목코드"), New FieldDef("종목명", "종목명"),
                New FieldDef("주문번호", "주문번호"), New FieldDef("주문상태", "주문상태"),
                New FieldDef("주문구분", "주문구분"), New FieldDef("주문수량", "주문수량"),
                New FieldDef("주문가격", "주문가격"), New FieldDef("미체결수량", "미체결수량"),
                New FieldDef("체결량", "체결량"), New FieldDef("시간", "주문시간")
            }
        })

        _all.Add(New FuncDef With {
            .Name = "당일실현손익", .TrCode = "OPT10074", .Category = FuncCategory.TrRequest,
            .Inputs = New List(Of FieldDef) From {
                New FieldDef("accountNo", "계좌번호"), New FieldDef("시작일자", "시작일자"),
                New FieldDef("종료일자", "종료일자"), New FieldDef("pass", "비밀번호"),
                New FieldDef("media", "비밀번호입력매체구분"), New FieldDef("query", "조회구분")
            },
            .MultiOutputs = New List(Of FieldDef) From {
                New FieldDef("실현손익", "실현손익"), New FieldDef("매수금액", "매수금액"),
                New FieldDef("매도금액", "매도금액")
            }
        })

        ' ════════════════════════════════════════
        ' 주문
        ' ════════════════════════════════════════

        _all.Add(New FuncDef With {.Name = "매수_시장가", .Category = FuncCategory.Order, .OrderType = 1, .QuoteType = "03"})
        _all.Add(New FuncDef With {.Name = "매수_지정가", .Category = FuncCategory.Order, .OrderType = 1, .QuoteType = "00"})
        _all.Add(New FuncDef With {.Name = "매도_시장가", .Category = FuncCategory.Order, .OrderType = 2, .QuoteType = "03"})
        _all.Add(New FuncDef With {.Name = "매도_지정가", .Category = FuncCategory.Order, .OrderType = 2, .QuoteType = "00"})
        _all.Add(New FuncDef With {.Name = "주문정정", .Category = FuncCategory.Order, .OrderType = 5, .QuoteType = "00"})
        _all.Add(New FuncDef With {.Name = "주문취소", .Category = FuncCategory.Order, .OrderType = 3, .QuoteType = ""})

        ' ════════════════════════════════════════
        ' 실시간
        ' ════════════════════════════════════════

        _all.Add(New FuncDef With {
            .Name = "실시간_체결", .Category = FuncCategory.RealtimeReg,
            .RealtimeType = "주식체결",
            .FidList = "10;11;12;13;15;16;17;18;20;25;27;28;228;311"
        })

        _all.Add(New FuncDef With {
            .Name = "실시간_호가", .Category = FuncCategory.RealtimeReg,
            .RealtimeType = "주식호가잔량",
            .FidList = "21;41;42;43;44;45;46;47;48;49;50;51;52;53;54;55;56;57;58;59;60;61;62;63;64;65;66;67;68;69;70;71;72;73;74;75;76;77;78;79;80;121;125;128;138"
        })

        _all.Add(New FuncDef With {
            .Name = "실시간_프로그램", .Category = FuncCategory.RealtimeReg,
            .RealtimeType = "주식프로그램매매",
            .FidList = "261;262;263;264;265;266"
        })

        _all.Add(New FuncDef With {
            .Name = "실시간_장시작", .Category = FuncCategory.RealtimeReg,
            .RealtimeType = "장시작시간",
            .FidList = "215;20;214"
        })

        _all.Add(New FuncDef With {
            .Name = "실시간_해제", .Category = FuncCategory.RealtimeUnreg
        })

        ' ════════════════════════════════════════
        ' 조건검색
        ' ════════════════════════════════════════

        _all.Add(New FuncDef With {.Name = "조건검색목록", .Category = FuncCategory.Condition})
        _all.Add(New FuncDef With {.Name = "조건검색시작", .Category = FuncCategory.Condition})
        _all.Add(New FuncDef With {.Name = "조건검색중지", .Category = FuncCategory.Condition})

        ' ════════════════════════════════════════
        ' 유틸리티
        ' ════════════════════════════════════════

        _all.Add(New FuncDef With {.Name = "종목코드목록", .Category = FuncCategory.Utility})
        _all.Add(New FuncDef With {.Name = "종목명", .Category = FuncCategory.Utility})
        _all.Add(New FuncDef With {.Name = "상장주식수", .Category = FuncCategory.Utility})
        _all.Add(New FuncDef With {.Name = "전일가", .Category = FuncCategory.Utility})
        _all.Add(New FuncDef With {.Name = "종목상태", .Category = FuncCategory.Utility})

        Return _all
    End Function

    ' ─── 이름으로 검색 ───

    Public Shared Function Find(name As String) As FuncDef
        Return GetAll().Find(Function(f) f.Name = name)
    End Function

    Public Shared Function FindByTr(trCode As String) As FuncDef
        Return GetAll().Find(Function(f) String.Equals(f.TrCode, trCode, StringComparison.OrdinalIgnoreCase))
    End Function

End Class
