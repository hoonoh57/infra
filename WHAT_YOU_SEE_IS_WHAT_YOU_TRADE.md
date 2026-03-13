# What You See Is What You Trade

## Core Principle

The first job is not to stack every possible condition.

The first job is to quantify the exact visual moment that repeatedly appears at the start of strong continuation moves.

This principle can be summarized as:

- what you see on the chart
- must be explainable as a small set of measurable events
- and those events must be what the strategy actually trades

If the chart says one thing and the engine trades another, the research result is not trustworthy.

## Practical Goal

Find the start of real continuation moves by quantifying the common visual pattern.

In practice, this means detecting:

1. `Cross`
2. `Separation`
3. `FollowThrough`
4. `Box Filter`

These four ideas are enough to build a stable first research loop.

## 1. Cross

The move usually begins when price regains or crosses a meaningful baseline.

Typical visual forms:

- price crosses above JMA
- price reclaims SuperTrend
- price regains a prior pivot or box top
- OBV turns above its signal

This is the start signal, but not enough by itself.

Why:

- crosses happen often in noisy sideways ranges
- many crosses fail immediately

## 2. Separation

After the cross, the move must create enough distance from the baseline.

This is the key idea:

- not just `cross`
- but `cross + expansion`

Useful separation examples:

- `(Close - JMA) / JMA`
- `(Close - SuperTrend) / SuperTrend`
- breakout distance above a prior box top

If separation is too small, the move is often just noise.

## 3. FollowThrough

The best moves do not only expand once.

They maintain or extend that separation for the next few bars.

Typical checks:

- separation remains positive for 2 to 3 bars
- separation widens after the cross
- price does not immediately fall back below the baseline
- tick intensity / OBV / volume slope remain supportive

This is what turns a visual move into a tradable move.

## 4. Box Filter

Crosses inside sideways ranges are the main source of false entries.

So every `Cross + Separation + FollowThrough` model needs a box filter in front of it.

The box filter should answer:

- is the chart still in a tight range?
- is the breakout happening inside resistance congestion?
- is this just repeated whipsaw around the same baseline?

Without this layer, the system will overtrade.

## Minimal First-Stage Pattern

The first stable pattern to test is:

1. price crosses above JMA or reclaims SuperTrend
2. separation from JMA or SuperTrend exceeds a minimum threshold
3. that separation survives for 2 to 3 bars
4. the chart is not in a flat box / range state

Optional support conditions:

- OBV > OBV signal
- TickIntensity > TickIntensityAvg5
- Volume20 slope positive

These are support conditions, not the main visual trigger.

## Why This Is Better

This approach avoids two common traps:

- forcing too many indicators before defining the visual event
- treating every indicator equally instead of identifying the actual start structure

The real chart edge is often:

- structure first
- confirmation second

## Backtest Questions

Before building a large rule tree, the system should answer these questions:

### Cross Questions

- how often does a JMA or SuperTrend cross happen?
- how many of those happen in box conditions?
- how many lead to continuation vs immediate fade?

### Separation Questions

- what separation rate is too weak?
- what separation rate is strong enough?
- is JMA separation or SuperTrend separation more reliable?

### FollowThrough Questions

- is 2 bars enough?
- is 3 bars better?
- does continuation require stable OBV or TickIntensity?

### Box Questions

- how should box state be defined?
- flat slope?
- narrow range?
- repeated cross frequency?

## StrategyLab Implementation Path

The framework should become explicit in StrategyLab.

### A. Validation Layer

Prompt validation should highlight:

- Cross clause
- Separation clause
- FollowThrough clause
- Box / range exclusion clause

### B. Evaluation Layer

Each trade should keep:

- which cross triggered entry
- what separation rate existed
- how long follow-through survived
- whether a box filter blocked or allowed the setup

### C. Reporting Layer

Reports should summarize:

- cross frequency
- cross success rate
- average separation at winning entries
- average separation at losing entries
- failure rate inside box conditions

## Example Natural-Language Prompts

- `m3 jma 상승전환이고 supertrend 상승중이며 교차 직후 jma 이격률이 0.8% 이상이고 3봉 유지되면 매수`
- `m3 박스권이 아닌 구간에서 가격이 jma를 상승돌파하고 이격률이 1.0% 이상 유지되면 매수`
- `m5 supertrend 상승복귀 후 2봉 이내에 tickintensity > tickintensityavg5 이고 obv > obvsignal 이면 매수`
- `횡보구간에서는 상승교차를 무시하고, 확장구간에서만 교차 후 이격이 커질 때 매수`

## Research Rule

Do not jump directly to a final strategy.

First identify:

- what the strong move looked like,
- what the weak fake move looked like,
- where the first meaningful separation began,
- and what box conditions produced false crosses.

Then encode only that.

## Final Principle

The system must keep this promise:

- if the user sees the start of a move on the chart,
- the engine must be able to explain it,
- quantify it,
- and trade only that same event.

That is the meaning of:

`What you see is what you trade.`
