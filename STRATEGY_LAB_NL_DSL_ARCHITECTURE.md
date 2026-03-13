# StrategyLab Natural Language Mapping Architecture

## Goal

StrategyLab must let the user write strategies in free natural language and still reach an execution state where the intended logic is applied with full confidence.

The system must not require users to write DSL directly.

The system must:

- accept natural Korean strategy text,
- map it into an internal executable rule graph,
- validate that mapping before evaluation,
- propose natural-language rewrites when mapping is incomplete,
- run evaluation only after the mapping is accepted.

This is the core principle:

`what you write -> what the system maps -> what gets executed`

If the system cannot guarantee that chain, the evaluation result is not trustworthy.

## Product Principle

The user writes only natural language.

The system internally converts the prompt into a hidden DSL / rule graph.

The user sees:

- what was understood,
- what was partially understood,
- what must be rewritten,
- what alternative wording will make the logic executable.

The user must never be forced to learn internal syntax such as `crossup()`, `valuewhen()`, or `tf.day.close(5)`.

Those expressions are internal execution forms only.

## User Flow

1. User writes a strategy in natural language.
2. User presses `Validate Strategy`.
3. System splits the strategy into clauses.
4. Each clause is classified:
   - supported
   - partial
   - unsupported
5. System proposes natural-language rewrites for non-final clauses.
6. User accepts rewrite or edits manually.
7. Validation reaches one of two states:
   - fully mapped
   - accepted substitution complete
8. Only then `Evaluate Prompt` becomes a trusted evaluation step.

## Required UI States

### 1. Prompt Input

The current natural-language input box remains the main authoring surface.

### 2. Validation Panel

A dedicated panel must show:

- validation summary
- clause-by-clause interpretation
- rewrite suggestions
- unresolved items

### 3. Mapping Approval State

The screen must show one of:

- `Ready for evaluation`
- `Rewrite required`
- `Needs data source`
- `Ambiguous interpretation`

### 4. Execution Trace

After evaluation, each trade should be traceable back to:

- entry clause
- hold clause
- exit clause
- exception clause

This lets the user confirm that the written strategy actually drove the trade.

## Internal Architecture

The architecture should have 4 layers.

### Layer 1. Natural Language Clause Layer

The prompt is split into clauses such as:

- timeframe clause
- entry clause
- hold clause
- exit clause
- exception clause
- context clause

Examples:

- `1봉전 jma를 종가가 돌파하면 매수`
- `매수 후 목표 2% 달성 전까지 supertrend 상승중이면 매도자제`
- `supertrend 하락전환시 매도`

### Layer 2. Semantic Mapping Layer

Each clause is transformed into normalized semantic objects:

- timeframe reference
- series reference
- comparison
- event
- window
- memory reference
- account state reference
- market context reference

Example:

Natural language:

`1봉전 20봉 최고가를 현재봉이 돌파`

Semantic mapping:

- left: current candle price
- operator: cross above
- right: highest high over 20 bars at offset 1

### Layer 3. Internal DSL / Rule Graph Layer

This is not exposed to users.

It exists only so the engine can execute complex logic consistently.

Examples of internal building blocks:

- `SeriesRef`
- `TimeframeRef`
- `OffsetRef`
- `WindowRef`
- `ComparisonNode`
- `CrossNode`
- `ValueWhenNode`
- `WithinBarsNode`
- `StateGuardNode`
- `ExitOverrideNode`

Example internal form:

- `CrossUp(Close(0), JMA(14, offset:=1))`
- `WithinBars(3, SuperTrendTurnUp())`
- `ValueWhen(CrossUp(Close, MA20), Close, 0)`

### Layer 4. Execution Layer

Only validated rule graphs are evaluated.

This layer produces:

- entries
- holds
- exits
- reasons
- KPI
- failed examples

## Core Natural Language Domains

The mapping engine must eventually cover these domains.

### 1. Time / Bar Reference

- 현재봉
- 1봉전
- 2봉전
- n봉전
- n봉 이내
- 이후
- 전까지

### 2. Price / Candle Structure

- 종가
- 시가
- 고가
- 저가
- 양봉
- 음봉
- 몸통
- 윗꼬리
- 아랫꼬리

### 3. Indicator Reference

- JMA
- MACD
- RSI
- SuperTrend
- VWAP
- OBV
- TickIntensity
- TradeStrength
- VolumeMA
- VolumeMASlope

### 4. Transform / Window

- 20봉 최고가
- 20봉 최저가
- 이동평균
- 기울기
- 상승전환
- 하락전환
- 돌파
- 이탈

### 5. Trading State

- 매수 후
- 보유 중
- 목표수익 달성 후
- 목표 미달 중
- 분할매도
- 추가매수 금지

### 6. Market Context

- 시장강도
- 지수강도
- 업종순위
- 순위변화
- 뉴스동향
- 호가우위
- 보유정보

## Multi-Timeframe Model

Internally, every series should be represented with a timeframe-aware reference model.

Example internal reference forms:

- `tf.day.close(5)` = 5 bars ago on daily close
- `tf.minute(5).high(0)` = current 5-minute high
- `tf.minute(3).jma(14, offset:=1)` = previous bar 3-minute JMA

Important:

- users should not be required to write this,
- but the engine should be able to map natural language into this form.

Natural language examples:

- `5일전 종가`
- `5분봉 고가`
- `1봉전 jma`
- `1봉전 20봉 최고가`

## Critical Semantic Operators

The internal engine must support at least these concepts.

### Reference

- current value
- n-bars-ago value
- higher timeframe value

### Comparison

- greater than
- less than
- equal or above
- equal or below

### Event

- cross up
- cross down
- turn up
- turn down
- breakout
- breakdown

### Window

- within n bars
- highest over n bars
- lowest over n bars
- average over n bars

### Memory

- value when condition occurred
- bars since condition
- hold until another condition

## Validation Contract

The system must never silently discard meaning.

For every clause, validation must resolve to one of:

- `Supported`
- `Supported with rewrite`
- `Needs data source`
- `Ambiguous`

The previous `Unsupported` framing is too weak by itself.

The preferred interaction is:

- explain what is missing,
- offer a natural rewrite,
- allow immediate correction.

## Replacement Strategy

When the exact requested logic is not yet implemented, the system should provide the closest natural-language alternative.

Examples:

Requested:

- `시장강도가 급격히 냉각되면 보유 절반 청산`

If raw market strength is unavailable:

- `대체안: 지수강도 5봉 평균이 직전 5봉 평균보다 약해지면 절반 청산`

Requested:

- `틱강도 5이상이고 틱강도 5이평보다 크면 매수`

If tick auxiliary data is unavailable:

- `대체안: 거래량20 기울기 양수 + 거래량 평균 상회 + obv 상승추세이면 매수`

## Strategy Example Library

StrategyLab should also include a library of natural-language example prompts.

These examples should appear in the prompt workspace as recommendations.

### Example Categories

- breakout
- pullback
- trend continuation
- failed breakout recovery
- early session momentum
- news + orderflow reaction
- market-relative strength

### Example Templates

- `m3 supertrend 상승중이고 거래량20 기울기 양수이며 obv 상승추세이면 매수`
- `1봉전 20봉 최고가를 현재봉 종가가 돌파하면 매수`
- `목표 2% 달성 후 jma 하락전환시 매도`
- `목표 미달 중에는 supertrend 상승 유지 시 매도자제`
- `5분봉 jma 상승중이고 1분봉 눌림 후 종가가 1봉전 고가를 돌파하면 매수`

## Recommended Roadmap

### Phase 1. Reliable Natural-Language Validation

- clause splitter
- support / partial / rewrite classification
- rewrite suggestion panel
- evaluation gated by validation

### Phase 2. Time-Series Language Expansion

- 현재봉 / n봉전 / n봉 이내
- 돌파 / 이탈 / 전환
- 최고가 / 최저가 / 평균 / 기울기
- cross / turn / breakout natural language patterns

### Phase 3. Multi-Timeframe Support

- natural mapping to timeframe-aware references
- cross-frame comparison
- higher timeframe confirmation

### Phase 4. Contextual Data Integration

- tick intensity
- trade strength
- program trade
- market/index strength
- sector ranking
- holdings/account state
- news direction
- orderbook state

### Phase 5. User Function Layer

- named reusable natural-language blocks
- user templates
- scenario libraries

## Success Criteria

The system succeeds when:

- the user can write strategies only in natural language,
- validation catches mismatches before evaluation,
- accepted prompts map to executable rules with confidence,
- every executed trade can explain which written clause triggered it,
- users can iterate rapidly without touching DSL,
- the system continuously expands its language coverage instead of forcing syntax discipline on the user.

## Final Principle

StrategyLab should not become another formula editor.

It should become a natural-language strategy research environment where:

- users think in trading ideas,
- the system thinks in rule graphs,
- validation connects the two before money or time is wasted.
