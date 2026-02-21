using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Common.Models;

namespace App64.Services
{
    /// <summary>
    /// 사용자님의 자유로운 자연어를 정밀한 기술적 논리로 해석하는 지능형 브릿지.
    /// 정규표현식(Regex)과 키워드 매핑을 결합하여 '그냥 말하는 대로' 전략을 설계합니다.
    /// </summary>
    public static class StrategyBridge
    {
        public static StrategyDefinition CreateFromNaturalLanguage(string nlPrompt)
        {
            if (string.IsNullOrEmpty(nlPrompt)) return null;

            // 1. 매수(진입)와 매도(청산) 섹션 분리
            string buyPart = "";
            string sellPart = "";

            var splitBuy = Regex.Split(nlPrompt, "매수|진행|진입", RegexOptions.IgnoreCase);
            if (splitBuy.Length > 1)
            {
                buyPart = splitBuy[0];
                var nextPart = splitBuy[1];
                var splitSell = Regex.Split(nextPart, "매도|청산|탈출", RegexOptions.IgnoreCase);
                if (splitSell.Length > 1) { sellPart = splitSell[0]; } // 매수 뒤에 오는 매도 조건
                else { sellPart = nextPart; } // 매수 설명 이후 나머지가 매도일 가능성
            }
            else
            {
                // 구분자가 명확하지 않으면 쉼표로 분리 시도
                var clauses = nlPrompt.Split(',', '.');
                buyPart = clauses[0];
                if (clauses.Length > 1) sellPart = string.Join(",", clauses.Skip(1));
            }

            // 2. 조건 추출 및 변환
            var buyConditions = ParseConditions(buyPart, true);
            var sellConditions = ParseConditions(sellPart, false);

            if (buyConditions.Count == 0 && sellConditions.Count == 0) return null;

            // 3. 전략 조립
            string strategyName = "AI_Custom_" + DateTime.Now.ToString("HHmmss");
            var buyGate = new LogicGate("EntryGate", LogicalOperator.AND, buyConditions);
            var sellGate = new LogicGate("ExitGate", LogicalOperator.OR, sellConditions);

            // [추가] 필요 데이터 일수 계산
            int maxDays = 0;
            var allReqs = buyConditions.Concat(sellConditions).SelectMany(c => new[] { c.IndicatorA, c.IndicatorB }).Where(s => !string.IsNullOrEmpty(s));
            foreach (var req in allReqs)
            {
                // DAILY_HIGH_COND_{days}_{pct}
                var m = Regex.Match(req, @"DAILY_HIGH_COND_(\d+)_");
                if (m.Success)
                {
                    int d = int.Parse(m.Groups[1].Value);
                    if (d > maxDays) maxDays = d;
                }
            }

            return new StrategyDefinition(
                strategyName,
                "자연어 해석 전략: " + nlPrompt,
                new List<LogicGate> { buyGate },
                new List<LogicGate> { sellGate },
                nlPrompt 
            ) { RequiredDataDays = maxDays };
        }

        private static List<ConditionCell> ParseConditions(string part, bool isBuy)
        {
            var results = new List<ConditionCell>();
            if (string.IsNullOrWhiteSpace(part)) return results;

            int condId = 1;
            string prefix = isBuy ? "B" : "S";

            // [패넌 0] N일 중 ... 고가 돌파 (복합 로직 - 일봉 기준)
            var mComplex = Regex.Match(part, @"(\d+)\s*(일|봉)\s*중\s*.*(\d+)\s*%\s*이상\s*.*고가를?\s*(돌파|이상)", RegexOptions.IgnoreCase);
            if (mComplex.Success)
            {
                int days = int.Parse(mComplex.Groups[1].Value);
                int pct = int.Parse(mComplex.Groups[3].Value);
                string indicatorName = $"DAILY_HIGH_COND_{days}_{pct}";
                results.Add(new ConditionCell($"{prefix}{condId++}", $"{days}일중 {pct}%이상 상승일 고가 돌파", "Price", ComparisonOperator.CrossUp, indicatorName));
            }

            // 패턴 1: 시가대비 X% 돌파/이상/하락
            var mOpen = Regex.Match(part, @"시가대비\s*(\d+(\.\d+)?)\s*%?\s*(상승|하락)?\s*(돌파|이상|이하|초과|미만)", RegexOptions.IgnoreCase);
            if (mOpen.Success)
            {
                double val = double.Parse(mOpen.Groups[1].Value);
                if (mOpen.Groups[3].Value == "하락") val = -val;
                string opStr = mOpen.Groups[4].Value;
                results.Add(new ConditionCell($"{prefix}{condId++}", $"시가대비 {val}% {opStr}", "CHG_OPEN_PCT", MapOperator(opStr), null, val));
            }

            // 패턴 2: 틱강도 X 이상/돌파
            var mTick = Regex.Match(part, @"(틱강도|체결강도)\s*(\w+)?\s*가?\s*(\d+(\.\d+)?)\s*(이상|돌파|초과)", RegexOptions.IgnoreCase);
            if (mTick.Success)
            {
                double val = double.Parse(mTick.Groups[3].Value);
                results.Add(new ConditionCell($"{prefix}{condId++}", $"틱강도 {val} {mTick.Groups[5].Value}", "TICK_RAT", MapOperator(mTick.Groups[5].Value), null, val));
            }

            // 패턴 3: SuperTrend 상승/하락 추세
            string lowerPart = part.ToLower();
            if (lowerPart.Contains("supertrend") || lowerPart.Contains("슈퍼트렌드"))
            {
                if (lowerPart.Contains("상승추세") || lowerPart.Contains("위") || (isBuy && lowerPart.Contains("돌파")))
                    results.Add(new ConditionCell($"{prefix}{condId++}", "SuperTrend 상승 유지", "Price", ComparisonOperator.GreaterThan, "SuperTrend"));
                else if (lowerPart.Contains("하락추세") || lowerPart.Contains("아래") || (!isBuy && lowerPart.Contains("이탈")))
                    results.Add(new ConditionCell($"{prefix}{condId++}", "SuperTrend 하락 유지", "Price", ComparisonOperator.LessThan, "SuperTrend"));
            }

            // 패턴 4: 매도 특화 (VI 직전, 손절 등)
            if (!isBuy)
            {
                if (lowerPart.Contains("vi") && (lowerPart.Contains("직전") || lowerPart.Contains("근접")))
                {
                    results.Add(new ConditionCell($"{prefix}{condId++}", "VI 상한가 근접 (99% 도달)", "Price", ComparisonOperator.GreaterThanOrEqual, "VI_UP_99"));
                }

                var mStop = Regex.Match(part, @"(-?\d+)\s*%\s*(하락|이탈|손절|시)", RegexOptions.IgnoreCase);
                if (mStop.Success)
                {
                    double val = double.Parse(mStop.Groups[1].Value);
                    if (val > 0) val = -val; 
                    results.Add(new ConditionCell($"{prefix}{condId++}", $"손절매 ({val}%)", "PROFIT_PCT", ComparisonOperator.LessThanOrEqual, null, val));
                }

                var mPctRange = Regex.Match(part, @"(\d+(\.\d+)?)\s*%?\s*(상승|하락)?\s*(하면|시)", RegexOptions.IgnoreCase);
                if (mPctRange.Success)
                {
                    double val = double.Parse(mPctRange.Groups[1].Value);
                    bool isProfitTarget = part.Contains("추가") || part.Contains("수익") || part.Contains("진입");
                    
                    if (isProfitTarget)
                    {
                        if (mPctRange.Groups[3].Value != "하락")
                             results.Add(new ConditionCell($"{prefix}{condId++}", $"목표 수익률 ({val}%)", "PROFIT_PCT", ComparisonOperator.GreaterThanOrEqual, null, val));
                    }
                    else
                    {
                        if (mPctRange.Groups[3].Value == "하락")
                             results.Add(new ConditionCell($"{prefix}{condId++}", $"시가대비 {val}% 하락 매도", "CHG_OPEN_PCT", ComparisonOperator.LessThanOrEqual, null, -val));
                        else
                             results.Add(new ConditionCell($"{prefix}{condId++}", $"시가대비 {val}% 상승 매도", "CHG_OPEN_PCT", ComparisonOperator.GreaterThanOrEqual, null, val));
                    }
                }
            }

            // 패턴 5: 이평선 돌파/이탈
            var mMa = Regex.Match(part, @"(\d+)\s*(이평|MA|이동평균선)\s*(돌파|이탈|상향)", RegexOptions.IgnoreCase);
            if (mMa.Success)
            {
                string period = mMa.Groups[1].Value;
                string maName = "MA_" + period;
                string act = mMa.Groups[3].Value;
                var op = (act == "이탈") ? ComparisonOperator.CrossDown : ComparisonOperator.CrossUp;
                results.Add(new ConditionCell($"{prefix}{condId++}", $"{period}이평 {act}", "Price", op, maName));
            }

            // 패턴 6: MACD
            var mMacd = Regex.Match(part, @"(MACD)\s*(가|이)?\s*(시그널|선)?\s*(골든|데드|상향|하향)?크로스", RegexOptions.IgnoreCase);
            if (mMacd.Success)
            {
                bool isGold = part.Contains("골든") || part.Contains("상향");
                var op = isGold ? ComparisonOperator.CrossUp : ComparisonOperator.CrossDown;
                results.Add(new ConditionCell($"{prefix}{condId++}", $"MACD {(isGold ? "골든" : "데드")}크로스", "MACD_Line", op, "MACD_Signal"));
            }

            // 패턴 7: JMA
            var mJma = Regex.Match(part, @"(JMA)\s*(\d+)?\s*(상향|하향)?\s*(돌파|이탈|반전)", RegexOptions.IgnoreCase);
            if (mJma.Success)
            {
                string period = mJma.Groups[2].Success && !string.IsNullOrEmpty(mJma.Groups[2].Value) ? mJma.Groups[2].Value : "14";
                string act = mJma.Groups[4].Value;
                if (act == "반전") {
                    var isUp = part.Contains("상승") || part.Contains("상향");
                    results.Add(new ConditionCell($"{prefix}{condId++}", $"JMA({period}) {(isUp ? "상승" : "하락")}반전", $"JMA_{period}", isUp ? ComparisonOperator.CrossUp : ComparisonOperator.CrossDown, $"JMA_{period}_Prev"));
                } else {
                    var op = (act == "이탈" || mJma.Groups[3].Value == "하향") ? ComparisonOperator.CrossDown : ComparisonOperator.CrossUp;
                    results.Add(new ConditionCell($"{prefix}{condId++}", $"Price JMA({period}) {act}", "Price", op, $"JMA_{period}"));
                }
            }

            // 패턴 8: RSI
            var mRsi = Regex.Match(part, @"(RSI)\s*(\d+)?\s*(가|이)?\s*(\d+)\s*(이상|이하|돌파|이탈)", RegexOptions.IgnoreCase);
            if (mRsi.Success)
            {
                string period = mRsi.Groups[2].Success && !string.IsNullOrEmpty(mRsi.Groups[2].Value) ? mRsi.Groups[2].Value : "14";
                double val = double.Parse(mRsi.Groups[4].Value);
                string opStr = mRsi.Groups[5].Value;
                var op = MapOperator(opStr);
                results.Add(new ConditionCell($"{prefix}{condId++}", $"RSI({period}) {val} {opStr}", $"RSI_{period}", op, null, val));
            }

            // 패턴 9: 고래 체결 / 대량 거래 (Whale Flow / THI)
            var mWhale = Regex.Match(part, @"(고래|대량)\s*(매수|매도|수급)\s*(\d+)?\s*(억|백만)?\s*(이상|유입|포착)", RegexOptions.IgnoreCase);
            if (mWhale.Success)
            {
                bool isBuyWhale = mWhale.Groups[2].Value != "매도";
                double amount = mWhale.Groups[3].Success && !string.IsNullOrEmpty(mWhale.Groups[3].Value) ? double.Parse(mWhale.Groups[3].Value) : 1;
                if (mWhale.Groups[4].Value == "억") amount *= 100000000;
                
                if (part.Contains("유입") || part.Contains("포착")) {
                    results.Add(new ConditionCell($"{prefix}{condId++}", "고래 매수세 유입 (THI)", "THI_Signal", ComparisonOperator.GreaterThanOrEqual, null, 1));
                } else {
                    string ind = isBuyWhale ? "WHALE_BUY_VOL" : "WHALE_SELL_VOL";
                    results.Add(new ConditionCell($"{prefix}{condId++}", $"고래 {(isBuyWhale ? "매수" : "매도")} {mWhale.Groups[3].Value}{mWhale.Groups[4].Value} 이상", ind, ComparisonOperator.GreaterThanOrEqual, null, amount));
                }
            }

            // 패턴 10: 프로그램 순매수
            var mProg = Regex.Match(part, @"(프로그램|외인|기관)\s*(순매수|매수)?가?\s*(\d+)\s*(만주|주|억)?\s*(이상|돌파)", RegexOptions.IgnoreCase);
            if (mProg.Success)
            {
                double amount = double.Parse(mProg.Groups[3].Value);
                results.Add(new ConditionCell($"{prefix}{condId++}", $"프로그램 순매수 {amount} 이상", "PROGRAM_NET", ComparisonOperator.GreaterThanOrEqual, null, amount));
            }

            // 패턴 11: 볼린저밴드 (BB)
            var mBb = Regex.Match(part, @"(볼린저밴드|볼밴|BB)\s*(상한선|하한선|중심선|상단|하단)\s*(돌파|이탈|터치|근접)", RegexOptions.IgnoreCase);
            if (mBb.Success)
            {
                string line = mBb.Groups[2].Value;
                string act = mBb.Groups[3].Value;
                string bbVar = (line == "상한선" || line == "상단") ? "BB_UPPER" : ((line == "하한선" || line == "하단") ? "BB_LOWER" : "BB_MID");
                var op = (act == "이탈") ? ComparisonOperator.CrossDown : ComparisonOperator.CrossUp;
                if (act == "터치" || act == "근접") op = (line == "하한선" || line == "하단") ? ComparisonOperator.LessThanOrEqual : ComparisonOperator.GreaterThanOrEqual;
                
                results.Add(new ConditionCell($"{prefix}{condId++}", $"볼린저밴드 {line} {act}", "Price", op, bbVar));
            }

            return results;
        }

        private static ComparisonOperator MapOperator(string text)
        {
            if (text.Contains("돌파") || text.Contains("상향")) return ComparisonOperator.CrossUp;
            if (text.Contains("이탈") || text.Contains("하향")) return ComparisonOperator.CrossDown;
            if (text.Contains("이상") || text.Contains("초과")) return ComparisonOperator.GreaterThanOrEqual;
            if (text.Contains("이하") || text.Contains("미만")) return ComparisonOperator.LessThanOrEqual;
            return ComparisonOperator.GreaterThan;
        }
    }
}
