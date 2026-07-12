using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PartyGame
{
    /// <summary>
    /// Baskin Robbins 31 - Extensible Co-op Party Game.
    /// Features:
    /// - Alor-dalor (알록달록) colorful Rich Text header.
    /// - Central Left: Large current number (or "?" if hidden).
    /// - Central Right: Next speakable numbers, labeled "말할 수 있는 최대 숫자".
    /// - Player Order UI: Bottom list of connected players, highlighting the active one.
    /// - Input: Mobile NumberPad template (0-9 pad + 4 item slots).
    /// - Items: Completely private (pushed secretly to phone, other players don't know).
    /// - Events (75% on action): +-2 max speak limit, target 31 change (+2 max), skip turn, reverse direction, reveal numbers, give item.
    /// </summary>
    public class BaskinRobbins31Game : MonoBehaviour
    {
        [Header("References")]
        public RectTransform playField;
        public PlayerScoreboard scoreboard;

        [Header("Game Settings")]
        [Tooltip("The target number. Saying this or higher loses the game.")]
        public int targetNumber = 31;
        [Tooltip("The maximum increment a player can speak (defaults to 3).")]
        public int maxAddLimit = 3;

        [Header("UI References (Optional - Assigned from Scene)")]
        public UnityEngine.UI.Text titleLabel;
        public UnityEngine.UI.Text directionLabel;
        public UnityEngine.UI.Text turnLabel;
        public UnityEngine.UI.Text currentNumLabel;
        public UnityEngine.UI.Text limitLabel;
        public UnityEngine.UI.Text speakableNumsLabel;
        public UnityEngine.UI.Text statusToast;
        public UnityEngine.UI.Text instructionText;
        public RectTransform playerOrderContainer;

        private Font _font;
        private PlayerConnectionManager _connections;
        private MobileServer _server;

        // UI Built elements
        private Text _titleLabel;
        private Text _currentNumLabel;
        private Text _limitLabel;
        private Text _speakableNumsLabel;
        private Text _statusToast;
        private Text _directionLabel;
        private Text _turnLabel;
        private Text _instructionText;
        private RectTransform _playerOrderContainer;

        // Game State
        private int _currentNumber = 0;
        private int _currentPlayerIndex = 0;
        private bool _isClockwise = true;
        private int _blindTurnsLeft = 0; // if > 0, current number is hidden as "?"
        private int _targetNumberOffset = 0; // target is 31 + offset
        private int _addLimitModifier = 0; // offset applied to maxAddLimit
        private bool _allowSpeakZero = false; // set to true via item usage

        // Player Data
        private class PlayerState
        {
            public PlayerInputData input;
            public int order;
            public Color color;
            public string[] items = new string[4]; // size 4 private inventory
            public GameObject uiBox;
            public Text highlightBorder;
            public Text nickText;
            public int nextTurnLimitModifier = 0; // used for opponent -1/-2/-3 item
        }

        private readonly List<PlayerState> _players = new List<PlayerState>();

        private static readonly Color[] PlayerColors = {
            new Color(0f, 0.94f, 1f, 1f),       // Cyber Cyan
            new Color(0.75f, 0.33f, 0.93f, 1f),  // Electric Purple
            new Color(1f, 0.16f, 0.43f, 1f),    // Neon Pink
            new Color(0.96f, 0.84f, 0.43f, 1f)   // Solar Yellow
        };

        private void Awake()
        {
            if (playField == null) playField = transform as RectTransform;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _connections = PlayerConnectionManager.Instance;
            _server = MobileServer.Instance;

            SetupCamera();

            // Wire public fields to the private references if assigned
            if (titleLabel != null) _titleLabel = titleLabel;
            if (directionLabel != null) _directionLabel = directionLabel;
            if (turnLabel != null) _turnLabel = turnLabel;
            if (currentNumLabel != null) _currentNumLabel = currentNumLabel;
            if (limitLabel != null) _limitLabel = limitLabel;
            if (speakableNumsLabel != null) _speakableNumsLabel = speakableNumsLabel;
            if (statusToast != null) _statusToast = statusToast;
            if (instructionText != null) _instructionText = instructionText;
            if (playerOrderContainer != null) _playerOrderContainer = playerOrderContainer;

            BuildUIMissingElements();
        }

        private void Start()
        {
            InitializePlayers();
            StartCoroutine(GameplayLoop());
        }

        private void SetupCamera()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.11f, 0.12f, 0.16f, 1f); // Dark panel theme background
            }
        }

        private void ReturnToLobby()
        {
            if (BoardGameManager.Instance != null && BoardGameManager.Instance.IsGameActive)
            {
                SceneManager.LoadScene("BoardGame");
            }
            else
            {
                SceneManager.LoadScene(SceneNavigator.GameSelectScene);
            }
        }

        // -------------------------------------------------------------- Game Initializations

        private void InitializePlayers()
        {
            _players.Clear();

            if (_connections != null && _connections.PlayerCount > 0)
            {
                for (int i = 0; i < _connections.PlayerCount; i++)
                {
                    var p = _connections.Players[i];
                    var state = new PlayerState {
                        input = p,
                        order = p.JoinOrder,
                        color = PlayerColors[(p.JoinOrder - 1) % PlayerColors.Length]
                    };
                    // Fill initial slots empty
                    for (int j = 0; j < 4; j++) state.items[j] = "";
                    _players.Add(state);
                }
            }
            else
            {
                // Standalone Editor testing fallback
                var testState = new PlayerState {
                    input = gameObject.AddComponent<PlayerInputData>(),
                    order = 1,
                    color = PlayerColors[0]
                };
                testState.input.Nickname = "TESTER_1";
                testState.input.ClientId = 999;
                for (int j = 0; j < 4; j++) testState.items[j] = "";
                _players.Add(testState);

                var testState2 = new PlayerState {
                    input = gameObject.AddComponent<PlayerInputData>(),
                    order = 2,
                    color = PlayerColors[1]
                };
                testState2.input.Nickname = "TESTER_2";
                testState2.input.ClientId = 888;
                for (int j = 0; j < 4; j++) testState2.items[j] = "";
                _players.Add(testState2);
            }

            BuildPlayerOrderUI();
        }

        private void BuildPlayerOrderUI()
        {
            // Clear old children
            foreach (Transform child in _playerOrderContainer)
            {
                Destroy(child.gameObject);
            }

            foreach (var p in _players)
            {
                var boxGo = new GameObject("PlayerBox_" + p.input.Nickname, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
                boxGo.transform.SetParent(_playerOrderContainer, false);

                var rt = boxGo.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(200f, 120f);

                var img = boxGo.GetComponent<Image>();
                img.color = new Color(0.17f, 0.20f, 0.31f, 1f); // panel color

                var vlg = boxGo.GetComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.padding = new RectOffset(6, 6, 6, 6);

                // Add Nickname
                var nickGo = new GameObject("NickText", typeof(RectTransform), typeof(Text));
                nickGo.transform.SetParent(boxGo.transform, false);
                var tNick = nickGo.GetComponent<Text>();
                tNick.font = _font;
                tNick.text = p.input.Nickname.ToUpper();
                tNick.fontSize = 24;
                tNick.fontStyle = FontStyle.Bold;
                tNick.color = p.color;
                tNick.alignment = TextAnchor.MiddleCenter;

                // Add Highlight Border
                var borderGo = new GameObject("Border", typeof(RectTransform), typeof(Text));
                borderGo.transform.SetParent(boxGo.transform, false);
                var tBorder = borderGo.GetComponent<Text>();
                tBorder.font = _font;
                tBorder.text = "ACTIVE";
                tBorder.fontSize = 18;
                tBorder.fontStyle = FontStyle.Bold;
                tBorder.color = Color.white;
                tBorder.alignment = TextAnchor.MiddleCenter;
                tBorder.gameObject.SetActive(false);

                p.uiBox = boxGo;
                p.highlightBorder = tBorder;
                p.nickText = tNick;
            }
        }

        private void UpdateHighlightUI()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                bool isActive = (i == _currentPlayerIndex);
                var p = _players[i];
                if (p.uiBox != null)
                {
                    var img = p.uiBox.GetComponent<Image>();
                    img.color = isActive ? p.color : new Color(0.17f, 0.20f, 0.31f, 1f);
                    p.highlightBorder.color = isActive ? Color.black : Color.white;
                    p.highlightBorder.gameObject.SetActive(isActive);
                    if (p.nickText != null)
                    {
                        p.nickText.color = isActive ? Color.black : p.color;
                    }
                }
            }
        }

        // -------------------------------------------------------------- Private Inventory Push

        private void PushPlayerItems(PlayerState p)
        {
            if (_server == null || p.input == null || p.input.ClientId == 999) return;

            // Generate pipeline delimited inventory label string
            string itemsStr = string.Join("|", p.items);
            _server.Send(p.input.ClientId, NetMessage.Items(itemsStr));
        }

        private bool AddItemToInventory(PlayerState p, string itemName)
        {
            for (int i = 0; i < 4; i++)
            {
                if (string.IsNullOrEmpty(p.items[i]))
                {
                    p.items[i] = itemName;
                    PushPlayerItems(p);
                    return true;
                }
            }
            return false; // inventory full
        }

        // -------------------------------------------------------------- Gameplay loop & logic

        private IEnumerator GameplayLoop()
        {
            // Activate NumberPad template for everyone
            SetTemplate(MobileTemplate.NumberPad);

            // Push initial clean empty slots to all players
            foreach (var p in _players)
            {
                PushPlayerItems(p);
            }

            _statusToast.text = "Baskin Robbins 31 starting!";
            _statusToast.gameObject.SetActive(true);
            yield return new WaitForSeconds(2.5f);
            _statusToast.gameObject.SetActive(false);

            bool gameOver = false;

            while (!gameOver)
            {
                UpdateHighlightUI();
                var activePlayer = _players[_currentPlayerIndex];
                activePlayer.input.ResetInputs(); // clean any old buffered submissions

                // Update current display
                RefreshPCDisplay();

                _statusToast.text = $"{activePlayer.input.Nickname.ToUpper()}'s Turn!";
                _statusToast.gameObject.SetActive(true);

                // Wait for player response (Submit number OR use item)
                int spokenNumber = -1;
                bool validTurn = false;

                while (!validTurn)
                {
                    // Check item tap
                    if (activePlayer.input.PendingItemSlot >= 1 && activePlayer.input.PendingItemSlot <= 4)
                    {
                        int slotIdx = activePlayer.input.PendingItemSlot - 1;
                        string itemUsed = activePlayer.items[slotIdx];
                        
                        if (!string.IsNullOrEmpty(itemUsed))
                        {
                            activePlayer.items[slotIdx] = ""; // consume
                            PushPlayerItems(activePlayer);
                            
                            // Handle private item execution!
                            yield return StartCoroutine(ExecuteItem(activePlayer, itemUsed));
                            RefreshPCDisplay();
                        }
                        activePlayer.input.PendingItemSlot = 0; // reset
                    }

                    // Check number submit
                    if (activePlayer.input.HasSubmittedNumber)
                    {
                        int spokenCount = activePlayer.input.SubmittedNumber;
                        activePlayer.input.HasSubmittedNumber = false;

                        // Validate if spokenCount (the amount of numbers to say) is within valid ranges
                        int currentLimit = maxAddLimit + _addLimitModifier - activePlayer.nextTurnLimitModifier;
                        if (currentLimit < 1) currentLimit = 1;

                        // Check special 0-speaking override
                        if (spokenCount == 0 && _allowSpeakZero)
                        {
                            _statusToast.text = $"{activePlayer.input.Nickname.ToUpper()} used Speak 0! Turn passed.";
                            _allowSpeakZero = false; // consume item state
                            spokenNumber = _currentNumber;
                            validTurn = true;
                        }
                        else if (spokenCount >= 1 && spokenCount <= currentLimit)
                        {
                            // Build a text of spoken numbers
                            string spokenListText = "";
                            for (int k = 1; k <= spokenCount; k++)
                            {
                                spokenListText += $"{_currentNumber + k} ";
                            }
                            _statusToast.text = $"{activePlayer.input.Nickname.ToUpper()} spoke: {spokenListText.Trim()}!";
                            
                            spokenNumber = _currentNumber + spokenCount;
                            validTurn = true;
                        }
                        else
                        {
                            _statusToast.text = $"Invalid! Speak 1 ~ {currentLimit} numbers.";
                        }
                    }

                    yield return null;
                }

                // Correctly submit number!
                _statusToast.gameObject.SetActive(false);
                activePlayer.nextTurnLimitModifier = 0; // Reset debit

                if (spokenNumber > _currentNumber)
                {
                    _currentNumber = spokenNumber;
                }

                RefreshPCDisplay();

                // Check game over
                int currentLosingTarget = targetNumber + _targetNumberOffset;
                if (_currentNumber >= currentLosingTarget)
                {
                    gameOver = true;
                    _statusToast.text = $"{activePlayer.input.Nickname.ToUpper()} SAID {currentLosingTarget}! GAME OVER!";
                    _statusToast.gameObject.SetActive(true);
                    
                    // Deduct score or rank up
                    activePlayer.input.Score = Mathf.Max(0, activePlayer.input.Score - 5);
                    if (scoreboard != null) scoreboard.Refresh();
                    
                    yield return new WaitForSeconds(5.0f);
                    break;
                }

                // Check 75% turn-start random event
                if (Random.value <= 0.75f)
                {
                    yield return StartCoroutine(TriggerRandomEvent(activePlayer));
                }

                // Decrement blind/hidden counter
                if (_blindTurnsLeft > 0)
                {
                    _blindTurnsLeft--;
                }

                // Advance to next player turn
                AdvanceTurn();
            }

            // Return to lobby
            SetTemplate(MobileTemplate.None);
            ReturnToLobby();
        }

        private void AdvanceTurn()
        {
            int step = _isClockwise ? 1 : -1;
            _currentPlayerIndex = (_currentPlayerIndex + step + _players.Count) % _players.Count;
        }

        // -------------------------------------------------------------- 6 Random Events (75% Chance)

        private IEnumerator TriggerRandomEvent(PlayerState p)
        {
            int eventChoice = Random.Range(1, 7); // 1 to 6
            string eventDesc = "";

            switch (eventChoice)
            {
                case 1:
                    // 1. 말할 수 있는 최대 숫자에 +-2까지 랜덤으로 변해.
                    int mod = Random.Range(-2, 3); // -2 to +2
                    if (mod == 0) mod = 1;
                    _addLimitModifier = Mathf.Clamp(_addLimitModifier + mod, -2, 2);
                    eventDesc = $"🔔 EVENT: 최대치 변동! ({(_addLimitModifier >= 0 ? "+" : "")}{_addLimitModifier})";
                    break;

                case 2:
                    // 2. 31을 말하는 규칙에서 +2까지 랜덤으로 변해.
                    _targetNumberOffset = Random.Range(0, 3); // target becomes 31, 32, or 33
                    eventDesc = $"🔔 EVENT: 탈락 목표가 {targetNumber + _targetNumberOffset}로 상향 조정!";
                    break;

                case 3:
                    // 3. 이번 턴 플레이어 건너뛰기!
                    eventDesc = $"🔔 EVENT: {p.input.Nickname.ToUpper()} 패스 건너뛰기!";
                    break;

                case 4:
                    // 4. 플레이 방향 바꾸기!
                    _isClockwise = !_isClockwise;
                    eventDesc = $"🔔 EVENT: 플레이 방향 반대로 역회전! ({(_isClockwise ? "우회전" : "좌회전")})";
                    break;

                case 5:
                    // 5. 3턴간 숫자 안 가리기! (가리기 해제)
                    _blindTurnsLeft = 0;
                    eventDesc = "🔔 EVENT: 숫자 정상 표기 가동! ";
                    break;

                case 6:
                    // 6. 플레이어에게 아이템을 줘 (비밀)
                    string[] itemPool = {
                        "+1 Point Add", "+2 Point Add", "+3 Point Add",
                        "-1 Opponent Limit", "-2 Opponent Limit", "-3 Opponent Limit",
                        "Speak 0", "Skip Next", "Reverse Direction", "Hide Numbers"
                    };
                    string randomItem = itemPool[Random.Range(0, itemPool.Length)];
                    bool ok = AddItemToInventory(p, randomItem);
                    if (ok)
                    {
                        // Secret notice to the player only, other players don't know!
                        eventDesc = "🔔 EVENT: 럭키 박스 가동! 누군가 비밀 선물을 받았습니다.";
                    }
                    else
                    {
                        eventDesc = "🔔 EVENT: 럭키 박스가 배달되었으나 인벤토리가 가득 찼습니다!";
                    }
                    break;
            }

            _statusToast.text = eventDesc;
            _statusToast.color = Color.yellow;
            _statusToast.gameObject.SetActive(true);
            yield return new WaitForSeconds(2.5f);
            _statusToast.color = Color.white;
            _statusToast.gameObject.SetActive(false);

            // Skip turn directly if event 3
            if (eventChoice == 3)
            {
                AdvanceTurn();
                UpdateHighlightUI();
            }
        }

        // -------------------------------------------------------------- 10 Secret Items Execution

        private IEnumerator ExecuteItem(PlayerState user, string item)
        {
            _statusToast.text = $"{user.input.Nickname.ToUpper()} used an ITEM!";
            _statusToast.gameObject.SetActive(true);
            yield return new WaitForSeconds(1.5f);

            switch (item)
            {
                // Item 1: 이번턴 내가 말하는 숫자 +1 / +2 / +3
                case "+1 Point Add":
                    _currentNumber += 1;
                    _statusToast.text = $"+1 Point added secretly! Current: {_currentNumber}";
                    break;
                case "+2 Point Add":
                    _currentNumber += 2;
                    _statusToast.text = $"+2 Points added secretly! Current: {_currentNumber}";
                    break;
                case "+3 Point Add":
                    _currentNumber += 3;
                    _statusToast.text = $"+3 Points added secretly! Current: {_currentNumber}";
                    break;

                // Item 2: 다음턴 상대가 말할 수 있는 숫자 -1 / -2 / -3
                case "-1 Opponent Limit":
                    int nextIdx1 = (_currentPlayerIndex + (_isClockwise ? 1 : -1) + _players.Count) % _players.Count;
                    _players[nextIdx1].nextTurnLimitModifier = 1;
                    _statusToast.text = $"Decreased next player's speak limit by 1!";
                    break;
                case "-2 Opponent Limit":
                    int nextIdx2 = (_currentPlayerIndex + (_isClockwise ? 1 : -1) + _players.Count) % _players.Count;
                    _players[nextIdx2].nextTurnLimitModifier = 2;
                    _statusToast.text = $"Decreased next player's speak limit by 2!";
                    break;
                case "-3 Opponent Limit":
                    int nextIdx3 = (_currentPlayerIndex + (_isClockwise ? 1 : -1) + _players.Count) % _players.Count;
                    _players[nextIdx3].nextTurnLimitModifier = 3;
                    _statusToast.text = $"Decreased next player's speak limit by 3!";
                    break;

                // Item 3: 0 말하기 아이템
                case "Speak 0":
                    // Handled as validated special number in update loop, no additional steps
                    _statusToast.text = "You can speak the same current number to skip!";
                    break;

                // Item 4: 다음 플레이어 건너뛰기!
                case "Skip Next":
                    int skipIdx = (_currentPlayerIndex + (_isClockwise ? 1 : -1) + _players.Count) % _players.Count;
                    _statusToast.text = $"Skipped next player {_players[skipIdx].input.Nickname.ToUpper()}'s turn!";
                    // Perform the skip
                    AdvanceTurn();
                    break;

                // Item 5: 플레이 방향 바꾸기
                case "Reverse Direction":
                    _isClockwise = !_isClockwise;
                    _statusToast.text = $"Direction Reversed! Clockwise: {_isClockwise}";
                    break;

                // Item 6: 3턴간 숫자 안보이게
                case "Hide Numbers":
                    _blindTurnsLeft = 3;
                    _statusToast.text = "Numbers will be hidden as '?' for 3 turns!";
                    break;
            }

            _statusToast.gameObject.SetActive(true);
            yield return new WaitForSeconds(2.0f);
            _statusToast.gameObject.SetActive(false);
        }

        // -------------------------------------------------------------- PC Screen Refresh

        private void RefreshPCDisplay()
        {
            // Title: Alor-dalor (알록달록) Baskin Robbins 31
            _titleLabel.text = "베스킨 라빈스 "+
                               $"<color=#00F0FF>{targetNumber + _targetNumberOffset}</color>";

            // Left Display: Current number or "?"
            if (_blindTurnsLeft > 0)
            {
                _currentNumLabel.text = "?";
            }
            else
            {
                _currentNumLabel.text = _currentNumber.ToString();
            }

            // Right Display: Labeled speakable numbers
            int activeLimit = maxAddLimit + _addLimitModifier;
            if (activeLimit < 1) activeLimit = 1;

            _limitLabel.text = $"말할 수 있는 최대 숫자: <color=yellow>{activeLimit}</color>";

            string rangeStr = "";
            for (int i = 1; i <= activeLimit; i++)
            {
                rangeStr += $"{_currentNumber + i}  ";
            }
            _speakableNumsLabel.text = rangeStr.Trim();

            // Directions
            _directionLabel.text = $"플레이 회전: <color=cyan>{(_isClockwise ? "우회전 ↻" : "좌회전 ↺")}</color>";
        }

        private void SetTemplate(MobileTemplate t)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.SetMobileTemplate(t);
            else if (_connections != null)
                _connections.SetTemplateForAll(t);
        }

        // -------------------------------------------------------------- UI Setup

        private void BuildUIMissingElements()
        {
            // Resolve any preplaced but unassigned UI children by name
            if (_titleLabel == null) _titleLabel = FindChildText("TitleLabel");
            if (_directionLabel == null) _directionLabel = FindChildText("DirectionLabel");
            if (_currentNumLabel == null) _currentNumLabel = FindChildText("CurrentNum");
            if (_limitLabel == null) _limitLabel = FindChildText("LimitLabel");
            if (_speakableNumsLabel == null) _speakableNumsLabel = FindChildText("SpeakableNums");
            if (_statusToast == null) _statusToast = FindChildText("StatusToast");
            if (_instructionText == null) _instructionText = FindChildText("ExitInstruction");
            if (_playerOrderContainer == null)
            {
                Transform t = playField.Find("PlayerOrderContainer");
                if (t != null) _playerOrderContainer = t as RectTransform;
            }

            // Fallback: build any elements that were completely missing
            if (_titleLabel == null)
            {
                _titleLabel = CreateText("TitleLabel", playField, new Vector2(0, 360), new Vector2(1000, 80), 54, FontStyle.Bold);
                _titleLabel.supportRichText = true;
            }
            if (_directionLabel == null)
            {
                _directionLabel = CreateText("DirectionLabel", playField, new Vector2(0, 290), new Vector2(1000, 50), 32, FontStyle.Bold);
                _directionLabel.supportRichText = true;
            }
            if (_currentNumLabel == null)
            {
                _currentNumLabel = CreateText("CurrentNum", playField, new Vector2(-250, 40), new Vector2(500, 350), 180, FontStyle.Bold);
                _currentNumLabel.color = new Color(0f, 0.94f, 1f, 1f); // cyber cyan
            }
            if (_limitLabel == null)
            {
                _limitLabel = CreateText("LimitLabel", playField, new Vector2(250, 120), new Vector2(500, 80), 34, FontStyle.Bold);
                _limitLabel.supportRichText = true;
            }
            if (_speakableNumsLabel == null)
            {
                _speakableNumsLabel = CreateText("SpeakableNums", playField, new Vector2(250, 20), new Vector2(500, 100), 52, FontStyle.Bold);
                _speakableNumsLabel.color = Color.yellow;
            }
            if (_playerOrderContainer == null)
            {
                var listGo = new GameObject("PlayerOrderContainer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                _playerOrderContainer = listGo.GetComponent<RectTransform>();
                _playerOrderContainer.SetParent(playField, false);
                _playerOrderContainer.anchorMin = _playerOrderContainer.anchorMax = new Vector2(0.5f, 0.5f);
                _playerOrderContainer.pivot = new Vector2(0.5f, 0.5f);
                _playerOrderContainer.sizeDelta = new Vector2(1200, 160);
                _playerOrderContainer.anchoredPosition = new Vector2(0, -280);

                var hlg = listGo.GetComponent<HorizontalLayoutGroup>();
                hlg.spacing = 20;
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlWidth = false;
                hlg.childControlHeight = false;
            }
            if (_statusToast == null)
            {
                _statusToast = CreateText("StatusToast", playField, new Vector2(0, -120), new Vector2(1400, 120), 40, FontStyle.Bold);
                _statusToast.text = "";
                _statusToast.gameObject.SetActive(false);
            }
            if (_instructionText == null)
            {
                _instructionText = CreateText("ExitInstruction", playField, new Vector2(0, -420), new Vector2(1000, 50), 28, FontStyle.Bold);
                _instructionText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
                _instructionText.text = "PRESS ESC ON KEYBOARD TO RETURN";
            }
        }

        private Text FindChildText(string childName)
        {
            if (playField == null) return null;
            Transform t = playField.Find(childName);
            if (t != null) return t.GetComponent<Text>();
            return null;
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