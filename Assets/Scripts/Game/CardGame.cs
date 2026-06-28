using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PartyGame
{
    /// <summary>
    /// "Memory Card Match" minigame.
    /// Spawns a 4x5 grid of 20 cards containing pairs of values 1-10.
    /// Players take turns using the mobile analog joystick to navigate a selector frame
    /// and pressing the A button to flip cards.
    /// Matching a pair awards 1 point and grants another turn.
    /// Mismatching flips the cards back and passes the turn.
    /// All UI and assets are generated procedurally at runtime.
    /// </summary>
    public class CardGame : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Full-screen RectTransform that gameplay visuals are parented to. Defaults to this object.")]
        public RectTransform playField;
        public PlayerScoreboard scoreboard;

        [Header("Card Design")]
        public Vector2 cardSize = new Vector2(160, 200);
        public Vector2 gridSpacing = new Vector2(24, 24);
        public Color cardBackInterColor = new Color(0.16f, 0.18f, 0.25f, 1f); // #292F40
        public Color cardFaceInterColor = new Color(0.31f, 0.49f, 1f, 1f);     // #4F7CFF
        public Color cardMatchInterColor = new Color(0.18f, 0.80f, 0.44f, 1f);  // #2ECC71 (Green)
        public Color cursorColor = new Color(1f, 0.36f, 0.42f, 1f);            // #FF5D6C (Red)

        [Header("Game Flow Timing (seconds)")]
        public float delayAfterMismatch = 1.5f;
        public float resultShowDuration = 5f;

        private Font _font;
        private PlayerConnectionManager _connections;

        // Grid cards
        private const int Rows = 4;
        private const int Cols = 5;
        private CardCell[,] _grid = new CardCell[Rows, Cols];
        private readonly List<CardCell> _flatCards = new List<CardCell>();

        // Text & panels
        private Text _statusText;
        private Text _turnText;
        private RectTransform _gridContainer;
        private RectTransform _cursor;

        // Selection & Input state
        private int _selectedRow = 0;
        private int _selectedCol = 0;
        private float _moveCooldownTimer = 0f;
        private const float MoveCooldown = 0.22f; // Cooldown for hold-joystick movement
        private bool _prevButtonA = false;

        // Turn state
        private int _activePlayerIdx = -1;
        private bool _isProcessingTurn = false;
        private bool _gameEnded = false;

        public int ActivePlayerIndex => _activePlayerIdx;

        // Cards flipped in current sub-turn
        private CardCell _firstFlipped = null;
        private CardCell _secondFlipped = null;

        private class CardCell
        {
            public int Index;
            public int Row;
            public int Col;
            public int Value;
            public bool IsFlipped;
            public bool IsMatched;

            public GameObject RootGo;
            public Image Background;
            public Text ValueText;
            public RectTransform Rect;
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
            InitializeGridValues();
            StartCoroutine(GameLoop());
        }

        private void Update()
        {
            if (_gameEnded) return;

            // Handle selection cooldown
            if (_moveCooldownTimer > 0f)
                _moveCooldownTimer -= Time.deltaTime;

            if (_connections == null || _connections.PlayerCount == 0)
            {
                // Fallback / Waiting state: no players connected
                return;
            }

            // Ensure the active player index is valid
            if (_activePlayerIdx < 0 || _activePlayerIdx >= _connections.PlayerCount)
                return;

            // Do not take player inputs while we are processing flips, showing mismatch delay, etc.
            if (_isProcessingTurn)
                return;

            PlayerInputData activePlayer = _connections.Players[_activePlayerIdx];
            if (activePlayer == null) return;

            // 1. Joystick Navigation
            HandleJoystickNavigation(activePlayer);

            // 2. Button A Flip Action
            HandleAButtonPress(activePlayer);

            // 3. Smooth cursor positioning
            UpdateCursorSmooth();
        }

        // -------------------------------------------------------------- Game Flow Loop

        private IEnumerator GameLoop()
        {
            // Initial Wait
            _turnText.text = "Waiting for players...";
            while (_connections == null || _connections.PlayerCount == 0)
            {
                yield return null;
            }

            _turnText.text = "Get Ready!";
            yield return new WaitForSeconds(1.5f);

            // Cycle turns until all pairs matched
            _activePlayerIdx = 0;
            while (!IsAllPairsMatched())
            {
                // Clean up disconnected player references gracefully
                if (_activePlayerIdx >= _connections.PlayerCount)
                    _activePlayerIdx = 0;

                PlayerInputData p = _connections.Players[_activePlayerIdx];
                _turnText.text = p.Nickname.ToUpper() + "'S TURN";

                // Setup templates
                SetTemplateForTurns();

                // Wait until the active player completes their full match attempt
                _firstFlipped = null;
                _secondFlipped = null;
                _isProcessingTurn = false;

                while (_firstFlipped == null || _secondFlipped == null)
                {
                    // Check if player disconnected mid-turn
                    if (p == null || !_connections.Players.Contains(p))
                    {
                        break;
                    }
                    yield return null;
                }

                // If player disconnected, just proceed to next turn
                if (p == null || !_connections.Players.Contains(p))
                {
                    _activePlayerIdx = (_activePlayerIdx + 1) % _connections.PlayerCount;
                    continue;
                }

                // We have flipped two cards. Evaluate match!
                _isProcessingTurn = true;
                yield return StartCoroutine(EvaluateFlips(p));
            }

            // Game over
            _gameEnded = true;
            SetTemplatesToNone();
            _turnText.text = "Game Over!";

            // Final winner declaration
            PlayerInputData winner = null;
            int topScore = -1;
            foreach (var p in _connections.Players)
            {
                if (p.Score > topScore)
                {
                    topScore = p.Score;
                    winner = p;
                }
            }

            if (winner != null)
                _statusText.text = "Winner: " + winner.Nickname + " (" + topScore + " pairs)";
            else
                _statusText.text = "Finished!";
            _statusText.gameObject.SetActive(true);

            yield return new WaitForSeconds(resultShowDuration);

            int winnerId = (winner != null) ? winner.ClientId : -1;
            if (BoardGameManager.Instance != null && BoardGameManager.Instance.IsGameActive)
            {
                BoardGameManager.Instance.ReportMinigameWinner(winnerId, "Card Match");
                SceneManager.LoadScene("BoardGame");
            }
            else
            {
                SceneManager.LoadScene(SceneNavigator.GameSelectScene);
            }
        }

        private IEnumerator EvaluateFlips(PlayerInputData p)
        {
            if (_firstFlipped.Value == _secondFlipped.Value)
            {
                // Match!
                _firstFlipped.IsMatched = true;
                _secondFlipped.IsMatched = true;
                _firstFlipped.Background.color = cardMatchInterColor;
                _secondFlipped.Background.color = cardMatchInterColor;

                p.Score++;
                if (scoreboard != null) scoreboard.Refresh();

                _statusText.text = "Matched!";
                _statusText.gameObject.SetActive(true);
                yield return new WaitForSeconds(1.0f);
                _statusText.gameObject.SetActive(false);

                // Note: active player keeps their turn! (Index doesn't change)
            }
            else
            {
                // Mismatch!
                _statusText.text = "No Match!";
                _statusText.gameObject.SetActive(true);
                yield return new WaitForSeconds(delayAfterMismatch);
                _statusText.gameObject.SetActive(false);

                // Flip them back down
                _firstFlipped.IsFlipped = false;
                _secondFlipped.IsFlipped = false;
                _firstFlipped.ValueText.text = "";
                _secondFlipped.ValueText.text = "";
                _firstFlipped.Background.color = cardBackInterColor;
                _secondFlipped.Background.color = cardBackInterColor;

                // Move turn to next player
                _activePlayerIdx = (_activePlayerIdx + 1) % _connections.PlayerCount;
            }

            _firstFlipped = null;
            _secondFlipped = null;
            _isProcessingTurn = false;
        }

        private bool IsAllPairsMatched()
        {
            foreach (var cell in _flatCards)
            {
                if (!cell.IsMatched) return false;
            }
            return true;
        }

        // -------------------------------------------------------------- Inputs & Navigation

        private void HandleJoystickNavigation(PlayerInputData player)
        {
            if (_moveCooldownTimer > 0f) return;

            Vector2 joy = player.Joystick;
            int rOffset = 0;
            int cOffset = 0;

            if (joy.y > 0.55f) rOffset = -1;       // Up
            else if (joy.y < -0.55f) rOffset = 1;  // Down

            if (joy.x > 0.55f) cOffset = 1;        // Right
            else if (joy.x < -0.55f) cOffset = -1; // Left

            if (rOffset != 0 || cOffset != 0)
            {
                _selectedRow = Mathf.Clamp(_selectedRow + rOffset, 0, Rows - 1);
                _selectedCol = Mathf.Clamp(_selectedCol + cOffset, 0, Cols - 1);
                _moveCooldownTimer = MoveCooldown;
            }
        }

        private void HandleAButtonPress(PlayerInputData player)
        {
            bool btnA = player.JoystickA;
            if (btnA && !_prevButtonA)
            {
                // Transition down (click)
                CardCell cell = _grid[_selectedRow, _selectedCol];
                if (cell != null && !cell.IsFlipped && !cell.IsMatched)
                {
                    FlipCard(cell);
                }
            }
            _prevButtonA = btnA;
        }

        private void FlipCard(CardCell cell)
        {
            cell.IsFlipped = true;
            cell.ValueText.text = cell.Value.ToString();
            cell.Background.color = cardFaceInterColor;

            if (_firstFlipped == null)
            {
                _firstFlipped = cell;
            }
            else if (_secondFlipped == null)
            {
                _secondFlipped = cell;
            }
        }

        private void SetTemplateForTurns()
        {
            if (_connections == null) return;
            for (int i = 0; i < _connections.PlayerCount; i++)
            {
                var p = _connections.Players[i];
                if (i == _activePlayerIdx)
                    _connections.SetTemplate(p.ClientId, MobileTemplate.JoystickAB);
                else
                    _connections.SetTemplate(p.ClientId, MobileTemplate.None);
            }
        }

        private void SetTemplatesToNone()
        {
            if (_connections != null)
                _connections.SetTemplateForAll(MobileTemplate.None);
        }

        // -------------------------------------------------------------- Grid initialization

        private void InitializeGridValues()
        {
            // Build 10 pairs (values 1 to 10)
            List<int> vals = new List<int>();
            for (int i = 1; i <= 10; i++)
            {
                vals.Add(i);
                vals.Add(i);
            }

            // Shuffle values
            for (int i = vals.Count - 1; i > 0; i--)
            {
                int r = Random.Range(0, i + 1);
                int tmp = vals[i];
                vals[i] = vals[r];
                vals[r] = tmp;
            }

            // Assign values to cells
            int valIdx = 0;
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    CardCell cell = _grid[r, c];
                    cell.Value = vals[valIdx++];
                    cell.IsFlipped = false;
                    cell.IsMatched = false;
                    cell.ValueText.text = "";
                    cell.Background.color = cardBackInterColor;
                }
            }
        }

        private Vector2 GetCardLocalPosition(int row, int col)
        {
            float totalWidth = Cols * cardSize.x + (Cols - 1) * gridSpacing.x;
            float totalHeight = Rows * cardSize.y + (Rows - 1) * gridSpacing.y;

            float startX = -totalWidth * 0.5f + cardSize.x * 0.5f;
            float startY = totalHeight * 0.5f - cardSize.y * 0.5f;

            float x = startX + col * (cardSize.x + gridSpacing.x);
            float y = startY - row * (cardSize.y + gridSpacing.y);

            return new Vector2(x, y);
        }

        // -------------------------------------------------------------- UI & Rendering Creation

        private void BuildUI()
        {
            // Labels
            _turnText = CreateText("TurnText", playField, new Vector2(0, 360), new Vector2(1000, 80), 48, FontStyle.Bold);
            _turnText.text = "Waiting for players...";

            _statusText = CreateText("StatusText", playField, new Vector2(0, 290), new Vector2(1000, 70), 38, FontStyle.Bold);
            _statusText.text = "";
            _statusText.gameObject.SetActive(false);

            // Container for Grid (NO GridLayoutGroup to prevent layout delays and conflicts)
            var gridGo = new GameObject("GridContainer", typeof(RectTransform));
            _gridContainer = gridGo.GetComponent<RectTransform>();
            _gridContainer.SetParent(playField, false);
            _gridContainer.anchorMin = _gridContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _gridContainer.pivot = new Vector2(0.5f, 0.5f);
            _gridContainer.sizeDelta = new Vector2(Cols * cardSize.x + (Cols - 1) * gridSpacing.x, Rows * cardSize.y + (Rows - 1) * gridSpacing.y);
            _gridContainer.anchoredPosition = new Vector2(0, -60);

            // Generate cells
            int flatIdx = 0;
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    var cellGo = new GameObject("Card_" + r + "_" + c, typeof(RectTransform), typeof(UnityEngine.UI.Image));
                    cellGo.transform.SetParent(_gridContainer, false);

                    var img = cellGo.GetComponent<UnityEngine.UI.Image>();
                    img.color = cardBackInterColor;

                    var rect = cellGo.GetComponent<RectTransform>();
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = cardSize;
                    rect.anchoredPosition = GetCardLocalPosition(r, c);

                    var textGo = new GameObject("ValueText", typeof(RectTransform), typeof(Text));
                    textGo.transform.SetParent(cellGo.transform, false);
                    var rtText = textGo.GetComponent<RectTransform>();
                    rtText.anchorMin = Vector2.zero;
                    rtText.anchorMax = Vector2.one;
                    rtText.sizeDelta = Vector2.zero;

                    var text = textGo.GetComponent<Text>();
                    text.font = _font;
                    text.fontSize = 60;
                    text.fontStyle = FontStyle.Bold;
                    text.color = Color.white;
                    text.alignment = TextAnchor.MiddleCenter;
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.verticalOverflow = VerticalWrapMode.Overflow;

                    var cell = new CardCell
                    {
                        Index = flatIdx++,
                        Row = r,
                        Col = c,
                        RootGo = cellGo,
                        Background = img,
                        ValueText = text,
                        Rect = rect
                    };

                    _grid[r, c] = cell;
                    _flatCards.Add(cell);
                }
            }

            // Cursor/Pointer Outline
            var cursorGo = new GameObject("Cursor", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            _cursor = cursorGo.GetComponent<RectTransform>();
            _cursor.SetParent(playField, false); // Sibling of grid, not child of GridLayoutGroup
            _cursor.anchorMin = _cursor.anchorMax = new Vector2(0.5f, 0.5f);
            _cursor.pivot = new Vector2(0.5f, 0.5f);
            _cursor.sizeDelta = cardSize + new Vector2(12, 12); // slightly larger than card size
            _cursor.anchoredPosition = Vector2.zero;

            // Render a procedural outline frame for the cursor
            var cursorImg = cursorGo.GetComponent<UnityEngine.UI.Image>();
            cursorImg.sprite = CreateOutlineSprite(172, 212, 8); // 8px thick outline
            cursorImg.color = cursorColor;

            // Setup initial position of cursor
            UpdateCursorImmediate();
        }

        private void UpdateCursorImmediate()
        {
            _cursor.anchoredPosition = _gridContainer.anchoredPosition + GetCardLocalPosition(_selectedRow, _selectedCol);
        }

        private void UpdateCursorSmooth()
        {
            Vector2 targetPos = _gridContainer.anchoredPosition + GetCardLocalPosition(_selectedRow, _selectedCol);
            _cursor.anchoredPosition = Vector2.Lerp(_cursor.anchoredPosition, targetPos, Time.deltaTime * 18f);
        }

        private Sprite CreateOutlineSprite(int width, int height, int thickness)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool insideX = x >= thickness && x < width - thickness;
                    bool insideY = y >= thickness && y < height - thickness;
                    if (insideX && insideY)
                    {
                        pixels[y * width + x] = new Color32(0, 0, 0, 0); // Transparent interior
                    }
                    else
                    {
                        pixels[y * width + x] = new Color32(255, 255, 255, 255); // White frame
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
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
    }
}
