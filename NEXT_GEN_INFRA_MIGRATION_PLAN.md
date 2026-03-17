# Next-Gen Infra Migration Plan

## 목적

현재 `MainApp` / `StrategyLab` / 연구 DB / 차트 / 서버 통신 기반은 보존한다.

동시에, 실전용 로직과 연구용 로직이 서로 침범하지 않는 차세대 구조를 별도 솔루션으로 설계하고 구현한다.

핵심 원칙은 다음과 같다.

- 현재 프로젝트는 계속 사용 가능해야 한다.
- 새 구조는 완전히 별도 솔루션에서 검증한다.
- 새 구조가 성공하면 점진 이주한다.
- 새 구조가 실패하면 현재 프로젝트는 그대로 유지한다.

즉, 현재 프로젝트를 리팩터링 실험장으로 쓰지 않는다.

## 현재 강점

현재 코드베이스가 이미 확보한 강점은 폐기 대상이 아니다.

- 키움 / 사이보스 이중 서버-클라이언트 통신 구조
- 고속 차트 / 고속 그리드 렌더링 기반
- 도킹 기반 UI 확장 구조
- StrategyLab / 연구 DB / 배치 리포트의 연구 흐름 초안
- 코스닥150 연구 DB와 일봉 / 1분봉 / 30틱봉 기반

즉 문제는 기반이 약해서가 아니라, 공용 경계 없이 기능이 섞였다는 점이다.

## 현재 프로젝트의 문제

현재 구조의 핵심 문제는 다음과 같다.

- 실전용 live 로직과 연구용 research 로직이 같은 경로를 공유하거나 침범한다.
- 캔들 접근, 영업일 계산, universe, 전략 평가 입력 계약이 UI/매니저/브리지에 흩어져 있다.
- 새 기능을 붙일 때마다 공용 인프라 대신 메인 코드에 직접 분기를 넣게 된다.
- 그 결과 작은 변경도 메인 로직에 영향을 줄 수 있다는 불신이 생긴다.

이 상태에서 현재 프로젝트를 계속 뜯는 방식은 장기적으로 맞지 않다.

## 전략

### 전략 요약

새 솔루션을 만든다.

예시 이름:

- `Infra.Next.sln`

이 솔루션은 현재 프로젝트를 참조 대상으로만 보고, 공용 인프라와 연구/실전 분리 구조를 처음부터 다시 세운다.

### 성공 조건

다음이 충족되면 이주를 시작한다.

- 공용 데이터 접근 계약이 분명하다.
- 연구용 앱이 실전 앱을 전혀 침범하지 않는다.
- 동일한 캔들 / 지수 / 틱 / 전략 데이터 계약을 쓴다.
- Strategy 검증이 현재보다 더 예측 가능하고 안정적이다.
- 실전 차트 / 종목정보 / 주문 로직과 독립적으로 돌아간다.

### 실패 조건

다음이 확인되면 새 솔루션 실험은 중단할 수 있다.

- 공용화 비용이 너무 커서 오히려 복잡도가 증가한다.
- 성능이 현재 구조보다 현저히 나빠진다.
- 연구와 실전의 분리가 예상보다 불안정하다.

이 경우 현재 프로젝트는 그대로 유지하고, 필요한 기능만 현 프로젝트에 보수적으로 추가한다.

## 새 솔루션 구조

차세대 구조는 UI가 아니라 공용 인프라부터 만든다.

### 1. Infra.Common.Contracts

역할:

- DTO
- request / response 계약
- enum
- 기본 전략 모델
- 결과 모델

포함 후보:

- candle row model
- tick candle row model
- index candle row model
- universe item
- strategy evaluation input
- strategy evaluation output
- trade record
- diagnosis / suggestion / toxic trade class

### 2. Infra.Common.Market

역할:

- 영업일 계산
- 타임프레임 정규화
- stopTime / count / paging 규칙
- 상대강도 계산
- 포착 시점 기준 수익률 계산

포함 후보:

- `TradingDayService`
- `TimeframeService`
- `RelativeStrengthService`
- `CapturedSetMetricsService`
- `TickIntensityAggregationService`

### 3. Infra.Common.DataAccess

역할:

- live 데이터 접근
- research DB 접근
- provider 추상화

핵심 인터페이스:

- `ICandleQueryService`
- `ITickCandleQueryService`
- `IIndexQueryService`
- `IUniverseQueryService`
- `IResearchRepository`

구현체 예시:

- `CybosCandleQueryService`
- `KiwoomCandleQueryService`
- `MySqlResearchRepository`

### 4. Infra.Research.Engine

역할:

- 자연어 전략 검증
- 내부 rule graph / DSL 변환
- 백테스트 실행
- 실패 유형 분류
- 배치 리포트 생성

포함 후보:

- `PromptValidationService`
- `StrategyCompiler`
- `BacktestEngine`
- `ToxicTradeClassifier`
- `BatchReportService`

### 5. Infra.Research.App

역할:

- StrategyLab
- 연구 DB 관리
- 코스닥150 후보 편성
- 리포트 UI

원칙:

- 실전 live 상태를 몰라야 한다.
- 주문 / 실시간 live 감시 / live 차트 상태를 변경하지 않는다.

### 6. Infra.Live.Adapter

역할:

- 현재 `MainApp`과의 연결
- 공용 서비스 계약을 현재 live 앱에 붙이는 어댑터

원칙:

- live 핵심 로직은 변경 최소화
- 새 공용 계약과 현재 코드 사이의 브릿지 역할만 담당

## 현재 프로젝트와의 관계

현재 프로젝트는 아래 역할을 유지한다.

- 실전 운용
- 기존 차트
- 기존 종목정보
- 기존 주문 / 실시간 감시
- 현재 StrategyLab 운용

즉 현재 프로젝트는 계속 사용한다.

새 솔루션은 처음에는 아래만 목표로 한다.

- 공용 계약 정의
- 연구 데이터 접근
- 연구용 전략 검증 엔진

live UI를 바로 옮기지 않는다.

## 이주 전략

### 단계 1: 설계 고정

목표:

- 공용 계약, 책임 경계, 프로젝트 분할을 문서로 확정

산출물:

- 이 문서
- 공용 계약 초안
- 서비스 목록

### 단계 2: 공용 계약 구현

목표:

- `Contracts`, `Market`, `DataAccess` 프로젝트 생성

성공 기준:

- UI 없이도 캔들 / 틱 / 지수 / 영업일 계산이 공용 서비스로만 동작

### 단계 3: 연구 엔진 이식

목표:

- StrategyLab 핵심 로직을 새 `Infra.Research.Engine`에 옮김

성공 기준:

- 자연어 전략 검증
- 상대강도 전략 실험
- 배치 백테스트
- 보고서 생성

### 단계 4: 연구 UI 분리

목표:

- 새 `Infra.Research.App`에서 StrategyLab / 연구 DB 관리 실행

성공 기준:

- 현재 MainApp을 건드리지 않고 연구 기능 수행 가능

### 단계 5: 점진 이주

목표:

- 성공한 기능부터 새 구조로 이전

예:

- 연구 DB 관리 먼저 이전
- StrategyLab 다음 이전
- 후보 종목 선별 도구 이전

## 지금 당장 해야 할 일

### 해야 할 일

1. 새 솔루션 생성
2. `Infra.Common.Contracts` 생성
3. `Infra.Common.Market` 생성
4. `Infra.Common.DataAccess` 생성
5. 현재 코드에서 공용 후보 목록 추출

### 지금 하지 말아야 할 일

- 현재 `MainApp`에 또 다른 연구 분기 추가
- live 경로에 research 예외 코드 추가
- 현재 프로젝트를 대규모 리팩터링 대상으로 삼기

## 공용 후보 1차 목록

우선 공용화 후보는 아래부터 시작한다.

- Trading calendar / last trading day resolver
- timeframe normalize
- candle request model
- tick candle request model
- universe item model
- strategy evaluation input/output model
- trade / failed example / diagnosis model
- relative strength calculation helpers

## 검증 원칙

새 구조의 모든 단계는 아래 원칙으로 검증한다.

- 하나씩 구현
- 하나씩 검증
- 메인 로직 무영향
- 실패 시 즉시 중단 가능
- 현재 프로젝트는 항상 작동 가능 상태 유지

## 결론

지금 필요한 것은 현재 프로젝트를 계속 뜯는 것이 아니다.

필요한 것은:

- 현재 강점을 보존하고
- 공용 인프라를 별도 솔루션으로 설계하고
- 성공 시 이주 가능한 차세대 구조를 만드는 것

즉 앞으로의 방향은 다음과 같이 고정한다.

- 현재 프로젝트: 운영 / 보존
- 새 솔루션: 공용화 / 분리 / 검증
- 성공 시 점진 이주
- 실패 시 즉시 폐기

