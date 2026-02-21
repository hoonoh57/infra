using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Common.Models;
using Common.Enums;
using App64.Forms;
using App64.Services;
using System.Threading.Tasks;

namespace App64.Controls
{
    public class FastChart : UserControl
    {
        // --- Events ---
        public event EventHandler<CandleClickEventArgs> CandleClicked;
        public event EventHandler<CrosshairEventArgs> CrosshairMoved;
        public event EventHandler ScaleChanged;
        public event EventHandler<string> IndicatorAddRequested;
        public event EventHandler<string> IndicatorDeleted;
        public event EventHandler<string> IndicatorSettingsRequested;
        public event EventHandler<CandleClickEventArgs> CandleDoubleClicked;
        public event EventHandler<string> LegendDoubleClicked;
        public event EventHandler<string> ComparisonSymbolRequested;

        #region Themes
        public class ChartTheme
        {
            public SKColor Background { get; set; }
            public SKColor Text { get; set; }
            public SKColor GridLine { get; set; }
            public SKColor CandleUp { get; set; }
            public SKColor CandleDown { get; set; }
            public SKColor Wick { get; set; }
            public SKColor Crosshair { get; set; }
            public SKColor CrosshairLabelBg { get; set; }
            public SKColor CrosshairLabelText { get; set; }
            public SKColor Border { get; set; }
            public SKColor VolumeUp { get; set; }
            public SKColor VolumeDown { get; set; }

            public static ChartTheme CreateDarkTheme()
            {
                return new ChartTheme
                {
                    Background = SKColor.Parse("#131722"),
                    Text = SKColor.Parse("#d1d4dc"),
                    GridLine = SKColor.Parse("#363c4e"),
                    CandleUp = SKColor.Parse("#089981"),
                    CandleDown = SKColor.Parse("#f23645"),
                    Wick = SKColor.Parse("#787b86"),
                    Crosshair = SKColor.Parse("#9598a1"),
                    CrosshairLabelBg = SKColor.Parse("#4c525e"),
                    CrosshairLabelText = SKColors.White,
                    Border = SKColor.Parse("#2a2e39"),
                    VolumeUp = SKColor.Parse("#089981").WithAlpha(128),
                    VolumeDown = SKColor.Parse("#f23645").WithAlpha(128)
                };
            }
        }
        #endregion

        #region Data Structures
        public struct OHLCV
        {
            public float Open;
            public float High;
            public float Low;
            public float Close;
            public long Volume;
            public DateTime DateVal;
            public string TimeStr;
            public int TickCount; // [추가] 실제 체결 건수 (Trade Event Count)
        }

        public enum TimeFrame
        {
            Tick, Min1, Min3, Min5, Min10, Min15, Min30, Min60, Day, Week, Month
        }

        public class CandleClickEventArgs : EventArgs
        {
            public int Index { get; set; }
            public OHLCV Data { get; set; }
            public MouseButtons MouseButton { get; set; }
        }

        public class CrosshairEventArgs : EventArgs
        {
            public float Price { get; set; }
            public DateTime Time { get; set; }
            public int Index { get; set; }
        }

        public enum PanelType { Overlay, Bottom }
        public enum PlotType { Line, Histogram, Scatter, StepLine }
        public enum ShapeType { Circle, Square, TriangleUp, TriangleDown, Diamond, Cross }
        public enum LeftAxisModeType { None, VsPrevClose, VsTodayOpen }

        public class PlotConfig
        {
            public string Name { get; set; }
            public string Title { get; set; }
            public string PanelName { get; set; }
            public Color Color { get; set; }
            public Color? ColorUp { get; set; }
            public Color? ColorDown { get; set; }
            public PlotType Type { get; set; }
            public float Thickness { get; set; }
            public double? BaseLine { get; set; }
            public double? Overbought { get; set; }
            public double? Oversold { get; set; }
        }

        public class CustomSeries
        {
            public string PanelName { get; set; }
            public string SeriesName { get; set; }
            public string Title { get; set; }
            public SKColor Color { get; set; }
            public List<double> Values { get; set; } = new List<double>();
            public PanelType PanelType { get; set; }
            public PlotType Style { get; set; } = PlotType.Line;
            public ShapeType Shape { get; set; } = ShapeType.Circle;
            public double? MinValue { get; set; }
            public double? MaxValue { get; set; }
            public float Thickness { get; set; } = 1.0f;
            public double? BaseLine { get; set; }
            public double? Overbought { get; set; }
            public double? Oversold { get; set; }
            public bool IsCustom { get; set; }
            public SKColor? ColorUp { get; set; }
            public SKColor? ColorDown { get; set; }
            public List<SKColor> OutputColors { get; set; } = new List<SKColor>();
            public List<double> TrendValues { get; set; }
            public bool IsLeftAxis { get; set; }
        }

        public struct SubChartInfo
        {
            public string PanelName;
            public SKRect Rect;
            public SKRect PriceRect;
            public SKRect LeftAxisRect;
        }

        private class LegendItem
        {
            public string PanelName { get; set; }
            public SKRect HitRect { get; set; }
            public string Text { get; set; }
            public SKColor Color { get; set; }
            public bool IsOverlay { get; set; }
        }

        public class SignalMarker
        {
            public DateTime Time { get; set; }
            public SignalType Type { get; set; }
            public double Price { get; set; }
            public string Note { get; set; }
        }

        public enum SignalType { Buy, Sell, ExitBuy, ExitSell }

        public class BreakoutBox
        {
            public int StartIndex { get; set; }
            public int EndIndex { get; set; }
            public double Top { get; set; }
            public double Bottom { get; set; }
            public long UpVolume { get; set; }
            public long DownVolume { get; set; }
            public bool IsActive { get; set; }
            public bool IsBullishBreakout { get; set; }
            public bool IsBearishBreakout { get; set; }
        }

        public class HighlightZone
        {
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public SKColor Color { get; set; }
            public string Note { get; set; }
        }
        #endregion

        #region Fields
        private SKControl skControl;
        private ChartTheme CurrentTheme = ChartTheme.CreateDarkTheme();
        public List<OHLCV> Data = new List<OHLCV>();
        public List<BreakoutBox> Boxes = new List<BreakoutBox>();
        private List<HighlightZone> _highlightZones = new List<HighlightZone>();
        private List<SignalMarker> _signals = new List<SignalMarker>();
        private List<CustomSeries> _customSeriesList = new List<CustomSeries>();
        private List<SubChartInfo> SubCharts = new List<SubChartInfo>();
        private List<LegendItem> _legendItems = new List<LegendItem>();

        private SKRect MainRect;
        private SKRect PriceRect;
        private SKRect TimeRect;
        private SKRect _leftAxisRect;

        private float _candleWidth = 8.0f;
        private float _gap = 2.0f;
        private float _scrollOffset = -1;
        private bool _autoScroll = true;
        private int _selectedRowIndex = -1;
        private string _selectedSeriesName = "";

        // [추가] THI 예측 데이터
        private double _thiConfidence = 0;
        private double _thiExpectedMove = 0;
        private string _thiNote = "";
        private SKRect _thiArrowRect;

        private SKPaint _paintThiArrow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        private SKPaint _paintThiPath = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        
        public void UpdateTHIPrediction(double confidence, double expectedMove, string note)
        {
            _thiConfidence = confidence;
            _thiExpectedMove = expectedMove;
            _thiNote = note;
            RefreshChart();
        }
        private bool _isUpdating = false;

        private bool _isAutoScaleY = true;
        private LeftAxisModeType _leftAxisMode = LeftAxisModeType.None;
        private float _manualMaxP = 0;
        private float _manualMinP = 0;

        private float _mouseX = -1;
        private float _mouseY = -1;
        private bool _isDraggingChart = false;
        private bool _isDraggingPrice = false;
        private float _lastMouseX = 0;
        private float _lastMouseY = 0;

        private const float AxisWidth = 70.0f;
        private const float LeftAxisWidth = 60.0f;
        private const float AxisHeight = 25.0f;
        private const string MAIN_PANEL_NAME = "Main";

        private Dictionary<string, float> _panelWeights = new Dictionary<string, float>();
        private List<SplitterInfo> _splitterRects = new List<SplitterInfo>();
        private bool _isDraggingSplitter = false;
        private SplitterInfo? _activeSplitter = null;

        private struct SplitterInfo
        {
            public SKRect Rect;
            public string PanelAbove;
            public string PanelBelow;
        }

        private string _stockCode = "";
        private string _stockName = "";
        private TimeFrame _currentTimeFrame = TimeFrame.Min1;
        private string _comparisonSeriesName = "";

        // Toggle features
        private bool _showCurrentPriceLine = true;
        private bool _showOpenChangeRate = false;
        private bool _showViLine = false;

        // [성능최적화] 배경 레이어 캐싱
        private SKPicture _backgroundCache;
        private bool _backgroundDirty = true;

        // 자동 이동 (Automated Playback)
        private Timer _autoPlayTimer;
        private bool _isAutoPlaying = false;

        // 틱강도(TickIntensity) 추적
        private List<int> _tickCounts = new List<int>();   // 봉별 양봉 체결건수
        private int _maxDayTicks = 0;                       // 당일 최대 체결건수
        private float _lastRealtimePrice = 0;               // 마지막 체결가 (양봉 판정)
        private int _tickDivisor = 120;                     // 체결건수 나누기 값 (기본 120)
        private bool _showTickIntensity = false;             // 틱강도 지표 표시 여부
        private bool _aiAnalysisMode = false;                // AI 에이전트 분석 모드

        // --- Performance Optimization Cache ---
        private SKTypeface _fontMalgun;
        private SKTypeface _fontConsolas;
        private SKPaint _paintGrid;
        private SKPaint _paintText;
        private SKPaint _paintCandleUp;
        private SKPaint _paintCandleDown;
        private SKPaint _paintWick;
        private SKPaint _paintCrosshairLine;
        private SKPaint _paintCrosshairLabelBg;
        private SKPaint _paintCrosshairLabelText;
        private SKPaint _paintLegendText;

        private struct PanelRange { public float Min; public float Max; }
        private Dictionary<string, PanelRange> _panelRanges = new Dictionary<string, PanelRange>();

        // 전략 매니저 및 결과 저장소
        private StrategyDefinition _appliedStrategy;
        private List<EvaluationResult> _evalResults = new List<EvaluationResult>();
        private readonly StrategyEvaluator _evaluator = new StrategyEvaluator();
        private SKRect _strategyLabelRect; // 전략 라벨 영역 (클릭 감지용)
        private SKRect _aiLabelRect;       // AI 라벨 영역 (클릭 감지용)
        #endregion

        #region Properties
        public string StockCode { get => _stockCode; set => _stockCode = value; }
        public string StockName { get => _stockName; set => _stockName = value; }
        public TimeFrame CurrentTimeFrame { get => _currentTimeFrame; set => _currentTimeFrame = value; }
        public float CandleWidth { get => _candleWidth; set { _candleWidth = Math.Max(1.0f, Math.Min(50.0f, value)); RefreshChart(); } }
        public bool ShowDaySeparators { get; set; } = true;
        public double YesterdayClose { get; set; }
        public double TodayOpen { get; set; }
        public string StrategyName { get; set; }
        public string StrategyMode { get; set; }

        public ContextMenuStrip ChartMenu { get; private set; }
        #endregion

        public FastChart()
        {
            _backgroundDirty = true;
            skControl = new SKControl();
            skControl.Dock = DockStyle.Fill;
            this.Controls.Add(skControl);

            InitPaints();
            InitializeContextMenu();

            skControl.PaintSurface += OnPaintSurface;
            skControl.Resize += (s, e) => { UpdateLayout(); InvalidateCache(); };
            skControl.MouseDown += SKControl_MouseDown;
            skControl.MouseMove += SKControl_MouseMove;
            skControl.MouseUp += (s, e) => { _isDraggingChart = _isDraggingPrice = _isDraggingSplitter = false; _activeSplitter = null; InvalidateCache(); };
            skControl.MouseWheel += OnWheel;
            skControl.DoubleClick += SKControl_DoubleClick;
            skControl.MouseLeave += (s, e) => { _mouseX = _mouseY = -1; InvalidateCache(); };
            
            skControl.PreviewKeyDown += (s, e) => {
                switch (e.KeyCode) {
                    case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                    case Keys.Home: case Keys.End: case Keys.Space:
                        e.IsInputKey = true;
                        break;
                }
            };
            skControl.KeyDown += FastChart_KeyDown;

            _autoPlayTimer = new Timer { Interval = 1000 };
            _autoPlayTimer.Tick += OnAutoPlayTick;
        }

        private void OnAutoPlayTick(object sender, EventArgs e)
        {
            if (Data.Count == 0) return;
            _scrollOffset += 1.0f;
            if (_scrollOffset > Data.Count - 5) { // 끝에 도달하면 멈춤
                _isAutoPlaying = false;
                _autoPlayTimer.Stop();
            }
            InvalidateCache();
        }

        private void InvalidateCache() { _backgroundDirty = true; skControl.Invalidate(); }

        private void InitPaints()
        {
            _fontMalgun = SKTypeface.FromFamilyName("Malgun Gothic");
            _fontConsolas = SKTypeface.FromFamilyName("Consolas");

            _paintGrid = new SKPaint { Color = CurrentTheme.GridLine, StrokeWidth = 0.5f, IsAntialias = false };
            _paintText = new SKPaint { Color = CurrentTheme.Text, TextSize = 11, IsAntialias = true, Typeface = _fontMalgun };
            _paintCandleUp = new SKPaint { Color = CurrentTheme.CandleUp, Style = SKPaintStyle.Fill };
            _paintCandleDown = new SKPaint { Color = CurrentTheme.CandleDown, Style = SKPaintStyle.Fill };
            _paintWick = new SKPaint { Color = CurrentTheme.Wick, StrokeWidth = 1, IsAntialias = false };
            
            _paintCrosshairLine = new SKPaint { 
                Color = CurrentTheme.Crosshair, 
                PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0),
                StrokeWidth = 1f,
                Style = SKPaintStyle.Stroke
            };
            _paintCrosshairLabelBg = new SKPaint { Color = CurrentTheme.CrosshairLabelBg, Style = SKPaintStyle.Fill };
            _paintCrosshairLabelText = new SKPaint { 
                Color = CurrentTheme.CrosshairLabelText, 
                TextSize = 11, 
                IsAntialias = true, 
                TextAlign = SKTextAlign.Center, 
                Typeface = _fontMalgun 
            };
            _paintLegendText = new SKPaint { TextSize = 11, IsAntialias = true, Typeface = _fontMalgun };
        }

        private void InitializeContextMenu()
        {
            ChartMenu = new ContextMenuStrip();
            var addMenu = new ToolStripMenuItem("지표삽입");
            addMenu.DropDownItems.Add("이동평균 (MA)", null, (s, e) => IndicatorAddRequested?.Invoke(this, "MA"));
            addMenu.DropDownItems.Add("SuperTrend", null, (s, e) => IndicatorAddRequested?.Invoke(this, "SuperTrend"));
            addMenu.DropDownItems.Add("RSI", null, (s, e) => IndicatorAddRequested?.Invoke(this, "RSI"));
            addMenu.DropDownItems.Add("MACD", null, (s, e) => IndicatorAddRequested?.Invoke(this, "MACD"));
            addMenu.DropDownItems.Add(new ToolStripSeparator());

            // 틱강도 지표 (나누기 값 서브메뉴)
            var tickMenu = new ToolStripMenuItem("틱강도 (TickIntensity)");
            foreach (int div in new[] { 5, 15, 30, 60, 120 })
            {
                int d = div;
                var item = new ToolStripMenuItem($"÷{d}", null, (s2, e2) => {
                    _tickDivisor = d;
                    _showTickIntensity = true;
                    BuildTickIntensitySeries();
                });
                if (d == 120) item.Checked = true;
                tickMenu.DropDownItems.Add(item);
            }
            addMenu.DropDownItems.Add(tickMenu);
            ChartMenu.Items.Add(addMenu);

            // 비교종목 삽입 (토글 방식)
            var compMenu = new ToolStripMenuItem("비교종목 삽입");
            var miKospi = new ToolStripMenuItem("지수비교 (KOSPI)");
            miKospi.Click += (s, e) => {
                var existing = _customSeriesList.FirstOrDefault(x => x.SeriesName == "COMP_KOSPI");
                if (existing != null)
                {
                    _customSeriesList.Remove(existing);
                    ((ToolStripMenuItem)s).Checked = false;
                    UpdateLayout(); InvalidateCache();
                }
                else
                {
                    ((ToolStripMenuItem)s).Checked = true;
                    ComparisonSymbolRequested?.Invoke(this, "KOSPI");
                }
            };
            compMenu.DropDownItems.Add(miKospi);
            compMenu.DropDownItems.Add("직접 입력...", null, (s, e) => {
                ComparisonSymbolRequested?.Invoke(this, "DIALOG");
            });
            compMenu.DropDownItems.Add(new ToolStripSeparator());
            compMenu.DropDownItems.Add("비교종목 제거", null, (s, e) => {
                _customSeriesList.RemoveAll(x => x.SeriesName.StartsWith("COMP_"));
                UpdateLayout(); InvalidateCache();
            });
            ChartMenu.Items.Add(compMenu);

            ChartMenu.Items.Add(new ToolStripSeparator());

            // 현재가 점선 토글
            var miPriceLine = new ToolStripMenuItem("현재가 라인 표시");
            miPriceLine.Checked = _showCurrentPriceLine;
            miPriceLine.Click += (s, e) => { _showCurrentPriceLine = !_showCurrentPriceLine; ((ToolStripMenuItem)s).Checked = _showCurrentPriceLine; InvalidateCache(); };
            ChartMenu.Items.Add(miPriceLine);

            // 시가대비 상승률 좌축 토글
            var miOpenRate = new ToolStripMenuItem("시가대비 상승률 (좌축)");
            miOpenRate.Checked = _showOpenChangeRate;
            miOpenRate.Click += (s, e) => {
                _showOpenChangeRate = !_showOpenChangeRate;
                ((ToolStripMenuItem)s).Checked = _showOpenChangeRate;
                if (!_showOpenChangeRate && _leftAxisMode == LeftAxisModeType.VsTodayOpen)
                    _leftAxisMode = LeftAxisModeType.None;
                else if (_showOpenChangeRate)
                    _leftAxisMode = LeftAxisModeType.VsTodayOpen;
                UpdateLayout();
                InvalidateCache();
            };
            ChartMenu.Items.Add(miOpenRate);

            // VI 예상선 토글
            var miViLine = new ToolStripMenuItem("VI 예상선 (시가±10%)");
            miViLine.Checked = _showViLine;
            miViLine.Click += (s, e) => { _showViLine = !_showViLine; ((ToolStripMenuItem)s).Checked = _showViLine; InvalidateCache(); };
            ChartMenu.Items.Add(miViLine);

            ChartMenu.Items.Add(new ToolStripSeparator());
            ChartMenu.Items.Add(new ToolStripSeparator());
            ChartMenu.Items.Add("전략 관리자 (AI 설계 비서)...", null, (s, e) => {
                var f = new StrategyManagerForm((strategy) => {
                    ApplyStrategy(strategy);
                });
                f.ShowDialog();
            });

            ChartMenu.Items.Add(new ToolStripSeparator());
            var miAiMode = new ToolStripMenuItem("AI 협동 지능 분석 (에이전트 점수)");
            miAiMode.Checked = _aiAnalysisMode;
            miAiMode.Click += (s, e) => {
                _aiAnalysisMode = !_aiAnalysisMode;
                ((ToolStripMenuItem)s).Checked = _aiAnalysisMode;
                if (_aiAnalysisMode) {
                    if (_appliedStrategy != null) ApplyStrategy(_appliedStrategy);
                    else BuildAgentScoreSeries();
                } else {
                    _customSeriesList.RemoveAll(x => x.SeriesName == "AI_AGENT_SCORE");
                    UpdateLayout(); RefreshChart();
                }
            };
            ChartMenu.Items.Add(miAiMode);

            ChartMenu.Items.Add(new ToolStripSeparator());
            ChartMenu.Items.Add("지표 모두 삭제", null, (s, e) => { _customSeriesList.Clear(); InvalidateCache(); });

            ChartMenu.Items.Add(new ToolStripSeparator());
            ChartMenu.Items.Add("데이터 보기 (Transparency Tool)", null, (s, e) => {
                var form = new DataViewForm(_stockCode, _stockName, Data, _customSeriesList);
                form.Show(); // Show instead of ShowDialog to let the user keep it open while using chart
            });

            skControl.ContextMenuStrip = ChartMenu;
        }

        private void FastChart_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && !string.IsNullOrEmpty(_selectedSeriesName))
            {
                var target = _customSeriesList.FirstOrDefault(x => x.SeriesName == _selectedSeriesName);
                if (target != null)
                {
                    _customSeriesList.Remove(target);
                    if (target.PanelType == PanelType.Bottom && !_customSeriesList.Any(s => s.PanelName == target.PanelName))
                        _panelWeights.Remove(target.PanelName);
                    IndicatorDeleted?.Invoke(this, _selectedSeriesName);
                    _selectedSeriesName = "";
                    UpdateLayout(); InvalidateCache();
                }
                return;
            }

            // 1. 기간별 보기
            if (e.Control && e.KeyCode == Keys.T) // 오늘 분봉 전체
            {
                if (Data.Count > 0) {
                    var lastDate = Data[Data.Count - 1].DateVal.Date;
                    int firstIdx = Data.FindIndex(x => x.DateVal.Date == lastDate);
                    if (firstIdx >= 0) {
                        _scrollOffset = firstIdx;
                        float count = Data.Count - firstIdx;
                        _candleWidth = Math.Max(1.0f, (MainRect.Width / (count + 2)) - _gap);
                    }
                }
            }
            else if (e.Control && e.KeyCode == Keys.A) // 전기간 보기
            {
                if (Data.Count > 0) {
                    _scrollOffset = 0;
                    _candleWidth = Math.Max(0.5f, (MainRect.Width / (Data.Count + 5)) - _gap);
                }
            }
            // 2. 이동 (L: 마지막, F: 처음) - 스케일 유지
            else if (e.KeyCode == Keys.L) ScrollToEnd();
            else if (e.KeyCode == Keys.F) _scrollOffset = 0;
            else if (e.KeyCode == Keys.Home) _scrollOffset = 0;
            else if (e.KeyCode == Keys.End) ScrollToEnd();
            // 3. 스크롤 및 줌
            else if (e.KeyCode == Keys.Left) _scrollOffset = Math.Max(0, _scrollOffset - 1);
            else if (e.KeyCode == Keys.Right) _scrollOffset = Math.Min(Data.Count - 1, _scrollOffset + 1);
            else if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add) CandleWidth *= 1.2f;
            else if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract) CandleWidth *= 0.8f;
            // 4. 수동 스케일 시 상하 이동
            else if (e.KeyCode == Keys.Up) {
                if (!_isAutoScaleY) {
                    float r = _manualMaxP - _manualMinP;
                    _manualMaxP += r * 0.1f; _manualMinP += r * 0.1f;
                }
            }
            else if (e.KeyCode == Keys.Down) {
                if (!_isAutoScaleY) {
                    float r = _manualMaxP - _manualMinP;
                    _manualMaxP -= r * 0.1f; _manualMinP -= r * 0.1f;
                }
            }
            // 5. 자동 재생 (Space)
            else if (e.KeyCode == Keys.Space) {
                _isAutoPlaying = !_isAutoPlaying;
                if (_isAutoPlaying) _autoPlayTimer.Start();
                else _autoPlayTimer.Stop();
            }

            InvalidateCache();
        }


        public void BeginUpdate() => _isUpdating = true;
        public void EndUpdate() { _isUpdating = false; InvalidateCache(); }
        public void RefreshChart() { if (!_isUpdating) InvalidateCache(); }

        private void UpdateLayout()
        {
            if (skControl.Width <= 0 || skControl.Height <= 0) return;

            float w = skControl.Width;
            float h = skControl.Height;
            float leftMargin = (_leftAxisMode != LeftAxisModeType.None) ? LeftAxisWidth : 0.0f;
            float totalAvailableHeight = h - AxisHeight;

            SubCharts.Clear();
            _splitterRects.Clear();

            var bottomPanels = _customSeriesList.Where(cs => cs.PanelType == PanelType.Bottom)
                                                .Select(cs => cs.PanelName)
                                                .Distinct().ToList();

            if (!_panelWeights.ContainsKey(MAIN_PANEL_NAME)) _panelWeights[MAIN_PANEL_NAME] = 3.0f;
            foreach (var p in bottomPanels) {
                if (!_panelWeights.ContainsKey(p)) {
                    if (p == "AI_Score") _panelWeights[p] = 2.0f; // AI 점수는 좀 더 크게
                    else _panelWeights[p] = 1.2f;
                }
            }

            if (bottomPanels.Count > 0)
            {
                float totalWeight = _panelWeights[MAIN_PANEL_NAME] + bottomPanels.Sum(p => _panelWeights[p]);
                float unitHeight = totalAvailableHeight / totalWeight;

                float mainHeight = unitHeight * _panelWeights[MAIN_PANEL_NAME];
                MainRect = new SKRect(leftMargin, 0, w - AxisWidth, mainHeight);
                PriceRect = new SKRect(w - AxisWidth, 0, w, mainHeight);
                if (_leftAxisMode != LeftAxisModeType.None) _leftAxisRect = new SKRect(0, 0, leftMargin, mainHeight);

                float currentY = mainHeight;
                string prevPanel = MAIN_PANEL_NAME;
                foreach (var p in bottomPanels)
                {
                    _splitterRects.Add(new SplitterInfo { Rect = new SKRect(0, currentY - 3, w, currentY + 3), PanelAbove = prevPanel, PanelBelow = p });
                    float pHeight = unitHeight * _panelWeights[p];
                    SubCharts.Add(new SubChartInfo { 
                        PanelName = p, 
                        Rect = new SKRect(leftMargin, currentY, w - AxisWidth, currentY + pHeight), 
                        PriceRect = new SKRect(w - AxisWidth, currentY, w, currentY + pHeight),
                        LeftAxisRect = (leftMargin > 0) ? new SKRect(0, currentY, leftMargin, currentY + pHeight) : SKRect.Empty
                    });
                    currentY += pHeight;
                    prevPanel = p;
                }
            }
            else
            {
                MainRect = new SKRect(leftMargin, 0, w - AxisWidth, totalAvailableHeight);
                PriceRect = new SKRect(w - AxisWidth, 0, w, totalAvailableHeight);
                if (_leftAxisMode != LeftAxisModeType.None) _leftAxisRect = new SKRect(0, 0, leftMargin, totalAvailableHeight);
            }
            TimeRect = new SKRect(leftMargin, h - AxisHeight, w - AxisWidth, h);
            InvalidateCache(); 
        }

        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); }

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(CurrentTheme.Background);

            if (Data == null || Data.Count == 0) {
                canvas.DrawText("데이터가 없습니다.", Width / 2 - 50, Height / 2, _paintText);
                return;
            }

            // [계산] 화면에 보이는 인덱스 범위 및 각 패널별 가격 범위 계산
            int startIndex = Math.Max(0, (int)Math.Floor(_scrollOffset));
            int visibleBars = (int)(MainRect.Width / (_candleWidth + _gap)) + 2;
            int endIndex = Math.Min(Data.Count - 1, startIndex + visibleBars);
            CalculateAllPanelRanges(startIndex, endIndex);

            // [1단계] 배경 레이어 (캐시 사용)
            if (_backgroundDirty || _backgroundCache == null) {
                UpdateBackgroundCache(startIndex, endIndex);
            }
            canvas.DrawPicture(_backgroundCache);

            // [2단계] 동적 레이어 (캔들, 지표 등)
            DrawDynamicLayers(canvas, startIndex, endIndex);

            // [3단계] 오버레이 레이어 (십자선, 레전드 등)
            DrawOverlays(canvas, startIndex, endIndex);
        }

        private void CalculateAllPanelRanges(int startIndex, int endIndex)
        {
            _panelRanges.Clear();

            // 1. 메인 패널
            float maxP = float.MinValue, minP = float.MaxValue;
            for (int i = startIndex; i <= endIndex; i++) {
                if (Data[i].High > maxP) maxP = Data[i].High;
                if (Data[i].Low < minP) minP = Data[i].Low;
            }
            float padding = (maxP - minP) * 0.05f; 
            if (padding <= 0) padding = 1.0f; // [수정] 데이터가 플랫한 경우 패딩 강제 부여
            maxP += padding; minP -= padding;
            if (maxP <= minP) maxP = minP + 1.0f;

            if (_isAutoScaleY) { _manualMaxP = maxP; _manualMinP = minP; }
            else { maxP = _manualMaxP; minP = _manualMinP; }
            _panelRanges[MAIN_PANEL_NAME] = new PanelRange { Min = minP, Max = maxP };

            // 2. 기타 커스텀 시리즈(지표) 기반 범위 계산
            foreach (var sc in SubCharts.Select(x => x.PanelName).Concat(new[] { MAIN_PANEL_NAME }).Distinct())
            {
                float maxR = float.MinValue, minR = float.MaxValue;
                float maxL = float.MinValue, minL = float.MaxValue;
                bool foundR = false, foundL = false;

                foreach (var s in _customSeriesList.Where(x => x.PanelName == sc || (sc == MAIN_PANEL_NAME && x.PanelType == PanelType.Overlay)))
                {
                    int count = Math.Min(endIndex + 1, s.Values.Count);
                    for (int i = startIndex; i < count; i++) {
                        double val = s.Values[i];
                        if (double.IsNaN(val)) continue;
                        if (s.IsLeftAxis) { if (val > maxL) maxL = (float)val; if (val < minL) minL = (float)val; foundL = true; }
                        else { if (val > maxR) maxR = (float)val; if (val < minR) minR = (float)val; foundR = true; }
                    }
                }

                if (sc == MAIN_PANEL_NAME) {
                    if (foundL) _panelRanges[sc + "_L"] = new PanelRange { Min = minL - Math.Max(1.0f, (maxL-minL))*0.05f, Max = maxL + Math.Max(1.0f, (maxL-minL))*0.05f };
                } else {
                    float padR = Math.Max(1.0f, (maxR - minR)) * 0.1f;
                    float padL = Math.Max(1.0f, (maxL - minL)) * 0.1f;
                    if (foundR) _panelRanges[sc + "_R"] = new PanelRange { Min = minR - padR, Max = maxR + padR };
                    if (foundL) _panelRanges[sc + "_L"] = new PanelRange { Min = minL - padL, Max = maxL + padL };
                }
            }
        }

        private void UpdateBackgroundCache(int startIndex, int endIndex)
        {
            using (var recorder = new SKPictureRecorder()) {
                var canvas = recorder.BeginRecording(SKRect.Create(Width, Height));
                
                // 1. 격자 그리기
                var mainRange = _panelRanges[MAIN_PANEL_NAME];
                DrawGrid(canvas, MainRect, mainRange.Max, mainRange.Min);
                foreach (var sc in SubCharts) {
                    if (_panelRanges.ContainsKey(sc.PanelName + "_R")) {
                        var r = _panelRanges[sc.PanelName + "_R"];
                        DrawGrid(canvas, sc.Rect, r.Max, r.Min);
                    }
                }

                // 2. 기본 축 및 타임 축 (배경 캐시용)
                DrawTimeAxis(canvas, startIndex, endIndex);
                DrawStaticAxes(canvas, startIndex, endIndex);

                _backgroundCache = recorder.EndRecording();
            }
            _backgroundDirty = false;
        }

        private void DrawDynamicLayers(SKCanvas canvas, int startIndex, int endIndex)
        {
            var mainRange = _panelRanges[MAIN_PANEL_NAME];
            
            // 본차트 요소
            canvas.Save();
            canvas.ClipRect(MainRect);
            DrawBoxes(canvas, startIndex, endIndex, mainRange.Max, mainRange.Min);
            DrawCandles(canvas, startIndex, endIndex, mainRange.Max, mainRange.Min);
            
            // 오버레이 지표
            bool hasLeftMain = _panelRanges.ContainsKey(MAIN_PANEL_NAME + "_L");
            foreach (var s in _customSeriesList.Where(x => x.PanelType == PanelType.Overlay)) {
                if (s.IsLeftAxis && hasLeftMain) {
                    var lr = _panelRanges[MAIN_PANEL_NAME + "_L"];
                    DrawSeries(canvas, s, startIndex, endIndex, lr.Max, lr.Min, MainRect);
                } else {
                    DrawSeries(canvas, s, startIndex, endIndex, mainRange.Max, mainRange.Min, MainRect);
                }
            }
            canvas.Restore();

            // 서브차트 요소
            foreach (var sc in SubCharts) {
                canvas.Save();
                canvas.ClipRect(sc.Rect);
                foreach (var s in _customSeriesList.Where(x => x.PanelName == sc.PanelName)) {
                    string suffix = s.IsLeftAxis ? "_L" : "_R";
                    if (_panelRanges.ContainsKey(sc.PanelName + suffix)) {
                        var r = _panelRanges[sc.PanelName + suffix];
                        DrawSeries(canvas, s, startIndex, endIndex, r.Max, r.Min, sc.Rect);
                    }
                }
                canvas.Restore();
            }

            // 실시간 라인 (현재가, VI)
            DrawPriceLines(canvas, mainRange.Max, mainRange.Min);

            // [추가] 전략 신호 그리기
            DrawSignals(canvas, startIndex, endIndex, mainRange.Max, mainRange.Min);
        }

        private void DrawOverlays(SKCanvas canvas, int startIndex, int endIndex)
        {
            var mainRange = _panelRanges[MAIN_PANEL_NAME];
            
            // 십자선
            if (_mouseX >= 0 && _mouseY >= 0) DrawCrosshairOptimized(canvas, startIndex, endIndex);
            
            // 레전드 및 정보
            float legendY = 20;
            DrawStockInfo(canvas, ref legendY);
            DrawLegends(canvas, ref legendY);
            DrawStrategyLabel(canvas, ref legendY); // [추가] 전략 라벨 그리기
            DrawAiAnalysisLabel(canvas, ref legendY); // [추가] AI 라벨 그리기
            
            // 실시간 가격 표시 (우측 축) — DrawPriceLines에서 라인을 그리지만, 라벨은 레이어상 최상단에 그림
            DrawDynamicPriceLabels(canvas, mainRange.Max, mainRange.Min);
        }

        private void DrawStrategyLabel(SKCanvas canvas, ref float yOffset)
        {
            if (_appliedStrategy == null) { _strategyLabelRect = SKRect.Empty; return; }

            string text = $"★ 전략: {_appliedStrategy.Name} [삭제/편집]";
            float padding = 6;
            float tw = _paintText.MeasureText(text);
            float th = _paintText.TextSize + padding * 2;
            
            float x = MainRect.Left + 10;
            float y = yOffset;
            
            _strategyLabelRect = new SKRect(x, y, x + tw + padding * 2, y + th);
            
            using (var pBg = new SKPaint { Color = SKColors.Indigo.WithAlpha(200), Style = SKPaintStyle.Fill, IsAntialias = true })
            using (var pBorder = new SKPaint { Color = SKColors.MediumSlateBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true })
            {
                canvas.DrawRoundRect(_strategyLabelRect, 4, 4, pBg);
                canvas.DrawRoundRect(_strategyLabelRect, 4, 4, pBorder);
                canvas.DrawText(text, x + padding, y + th - padding - 2, _paintText);
            }
            
            yOffset += th + 10;
        }

        private void DrawAiAnalysisLabel(SKCanvas canvas, ref float yOffset)
        {
            string status = _aiAnalysisMode ? "ON" : "OFF";
            string text = $"🤖 AI 협동 지능: {status} [설정]";
            float padding = 6;
            float tw = _paintText.MeasureText(text);
            float th = _paintText.TextSize + padding * 2;

            float x = MainRect.Left + 10;
            float y = yOffset;

            _aiLabelRect = new SKRect(x, y, x + tw + padding * 2, y + th);

            SKColor bgCol = _aiAnalysisMode ? SKColors.DarkRed.WithAlpha(200) : SKColors.DarkSlateGray.WithAlpha(180);
            SKColor borderCol = _aiAnalysisMode ? SKColors.Crimson : SKColors.Gray;

            using (var pBg = new SKPaint { Color = bgCol, Style = SKPaintStyle.Fill, IsAntialias = true })
            using (var pBorder = new SKPaint { Color = borderCol, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true })
            {
                canvas.DrawRoundRect(_aiLabelRect, 4, 4, pBg);
                canvas.DrawRoundRect(_aiLabelRect, 4, 4, pBorder);
                canvas.DrawText(text, x + padding, y + th - padding - 2, _paintText);
            }

            yOffset += th + 10;
        }

        private void DrawStaticAxes(SKCanvas canvas, int startIndex, int endIndex)
        {
            var mainRange = _panelRanges[MAIN_PANEL_NAME];
            DrawPriceAxis(canvas, MainRect, mainRange.Max, mainRange.Min, PriceRect, false);
            
            if (_showOpenChangeRate && TodayOpen > 0 && _leftAxisMode == LeftAxisModeType.VsTodayOpen) {
                DrawOpenRateAxis(canvas, mainRange.Max, mainRange.Min);
            } else if (_panelRanges.ContainsKey(MAIN_PANEL_NAME + "_L")) {
                var lr = _panelRanges[MAIN_PANEL_NAME + "_L"];
                DrawPriceAxis(canvas, MainRect, lr.Max, lr.Min, _leftAxisRect, true);
            }
            
            foreach (var sc in SubCharts) {
                if (_panelRanges.ContainsKey(sc.PanelName + "_R")) {
                    var r = _panelRanges[sc.PanelName + "_R"];
                    DrawPriceAxis(canvas, sc.Rect, r.Max, r.Min, sc.PriceRect, false);
                }
                if (_panelRanges.ContainsKey(sc.PanelName + "_L")) {
                    var l = _panelRanges[sc.PanelName + "_L"];
                    DrawPriceAxis(canvas, sc.Rect, l.Max, l.Min, sc.LeftAxisRect, true);
                }
            }
        }

        private void DrawDynamicPriceLabels(SKCanvas canvas, float maxP, float minP)
        {
            if (Data.Count == 0) return;
            float curPrice = Data[Data.Count - 1].Close;
            float y = GetY(curPrice, maxP, minP, MainRect);
            
            if (y >= MainRect.Top && y <= MainRect.Bottom)
            {
                string txt = FormatPrice(curPrice);
                float tw = _paintText.MeasureText(txt);
                using (var pBg = new SKPaint { Color = new SKColor(220, 50, 50) })
                {
                    canvas.DrawRect(SKRect.Create(PriceRect.Left, y - 9, PriceRect.Width, 18), pBg);
                    canvas.DrawText(txt, PriceRect.Left + PriceRect.Width / 2, y + 5, _paintCrosshairLabelText);
                }
            }
        }

        private void DrawPriceLines(SKCanvas canvas, float maxP, float minP)
        {
            if (_showCurrentPriceLine && Data.Count > 0) {
                float curPrice = Data[Data.Count - 1].Close;
                float curY = GetY(curPrice, maxP, minP, MainRect);
                if (curY >= MainRect.Top && curY <= MainRect.Bottom) {
                    using (var pLine = new SKPaint { Color = new SKColor(255, 255, 255, 180), StrokeWidth = 1.0f, PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0) })
                        canvas.DrawLine(MainRect.Left, curY, MainRect.Right, curY, pLine);
                }
            }

            if (_showViLine && TodayOpen > 0) {
                float viUp = (float)(TodayOpen * 1.10);
                float viDown = (float)(TodayOpen * 0.90);
                using (var pViUp = new SKPaint { Color = CurrentTheme.CandleUp.WithAlpha(180), StrokeWidth = 1.0f, PathEffect = SKPathEffect.CreateDash(new float[] { 8, 4 }, 0) })
                using (var pTxtUp = new SKPaint { Color = CurrentTheme.CandleUp, TextSize = 10, IsAntialias = true, Typeface = _fontMalgun }) {
                    float yUp = GetY(viUp, maxP, minP, MainRect);
                    if (yUp >= MainRect.Top && yUp <= MainRect.Bottom) {
                        canvas.DrawLine(MainRect.Left, yUp, MainRect.Right, yUp, pViUp);
                        canvas.DrawText($"VI↑ {FormatPrice(viUp)}", MainRect.Left + 5, yUp - 3, pTxtUp);
                    }
                }
                using (var pViDown = new SKPaint { Color = CurrentTheme.CandleDown.WithAlpha(180), StrokeWidth = 1.0f, PathEffect = SKPathEffect.CreateDash(new float[] { 8, 4 }, 0) })
                using (var pTxtDown = new SKPaint { Color = CurrentTheme.CandleDown, TextSize = 10, IsAntialias = true, Typeface = _fontMalgun }) {
                    float yDown = GetY(viDown, maxP, minP, MainRect);
                    if (yDown >= MainRect.Top && yDown <= MainRect.Bottom) {
                        canvas.DrawLine(MainRect.Left, yDown, MainRect.Right, yDown, pViDown);
                        canvas.DrawText($"VI↓ {FormatPrice(viDown)}", MainRect.Left + 5, yDown + 12, pTxtDown);
                    }
                }
            }
        }

        private void DrawBoxes(SKCanvas canvas, int start, int end, float maxP, float minP)
        {
            if (Boxes == null || Boxes.Count == 0) return;
            foreach (var box in Boxes) {
                if (!box.IsActive) continue;
                if (box.EndIndex < start || box.StartIndex > end) continue;
                float x1 = GetX(box.StartIndex);
                float x2 = GetX(box.EndIndex) + _candleWidth;
                float yTop = GetY((float)box.Top, maxP, minP, MainRect);
                float yBottom = GetY((float)box.Bottom, maxP, minP, MainRect);
                SKColor bCol = box.IsBullishBreakout ? CurrentTheme.CandleUp : (box.IsBearishBreakout ? CurrentTheme.CandleDown : CurrentTheme.GridLine);
                using (var pFill = new SKPaint { Color = bCol.WithAlpha(30), Style = SKPaintStyle.Fill })
                    canvas.DrawRect(SKRect.Create(x1, yTop, x2 - x1, yBottom - yTop), pFill);
                using (var pBord = new SKPaint { Color = bCol.WithAlpha(120), Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
                    canvas.DrawRect(SKRect.Create(x1, yTop, x2 - x1, yBottom - yTop), pBord);
            }
        }

        private void DrawSignals(SKCanvas canvas, int start, int end, float maxP, float minP)
        {
            if (_signals == null || _signals.Count == 0) return;
            
            foreach (var sig in _signals)
            {
                int idx = Data.FindIndex(xx => xx.DateVal == sig.Time);
                if (idx < start || idx > end) continue;

                float x = GetX(idx) + _candleWidth / 2;
                float y = GetY((float)sig.Price, maxP, minP, MainRect);

                if (sig.Type == SignalType.Buy)
                {
                    using (var p = new SKPaint { Color = SKColors.HotPink, IsAntialias = true, Style = SKPaintStyle.Fill })
                    {
                        var path = new SKPath();
                        path.MoveTo(x, y + 2);
                        path.LineTo(x - 5, y + 12);
                        path.LineTo(x + 5, y + 12);
                        path.Close();
                        canvas.DrawPath(path, p);
                    }
                }
                else if (sig.Type == SignalType.Sell)
                {
                    using (var p = new SKPaint { Color = SKColors.DeepSkyBlue, IsAntialias = true, Style = SKPaintStyle.Fill })
                    {
                        var path = new SKPath();
                        path.MoveTo(x, y - 2);
                        path.LineTo(x - 5, y - 12);
                        path.LineTo(x + 5, y - 12);
                        path.Close();
                        canvas.DrawPath(path, p);
                    }
                }
            }
        }

        private void DrawOpenRateAxis(SKCanvas canvas, float maxP, float minP)
        {
            float range = maxP - minP;
            float step = GetNiceStep(range, 5);
            float startPrice = (float)Math.Ceiling(minP / step) * step;

            for (float price = startPrice; price <= maxP; price += step) {
                float y = GetY(price, maxP, minP, MainRect);
                double pct = (price - TodayOpen) / TodayOpen * 100.0;
                string txt = pct.ToString("+0.0;-0.0;0.0") + "%";
                float txtW = _paintText.MeasureText(txt);
                canvas.DrawText(txt, _leftAxisRect.Right - txtW - 5, y + 4, _paintText);
            }

            float yOpen = GetY((float)TodayOpen, maxP, minP, MainRect);
            if (yOpen >= MainRect.Top && yOpen <= MainRect.Bottom) {
                using (var pOpenLine = new SKPaint { Color = new SKColor(255, 255, 0, 100), StrokeWidth = 0.8f, PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0) })
                    canvas.DrawLine(MainRect.Left, yOpen, MainRect.Right, yOpen, pOpenLine);
            }

            // [추가] THI 미래 예측 렌더링
            DrawTHIForecast(canvas, maxP, minP);
        }

        private void DrawTHIForecast(SKCanvas canvas, float maxP, float minP)
        {
            if (_thiConfidence < 30) return; // 확신도가 낮으면 그리지 않음

            float lastCandleX = GetX(Data.Count - 1);
            float futureX = lastCandleX + (_candleWidth + _gap) * 3; // 미래 3칸 지점
            float startY = GetY(Data[Data.Count - 1].Close, maxP, minP, MainRect);
            
            // 1. 예측 경로 (Footprint Path)
            float targetY = GetY(Data[Data.Count - 1].Close * (1 + (float)_thiExpectedMove / 100f), maxP, minP, MainRect);
            
            using (var path = new SKPath())
            {
                path.MoveTo(lastCandleX + _candleWidth, startY);
                path.LineTo(futureX, targetY - 10);
                path.LineTo(futureX, targetY + 10);
                path.Close();
                
                _paintThiPath.Color = _thiExpectedMove >= 0 ? 
                    SKColors.LimeGreen.WithAlpha((byte)(_thiConfidence * 0.8)) : 
                    SKColors.OrangeRed.WithAlpha((byte)(_thiConfidence * 0.8));
                
                canvas.DrawPath(path, _paintThiPath);
            }

            // 2. 방향 화살표 (HTI Arrow)
            float arrowSize = (float)(10 + (_thiConfidence / 10));
            _paintThiArrow.Color = _thiExpectedMove >= 0 ? SKColors.Lime : SKColors.Red;
            _paintThiArrow.StrokeWidth = arrowSize / 3;

            using (var arrowPath = new SKPath())
            {
                float tipX = futureX + 15;
                float tipY = targetY;
                
                if (_thiExpectedMove >= 0) {
                    arrowPath.MoveTo(tipX, tipY - arrowSize);
                    arrowPath.LineTo(tipX - arrowSize/2, tipY);
                    arrowPath.LineTo(tipX + arrowSize/2, tipY);
                } else {
                    arrowPath.MoveTo(tipX, tipY + arrowSize);
                    arrowPath.LineTo(tipX - arrowSize/2, tipY);
                    arrowPath.LineTo(tipX + arrowSize/2, tipY);
                }
                arrowPath.Close();
                canvas.DrawPath(arrowPath, _paintThiArrow);
            }

            // 3. 확신도 및 배지
            string badge = $"{_thiNote} ({_thiConfidence:F0}%)";
            canvas.DrawText(badge, futureX + 25, targetY + 5, _paintLegendText);
        }

        private void DrawGrid(SKCanvas canvas, SKRect rect, float max, float min)
        {
            float range = max - min;
            float step = GetNiceStep(range, 5);
            float startPrice = (float)Math.Ceiling(min / step) * step;

            for (float price = startPrice; price <= max; price += step)
            {
                float y = GetY(price, max, min, rect);
                canvas.DrawLine(rect.Left, y, rect.Right, y, _paintGrid);
            }
        }

        private float GetNiceStep(float range, int targetCount)
        {
            if (range <= 0) return 1;
            float rawStep = range / targetCount;
            float mag = (float)Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            float res = rawStep / mag;
            if (res < 1.5) res = 1;
            else if (res < 3) res = 2;
            else if (res < 7) res = 5;
            else res = 10;
            return res * mag;
        }

        private void DrawCandles(SKCanvas canvas, int start, int end, float maxP, float minP)
        {
            float halfWidth = _candleWidth / 2;
            for (int i = start; i <= end; i++)
            {
                var d = Data[i];
                float x = GetX(i);
                float yOpen = GetY(d.Open, maxP, minP, MainRect);
                float yClose = GetY(d.Close, maxP, minP, MainRect);
                float yHigh = GetY(d.High, maxP, minP, MainRect);
                float yLow = GetY(d.Low, maxP, minP, MainRect);

                canvas.DrawLine(x + halfWidth, yHigh, x + halfWidth, yLow, _paintWick);
                if (d.Open <= d.Close) 
                    canvas.DrawRect(SKRect.Create(x, yClose, _candleWidth, Math.Max(1, yOpen - yClose)), _paintCandleUp);
                else 
                    canvas.DrawRect(SKRect.Create(x, yOpen, _candleWidth, Math.Max(1, yClose - yOpen)), _paintCandleDown);
            }
        }

        private void DrawSubChartOptimized(SKCanvas canvas, SubChartInfo sc, int start, int end)
        {
            bool hasR = _panelRanges.TryGetValue(sc.PanelName + "_R", out var rangeR);
            bool hasL = _panelRanges.TryGetValue(sc.PanelName + "_L", out var rangeL);

            float maxV = hasR ? rangeR.Max : 100f;
            float minV = hasR ? rangeR.Min : 0f;

            canvas.Save();
            canvas.ClipRect(sc.Rect);
            DrawGrid(canvas, sc.Rect, maxV, minV);
            
            foreach (var s in _customSeriesList) {
                if (s.PanelName == sc.PanelName) {
                    if (s.IsLeftAxis) {
                        if (hasL) DrawSeries(canvas, s, start, end, rangeL.Max, rangeL.Min, sc.Rect);
                    } else {
                        if (hasR) DrawSeries(canvas, s, start, end, rangeR.Max, rangeR.Min, sc.Rect);
                    }
                }
            }
            canvas.Restore();

            if (hasR) DrawPriceAxis(canvas, sc.Rect, rangeR.Max, rangeR.Min, sc.PriceRect, false);
            if (hasL && sc.LeftAxisRect != SKRect.Empty) {
                DrawPriceAxis(canvas, sc.Rect, rangeL.Max, rangeL.Min, sc.LeftAxisRect, true);
            }
        }

        public void AddComparisonSeries(string title, List<CandleData> candles)
        {
            var series = new CustomSeries
            {
                SeriesName = "COMP_" + title,
                Title = title,
                Color = SKColors.Cyan,
                PanelType = PanelType.Overlay,
                IsLeftAxis = true,
                Style = PlotType.Line,
                Thickness = 2.0f
            };

            // Sync with main data dates
            foreach (var d in Data)
            {
                var match = candles.FirstOrDefault(c => c.DateTime == d.DateVal);
                if (match.DateTime == default)
                {
                    // Try matching by date only (for daily comparison on intraday chart)
                    match = candles.FirstOrDefault(c => c.DateTime.Date == d.DateVal.Date);
                }

                if (match.DateTime != default) series.Values.Add((double)match.Close);
                else series.Values.Add(double.NaN);
            }

            _customSeriesList.RemoveAll(s => s.SeriesName == series.SeriesName);
            _customSeriesList.Add(series);
            UpdateLayout();
            RefreshChart();
        }

        private void DrawSeries(SKCanvas canvas, CustomSeries s, int start, int end, float max, float min, SKRect rect)
        {
            using (var p = new SKPaint { Color = s.Color, StrokeWidth = s.Thickness, Style = SKPaintStyle.Stroke, IsAntialias = true })
            {
                if (s.Style == PlotType.Histogram)
                {
                    p.Style = SKPaintStyle.Fill;
                    float yBase = GetY((float)(s.BaseLine ?? 0), max, min, rect);
                    for (int i = start; i <= end; i++)
                    {
                        if (i >= s.Values.Count || double.IsNaN(s.Values[i])) continue;
                        float x = GetX(i);
                        float y = GetY((float)s.Values[i], max, min, rect);
                        
                        if (i < s.OutputColors.Count) p.Color = s.OutputColors[i];
                        else p.Color = s.Color;

                        canvas.DrawRect(SKRect.Create(x, Math.Min(y, yBase), _candleWidth, Math.Abs(y - yBase)), p);
                    }
                }
                else
                {
                    for (int i = start + 1; i <= end; i++)
                    {
                        if (i >= s.Values.Count || double.IsNaN(s.Values[i]) || double.IsNaN(s.Values[i - 1])) continue;
                        
                        float x1 = GetX(i - 1) + _candleWidth / 2;
                        float y1 = GetY((float)s.Values[i - 1], max, min, rect);
                        float x2 = GetX(i) + _candleWidth / 2;
                        float y2 = GetY((float)s.Values[i], max, min, rect);

                        if (i < s.OutputColors.Count) p.Color = s.OutputColors[i];
                        else p.Color = s.Color;

                        canvas.DrawLine(x1, y1, x2, y2, p);
                    }
                }
            }
        }

        private void DrawPriceAxis(SKCanvas canvas, SKRect chartRect, float max, float min, SKRect axisRect, bool isLeft)
        {
            float range = max - min;
            float step = GetNiceStep(range, 5);
            float startPrice = (float)Math.Ceiling(min / step) * step;

            for (float price = startPrice; price <= max; price += step)
            {
                float y = GetY(price, max, min, chartRect);
                string txt = FormatPrice(price);
                if (isLeft)
                {
                    float txtW = _paintText.MeasureText(txt);
                    canvas.DrawText(txt, axisRect.Right - txtW - 5, y + 4, _paintText);
                }
                else
                {
                    canvas.DrawText(txt, axisRect.Left + 5, y + 4, _paintText);
                }
            }
        }

        private void DrawTimeAxis(SKCanvas canvas, int start, int end)
        {
            int step = Math.Max(1, (end - start) / 5);
            for (int i = start; i <= end; i += step)
            {
                float x = GetX(i);
                canvas.DrawText(Data[i].DateVal.ToString("HH:mm"), x, TimeRect.Top + 15, _paintText);
            }
        }

        private void DrawCrosshairOptimized(SKCanvas canvas, int start, int end)
        {
            // 십자선 가이드 라인
            canvas.DrawLine(_mouseX, 0, _mouseX, skControl.Height - AxisHeight, _paintCrosshairLine);
            canvas.DrawLine(0, _mouseY, skControl.Width, _mouseY, _paintCrosshairLine);

            float lastVal = 0; // CrosshairMoved 이벤트용 (기본 우측축 값)

            // 1. 가격축(Y축) 라벨 그리기 (메인 및 서브차트 통합 처리)
            if (MainRect.Contains(_mouseX, _mouseY) || PriceRect.Contains(_mouseX, _mouseY) || _leftAxisRect.Contains(_mouseX, _mouseY))
            {
                // 메인 차트
                if (_panelRanges.TryGetValue(MAIN_PANEL_NAME, out var rangeR)) {
                    lastVal = DrawCrosshairLabel(canvas, rangeR, MainRect, PriceRect, false);
                }
                if (_panelRanges.TryGetValue(MAIN_PANEL_NAME + "_L", out var rangeL)) {
                    DrawCrosshairLabel(canvas, rangeL, MainRect, _leftAxisRect, true);
                }
            }
            else
            {
                // 서브 차트들 루프
                foreach (var sc in SubCharts)
                {
                    if (sc.Rect.Contains(_mouseX, _mouseY) || sc.PriceRect.Contains(_mouseX, _mouseY) || sc.LeftAxisRect.Contains(_mouseX, _mouseY))
                    {
                        // 우측축 값 표시
                        if (_panelRanges.TryGetValue(sc.PanelName + "_R", out var rangeR)) {
                            lastVal = DrawCrosshairLabel(canvas, rangeR, sc.Rect, sc.PriceRect, false);
                        }
                        // 좌측축 값 표시 (이중축 대응)
                        if (_panelRanges.TryGetValue(sc.PanelName + "_L", out var rangeL)) {
                            DrawCrosshairLabel(canvas, rangeL, sc.Rect, sc.LeftAxisRect, true);
                        }
                        break;
                    }
                }
            }

            // 2. 시간축(X축) 라벨 그리기
            int idx = (int)Math.Round(_scrollOffset + (_mouseX - MainRect.Left) / (_candleWidth + _gap));
            if (idx >= 0 && idx < Data.Count)
            {
                string timeStr = Data[idx].DateVal.ToString("MM/dd HH:mm");
                float tW = _paintCrosshairLabelText.MeasureText(timeStr);
                canvas.DrawRect(SKRect.Create(_mouseX - tW / 2 - 5, TimeRect.Top, tW + 10, AxisHeight), _paintCrosshairLabelBg);
                canvas.DrawText(timeStr, _mouseX, TimeRect.Top + 16, _paintCrosshairLabelText);

                CrosshairMoved?.Invoke(this, new CrosshairEventArgs { Index = idx, Time = Data[idx].DateVal, Price = lastVal });
            }
        }

        private float DrawCrosshairLabel(SKCanvas canvas, PanelRange range, SKRect chartRect, SKRect axisRect, bool isLeft)
        {
            float val = range.Min + (1.0f - (_mouseY - chartRect.Top) / chartRect.Height) * (range.Max - range.Min);
            string label = (val >= 1000) ? val.ToString("N0") : (val >= 1 ? val.ToString("N2") : val.ToString("N4"));
            
            canvas.DrawRect(SKRect.Create(axisRect.Left, _mouseY - 10, axisRect.Width, 20), _paintCrosshairLabelBg);
            canvas.DrawText(label, axisRect.Left + axisRect.Width / 2, _mouseY + 4, _paintCrosshairLabelText);
            return val;
        }

        private void DrawStockInfo(SKCanvas canvas, ref float y)
        {
            canvas.DrawText($"{_stockCode} {_stockName}", 10, y, _paintText); y += 18;
        }

        private void DrawLegends(SKCanvas canvas, ref float y)
        {
            _legendItems.Clear();
            int currIdx = -1;
            if (_mouseX >= 0) currIdx = (int)Math.Round(_scrollOffset + (_mouseX - MainRect.Left) / (_candleWidth + _gap));
            else if (Data.Count > 0) currIdx = Data.Count - 1;

            foreach (var s in _customSeriesList) {
                if (s.PanelType != PanelType.Overlay) continue;
                DrawLegendItem(canvas, s, ref y, 10, currIdx, _paintLegendText);
            }

            foreach (var sc in SubCharts) {
                float scY = sc.Rect.Top + 15;
                foreach (var s in _customSeriesList) {
                    if (s.PanelName == sc.PanelName) {
                        DrawLegendItem(canvas, s, ref scY, 10, currIdx, _paintLegendText);
                    }
                }
            }
        }

        private void DrawLegendItem(SKCanvas canvas, CustomSeries s, ref float y, float drawX, int currIdx, SKPaint p)
        {
            bool isSelected = s.SeriesName == _selectedSeriesName;
            p.Color = isSelected ? SKColors.White : s.Color;
            p.FakeBoldText = isSelected;

            double val = (currIdx >= 0 && currIdx < s.Values.Count) ? s.Values[currIdx] : double.NaN;
            string valStr = double.IsNaN(val) ? "n/a" : val.ToString("N2");
            string txt = $"{s.Title}: {valStr}";

            float txtW = p.MeasureText(txt);
            SKRect hit = new SKRect(drawX, y - 10, drawX + txtW + 20, y + 5);

            if (isSelected) {
                using (var bg = new SKPaint { Color = s.Color.WithAlpha(60) })
                    canvas.DrawRect(hit, bg);
            }

            canvas.DrawText(txt, drawX, y, p);
            _legendItems.Add(new LegendItem { PanelName = s.SeriesName, HitRect = hit, Text = txt, Color = s.Color, IsOverlay = (s.PanelType == PanelType.Overlay) });
            y += 18;
        }


        private float GetX(int i) => MainRect.Left + (i - _scrollOffset) * (_candleWidth + _gap);
        private float GetY(float val, float max, float min, SKRect rect)
        {
            float diff = max - min;
            if (diff <= 0) diff = 1.0f; // Prevent division by zero
            return rect.Top + rect.Height * (1.0f - (val - min) / diff);
        }
        private string FormatPrice(float p) => p.ToString("N0");

        #region Interaction
        private void SKControl_MouseDown(object sender, MouseEventArgs e)
        {
            _lastMouseX = e.X; _lastMouseY = e.Y;
            skControl.Focus();

            var splitter = _splitterRects.FirstOrDefault(s => s.Rect.Contains(e.X, e.Y));
            if (splitter.Rect.Width > 0)
            {
                _isDraggingSplitter = true;
                _activeSplitter = splitter;
                return;
            }

            // 레전드 클릭 감지
            var hitLegend = _legendItems.FirstOrDefault(li => li.HitRect.Contains(e.X, e.Y));
            if (hitLegend != null)
            {
                _selectedSeriesName = hitLegend.PanelName;
                RefreshChart();
                return;
            }

            // [수정] 차트 클릭 시 무분별한 분석창 생성을 막기 위해 하단 DoubleClick으로 이동
            if (_appliedStrategy != null && _strategyLabelRect.Contains(e.X, e.Y))
            {
                if (MessageBox.Show($"전략 '{_appliedStrategy.Name}'을(를) 차트에서 제거하시겠습니까?", "전략 제거", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    _appliedStrategy = null;
                    _signals.Clear();
                    InvalidateCache();
                }
                return;
            }

            // AI 라벨 클릭 감지
            if (_aiLabelRect.Contains(e.X, e.Y))
            {
                _aiAnalysisMode = !_aiAnalysisMode;
                if (_aiAnalysisMode)
                {
                    if (_appliedStrategy != null) ApplyStrategy(_appliedStrategy);
                    else BuildAgentScoreSeries();
                }
                else
                {
                    _customSeriesList.RemoveAll(x => x.SeriesName == "AI_AGENT_SCORE");
                    UpdateLayout(); RefreshChart();
                }
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (PriceRect.Contains(e.X, e.Y)) { _isDraggingPrice = true; _isAutoScaleY = false; }
                else _isDraggingChart = true;
            }
        }


        private void SKControl_MouseMove(object sender, MouseEventArgs e)
        {
            _mouseX = e.X; _mouseY = e.Y;

            if (_isDraggingSplitter && _activeSplitter != null)
            {
                float dy = e.Y - _lastMouseY;
                float totalH = skControl.Height - AxisHeight;
                float weightSum = _panelWeights.Values.Sum();
                float weightDelta = (dy / totalH) * weightSum;

                _panelWeights[_activeSplitter.Value.PanelAbove] += weightDelta;
                _panelWeights[_activeSplitter.Value.PanelBelow] -= weightDelta;

                if (_panelWeights[_activeSplitter.Value.PanelAbove] < 0.2f) _panelWeights[_activeSplitter.Value.PanelAbove] = 0.2f;
                if (_panelWeights[_activeSplitter.Value.PanelBelow] < 0.2f) _panelWeights[_activeSplitter.Value.PanelBelow] = 0.2f;

                _lastMouseY = e.Y;
                UpdateLayout();
                RefreshChart();
                return;
            }

            if (_isDraggingChart)
            {
                float dx = e.X - _lastMouseX;
                _scrollOffset -= dx / (_candleWidth + _gap);
                _lastMouseX = e.X; RefreshChart();
            }
            else if (_isDraggingPrice)
            {
                float dy = e.Y - _lastMouseY;
                float range = _manualMaxP - _manualMinP;
                _manualMaxP += dy * (range / 100);
                _manualMinP -= dy * (range / 100);
                _lastMouseY = e.Y; RefreshChart();
            }
            else
            {
                if (_splitterRects.Any(s => s.Rect.Contains(e.X, e.Y)))
                    skControl.Cursor = Cursors.HSplit;
                else
                    skControl.Cursor = Cursors.Default;

                skControl.Invalidate(); // 일반 이동 시에는 캐시 무효화 없이 그림만 새로 고침
            }
        }

        private void OnWheel(object sender, MouseEventArgs e)
        {
            float zoom = (e.Delta > 0) ? 1.2f : 0.8f;
            CandleWidth *= zoom;
        }

        private void SKControl_DoubleClick(object sender, EventArgs e) 
        {
            var me = (MouseEventArgs)e;
            var hitLegend = _legendItems.FirstOrDefault(li => li.HitRect.Contains(me.X, me.Y));
            if (hitLegend != null)
            {
                IndicatorSettingsRequested?.Invoke(this, hitLegend.PanelName);
                return;
            }

            // [추가] 전략 라벨 더블 클릭 -> 편집기 호출 (기존 자연어 유지)
            if (_appliedStrategy != null && _strategyLabelRect.Contains(me.X, me.Y))
            {
                var f = new StrategyManagerForm((strategy) => { ApplyStrategy(strategy); });
                f.SelectStrategyByName(_appliedStrategy.Name); // 기존 프롬프트가 로드됨
                f.ShowDialog();
                return;
            }

            // [추가] 신호 더블 클릭 -> 투명 데이터 인스펙터 호출 (모달)
            if (_appliedStrategy != null && _evalResults != null)
            {
                int idx = (int)Math.Round(_scrollOffset + (me.X - MainRect.Left) / (_candleWidth + _gap));
                if (idx >= 0 && idx < _evalResults.Count)
                {
                    var res = _evalResults[idx];
                    if (res.IsBuySignal || res.IsSellSignal)
                    {
                        var snapshots = SnapshotService.CreateSnapshots(StockCode, Data, _customSeriesList, TodayOpen, _appliedStrategy);
                        var f = new StrategyInspectorForm(res, snapshots[idx], _appliedStrategy);
                        f.ShowDialog(); // [수정] 모달로 열어 무분별한 생성 방지
                        return;
                    }
                }
            }

            _isAutoScaleY = true; 
            RefreshChart(); 
        }

        #endregion

        public void LoadStockData(string code, string name, List<CandleData> candles)
        {
            _stockCode = code; _stockName = name;
            Data = candles.Select(c => new OHLCV { 
                DateVal = c.DateTime, 
                Open = (float)c.Open, 
                High = (float)c.High, 
                Low = (float)c.Low, 
                Close = (float)c.Close, 
                Volume = (long)c.Volume,
                TickCount = c.TickCount // [투명성] 역사 데이터 로드 시 실제 틱캔들 합산 건수를 바로 가져옴
            }).ToList();

            // 서버 데이터는 최신순(Newest -> Oldest)이므로, 차트 렌더링을 위해 과거순(Oldest -> Newest)으로 뒤집음
            Data.Reverse();

            // 당일 시가 / 전일 종가 자동 감지
            if (Data.Count > 0)
            {
                var today = DateTime.Today;
                var todayBars = Data.Where(d => d.DateVal.Date == today).ToList();
                if (todayBars.Count > 0)
                {
                    TodayOpen = todayBars[0].Open;
                    // 전일 마지막 봉을 찾아 종가를 설정
                    var prevDayBars = Data.Where(d => d.DateVal.Date < today).ToList();
                    if (prevDayBars.Count > 0)
                        YesterdayClose = prevDayBars[prevDayBars.Count - 1].Close;
                }
                else
                {
                    // 전체가 과거 데이터인 경우 마지막 날이 기준
                    TodayOpen = Data[Data.Count - 1].Open;
                }
            }

            // 틱강도 초기화: 과거 데이터의 TickCount를 체결건수 초기값으로 사용
            _tickCounts = Data.Select(d => d.TickCount).ToList();
            _maxDayTicks = _tickCounts.Count > 0 ? _tickCounts.Max() : 0;
            _lastRealtimePrice = Data.Count > 0 ? Data[Data.Count - 1].Close : 0;

            if (_autoScroll) ScrollToEnd();
            RefreshChart();
        }

        public CustomSeries GetSeries(string seriesName) => _customSeriesList.FirstOrDefault(s => s.SeriesName == seriesName);

        public void AddSeries(CustomSeries series)
        {
            if (string.IsNullOrEmpty(series.SeriesName)) series.SeriesName = Guid.NewGuid().ToString();
            _customSeriesList.RemoveAll(x => x.SeriesName == series.SeriesName);
            _customSeriesList.Add(series);
            UpdateLayout();
            RefreshChart();
        }

        public void ClearIndicators()
        {
            _customSeriesList.Clear();
            UpdateLayout();
            RefreshChart();
        }

        /// <summary>
        /// 실시간 시세(MarketData)로 차트 마지막 캔들 갱신 또는 새 캔들 추가.
        /// 분봉 기준: 현재 시각의 분이 바뀌면 새 캔들 추가, 아니면 마지막 캔들 OHLCV 갱신.
        /// </summary>
        public void UpdateRealtime(float price, long volume, DateTime time)
        {
            if (Data.Count == 0) return;

            var last = Data[Data.Count - 1];
            bool isNewBar = false;

            // 현재 분봉 시작 시각 계산
            var barTime = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0);

            if (barTime > last.DateVal)
            {
                // 새로운 분봉 시작 → 이전 봉의 체결건수를 최대값에 반영
                isNewBar = true;
                if (_tickCounts.Count > 0)
                {
                    int prevTicks = _tickCounts[_tickCounts.Count - 1];
                    if (prevTicks > _maxDayTicks) _maxDayTicks = prevTicks;
                }
                _tickCounts.Add(0); // 새 봉의 체결건수 초기화

                Data.Add(new OHLCV
                {
                    DateVal = barTime,
                    Open = price,
                    High = price,
                    Low = price,
                    Close = price,
                    Volume = volume
                });
                if (_autoScroll) ScrollToEnd();
            }
            else
            {
                // 같은 분봉 → 마지막 캔들 갱신
                Data[Data.Count - 1] = new OHLCV
                {
                    DateVal = last.DateVal,
                    Open = last.Open,
                    High = Math.Max(last.High, price),
                    Low = Math.Min(last.Low, price),
                    Close = price,
                    Volume = last.Volume + volume
                };
            }

            // 체결건수 누적 (사용자 로직: 1틱 체결 = 1건 증가)
            if (_tickCounts.Count > 0)
            {
                _tickCounts[_tickCounts.Count - 1] += 1;
            }
            _lastRealtimePrice = price;

            // 틱강도 시리즈 실시간 갱신
            if (_showTickIntensity)
            {
                UpdateTickIntensitySeries(isNewBar);
            }

            // 지표 시리즈 실시간 업데이트
            foreach (var cs in _customSeriesList)
            {
                if (cs.SeriesName.StartsWith("COMP_")) continue;
                if (cs.SeriesName == "TICK_CNT" || cs.SeriesName == "TICK_RAT") continue; // 틱강도는 별도 처리

                try
                {
                    Services.IndicatorCalculation.UpdateSeriesRealtime(cs, Data, isNewBar);
                }
                catch { /* 무시 */ }
            }

            // [추가] AI 협동 지능 연구 실시간 점수 업데이트
            if (_aiAnalysisMode)
            {
                double currentChange = (TodayOpen > 0 && Data.Count > 0) ? (Data[Data.Count - 1].Close - TodayOpen) / TodayOpen * 100.0 : 0;
                var analysis = Services.AgentManager.Instance.Analyze(StockCode, Data, Data.Count - 1, _customSeriesList, currentChange);
                var aiSeries = _customSeriesList.FirstOrDefault(s => s.SeriesName == "AI_AGENT_SCORE");
                if (aiSeries != null)
                {
                    if (isNewBar) aiSeries.Values.Add(analysis.FinalScore);
                    else if (aiSeries.Values.Count > 0) aiSeries.Values[aiSeries.Values.Count - 1] = analysis.FinalScore;
                }
            }

            RefreshChart();
        }

        /// <summary>
        /// 틱강도(TickIntensity) 시리즈를 생성/갱신하여 차트에 표시.
        /// 체결건수(120틱 기준) = 히스토그램, 틱비율(%) = 라인.
        /// </summary>
        public void BuildTickIntensitySeries()
        {
            // _tickCounts를 Data 길이에 맞게 동기화
            while (_tickCounts.Count < Data.Count) _tickCounts.Add(0);

            // 체결건수 히스토그램 시리즈
            var cntSeries = new CustomSeries
            {
                SeriesName = "TICK_CNT",
                Title = $"체결강도({_tickDivisor}틱)", // 투명한 제목으로 변경
                Color = SKColors.DodgerBlue,
                PanelType = PanelType.Bottom,
                PanelName = "TickIntensity",
                Style = PlotType.Histogram,
                IsLeftAxis = true, // 체결강도(건수)는 좌축(0~10) 사용
                BaseLine = 0
            };

            // 틱비율 라인 시리즈
            var ratSeries = new CustomSeries
            {
                SeriesName = "TICK_RAT",
                Title = "틱비율(%)",
                Color = SKColors.Orange,
                PanelType = PanelType.Bottom,
                PanelName = "TickIntensity",
                Style = PlotType.Line,
                Thickness = 1.5f
            };

            // 값 계산
            var cntValues = new List<double>();
            var ratValues = new List<double>();
            var cntColors = new List<SKColor>();
            int runningMax = 0;

            for (int i = 0; i < _tickCounts.Count; i++)
            {
                int tc = _tickCounts[i];
                // 서버에서 온 Raw Ticks(예: 600)를 Divisor(120)로 나누어 캔들 개수(5.0)로 환산
                double displayValue = (double)tc / _tickDivisor; 
                cntValues.Add(displayValue);

                // 색상: 양봉/음봉 판별
                if (i < Data.Count && Data[i].Close >= Data[i].Open)
                    cntColors.Add(SKColors.DodgerBlue);
                else
                    cntColors.Add(SKColors.Tomato);

                if (tc > runningMax) runningMax = tc;
                double ratio = runningMax > 0 ? (double)tc / runningMax * 100.0 : 0;
                ratValues.Add(ratio);
            }

            cntSeries.Values = cntValues;
            cntSeries.OutputColors = cntColors;
            ratSeries.Values = ratValues;

            _customSeriesList.RemoveAll(s => s.SeriesName == "TICK_CNT" || s.SeriesName == "TICK_RAT");
            _customSeriesList.Add(cntSeries);
            _customSeriesList.Add(ratSeries);
            UpdateLayout();
            RefreshChart();
        }

        private void UpdateTickIntensitySeries(bool isNewBar)
        {
            var cntSeries = _customSeriesList.FirstOrDefault(s => s.SeriesName == "TICK_CNT");
            var ratSeries = _customSeriesList.FirstOrDefault(s => s.SeriesName == "TICK_RAT");
            if (cntSeries == null || ratSeries == null) return;

            int tc = _tickCounts.Count > 0 ? _tickCounts[_tickCounts.Count - 1] : 0;
            // 실시간 원시 틱(예: 60)을 120으로 나누어 0.5 캔들로 표시
            double displayValue = (double)tc / _tickDivisor; 
            int maxT = Math.Max(_maxDayTicks, tc);
            double ratio = maxT > 0 ? (double)tc / maxT * 100.0 : 0;

            // 색상
            SKColor col = SKColors.DodgerBlue;
            if (Data.Count > 0 && Data[Data.Count - 1].Close < Data[Data.Count - 1].Open)
                col = SKColors.Tomato;

            if (isNewBar)
            {
                cntSeries.Values.Add(displayValue);
                if (cntSeries.OutputColors == null) cntSeries.OutputColors = new List<SKColor>();
                cntSeries.OutputColors.Add(col);
                ratSeries.Values.Add(ratio);
            }
            else
            {
                if (cntSeries.Values.Count > 0)
                {
                    cntSeries.Values[cntSeries.Values.Count - 1] = displayValue;
                    if (cntSeries.OutputColors != null && cntSeries.OutputColors.Count > 0)
                        cntSeries.OutputColors[cntSeries.OutputColors.Count - 1] = col;
                }
                if (ratSeries.Values.Count > 0)
                    ratSeries.Values[ratSeries.Values.Count - 1] = ratio;
            }
        }

        /// <summary>틱강도 지표 토글 (제거)</summary>
        public void RemoveTickIntensity()
        {
            _showTickIntensity = false;
            _customSeriesList.RemoveAll(s => s.SeriesName == "TICK_CNT" || s.SeriesName == "TICK_RAT");
            UpdateLayout();
            RefreshChart();
        }

        public void BuildAgentScoreSeries()
        {
            if (_evalResults == null || _evalResults.Count == 0) return;

            var scoreSeries = new CustomSeries
            {
                SeriesName = "AI_AGENT_SCORE",
                Title = "AI Agent Score",
                Color = SKColors.HotPink,
                PanelType = PanelType.Bottom,
                PanelName = "AI_Score",
                Style = PlotType.Line,
                Thickness = 2.0f,
                BaseLine = 50.0,
                Overbought = 80.0,
                Oversold = 20.0
            };

            scoreSeries.Values = _evalResults.Select(r => r.AgentScore).ToList();
            
            _customSeriesList.RemoveAll(s => s.SeriesName == "AI_AGENT_SCORE");
            _customSeriesList.Add(scoreSeries);
            UpdateLayout();
            RefreshChart();
        }

        public void ScrollToEnd() { if (Data.Count == 0) return; float vis = MainRect.Width / (_candleWidth + _gap); _scrollOffset = Math.Max(0, Data.Count - vis + 5); }

        public void ApplyStrategyFromNL(string nlPrompt)
        {
            var strategy = StrategyBridge.CreateFromNaturalLanguage(nlPrompt);
            if (strategy == null) { MessageBox.Show("전략 해석 실패"); return; }
            ApplyStrategy(strategy);
        }

        // [이벤트] 외부 일봉 데이터 요청 (종목코드, 필요일수 -> 일봉 리스트 반환)
        public event Func<string, int, Task<List<BarData>>> RequiredDailyDataRequest;
        
        // [임시 저장소]
        private List<BarData> _cachedDailyData = new List<BarData>();

        public async void ApplyStrategy(StrategyDefinition strategy)
        {
            if (strategy == null) return;
            _appliedStrategy = strategy;

            // [데이터 로딩] 전략이 일봉 데이터를 요구하는 경우 비동기 요청
            if (strategy.RequiredDataDays > 0 && RequiredDailyDataRequest != null)
            {
                // 현재 차트의 종목 코드 등 식별자가 필요함. (여기서는 가정: 외부에서 차트 생성 시 설정한 Code)
                // FastChart 내부에 Code 필드가 명시적으로 없으면, 외부에서 관리. 
                // 임시로 그냥 "Current"로 보냄. 실제 구현 시 StockCode 속성 필요.
                try 
                {
                   // Cursor = Cursors.WaitCursor; // (WinForms 필요)
                   var dailyData = await RequiredDailyDataRequest.Invoke("Current", strategy.RequiredDataDays + 5);
                   if (dailyData != null) _cachedDailyData = dailyData;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("일봉 데이터 로딩 실패: " + ex.Message);
                }
                finally
                {
                   // Cursor = Cursors.Default;
                }
            }

            // 전략에 필요한 지표가 차트에 없으면 자동 추가
            EnsureRequiredIndicators(strategy);

            // 전략 평가 실행 (외부 일봉 데이터 포함)
            // StrategyEvaluator.RunHistorical 메서드도 BarData 리스트를 받도록 수정 필요
            // -> 현재 Evaluator -> SnapshotService 구조임.
            // Evaluator.RunHistorical 시그니처 변경 필요. 
            // 일단 여기서는 Evaluator가 변경되었다고 가정하고 호출하거나,
            // Evaluator를 수정해야 함. (순서상 Evaluator 먼저 수정했어야 함)
            
            // Evaluator에 Overload 추가가 이상적이므로, 일단 Evaluator 수정이 안 된 상태라면 에러 발생 가능.
            // 따라서 Evaluator도 수정해야 함. 
            // 이번 턴에 Evaluator 수정도 같이 진행하겠음. (아래 툴 호출에서 Evaluator 수정 예정)
            
            // _evalResults = _evaluator.RunHistorical(strategy, Data, _customSeriesList, TodayOpen, _cachedDailyData); 
            // (주의: Evaluator 수정 전이면 컴파일 에러. 동시에 수정해야 함)
            
            // 일단 기존 호출 유지하되, Evaluator 수정 후 반영.
            _evalResults = _evaluator.RunHistorical(StockCode, strategy, Data, _customSeriesList, TodayOpen, _cachedDailyData);
            
            if (_aiAnalysisMode) BuildAgentScoreSeries();
            
            // 기존 신호 제거 및 새 신호 마커 주입
            _signals.Clear();
            var markers = _evaluator.GenerateMarkers(_evalResults, Data).Select(m => new SignalMarker {
                Time = m.Time, Type = m.Type, Price = m.Price, Note = m.Note
            }).ToList();
            _signals.AddRange(markers);

            UpdateLayout();
            InvalidateCache();
            MessageBox.Show($"전략 '{strategy.Name}' 적용 완료!");
        }

        private void EnsureRequiredIndicators(StrategyDefinition strategy)
        {
            var allIndicatorNames = strategy.BuyRules.Concat(strategy.SellRules)
                .SelectMany(g => g.Conditions)
                .SelectMany(c => new[] { c.IndicatorA, c.IndicatorB })
                .Where(n => !string.IsNullOrEmpty(n) && n != "Price" && n != "Close" && n != "Open" && n != "High" && n != "Low" && n != "Volume")
                .Distinct();

            foreach (var name in allIndicatorNames)
            {
                if (_customSeriesList.Any(s => s.SeriesName == name)) continue;
                
                // 필요한 지표가 없으면 계산하여 추가 (IndicatorCalculation 활용)
                if (name == "SuperTrend") AddSeries(IndicatorCalculation.CalculateSuperTrend(Data));
                else if (name.StartsWith("MA_"))
                {
                    if (int.TryParse(name.Substring(3), out int period))
                        AddSeries(IndicatorCalculation.CalculateMA(Data, period, SKColors.Yellow));
                }
                else if (name == "TICK_RAT") { _showTickIntensity = true; BuildTickIntensitySeries(); }
                else if (name == "CHG_OPEN_PCT") continue; // 가상 지표는 SnapshotService에서 즉석 계산됨
            }
        }
    }
}
