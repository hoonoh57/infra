' ═══════════════════════════════════════════════════════════════
' Topics.vb — 전체 시스템 토픽 상수
' ═══════════════════════════════════════════════════════════════
' 수정 금지. 새 토픽 추가만 허용 (기존 상수 변경/삭제 절대 불가).
' ═══════════════════════════════════════════════════════════════

Public Class Topics

    ' ═══ 캔들/데이터 요청 ═══
    Public Const CANDLE_REQUEST As String = "candle.request"
    Public Const CANDLE_LOADED As String = "candle.loaded"
    Public Const DAILY_REQUEST As String = "daily.request"
    Public Const DAILY_LOADED As String = "daily.loaded"
    Public Const WEEKLY_REQUEST As String = "weekly.request"
    Public Const WEEKLY_LOADED As String = "weekly.loaded"
    Public Const MONTHLY_REQUEST As String = "monthly.request"
    Public Const MONTHLY_LOADED As String = "monthly.loaded"
    Public Const TICK_CANDLE_REQUEST As String = "tickcandle.request"
    Public Const TICK_CANDLE_LOADED As String = "tickcandle.loaded"
    Public Const CANDLE_PERIOD_REQUEST As String = "candle.period.request"
    Public Const CANDLE_PERIOD_LOADED As String = "candle.period.loaded"

    ' ═══ 실시간 틱 ═══
    Public Const TICK As String = "tick"
    Public Const ORDERBOOK As String = "orderbook"
    Public Const PROGRAM_TRADE As String = "program.trade"
    Public Const TRADE_STRENGTH As String = "trade.strength"
    Public Const MARKET_STATUS As String = "market.status"

    ' ═══ 실시간 등록/해제 ═══
    Public Const REALTIME_SUBSCRIBE As String = "realtime.subscribe"
    Public Const REALTIME_UNSUBSCRIBE As String = "realtime.unsubscribe"
    Public Const REALTIME_UNSUBSCRIBE_ALL As String = "realtime.unsubscribe.all"

    ' ═══ 지표 ═══
    Public Const INDICATOR_REQUEST As String = "indicator.request"
    Public Const INDICATOR_RESULT As String = "indicator.result"
    Public Const INDICATOR_REMOVE As String = "indicator.remove"

    ' ═══ 전략/신호 ═══
    Public Const STRATEGY_SIGNAL As String = "strategy.signal"
    Public Const STRATEGY_APPLY As String = "strategy.apply"
    Public Const STRATEGY_REMOVE As String = "strategy.remove"

    ' ═══ 주문 ═══
    Public Const ORDER_BUY_MARKET As String = "order.buy.market"
    Public Const ORDER_BUY_LIMIT As String = "order.buy.limit"
    Public Const ORDER_SELL_MARKET As String = "order.sell.market"
    Public Const ORDER_SELL_LIMIT As String = "order.sell.limit"
    Public Const ORDER_MODIFY As String = "order.modify"
    Public Const ORDER_CANCEL As String = "order.cancel"
    Public Const ORDER_EXECUTED As String = "order.executed"
    Public Const ORDER_BALANCE_CHANGED As String = "order.balance.changed"

    ' ═══ 계좌 ═══
    Public Const ACCOUNT_BALANCE_REQUEST As String = "account.balance.request"
    Public Const ACCOUNT_BALANCE_RESULT As String = "account.balance.result"
    Public Const ACCOUNT_OPEN_ORDERS_REQUEST As String = "account.openorders.request"
    Public Const ACCOUNT_OPEN_ORDERS_RESULT As String = "account.openorders.result"
    Public Const ACCOUNT_TODAY_PNL_REQUEST As String = "account.todaypnl.request"
    Public Const ACCOUNT_TODAY_PNL_RESULT As String = "account.todaypnl.result"

    ' ═══ 조건검색 ═══
    Public Const CONDITION_LIST_REQUEST As String = "condition.list.request"
    Public Const CONDITION_LIST_RESULT As String = "condition.list.result"
    Public Const CONDITION_SEARCH_REQUEST As String = "condition.search.request"
    Public Const CONDITION_SEARCH_RESULT As String = "condition.search.result"
    Public Const CONDITION_HIT As String = "condition.hit"
    Public Const CONDITION_START As String = "condition.start"
    Public Const CONDITION_STOP As String = "condition.stop"

    ' ═══ 종목 정보 ═══
    Public Const STOCK_BASIC_REQUEST As String = "stock.basic.request"
    Public Const STOCK_BASIC_RESULT As String = "stock.basic.result"
    Public Const STOCK_LIST_REQUEST As String = "stock.list.request"
    Public Const STOCK_LIST_RESULT As String = "stock.list.result"
    Public Const STOCK_MULTI_INFO_REQUEST As String = "stock.multiinfo.request"
    Public Const STOCK_MULTI_INFO_RESULT As String = "stock.multiinfo.result"

    ' ═══ 투자자/프로그램 ═══
    Public Const INVESTOR_REQUEST As String = "investor.request"
    Public Const INVESTOR_RESULT As String = "investor.result"
    Public Const PROGRAM_TRADE_REQUEST As String = "program.trade.request"
    Public Const PROGRAM_TRADE_RESULT As String = "program.trade.result"
    Public Const PROGRAM_TRADE_RT_SUBSCRIBE As String = "program.trade.rt.subscribe"
    Public Const PROGRAM_TRADE_RT_UNSUBSCRIBE As String = "program.trade.rt.unsubscribe"

    ' ═══ 재무 ═══
    Public Const FINANCE_REQUEST As String = "finance.request"
    Public Const FINANCE_RESULT As String = "finance.result"

    ' ═══ 섹터/업종 ═══
    Public Const SECTOR_LIST_REQUEST As String = "sector.list.request"
    Public Const SECTOR_LIST_RESULT As String = "sector.list.result"
    Public Const SECTOR_STOCKS_REQUEST As String = "sector.stocks.request"
    Public Const SECTOR_STOCKS_RESULT As String = "sector.stocks.result"
    Public Const THEME_STOCKS_REQUEST As String = "theme.stocks.request"
    Public Const THEME_STOCKS_RESULT As String = "theme.stocks.result"
    Public Const PROGRAM_TOP_BUY_REQUEST As String = "program.top.buy.request"
    Public Const PROGRAM_TOP_BUY_RESULT As String = "program.top.buy.result"
    Public Const WATCHLIST_LOAD As String = "watchlist.load"
    Public Const WATCHLIST_SAVE As String = "watchlist.save"
    Public Const WATCHLIST_LOADED As String = "watchlist.loaded"
    Public Const MARKETCAP_TOP_REQUEST As String = "marketcap.top.request"
    Public Const MARKETCAP_TOP_RESULT As String = "marketcap.top.result"

    ' ═══ 뉴스 ═══
    Public Const NEWS_LIST_REQUEST As String = "news.list.request"
    Public Const NEWS_LIST_RESULT As String = "news.list.result"
    Public Const NEWS_BODY_REQUEST As String = "news.body.request"
    Public Const NEWS_BODY_RESULT As String = "news.body.result"

    ' ═══ 순위 ═══
    Public Const RANK_VOLUME_REQUEST As String = "rank.volume.request"
    Public Const RANK_VOLUME_RESULT As String = "rank.volume.result"
    Public Const RANK_CHANGE_REQUEST As String = "rank.change.request"
    Public Const RANK_CHANGE_RESULT As String = "rank.change.result"

    ' ═══ 공매도 ═══
    Public Const SHORT_SELLING_REQUEST As String = "short.selling.request"
    Public Const SHORT_SELLING_RESULT As String = "short.selling.result"

    ' ═══ 호가 ═══
    Public Const HOGA_REQUEST As String = "hoga.request"
    Public Const HOGA_RESULT As String = "hoga.result"

    ' ═══ 시스템 ═══
    Public Const SYS_LOG As String = "sys.log"
    Public Const SYS_ERROR As String = "sys.error"
    Public Const SYS_AUTOTRADE As String = "sys.autotrade"
    Public Const SYS_SERVER_STATUS As String = "sys.server.status"

    ' ═══ 인증 ═══
    Public Const AUTH_LOGIN_REQUEST As String = "auth.login.request"
    Public Const AUTH_LOGIN_RESULT As String = "auth.login.result"
    Public Const AUTH_STATUS_REQUEST As String = "auth.status.request"
    Public Const AUTH_STATUS_RESULT As String = "auth.status.result"

    ' ═══ UI ═══
    Public Const UI_ORDER_UPDATE As String = "ui.order.update"
    Public Const UI_STATUS As String = "ui.status"
    Public Const UI_CHART_OPEN As String = "ui.chart.open"

    ' ═══ 로깅 (추가) ═══
    Public Const LOG_INFO As String = "log.info"
    Public Const LOG_WARN As String = "log.warn"
    Public Const LOG_ERROR As String = "log.error"
    Public Const LOG_DEBUG As String = "log.debug"
    Public Const LOG_TEST As String = "log.test"       ' 테스트 전용
    Public Const LOG_TRADE As String = "log.trade"     ' 매매 로그 전용
    Public Const LOG_COMM As String = "log.comm"       ' 통신 로그 전용

    ' ═══ 종목정보 관리 (추가) ═══
    Public Const STOCKINFO_ADD_REQUEST As String = "stockinfo.add.request"
    Public Const STOCKINFO_ADDED As String = "stockinfo.added"
    Public Const STOCKINFO_UPDATED As String = "stockinfo.updated"
    Public Const STOCKINFO_REMOVED As String = "stockinfo.removed"
    Public Const STOCKINFO_CLEAR As String = "stockinfo.clear"
    Public Const STOCKINFO_DATA_READY As String = "stockinfo.data.ready"
    Public Const STOCKINFO_CANDLE_PROGRESS As String = "stockinfo.candle.progress"
    Public Const STOCKINFO_FILTER_APPLIED As String = "stockinfo.filter.applied"
    ' ═══ 매매관리자 ═══
    Public Const TRADE_ORDER_REQUEST As String = "trade.order.request"
    Public Const TRADE_ORDER_ACCEPTED As String = "trade.order.accepted"
    Public Const TRADE_ORDER_REJECTED As String = "trade.order.rejected"
    Public Const TRADE_ORDER_FILLED As String = "trade.order.filled"
    Public Const TRADE_POSITION_UPDATED As String = "trade.position.updated"
    Public Const TRADE_BALANCE_UPDATED As String = "trade.balance.updated"
    Public Const TRADE_SYNC_REQUEST As String = "trade.sync.request"
    Public Const TRADE_SYNC_COMPLETE As String = "trade.sync.complete"
    Public Const TRADE_RISK_ALERT As String = "trade.risk.alert"

End Class
