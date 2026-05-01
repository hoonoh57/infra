ChartEngineV2 — 실험용 안전 차트 엔진

이 폴더는 기존 FastChartControl.vb를 건드리지 않고 새 차트 컨트롤을 병렬 구현하기 위한 독립 영역입니다.

삭제 복구:
- 이 폴더 전체(MainApp/ChartEngineV2)를 삭제하면 기존 차트 로직에는 영향이 없습니다.

원칙:
- 기존 FastChartControl.vb 수정 금지
- 기존 TickIntensity_Indicator.vb 수정 금지
- 기존 StockInfoManager.vb 수정 금지
- 기존 MessageBus / CandleItem / IndicatorEngine 수정 금지
- 새 차트는 기존 데이터/지표 클래스를 호출만 하고 변경하지 않습니다.
