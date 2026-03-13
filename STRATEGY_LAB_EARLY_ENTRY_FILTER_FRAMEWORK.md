# StrategyLab Early-Entry Filter Framework

## Core Reality

The ideal target is clear:

- identify only the true continuation stocks,
- filter out weak rebound traps,
- extract about one strong candidate per day,
- trade only that candidate.

However, real experience shows that this is extremely difficult to achieve directly.

Therefore StrategyLab must support two modes of progress at the same time:

1. direct search for strong early-entry filters
2. incremental elimination of toxic trades

The second approach is the more reliable path.

## Main Objective

Increase the probability of entering true early-stage continuation stocks by continuously reducing bad entries.

This means the system should focus on:

- identifying which trades should never have happened,
- classifying those trades,
- building filters that remove them,
- measuring whether the removal improved practical quality.

## Two Strategic Paths

### Path A. Ideal Selector Path

Goal:

- build a condition search or strategy filter that extracts only top continuation candidates
- ideally around one stock per day

This is the dream state, but it is hard to reach early.

### Path B. Toxic Trade Removal Path

Goal:

- run the base logic over broad history,
- find the worst trade patterns,
- eliminate them one by one,
- accept even small improvements if they reduce structural loss.

This is the practical research path.

## Recommended Principle

Do not start by trying to find the perfect winning stock.

Start by removing the trades that clearly should not have been taken.

This is the key shift:

- not `find only great trades`
- but `stop taking obviously bad trades`

Over time, what remains becomes the real continuation candidate pool.

## Research Loop

1. Apply the current base logic to the full evaluation window.
2. Record all trades.
3. Identify the worst trades.
4. Classify them into toxic patterns.
5. Select one dominant toxic pattern.
6. Design one filter that removes that pattern.
7. Re-evaluate over the same period.
8. Compare:
   - win rate
   - avg return
   - trade count
   - max drawdown
   - toxic trade count
9. Keep the filter only if it improves structural quality.
10. Repeat.

## Why This Works

Because real edge often comes from many small removals, not one perfect insight.

The system should assume:

- a 0.001% increase in early-entry quality matters,
- repeated structural removals compound,
- reducing catastrophic mistakes is often more important than boosting best-case return.

## Toxic Trade Classification

Every losing or weak trade should be tagged into one or more failure classes.

### 1. Early Overheat Chase

Characteristics:

- entered after an excessive vertical move
- no controlled pullback
- poor reward-to-risk immediately after entry

Possible filters:

- reject if extension from VWAP / JMA is too large
- reject if candle body expansion is too extreme
- require pullback stability before entry

### 2. False Breakout

Characteristics:

- price briefly breaks resistance
- quickly loses momentum
- closes back below breakout level

Possible filters:

- require close above breakout level, not just wick
- require volume confirmation
- require higher timeframe trend confirmation

### 3. Isolated Move Without Theme Support

Characteristics:

- stock spikes alone
- related theme or sector does not confirm
- continuation probability is weak

Possible filters:

- require sector rank improvement
- require theme breadth expansion
- require related stocks strength confirmation

### 4. Fake Volume Expansion

Characteristics:

- strong volume impression
- poor follow-through
- volume spike lacks directional continuation

Possible filters:

- require OBV trend confirmation
- require Volume20 slope positive
- require tick intensity consistency, not single burst

### 5. Market Against Trade

Characteristics:

- stock setup looks valid
- index / market strength is weakening
- trade fails because broader market pressure dominates

Possible filters:

- require index strength above threshold
- avoid entry when market momentum is reversing down
- restrict long entries during weak market regime

### 6. No Real Hand-Change / No Accumulation

Characteristics:

- breakout occurs
- but no structural ownership transfer or accumulation signs
- move cannot sustain

Possible filters:

- require OBV > signal
- require sustained trade-strength behavior
- require program / institutional direction confirmation

### 7. News Flash Fade

Characteristics:

- driven by one headline only
- initial jump is sharp
- follow-through disappears quickly

Possible filters:

- require repeated follow-up news or theme spread
- require post-news consolidation success
- require not just headline but orderflow continuation

### 8. No Pullback Quality

Characteristics:

- entry occurs without a healthy pullback
- entry is too close to exhaustion

Possible filters:

- require controlled retrace
- require support reclaim after pullback
- require JMA / SuperTrend hold after impulse

## Continuation Classification

The system should also classify the trade context before entry.

### 1. Strong Continuation Type

Characteristics:

- strong trend continuation
- pullback holds
- volume / OBV / structure aligned
- theme and market support present

This is the ideal early-entry target.

### 2. Retail Trap Type

Characteristics:

- looks explosive
- attracts late retail buying
- fails quickly
- structure is not sustained

This must be aggressively filtered out.

### 3. Range / Noise Type

Characteristics:

- repeated fake moves
- insufficient expansion
- unclear follow-through

This should usually be excluded.

## Evaluation Metrics

The system must evaluate not only profit but structural improvement.

### Primary Metrics

- win rate
- avg return
- target hit rate
- max drawdown
- trade count

### Structural Metrics

- toxic trade count
- toxic trade ratio
- number of early overheat entries
- number of false breakout entries
- number of isolated-theme entries

### Stability Metrics

- performance by stock
- performance by date cluster
- performance by regime
- concentration risk in only top momentum names

## Keep / Reject Logic

A filter should not be accepted only because avg return increased.

A filter can be accepted if:

- avg return is stable or slightly lower,
- but toxic loss is clearly reduced,
- max drawdown improves,
- bad-trade frequency declines,
- continuation-entry quality improves.

This matters because some filters reduce headline return but improve true survivability.

## System Requirements

To support this framework, StrategyLab should provide:

- full trade log over the evaluation period
- toxic trade auto-classification
- failed-example extraction
- filter-before / filter-after comparison
- reason trace for every entry and exit
- batch evaluation over all watchlist names

## Research Output

The system should produce reports that answer:

- what trades worked
- what trades failed
- what type of failure dominated
- what filter was added
- what changed after the filter

This turns strategy development into knowledge accumulation.

## Long-Term Target

If enough toxic structures are removed, then a high-quality condition search becomes possible.

That is the point where the logic may realistically evolve into:

- one strong candidate per day,
- only high-quality continuation setups,
- reduced need for broad trade participation.

But that outcome should be treated as the result of repeated toxic-trade elimination, not the starting assumption.

## Final Principle

Do not chase the fantasy of a perfect selector too early.

Build a system that continuously removes bad trades.

That is how the probability of entering true continuation leaders increases over time.
