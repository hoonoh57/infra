using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.Models;
using Common.Enums;
using Microsoft.VisualBasic;

namespace App64.Forms
{
    public class ChartForm : DockContent
    {
        public string StockCode { get; }
        public string StockName { get; }

        private readonly Controls.FastChart _chart;
        private readonly Services.CandleService _candleService;

        public ChartForm(string stockCode, string stockName, Services.CandleService candleService)
        {
            StockCode = stockCode;
            StockName = stockName;
            _candleService = candleService;

            this.Text = $"{stockName} ({stockCode})";
            this.DockAreas = DockAreas.Document | DockAreas.Float;
            this.ShowHint = DockState.Document;

            _chart = new Controls.FastChart
            {
                Dock = DockStyle.Fill,
                StockCode = stockCode
            };
            _chart.IndicatorAddRequested += OnIndicatorAddRequested;
            _chart.IndicatorSettingsRequested += (s, e) => {
                var series = _chart.GetSeries(e);
                if (series != null) {
                    using (var dlg = new IndicatorSettingsForm(series)) {
                        if (dlg.ShowDialog() == DialogResult.OK) _chart.RefreshChart();
                    }
                }
            };
            _chart.IndicatorDeleted += (s, e) => {
                // Handle indicator deletion if needed
            };
            _chart.ComparisonSymbolRequested += OnComparisonSymbolRequested;

            this.Controls.Add(_chart);
        }

        private void OnIndicatorAddRequested(object sender, string indicatorName)
        {
            if (_chart.Data == null || _chart.Data.Count == 0) return;

            switch (indicatorName)
            {
                case "MA":
                    var ma20 = Services.IndicatorCalculation.CalculateMA(_chart.Data, 20, SkiaSharp.SKColors.Yellow);
                    _chart.AddSeries(ma20);
                    break;
                case "RSI":
                    var rsi = Services.IndicatorCalculation.CalculateRSI(_chart.Data, 14);
                    if (rsi != null) _chart.AddSeries(rsi);
                    break;
                case "SuperTrend":
                    var st = Services.IndicatorCalculation.CalculateSuperTrend(_chart.Data);
                    if (st != null) _chart.AddSeries(st);
                    break;
                case "MACD":
                    var macd = Services.IndicatorCalculation.CalculateMACD(_chart.Data);
                    if (macd != null) _chart.AddSeries(macd);
                    break;
            }
        }

        public void LoadChartData(string code, string name, System.Collections.Generic.List<Common.Models.CandleData> candles)
        {
            _chart.LoadStockData(code, name, candles);
        }

        /// <summary>
        /// 실시간 시세 수신 시 MainForm에서 호출. 이 차트의 종목코드와 일치하는 경우만 갱신.
        /// </summary>
        public void OnMarketDataUpdated(MarketData md)
        {
            if (md.Code != StockCode) return;
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(new System.Action<MarketData>(OnMarketDataUpdated), md);
                return;
            }

            _chart.UpdateRealtime((float)md.Price, md.Volume, md.Time);

            // [추가] THI(WhaleFlow) 예측 정보 가져오기
            var thi = Services.AgentManager.Instance.GetTHIPrediction(md.Code);
            if (thi.Prob > 0)
            {
                _chart.UpdateTHIPrediction(thi.Prob, thi.Move, thi.Note);
            }
        }

        private async void OnComparisonSymbolRequested(object sender, string symbol)
        {
            string targetCode = symbol;
            if (symbol == "KOSPI") targetCode = "001"; // KOSPI 지수 코드 예시
            else if (symbol == "DIALOG")
            {
                targetCode = Interaction.InputBox("비교할 종목코드를 입력하세요", "비교종목", "005930");
                if (string.IsNullOrEmpty(targetCode)) return;
            }

            try
            {
                var candles = await _candleService.GetCandlesAsync(targetCode, CandleType.Minute, 1, 900);
                if (candles != null && candles.Count > 0)
                {
                    _chart.AddComparisonSeries(targetCode, new List<CandleData>(candles));
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("비교종목 로드 실패: " + ex.Message);
            }
        }
    }
}
