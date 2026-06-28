using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PartyGame
{
    /// <summary>
    /// Master Board Game Minigame controller.
    /// Manages a snaking path of 41 tiles (0 to 40) where players roll dice
    /// to move forward, land on colored tiles, and trigger events:
    /// - Green: Blank
    /// - Blue: Minigame (rolls into existing coin/card/rps games, winner gets free roll)
    /// - Purple: Swap positions with a random other player
    /// - Red: Roll dice to move backward (with a backward arrow ⬅)
    /// - Yellow: Roll dice to move forward (with a forward arrow ➡)
    /// Procedurally builds all tiles, labels, player tokens, and a central dice rolling HUD.
    /// </summary>
    public class BoardGame : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Full-screen RectTransform that board visuals are parented to. Defaults to this object.")]
        public RectTransform playField;
        public PlayerScoreboard scoreboard;

        [Header("Design Colors")]
        public Color tileStartColor = new Color(0.49f, 0.55f, 0.55f, 1f); // Gray
        public Color tileGoalColor = new Color(0.83f, 0.69f, 0.22f, 1f);  // Gold
        public Color tileGreenColor = new Color(0.18f, 0.80f, 0.44f, 1f); // Green
        public Color tileBlueColor = new Color(0.20f, 0.60f, 0.86f, 1f);  // Blue
        public Color tilePurpleColor = new Color(0.61f, 0.35f, 0.71f, 1f); // Purple
        public Color tileRedColor = new Color(0.91f, 0.30f, 0.24f, 1f);    // Red
        public Color tileYellowColor = new Color(0.95f, 0.77f, 0.06f, 1f); // Yellow

        private Font _font;
        private PlayerConnectionManager _connections;
        private BoardGameManager _boardManager;

        // UI references
        private Text _statusText;
        private Text _turnText;
        private RectTransform _gridContainer;
        private RectTransform _dicePanel;
        private Text _diceLabel;
        private Text _diceValueText;

        // Visual board state
        private const int Cols = 9;
        private readonly List<TileCell> _tiles = new List<TileCell>();
        private readonly Dictionary<int, RectTransform> _playerTokens = new Dictionary<int, RectTransform>();

        // Dice roll state
        private int _rolledValue = 0;
        private bool _hasDiceInput = false;

        private class TileCell
        {
            public int Index;
            public GameObject RootGo;
            public Image Background;
            public Text Label;
            public Text ArrowText;
        }

        private void Awake()
        {
            if (playField == null) playField = transform as RectTransform;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _connections = PlayerConnectionManager.Instance;
            _boardManager = BoardGameManager.Instance;

            // Ensure BoardGameManager is initialized
            if (_boardManager != null && !_boardManager.IsGameActive)
            {
                _boardManager.StartBoardGame();
            }

            BuildUI();
        }

        private void Start()
        {
            InitializePlayerTokens();
            StartCoroutine(MainGameLoop());
        }

        private void Update()
        {
            UpdatePlayerTokenPositionsSmooth();
        }

        // -------------------------------------------------------------- Game Flow Loop

        private IEnumerator MainGameLoop()
        {
            // Initial Wait
            _turnText.text = "Waiting for players...";
            while (_connections == null || _connections.PlayerCount == 0)
            {
                yield return null;
            }

            // Sync token list in case new players joined
            InitializePlayerTokens();

            // 1. Check if returning from minigame
            if (_boardManager != null && _boardManager.IsReturningFromMinigame)
            {
                yield return StartCoroutine(HandleMinigameReturn());
            }

            // 2. Play standard loop
            while (true)
            {
                int activeIdx = _boardManager != null ? _boardManager.ActivePlayerIndex : 0;
                if (activeIdx >= _connections.PlayerCount)
                {
                    _boardManager.ActivePlayerIndex = 0;
                    activeIdx = 0;
                }

                PlayerInputData p = _connections.Players[activeIdx];
                _turnText.text = p.Nickname.ToUpper() + "'S TURN";

                // Setup templates - Active gets SingleButton, others None
                SetTemplatesForTurns(p.ClientId);

                // Wait for player to roll dice
                yield return StartCoroutine(PromptAndRollDice(p));

                // Move player token
                yield return StartCoroutine(AnimateMove(p.ClientId, _rolledValue));

                // Evaluate tile landed on
                yield return StartCoroutine(EvaluateTileLand(p.ClientId));

                if (HasAnyoneWon())
                    break;

                // Move to next player
                int nextIdx = (_boardManager.ActivePlayerIndex + 1) % _connections.PlayerCount;
                _boardManager.ActivePlayerIndex = nextIdx;
            }

            // Declaring Winner
            _turnText.text = "Game Over!";
            int winningClientId = GetWinningClientId();
            string winnerNick = "Player";
            if (_connections != null)
            {
                var winnerP = _connections.GetPlayer(winningClientId);
                if (winnerP != null) winnerNick = winnerP.Nickname;
            }

            _statusText.text = winnerNick + " Wins the Board Game!";
            _statusText.gameObject.SetActive(true);

            if (_boardManager != null) _boardManager.EndBoardGame();

            yield return new WaitForSeconds(resultShowDuration());
            SceneManager.LoadScene(SceneNavigator.GameSelectScene);
        }

        private float resultShowDuration() => 6.0f;

        private IEnumerator HandleMinigameReturn()
        {
            int winnerId = _boardManager.MinigameWinnerClientId;
            string gameName = _boardManager.LastMinigamePlayed;

            _statusText.text = "Returned from " + gameName + "!";
            _statusText.gameObject.SetActive(true);
            yield return new WaitForSeconds(2.0f);
            _statusText.gameObject.SetActive(false);

            PlayerInputData winnerP = null;
            if (_connections != null && winnerId != -1)
                winnerP = _connections.GetPlayer(winnerId);

            if (winnerP != null)
            {
                _turnText.text = "WINNER: " + winnerP.Nickname.ToUpper();
                _statusText.text = winnerP.Nickname + " gets a bonus roll!";
                _statusText.gameObject.SetActive(true);
                yield return new WaitForSeconds(2.0f);
                _statusText.gameObject.SetActive(false);

                // Give bonus roll
                SetTemplatesForTurns(winnerId);
                yield return StartCoroutine(PromptAndRollDice(winnerP));
                yield return StartCoroutine(AnimateMove(winnerId, _rolledValue));
                yield return StartCoroutine(EvaluateTileLand(winnerId));
            }
            else
            {
                _statusText.text = "No minigame winner.";
                _statusText.gameObject.SetActive(true);
                yield return new WaitForSeconds(2.0f);
                _statusText.gameObject.SetActive(false);
            }

            if (_boardManager != null)
                _boardManager.ClearMinigameReturnState();
        }

        // -------------------------------------------------------------- Dice Roll Handling

        private IEnumerator PromptAndRollDice(PlayerInputData p)
        {
            _dicePanel.gameObject.SetActive(true);
            _diceLabel.text = p.Nickname + "'s Roll";
            _diceValueText.text = "TAP A";

            // Clear inputs
            p.ResetInputs();
            _hasDiceInput = false;

            // Wait for A button press
            while (!_hasDiceInput)
            {
                // Disconnect safety
                if (p == null || _connections == null || !_connections.Players.Contains(p))
                    break;

                if (p.SingleA)
                    _hasDiceInput = true;

                yield return null;
            }

            // Animate rolling
            float rollTime = 1.2f;
            float t = 0f;
            int num = 1;
            while (t < rollTime)
            {
                num = Random.Range(1, 7);
                _diceValueText.text = num.ToString();
                t += Time.deltaTime * 3.5f;
                yield return new WaitForSeconds(0.06f);
            }

            _rolledValue = Random.Range(1, 7);
            _diceValueText.text = _rolledValue.ToString();

            yield return new WaitForSeconds(1.2f);
            _dicePanel.gameObject.SetActive(false);
        }

        private IEnumerator AnimateMove(int clientId, int steps)
        {
            if (_boardManager == null) yield break;

            int currentPos = _boardManager.GetPlayerPosition(clientId);
            int targetPos = Mathf.Clamp(currentPos + steps, 0, BoardGameManager.MaxTiles - 1);

            _statusText.text = "Moving " + steps + " steps!";
            _statusText.gameObject.SetActive(true);

            // Animate hop-by-hop
            int stepSign = steps > 0 ? 1 : -1;
            int remaining = Mathf.Abs(steps);

            while (remaining > 0)
            {
                currentPos += stepSign;
                if (currentPos < 0 || currentPos >= BoardGameManager.MaxTiles)
                    break;

                _boardManager.SetPlayerPosition(clientId, currentPos);
                remaining--;
                yield return new WaitForSeconds(0.28f); // Hop delay
            }

            _statusText.gameObject.SetActive(false);
        }

        // -------------------------------------------------------------- Event Evaluators

        private IEnumerator EvaluateTileLand(int clientId)
        {
            if (_boardManager == null) yield break;

            int pos = _boardManager.GetPlayerPosition(clientId);
            int type = GetTileType(pos);

            PlayerInputData landingPlayer = null;
            if (_connections != null) landingPlayer = _connections.GetPlayer(clientId);

            string nameStr = landingPlayer != null ? landingPlayer.Nickname : "Player";

            switch (type)
            {
                case 2: // Green (Blank)
                    _statusText.text = nameStr + " landed on a safe spot!";
                    _statusText.gameObject.SetActive(true);
                    yield return new WaitForSeconds(2.0f);
                    _statusText.gameObject.SetActive(false);
                    break;

                case 3: // Blue (Minigame)
                    _statusText.text = "Minigame Tile! Loading Random game...";
                    _statusText.gameObject.SetActive(true);
                    yield return new WaitForSeconds(2.5f);
                    _statusText.gameObject.SetActive(false);
                    LoadRandomMinigame();
                    break;

                case 4: // Purple (Swap)
                    _statusText.text = "Swap Positions Tile!";
                    _statusText.gameObject.SetActive(true);
                    yield return new WaitForSeconds(1.5f);

                    int otherId = GetRandomOtherPlayerClientId(clientId);
                    if (otherId != -1)
                    {
                        PlayerInputData otherP = _connections.GetPlayer(otherId);
                        string otherName = otherP != null ? otherP.Nickname : "Player";

                        _statusText.text = "Swapping " + nameStr + " & " + otherName + "!";

                        int activePos = _boardManager.GetPlayerPosition(clientId);
                        int otherPos = _boardManager.GetPlayerPosition(otherId);

                        // Swap Positions
                        _boardManager.SetPlayerPosition(clientId, otherPos);
                        _boardManager.SetPlayerPosition(otherId, activePos);

                        yield return new WaitForSeconds(2.0f);
                    }
                    else
                    {
                        _statusText.text = "No other player to swap with.";
                        yield return new WaitForSeconds(1.8f);
                    }
                    _statusText.gameObject.SetActive(false);
                    break;

                case 5: // Red (Move Backward)
                    _statusText.text = "Backward Tile! Roll to go back!";
                    _statusText.gameObject.SetActive(true);
                    yield return new WaitForSeconds(1.5f);
                    _statusText.gameObject.SetActive(false);

                    yield return StartCoroutine(PromptAndRollDice(landingPlayer));
                    yield return StartCoroutine(AnimateMove(clientId, -_rolledValue));
                    // Evaluate new spot landing
                    yield return StartCoroutine(EvaluateTileLand(clientId));
                    break;

                case 6: // Yellow (Move Forward)
                    _statusText.text = "Forward Tile! Roll to advance!";
                    _statusText.gameObject.SetActive(true);
                    yield return new WaitForSeconds(1.5f);
                    _statusText.gameObject.SetActive(false);

                    yield return StartCoroutine(PromptAndRollDice(landingPlayer));
                    yield return StartCoroutine(AnimateMove(clientId, _rolledValue));
                    // Evaluate new spot landing
                    yield return StartCoroutine(EvaluateTileLand(clientId));
                    break;
            }
        }

        private int GetRandomOtherPlayerClientId(int excludeClientId)
        {
            if (_connections == null || _connections.PlayerCount <= 1) return -1;

            var candidates = new List<int>();
            foreach (var p in _connections.Players)
            {
                if (p.ClientId != excludeClientId)
                    candidates.Add(p.ClientId);
            }

            if (candidates.Count == 0) return -1;
            return candidates[Random.Range(0, candidates.Count)];
        }

        private void LoadRandomMinigame()
        {
            string[] minigames = { SceneNavigator.CoinGameScene, SceneNavigator.CardGameScene, SceneNavigator.RpsGameScene };
            string chosen = minigames[Random.Range(0, minigames.Length)];

            // Clear scores since we use player scores to find minigame winner
            if (_connections != null)
            {
                foreach (var p in _connections.Players)
                {
                    p.Score = 0;
                }
            }

            SceneManager.LoadScene(chosen);
        }

        private bool HasAnyoneWon()
        {
            if (_connections == null) return false;
            foreach (var p in _connections.Players)
            {
                if (_boardManager.GetPlayerPosition(p.ClientId) >= BoardGameManager.MaxTiles - 1)
                    return true;
            }
            return false;
        }

        private int GetWinningClientId()
        {
            if (_connections == null) return -1;
            foreach (var p in _connections.Players)
            {
                if (_boardManager.GetPlayerPosition(p.ClientId) >= BoardGameManager.MaxTiles - 1)
                    return p.ClientId;
            }
            return -1;
        }

        // -------------------------------------------------------------- Templates & Networking

        private void SetTemplatesForTurns(int activeClientId)
        {
            if (_connections == null) return;
            foreach (var p in _connections.Players)
            {
                if (p.ClientId == activeClientId)
                    _connections.SetTemplate(p.ClientId, MobileTemplate.SingleButton);
                else
                    _connections.SetTemplate(p.ClientId, MobileTemplate.None);
            }
        }

        // -------------------------------------------------------------- Tokens initialization

        private void InitializePlayerTokens()
        {
            if (_connections == null) return;

            var activeIds = new HashSet<int>();
            foreach (var p in _connections.Players)
            {
                activeIds.Add(p.ClientId);
                if (!_playerTokens.ContainsKey(p.ClientId))
                {
                    // Create beautiful procedural circle token
                    var tokenGo = new GameObject("Token_" + p.Nickname, typeof(RectTransform), typeof(Image));
                    tokenGo.transform.SetParent(playField, false);

                    var rt = tokenGo.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(46, 46); // Circle token size

                    var img = tokenGo.GetComponent<Image>();
                    img.sprite = CircleSpriteFactory.WhiteCircle;
                    // Assign a unique color based on client index
                    img.color = GetPlayerTokenColor(p.JoinOrder);

                    // Add a tiny white border inside/procedurally or a text label with first initial
                    var labelGo = new GameObject("Initial", typeof(RectTransform), typeof(Text));
                    labelGo.transform.SetParent(tokenGo.transform, false);
                    var rtLabel = labelGo.GetComponent<RectTransform>();
                    rtLabel.anchorMin = Vector2.zero;
                    rtLabel.anchorMax = Vector2.one;
                    rtLabel.sizeDelta = Vector2.zero;

                    var txt = labelGo.GetComponent<Text>();
                    txt.font = _font;
                    txt.text = p.Nickname.Length > 0 ? p.Nickname.Substring(0, 1).ToUpper() : "P";
                    txt.fontSize = 24;
                    txt.fontStyle = FontStyle.Bold;
                    txt.color = Color.white;
                    txt.alignment = TextAnchor.MiddleCenter;

                    _playerTokens[p.ClientId] = rt;

                    // Initialize immediately at START tile
                    int startPos = _boardManager != null ? _boardManager.GetPlayerPosition(p.ClientId) : 0;
                    rt.anchoredPosition = CalculateTokenTargetPosition(p.ClientId, startPos);
                }
            }

            // Remove disconnected player tokens
            var toRemove = new List<int>();
            foreach (var id in _playerTokens.Keys)
            {
                if (!activeIds.Contains(id)) toRemove.Add(id);
            }
            foreach (var id in toRemove)
            {
                if (_playerTokens[id] != null) Destroy(_playerTokens[id].gameObject);
                _playerTokens.Remove(id);
            }
        }

        private Color GetPlayerTokenColor(int order)
        {
            Color[] colors = {
                new Color(1f, 0.36f, 0.42f),    // Red Pink
                new Color(0.18f, 0.80f, 0.44f), // Green
                new Color(0.607f, 0.349f, 0.714f), // Purple
                new Color(0.95f, 0.77f, 0.06f), // Yellow
                new Color(0.20f, 0.60f, 0.86f)  // Blue
            };
            return colors[(order - 1) % colors.Length];
        }

        private Vector2 CalculateTokenTargetPosition(int clientId, int tileIndex)
        {
            if (_connections == null) return GetTileLocalPosition(tileIndex);

            Vector2 tileCenter = GetTileLocalPosition(tileIndex);

            // Lay out multiple player tokens on the same tile so they don't overlap!
            int countOnTile = 0;
            int myOffsetIdx = 0;
            for (int i = 0; i < _connections.Players.Count; i++)
            {
                int pId = _connections.Players[i].ClientId;
                int pPos = _boardManager != null ? _boardManager.GetPlayerPosition(pId) : 0;
                if (pPos == tileIndex)
                {
                    if (pId == clientId)
                    {
                        myOffsetIdx = countOnTile;
                    }
                    countOnTile++;
                }
            }

            if (countOnTile > 1)
            {
                float offsetVal = 22f;
                Vector2[] offsets = {
                    new Vector2(-offsetVal, -offsetVal),
                    new Vector2(offsetVal, -offsetVal),
                    new Vector2(-offsetVal, offsetVal),
                    new Vector2(offsetVal, offsetVal),
                    new Vector2(0, 0)
                };
                tileCenter += offsets[myOffsetIdx % offsets.Length];
            }

            return tileCenter;
        }

        private void UpdatePlayerTokenPositionsSmooth()
        {
            if (_connections == null) return;
            foreach (var p in _connections.Players)
            {
                if (_playerTokens.TryGetValue(p.ClientId, out var rt))
                {
                    int pos = _boardManager != null ? _boardManager.GetPlayerPosition(p.ClientId) : 0;
                    Vector2 target = CalculateTokenTargetPosition(p.ClientId, pos);
                    rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, target, Time.deltaTime * 12f);
                }
            }
        }

        // -------------------------------------------------------------- UI & Board Creation

        private Vector2 GetTileLocalPosition(int index)
        {
            int cols = 9;
            int r = index / cols;
            int c = index % cols;
            if (r % 2 != 0)
            {
                c = (cols - 1) - c;
            }

            float tileW = 145f;
            float tileH = 110f;
            float spaceX = 16f;
            float spaceY = 16f;

            float totalWidth = cols * tileW + (cols - 1) * spaceX;
            float totalHeight = 5f * tileH + 4f * spaceY;

            float startX = -totalWidth * 0.5f + tileW * 0.5f;
            float startY = -totalHeight * 0.5f + tileH * 0.5f - 40f; // offset slightly down to clear headers

            float x = startX + c * (tileW + spaceX);
            float y = startY + r * (tileH + spaceY);

            return new Vector2(x, y);
        }

        private int GetTileType(int index)
        {
            if (index == 0) return 0; // START
            if (index == BoardGameManager.MaxTiles - 1) return 1; // GOAL

            // Deterministic random layout
            int hash = (index * 7 + 13) % 100;
            if (hash < 12) return 5;      // Red (Move Backward)
            else if (hash < 24) return 6; // Yellow (Move Forward)
            else if (hash < 39) return 4; // Purple (Swap)
            else if (hash < 59) return 3; // Blue (Minigame)
            else return 2;                // Green (Blank)
        }

        private void BuildUI()
        {
            // Turn Text Header
            _turnText = CreateText("TurnText", playField, new Vector2(0, 375), new Vector2(1000, 75), 44, FontStyle.Bold);
            _turnText.text = "Waiting for players...";

            // Status message
            _statusText = CreateText("StatusText", playField, new Vector2(0, 315), new Vector2(1400, 70), 34, FontStyle.Bold);
            _statusText.text = "";
            _statusText.gameObject.SetActive(false);

            // Create Grid Container
            var gridGo = new GameObject("GridContainer", typeof(RectTransform));
            _gridContainer = gridGo.GetComponent<RectTransform>();
            _gridContainer.SetParent(playField, false);
            _gridContainer.anchorMin = _gridContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _gridContainer.pivot = new Vector2(0.5f, 0.5f);
            _gridContainer.sizeDelta = new Vector2(1600, 800);
            _gridContainer.anchoredPosition = Vector2.zero;

            // Generate 41 Board tiles
            for (int i = 0; i < BoardGameManager.MaxTiles; i++)
            {
                var tileGo = new GameObject("Tile_" + i, typeof(RectTransform), typeof(Image));
                tileGo.transform.SetParent(_gridContainer, false);

                var rt = tileGo.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(145, 110);
                rt.anchoredPosition = GetTileLocalPosition(i);

                var img = tileGo.GetComponent<Image>();
                int type = GetTileType(i);
                img.color = GetTileColor(type);

                // Title Label
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelGo.transform.SetParent(tileGo.transform, false);
                var rtLabel = labelGo.GetComponent<RectTransform>();
                rtLabel.anchorMin = new Vector2(0f, 0.65f);
                rtLabel.anchorMax = new Vector2(1f, 1f);
                rtLabel.sizeDelta = Vector2.zero;

                var txt = labelGo.GetComponent<Text>();
                txt.font = _font;
                txt.text = GetTileLabelText(i, type);
                txt.fontSize = i == 0 || i == BoardGameManager.MaxTiles - 1 ? 26 : 20;
                txt.fontStyle = FontStyle.Bold;
                txt.color = Color.white;
                txt.alignment = TextAnchor.MiddleCenter;

                // Unique Arrow Display for Forward/Backward
                var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(Text));
                arrowGo.transform.SetParent(tileGo.transform, false);
                var rtArrow = arrowGo.GetComponent<RectTransform>();
                rtArrow.anchorMin = new Vector2(0f, 0f);
                rtArrow.anchorMax = new Vector2(1f, 0.65f);
                rtArrow.sizeDelta = Vector2.zero;

                var txtArrow = arrowGo.GetComponent<Text>();
                txtArrow.font = _font;
                txtArrow.text = GetTileArrowUnicode(type);
                txtArrow.fontSize = 44;
                txtArrow.fontStyle = FontStyle.Bold;
                txtArrow.color = Color.white;
                txtArrow.alignment = TextAnchor.MiddleCenter;

                _tiles.Add(new TileCell { Index = i, RootGo = tileGo, Background = img, Label = txt, ArrowText = txtArrow });
            }

            // Central Dice Panel
            BuildDicePanel();
        }

        private Color GetTileColor(int type)
        {
            switch (type)
            {
                case 0: return tileStartColor;
                case 1: return tileGoalColor;
                case 2: return tileGreenColor;
                case 3: return tileBlueColor;
                case 4: return tilePurpleColor;
                case 5: return tileRedColor;
                case 6: return tileYellowColor;
                default: return Color.gray;
            }
        }

        private string GetTileLabelText(int index, int type)
        {
            if (index == 0) return "START";
            if (index == BoardGameManager.MaxTiles - 1) return "GOAL";

            switch (type)
            {
                case 2: return "SAFE";
                case 3: return "GAME";
                case 4: return "SWAP";
                case 5: return "BACK";
                case 6: return "GO";
                default: return index.ToString();
            }
        }

        private string GetTileArrowUnicode(int type)
        {
            if (type == 5) return "⬅"; // Red (Backward)
            if (type == 6) return "➡"; // Yellow (Forward)
            return "";
        }

        private void BuildDicePanel()
        {
            var panelGo = new GameObject("DicePanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _dicePanel = panelGo.GetComponent<RectTransform>();
            _dicePanel.SetParent(playField, false);
            _dicePanel.anchorMin = _dicePanel.anchorMax = new Vector2(0.5f, 0.5f);
            _dicePanel.pivot = new Vector2(0.5f, 0.5f);
            _dicePanel.sizeDelta = new Vector2(360, 280);
            _dicePanel.anchoredPosition = new Vector2(0, -60); // Centered in the middle of the board

            var img = panelGo.GetComponent<Image>();
            img.color = new Color(0.12f, 0.14f, 0.20f, 0.95f); // Rich dark blue-gray card

            var vlg = panelGo.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.padding = new RectOffset(16, 16, 16, 16);
            vlg.spacing = 10;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            _diceLabel = MakeChildText("DiceLabel", panelGo.transform, "Dice Roll", 32, FontStyle.Bold, new Color(0.85f, 0.85f, 0.85f));
            _diceValueText = MakeChildText("DiceValue", panelGo.transform, "TAP A", 88, FontStyle.Bold, Color.white);

            _dicePanel.gameObject.SetActive(false);
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
