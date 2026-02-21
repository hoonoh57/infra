using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Common.Models;
using App64.Services;

namespace App64.Forms
{
    /// <summary>
    /// 전략 CRUD 관리 및 AI와 자연어 대화가 가능한 고도화된 전략 인터페이스.
    /// 사용자의 영감을 즉시 기술적 논리로 변환하고 시각화합니다.
    /// </summary>
    public class StrategyManagerForm : Form
    {
        private ListBox _lstStrategies;
        private TreeView _tvLogic;
        private RichTextBox _rtbChat;
        private TextBox _txtPrompt;
        private Button _btnApply;
        private List<StrategyDefinition> _allStrategies;
        private Action<StrategyDefinition> _onApply;

        public StrategyManagerForm(Action<StrategyDefinition> onApply)
        {
            _onApply = onApply;
            _allStrategies = StrategyPersistenceService.LoadStrategies();

            if (!_allStrategies.Any(s => s.Name == "기본 돌파 전략"))
            {
                _allStrategies.Add(new StrategyDefinition("기본 돌파 전략", "슈퍼트렌드 상향 돌파 시 매수", 
                    new List<LogicGate> { new LogicGate("Entry", LogicalOperator.AND, new List<ConditionCell> { 
                        new ConditionCell("C1", "Price CrossUp SuperTrend", "Price", ComparisonOperator.CrossUp, "SuperTrend") 
                    }) }, 
                    new List<LogicGate> { new LogicGate("Exit", LogicalOperator.AND, new List<ConditionCell> { 
                        new ConditionCell("C2", "Price CrossDown SuperTrend", "Price", ComparisonOperator.CrossDown, "SuperTrend") 
                    }) }));
            }

            InitializeUI();
            RefreshStrategyList();
        }

        private void InitializeUI()
        {
            this.Text = "전략 관리자 (AI Assistant & CRUD)";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(28, 28, 38);
            this.ForeColor = Color.White;

            var mainSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1 };
            var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };

            // --- 왼쪽: 전략 리스트 ---
            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            _lstStrategies = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("맑은 고딕", 10), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 35 };
            _lstStrategies.DrawItem += _lstStrategies_DrawItem;
            _lstStrategies.SelectedIndexChanged += (s, e) => ShowSelectedStrategy();
            
            var btnAdd = new Button { Text = "새 전략", Dock = DockStyle.Bottom, Height = 35, FlatStyle = FlatStyle.Flat };
            btnAdd.Click += (s, e) => AddNewStrategy();

            leftPanel.Controls.Add(_lstStrategies);
            leftPanel.Controls.Add(btnAdd);
            mainSplit.Panel1.Controls.Add(leftPanel);

            // --- 오른쪽 위: 논리 투명화 보기 (TreeView) ---
            var logicPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            _tvLogic = new TreeView { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.Lime, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10) };
            logicPanel.Controls.Add(new Label { Text = "전략 논리 구조 (Transparency View)", Dock = DockStyle.Top, Height = 25, ForeColor = Color.Cyan });
            logicPanel.Controls.Add(_tvLogic);
            rightSplit.Panel1.Controls.Add(logicPanel);

            // --- 오른쪽 아래: AI 대화창 ---
            var chatPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            _rtbChat = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 30), ForeColor = Color.FromArgb(200, 200, 200), ReadOnly = true, BorderStyle = BorderStyle.None };
            
            var promptContainer = new Panel { Dock = DockStyle.Bottom, Height = 80, Padding = new Padding(0, 5, 0, 0) };
            _txtPrompt = new TextBox { Dock = DockStyle.Fill, Multiline = true, BackColor = Color.FromArgb(45, 45, 55), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            var btnSend = new Button { Text = "AI 설계 요청", Dock = DockStyle.Right, Width = 100, BackColor = Color.FromArgb(60, 100, 60), FlatStyle = FlatStyle.Flat };
            btnSend.Click += (s, e) => RequestAIDesign();

            // [추가] 실시간 문법 힌트/검증
            _txtPrompt.TextChanged += (s, e) => {
                string t = _txtPrompt.Text;
                if (t.Contains("상승") || t.Contains("돌파") || t.Contains("이탈")) _txtPrompt.ForeColor = Color.Cyan;
                else _txtPrompt.ForeColor = Color.White;
            };

            promptContainer.Controls.Add(_txtPrompt);
            promptContainer.Controls.Add(btnSend);
            
            chatPanel.Controls.Add(_rtbChat);
            chatPanel.Controls.Add(promptContainer);
            chatPanel.Controls.Add(new Label { Text = "AI 전략 비서 (Natural Language Design)", Dock = DockStyle.Top, Height = 25, ForeColor = Color.Orange });
            rightSplit.Panel2.Controls.Add(chatPanel);

            // --- 하단: 하단 버튼 ---
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(10) };
            _btnApply = new Button { Text = "차트에 적용 및 검증", Dock = DockStyle.Right, Width = 150, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(100, 60, 100) };
            _btnApply.Click += (s, e) => ApplySelected();
            bottomPanel.Controls.Add(_btnApply);

            mainSplit.Panel2.Controls.Add(rightSplit);
            this.Controls.Add(mainSplit);
            this.Controls.Add(bottomPanel);

            this.Load += (s, e) => {
                try {
                    mainSplit.SplitterDistance = 220;
                    rightSplit.SplitterDistance = this.Height - 320;
                } catch { } // Fallback in case size is too small initially
            };

            LogChat("AI: 안녕하세요! 원하시는 매매 철학을 말씀해 주시면 정밀한 논리 게이트로 설계해 드립니다.");
        }

        private void RefreshStrategyList()
        {
            _lstStrategies.Items.Clear();
            foreach (var s in _allStrategies) _lstStrategies.Items.Add(s.Name);
        }

        private void ShowSelectedStrategy()
        {
            int idx = _lstStrategies.SelectedIndex;
            if (idx < 0) return;

            var s = _allStrategies[idx];
            
            // [추가] 선택 시 자연어 원문 불러오기 (수정/개선 용이성)
            if (!string.IsNullOrEmpty(s.NaturalLanguagePrompt))
                _txtPrompt.Text = s.NaturalLanguagePrompt;

            _tvLogic.Nodes.Clear();
            var root = _tvLogic.Nodes.Add(s.Name);
            
            var buyNode = root.Nodes.Add("매수 규칙 (Buy Gates)");
            foreach (var gate in s.BuyRules) {
                var gNode = buyNode.Nodes.Add($"{gate.Name} ({gate.Operator})");
                foreach (var cond in gate.Conditions) gNode.Nodes.Add(cond.Description);
            }

            var sellNode = root.Nodes.Add("매도 규칙 (Sell Gates)");
            foreach (var gate in s.SellRules) {
                var gNode = sellNode.Nodes.Add($"{gate.Name} ({gate.Operator})");
                foreach (var cond in gate.Conditions) gNode.Nodes.Add(cond.Description);
            }
            _tvLogic.ExpandAll();
        }

        private void AddNewStrategy()
        {
            _txtPrompt.Text = "새로운 전략의 특징을 입력하세요...";
            _txtPrompt.Focus();
        }

        private async void RequestAIDesign()
        {
            string prompt = _txtPrompt.Text.Trim();
            if (string.IsNullOrEmpty(prompt)) return;

            LogChat($"USER: {prompt}");
            _txtPrompt.Clear();

            // AI 해석 시뮬레이션 (실제로는 LLM API 호출)
            LogChat("AI: 요청하신 내용을 분석 중입니다... [원자적 조건 추출 중]");
            
            await System.Threading.Tasks.Task.Delay(800);
            var newStrategy = StrategyBridge.CreateFromNaturalLanguage(prompt);
            
            if (newStrategy != null) {
                _allStrategies.Add(newStrategy);
                RefreshStrategyList();
                _lstStrategies.SelectedIndex = _allStrategies.Count - 1;
                StrategyPersistenceService.SaveStrategies(_allStrategies);
                LogChat($"AI: '{newStrategy.Name}' 전략 설계가 완료되었습니다. 논리 구조를 확인해 보세요.");
            } else {
                LogChat("AI: 죄송합니다. 해당 내용을 전략 논리로 해석하지 못했습니다. 조금 더 구체적으로 말씀해 주시겠어요?");
            }
        }

        private void ApplySelected()
        {
            int idx = _lstStrategies.SelectedIndex;
            if (idx >= 0) {
                _onApply?.Invoke(_allStrategies[idx]);
                this.Close();
            }
        }

        public void SelectStrategyByName(string name)
        {
            for (int i = 0; i < _lstStrategies.Items.Count; i++)
            {
                if (_lstStrategies.Items[i].ToString() == name)
                {
                    _lstStrategies.SelectedIndex = i;
                    break;
                }
            }
        }

        private void _lstStrategies_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.DrawBackground();
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            
            var brush = isSelected ? new SolidBrush(Color.FromArgb(100, 60, 100)) : new SolidBrush(Color.FromArgb(40, 40, 50));
            e.Graphics.FillRectangle(brush, e.Bounds);

            var text = _lstStrategies.Items[e.Index].ToString();
            var font = _lstStrategies.Font;
            var textBrush = isSelected ? Brushes.Yellow : Brushes.White;
            
            var stringSize = e.Graphics.MeasureString(text, font);
            float y = e.Bounds.Y + (e.Bounds.Height - stringSize.Height) / 2;
            
            e.Graphics.DrawString(text, font, textBrush, e.Bounds.X + 5, y);
            e.DrawFocusRectangle();
        }

        private void LogChat(string msg)
        {
            _rtbChat.AppendText(msg + Environment.NewLine + Environment.NewLine);
            _rtbChat.SelectionStart = _rtbChat.Text.Length;
            _rtbChat.ScrollToCaret();
        }
    }
}
