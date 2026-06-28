using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PartyGame
{
    /// <summary>
    /// Rock Paper Scissors minigame.
    /// Each of 5 rounds: players choose Scissors (1), Rock (2), or Paper (3).
    /// The host/boss makes a random choice.
    /// Wins beat the boss (+3 pts), draws tie (+1 pt), losses lose (0 pts).
    /// </summary>
    public class RpsGame : MonoBehaviour
    {
        [Header("References")]
        public RectTransform playField;
        public PlayerScoreboard scoreboard;

        [Header("Rounds")]
        public int totalRounds = 5;

        [Header("Timing (seconds)")]
        public float prepDuration = 1.5f;
        public float choiceDuration = 6.0f;
        public float revealDelay = 1.0f;
        public float resultDuration = 4.0f;
        public float returnDelay = 5f;

        private Font _font;
        private PlayerConnectionManager _connections;

        // Built UI elements
        private Text _roundLabel;
        private Text _statusText;
        private Text _countdownText;
        private RectTransform _bossPanel;
        private Text _bossLabel;
        private Text _bossChoiceText;
        private RectTransform _playerChoicesRow;

        private static readonly string[] WeaponEmojis = { "❓", "✌️", "✊", "🖐️" };

        private static readonly Color[] ChoiceColors =
        {
            new Color(1f, 0.36f, 0.42f),    // 1 (Scissors) - Light red/pink
            new Color(0.31f, 0.49f, 1f),    // 2 (Rock) - Blue
            new Color(0.18f, 0.80f, 0.44f), // 3 (Paper) - Green
        };

        private class ChoiceBox
        {
            public GameObject root;
            public Text countText;
            public Text namesText;
        }

        private readonly List<ChoiceBox> _choiceBoxes = new List<ChoiceBox>();

        private class RoundChoiceRecord
        {
            public PlayerInputData player;
            public int choice; // 1=Scissors, 2=Rock, 3=Paper
        }

        private readonly List<RoundChoiceRecord> _roundChoices = new List<RoundChoiceRecord>();

        private void Awake()
        {
            if (playField == null) playField = transform as RectTransform;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _connections = PlayerConnectionManager.Instance;
            BuildUI();
        }

        private void Start()
        {
            StartCoroutine(RunGame());
        }

        // -------------------------------------------------------------- game flow

        private IEnumerator RunGame()
        {
            SetTemplate(MobileTemplate.None);
            _statusText.text = "Rock, Paper, Scissors!";
            _statusText.gameObject.SetActive(true);
            yield return new WaitForSeconds(prepDuration);
            _statusText.gameObject.SetActive(false);

            for (int round = 1; round <= totalRounds; round++)
            {
                _roundLabel.text = "Round " + round + " / " + totalRounds;

                // Reset for this round
                _bossPanel.gameObject.SetActive(false);
                HideChoiceBoxes();
                _roundChoices.Clear();

                // 1) Preparation
                _statusText.text = "Get Ready!";
                _statusText.gameObject.SetActive(true);
                yield return new WaitForSeconds(prepDuration);
                _statusText.gameObject.SetActive(false);

                // 2) Choice Phase
                _statusText.text = "Choose Your Weapon!";
                _statusText.gameObject.SetActive(true);
                _countdownText.gameObject.SetActive(true);

                SetTemplate(MobileTemplate.ThreeChoice);
                
                yield return StartCoroutine(ChoicePhase());

                SetTemplate(MobileTemplate.None);
                _statusText.gameObject.SetActive(false);
                _countdownText.gameObject.SetActive(false);

                // 3) Reveal Boss's Choice
                int bossChoice = Random.Range(1, 4); // 1=Scissors, 2=Rock, 3=Paper
                
                _bossPanel.gameObject.SetActive(true);
                _bossLabel.text = "Boss is choosing...";
                _bossChoiceText.text = "❓";
                yield return new WaitForSeconds(revealDelay);

                _bossLabel.text = "Boss Chose:";
                _bossChoiceText.text = WeaponEmojis[bossChoice];

                // 4) Evaluate results and show who chose what
                AwardScores(bossChoice);
                ShowChoiceResults(bossChoice);

                yield return new WaitForSeconds(resultDuration);
            }

            // Final standings
            yield return ShowFinal();

            yield return new WaitForSeconds(returnDelay);

            int winnerId = -1;
            int maxScore = -1;
            if (_connections != null)
            {
                foreach (var p in _connections.Players)
                {
                    if (p.Score > maxScore)
                    {
                        maxScore = p.Score;
                        winnerId = p.ClientId;
                    }
                }
            }

            if (BoardGameManager.Instance != null && BoardGameManager.Instance.IsGameActive)
            {
                BoardGameManager.Instance.ReportMinigameWinner(winnerId, "Rock Paper Scissors");
                SceneManager.LoadScene("BoardGame");
            }
            else
            {
                SceneManager.LoadScene(SceneNavigator.GameSelectScene);
            }
        }

        private IEnumerator ChoicePhase()
        {
            float t = choiceDuration;
            var answeredIds = new HashSet<int>();

            while (t > 0f)
            {
                _countdownText.text = Mathf.CeilToInt(t).ToString();

                if (_connections != null)
                {
                    var players = _connections.Players;
                    for (int i = 0; i < players.Count; i++)
                    {
                        var p = players[i];
                        if (answeredIds.Contains(p.ClientId)) continue;

                        int ch = p.PressedChoice;
                        if (ch >= 1 && ch <= 3)
                        {
                            answeredIds.Add(p.ClientId);
                            _roundChoices.Add(new RoundChoiceRecord { player = p, choice = ch });
                        }
                    }

                    // If all connected players answered, we can end early!
                    if (_connections.PlayerCount > 0 && answeredIds.Count >= _connections.PlayerCount)
                        break;
                }

                t -= Time.deltaTime;
                yield return null;
            }
        }

        private void AwardScores(int bossChoice)
        {
            // Create a list of players who actually chose
            foreach (var rec in _roundChoices)
            {
                int outcome = GetOutcome(rec.choice, bossChoice); // 1=Win, 0=Draw, -1=Lose
                int pts = 0;
                if (outcome == 1) pts = 3;
                else if (outcome == 0) pts = 1;

                rec.player.Score += pts;
            }

            if (scoreboard != null) scoreboard.Refresh();
        }

        /// <summary>
        /// Returns 1 if choice1 beats choice2, 0 if draw, -1 if choice1 loses to choice2.
        /// 1 = Scissors, 2 = Rock, 3 = Paper
        /// </summary>
        private int GetOutcome(int choice1, int choice2)
        {
            if (choice1 == choice2) return 0; // Draw

            // 1 (Scissors) beats 3 (Paper)
            // 2 (Rock) beats 1 (Scissors)
            // 3 (Paper) beats 2 (Rock)
            if ((choice1 == 1 && choice2 == 3) ||
                (choice1 == 2 && choice2 == 1) ||
                (choice1 == 3 && choice2 == 2))
            {
                return 1; // Win
            }

            return -1; // Lose
        }

        private void ShowChoiceResults(int bossChoice)
        {
            // Clear boxes
            for (int i = 0; i < 3; i++)
            {
                _choiceBoxes[i].countText.text = "0";
                _choiceBoxes[i].namesText.text = "";
                
                // Highlight box colors
                var img = _choiceBoxes[i].root.GetComponent<Image>();
                int choiceNum = i + 1;
                int outcome = GetOutcome(choiceNum, bossChoice);
                if (outcome == 1)
                {
                    img.color = ChoiceColors[i]; // Winner highlight
                }
                else if (outcome == 0)
                {
                    img.color = new Color(ChoiceColors[i].r * 0.7f, ChoiceColors[i].g * 0.7f, ChoiceColors[i].b * 0.7f, 0.8f); // Draw (slightly dark)
                }
                else
                {
                    img.color = new Color(ChoiceColors[i].r, ChoiceColors[i].g, ChoiceColors[i].b, 0.2f); // Loser faded
                }
            }

            // Populate boxes
            var choiceCounts = new int[3];
            var choiceNames = new List<string>[3] { new List<string>(), new List<string>(), new List<string>() };

            foreach (var rec in _roundChoices)
            {
                int idx = rec.choice - 1;
                if (idx >= 0 && idx < 3)
                {
                    choiceCounts[idx]++;
                    
                    int outcome = GetOutcome(rec.choice, bossChoice);
                    string statusEmoji = outcome == 1 ? " 👑" : (outcome == 0 ? " 🤝" : " 💥");
                    choiceNames[idx].Add(rec.player.Nickname + statusEmoji);
                }
            }

            for (int i = 0; i < 3; i++)
            {
                _choiceBoxes[i].countText.text = choiceCounts[i].ToString();
                _choiceBoxes[i].namesText.text = string.Join("\n", choiceNames[i]);
            }

            _playerChoicesRow.gameObject.SetActive(true);
        }

        private void HideChoiceBoxes()
        {
            _playerChoicesRow.gameObject.SetActive(false);
        }

        private IEnumerator ShowFinal()
        {
            _bossPanel.gameObject.SetActive(false);
            HideChoiceBoxes();

            string standings = "Game Over!\n";
            if (_connections != null)
            {
                var sorted = new List<PlayerInputData>(_connections.Players);
                sorted.Sort((a, b) => b.Score.CompareTo(a.Score));
                for (int i = 0; i < sorted.Count; i++)
                    standings += (i + 1) + ". " + sorted[i].Nickname + " - " + sorted[i].Score + "\n";
            }
            _statusText.text = standings;
            _statusText.gameObject.SetActive(true);
            _roundLabel.text = "Finished";
            yield return null;
        }

        // -------------------------------------------------------------- helpers

        private void SetTemplate(MobileTemplate t)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.SetMobileTemplate(t);
            else if (_connections != null)
                _connections.SetTemplateForAll(t);
        }

        private Text CreateText(string name, RectTransform parent, Vector2 pos, Vector2 size, int fontSize, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private Text MakeChildText(string name, Transform parent, string content, int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // -------------------------------------------------------------- UI build

        private void BuildUI()
        {
            // Round Label
            _roundLabel = CreateText("RoundLabel", playField, new Vector2(0, 360), new Vector2(800, 80), 48, FontStyle.Bold);
            _roundLabel.text = "Round 1 / " + totalRounds;

            // Status Text
            _statusText = CreateText("StatusText", playField, new Vector2(0, 240), new Vector2(1400, 200), 64, FontStyle.Bold);
            _statusText.text = "";
            _statusText.gameObject.SetActive(false);

            // Countdown Text
            _countdownText = CreateText("CountdownText", playField, new Vector2(0, 40), new Vector2(300, 200), 120, FontStyle.Bold);
            _countdownText.color = new Color(1f, 0.3f, 0.3f);
            _countdownText.text = "";
            _countdownText.gameObject.SetActive(false);

            // Boss Panel
            var bossGo = new GameObject("BossPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _bossPanel = bossGo.GetComponent<RectTransform>();
            _bossPanel.SetParent(playField, false);
            _bossPanel.anchorMin = _bossPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _bossPanel.pivot = new Vector2(0.5f, 0.5f);
            _bossPanel.sizeDelta = new Vector2(400, 300);
            _bossPanel.anchoredPosition = new Vector2(0, 80);

            var bimg = bossGo.GetComponent<Image>();
            bimg.color = new Color(0.15f, 0.17f, 0.25f, 0.9f);

            var bvlg = bossGo.GetComponent<VerticalLayoutGroup>();
            bvlg.childAlignment = TextAnchor.MiddleCenter;
            bvlg.spacing = 10;
            bvlg.childControlWidth = true;
            bvlg.childControlHeight = true;

            _bossLabel = MakeChildText("BossLabel", bossGo.transform, "Boss Chose:", 32, FontStyle.Normal, new Color(0.8f, 0.8f, 0.8f));
            _bossChoiceText = MakeChildText("BossChoiceText", bossGo.transform, "❓", 110, FontStyle.Bold, Color.white);

            _bossPanel.gameObject.SetActive(false);

            // Player Choices Row
            BuildPlayerChoicesRow();
        }

        private void BuildPlayerChoicesRow()
        {
            var rowGo = new GameObject("PlayerChoicesRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _playerChoicesRow = rowGo.GetComponent<RectTransform>();
            _playerChoicesRow.SetParent(playField, false);
            _playerChoicesRow.anchorMin = _playerChoicesRow.anchorMax = new Vector2(0.5f, 0.5f);
            _playerChoicesRow.pivot = new Vector2(0.5f, 0.5f);
            _playerChoicesRow.sizeDelta = new Vector2(1000, 280);
            _playerChoicesRow.anchoredPosition = new Vector2(0, -260);

            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 40;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            string[] weaponLabels = { "✌️ Scissors", "✊ Rock", "🖐️ Paper" };

            for (int i = 0; i < 3; i++)
            {
                var boxGo = new GameObject("ChoiceBox" + (i + 1), typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
                boxGo.transform.SetParent(_playerChoicesRow, false);

                var img = boxGo.GetComponent<Image>();
                img.color = ChoiceColors[i];

                var le = boxGo.GetComponent<LayoutElement>();
                le.minWidth = 260; le.minHeight = 260;

                var vlg = boxGo.GetComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.spacing = 8;
                vlg.padding = new RectOffset(12, 12, 12, 12);
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                MakeChildText("WeaponLabel", boxGo.transform, weaponLabels[i], 28, FontStyle.Bold, Color.white);
                
                var countLabel = MakeChildText("CountLabel", boxGo.transform, "0", 40, FontStyle.Bold, new Color(1f, 1f, 1f, 0.9f));
                
                var namesLabel = MakeChildText("NamesLabel", boxGo.transform, "", 22, FontStyle.Normal, Color.white);
                namesLabel.alignment = TextAnchor.UpperCenter;

                _choiceBoxes.Add(new ChoiceBox { root = boxGo, countText = countLabel, namesText = namesLabel });
            }

            _playerChoicesRow.gameObject.SetActive(false);
        }
    }
}