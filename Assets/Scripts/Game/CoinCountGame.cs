using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PartyGame
{
    /// <summary>
    /// "Count the coins" minigame.
    /// Each of 5 rounds: spawn 5-12 yellow coins orbiting on a ring for 2s, show
    /// "How many coins?" for 1s, then present 4 consecutive numbers (one is the answer)
    /// in colored boxes. Players answer with the FourChoice (1-4) mobile template.
    /// Correct players score by press order: 1st=4, 2nd=3, 3rd=2, 4th=1, then minimum 1.
    /// All gameplay UI (coins, texts, answer boxes) is created at runtime.
    /// </summary>
    public class CoinCountGame : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Full-screen RectTransform that gameplay visuals are parented to. Defaults to this object.")]
        public RectTransform playField;
        public PlayerScoreboard scoreboard;

        [Header("Rounds")]
        public int totalRounds = 5;
        public int minCoins = 5;
        public int maxCoins = 12;

        [Header("Timing (seconds)")]
        public float coinShowDuration = 2f;
        public float questionDuration = 1f;
        public float answerTimeout = 12f;
        public float resultDuration = 2f;
        public float returnDelay = 5f;

        [Header("Coin Orbit")]
        public float orbitRadius = 220f;
        public float orbitSpeed = 70f;        // degrees / second
        public float coinSize = 90f;
        public Vector2 orbitCenter = new Vector2(0f, 40f);
        public Color coinColor = new Color(1f, 0.85f, 0.15f, 1f);

        private static readonly Color[] BoxColors =
        {
            new Color(0.31f, 0.49f, 1f),    // 1 blue
            new Color(1f, 0.36f, 0.42f),    // 2 red
            new Color(0.18f, 0.80f, 0.44f), // 3 green
            new Color(1f, 0.71f, 0.28f),    // 4 orange
        };

        private Font _font;
        private PlayerConnectionManager _connections;

        // Built UI
        private Text _roundLabel;
        private Text _statusText;
        private RectTransform _coinsParent;
        private RectTransform _answerRow;
        private readonly List<AnswerBox> _boxes = new List<AnswerBox>();

        // Coin orbit state
        private readonly List<RectTransform> _coins = new List<RectTransform>();
        private bool _orbiting;
        private float _angle;

        /// <summary>The correct coin count for the current round (0 before the first round resolves).</summary>
        public int CurrentAnswer { get; private set; }

        /// <summary>True while players are allowed to answer (FourChoice template is active).</summary>
        public bool AcceptingAnswers { get; private set; }

        private class AnswerBox
        {
            public GameObject root;
            public Text numberText;
        }

        private class AnswerRecord
        {
            public PlayerInputData player;
            public int selectedNumber;
        }

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

        private void Update()
        {
            if (!_orbiting || _coins.Count == 0) return;
            _angle += orbitSpeed * Time.deltaTime;
            int n = _coins.Count;
            for (int i = 0; i < n; i++)
            {
                float a = (_angle + i * (360f / n)) * Mathf.Deg2Rad;
                _coins[i].anchoredPosition = orbitCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * orbitRadius;
            }
        }

        // -------------------------------------------------------------- game flow

        private IEnumerator RunGame()
        {
            SetTemplate(MobileTemplate.None);
            _statusText.text = "Get Ready!";
            _statusText.gameObject.SetActive(true);
            yield return new WaitForSeconds(1.5f);
            _statusText.gameObject.SetActive(false);

            for (int round = 1; round <= totalRounds; round++)
            {
                _roundLabel.text = "Round " + round + " / " + totalRounds;

                int answer = Random.Range(minCoins, maxCoins + 1);
                CurrentAnswer = answer;

                // 1) Show orbiting coins for 2s.
                SpawnCoins(answer);
                yield return new WaitForSeconds(coinShowDuration);
                ClearCoins();

                // 2) Show the question for 1s.
                _statusText.text = "How many coins?";
                _statusText.gameObject.SetActive(true);
                yield return new WaitForSeconds(questionDuration);
                _statusText.gameObject.SetActive(false);

                // 3) Build 4 consecutive numbers that include the answer at a random position.
                int offset = Random.Range(0, 4);          // answer position 0..3
                int startNum = answer - offset;
                if (startNum < 1) startNum = 1;            // keep all numbers positive
                int[] numbers = { startNum, startNum + 1, startNum + 2, startNum + 3 };
                ShowAnswerBoxes(numbers);

                // 4) Let players answer via the four-choice template.
                SetTemplate(MobileTemplate.FourChoice);
                AcceptingAnswers = true;
                yield return StartCoroutine(AnswerPhase(numbers, answer));
                AcceptingAnswers = false;
                SetTemplate(MobileTemplate.None);

                // 5) Reveal the correct answer & award points.
                int[] gained = AwardScores(answer);
                HighlightCorrect(numbers, answer);
                _statusText.text = "Answer: " + answer;
                _statusText.gameObject.SetActive(true);
                yield return new WaitForSeconds(resultDuration);
                _statusText.gameObject.SetActive(false);
                HideAnswerBoxes();
            }

            // Final standings.
            yield return ShowFinal();

            yield return new WaitForSeconds(returnDelay);
            SceneManager.LoadScene(SceneNavigator.GameSelectScene);
        }

        private IEnumerator AnswerPhase(int[] numbers, int answer)
        {
            _lastAnswers.Clear();
            var answeredIds = new HashSet<int>();
            float t = 0f;

            while (t < answerTimeout)
            {
                if (_connections != null)
                {
                    var players = _connections.Players;
                    for (int i = 0; i < players.Count; i++)
                    {
                        var p = players[i];
                        if (answeredIds.Contains(p.ClientId)) continue;
                        int ch = p.PressedChoice;
                        if (ch >= 1 && ch <= 4)
                        {
                            answeredIds.Add(p.ClientId);
                            _lastAnswers.Add(new AnswerRecord { player = p, selectedNumber = numbers[ch - 1] });
                        }
                    }

                    if (_connections.PlayerCount > 0 && answeredIds.Count >= _connections.PlayerCount)
                        break;
                }

                t += Time.deltaTime;
                yield return null;
            }
        }

        private readonly List<AnswerRecord> _lastAnswers = new List<AnswerRecord>();

        private int[] AwardScores(int answer)
        {
            var gained = new List<int>();
            int rank = 0;
            foreach (var rec in _lastAnswers)
            {
                if (rec.selectedNumber == answer)
                {
                    rank++;
                    int pts = Mathf.Max(1, 5 - rank); // 1st=4,2nd=3,3rd=2,4th=1, then 1
                    rec.player.Score += pts;
                    gained.Add(pts);
                }
            }
            if (scoreboard != null) scoreboard.Refresh();
            return gained.ToArray();
        }

        private IEnumerator ShowFinal()
        {
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

        // -------------------------------------------------------------- coins

        private void SpawnCoins(int count)
        {
            ClearCoins();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Coin", typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(_coinsParent, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(coinSize, coinSize);

                var img = go.GetComponent<Image>();
                img.sprite = CircleSpriteFactory.WhiteCircle;
                img.color = coinColor;
                img.raycastTarget = false;

                _coins.Add(rt);
            }
            _angle = Random.Range(0f, 360f);
            _orbiting = true;
            // place immediately so they don't flash at origin for one frame
            UpdateCoinPositionsImmediate();
        }

        private void UpdateCoinPositionsImmediate()
        {
            int n = _coins.Count;
            for (int i = 0; i < n; i++)
            {
                float a = (_angle + i * (360f / n)) * Mathf.Deg2Rad;
                _coins[i].anchoredPosition = orbitCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * orbitRadius;
            }
        }

        private void ClearCoins()
        {
            _orbiting = false;
            foreach (var c in _coins)
                if (c != null) Destroy(c.gameObject);
            _coins.Clear();
        }

        // -------------------------------------------------------------- UI build

        private void BuildUI()
        {
            _roundLabel = CreateText("RoundLabel", playField, new Vector2(0, 360), new Vector2(800, 80), 48, FontStyle.Bold);
            _roundLabel.text = "Round 1 / " + totalRounds;

            _statusText = CreateText("StatusText", playField, new Vector2(0, 40), new Vector2(1400, 400), 64, FontStyle.Bold);
            _statusText.text = "";
            _statusText.gameObject.SetActive(false);

            var coinsGo = new GameObject("CoinsParent", typeof(RectTransform));
            _coinsParent = coinsGo.GetComponent<RectTransform>();
            _coinsParent.SetParent(playField, false);
            _coinsParent.anchorMin = _coinsParent.anchorMax = new Vector2(0.5f, 0.5f);
            _coinsParent.pivot = new Vector2(0.5f, 0.5f);
            _coinsParent.sizeDelta = new Vector2(900, 700);
            _coinsParent.anchoredPosition = Vector2.zero;

            BuildAnswerRow();
        }

        private void BuildAnswerRow()
        {
            var rowGo = new GameObject("AnswerRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _answerRow = rowGo.GetComponent<RectTransform>();
            _answerRow.SetParent(playField, false);
            _answerRow.anchorMin = _answerRow.anchorMax = new Vector2(0.5f, 0.5f);
            _answerRow.pivot = new Vector2(0.5f, 0.5f);
            _answerRow.sizeDelta = new Vector2(960, 200);
            _answerRow.anchoredPosition = new Vector2(0, -320);

            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 30;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            for (int i = 0; i < 4; i++)
            {
                var boxGo = new GameObject("Box" + (i + 1), typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
                boxGo.transform.SetParent(_answerRow, false);

                var img = boxGo.GetComponent<Image>();
                img.color = BoxColors[i];

                var le = boxGo.GetComponent<LayoutElement>();
                le.minWidth = 200; le.minHeight = 180;

                var vlg = boxGo.GetComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.padding = new RectOffset(8, 8, 8, 8);
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                var indexText = MakeChildText("Index", boxGo.transform, "Button " + (i + 1), 26, FontStyle.Normal, new Color(1, 1, 1, 0.85f));
                var numberText = MakeChildText("Number", boxGo.transform, "?", 80, FontStyle.Bold, Color.white);

                _boxes.Add(new AnswerBox { root = boxGo, numberText = numberText });
            }

            _answerRow.gameObject.SetActive(false);
        }

        private void ShowAnswerBoxes(int[] numbers)
        {
            for (int i = 0; i < _boxes.Count; i++)
            {
                _boxes[i].numberText.text = numbers[i].ToString();
                _boxes[i].root.GetComponent<Image>().color = BoxColors[i];
            }
            _answerRow.gameObject.SetActive(true);
        }

        private void HighlightCorrect(int[] numbers, int answer)
        {
            for (int i = 0; i < _boxes.Count; i++)
            {
                bool correct = numbers[i] == answer;
                var img = _boxes[i].root.GetComponent<Image>();
                img.color = correct ? BoxColors[i] : new Color(BoxColors[i].r, BoxColors[i].g, BoxColors[i].b, 0.25f);
            }
        }

        private void HideAnswerBoxes()
        {
            _answerRow.gameObject.SetActive(false);
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
    }
}
