using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PartyGame
{
    /// <summary>
    /// "Escape" Cooperative Game.
    /// All players control 2D circular pointers (tagged "playerpointer") using their mobile joysticks.
    /// Simpler design: no hazards, no safe zones, no trails. Just pure cooperative pointer synchronization on screen.
    /// </summary>
    public class EscapeGame : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Full-screen RectTransform that gameplay visuals are parented to. Defaults to this object.")]
        public RectTransform playField;
        public PlayerScoreboard scoreboard;

        [Header("Design Colors")]
        public Color backgroundColor = new Color(0.067f, 0.075f, 0.102f, 1f); // #11131A

        [Header("Movement & Constraints")]
        public float pointerSpeed = 440f;
        public float pointerRadius = 24f; // 48px diameter

        private Font _font;
        private PlayerConnectionManager _connections;

        private Text _statusText;
        private Text _turnText;

        // Pointers and players
        private readonly List<PointerInstance> _pointers = new List<PointerInstance>();
        private static readonly Color[] PlayerColors = {
            new Color(0f, 0.94f, 1f, 1f),       // Cyber Cyan (#00F0FF)
            new Color(0.75f, 0.33f, 0.93f, 1f),  // Electric Purple (#BF55EC)
            new Color(1f, 0.16f, 0.43f, 1f),    // Neon Pink (#FF2A6D)
            new Color(0.96f, 0.84f, 0.43f, 1f)   // Solar Yellow (#F5D76E)
        };

        private class PointerInstance
        {
            public int ClientId;
            public string Nickname;
            public int Order;
            public Color Color;
            public RectTransform Rect;
            public PlayerInputData InputSource; // Can be null in editor fallback
            public Vector2 Position;
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
            InitializePlayersAndPointers();
            StartCoroutine(GameplayLoop());
        }

        private void Update()
        {
            // 1. Move Player Pointers
            HandlePointerMovement();

            // 2. Allow host to exit the sandbox back to selection by pressing Escape key
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ReturnToLobby();
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

        // -------------------------------------------------------------- Main Game Loop

        private IEnumerator GameplayLoop()
        {
            // Set all active players to JoystickAB template
            SetMobileTemplates(MobileTemplate.JoystickAB);

            _statusText.text = "Escape Sandbox!";
            _statusText.gameObject.SetActive(true);
            yield return new WaitForSeconds(2.0f);
            _statusText.gameObject.SetActive(false);

            // Infinite loop allowing players to move their cursors until manually exited
            while (true)
            {
                yield return null;
            }
        }

        // -------------------------------------------------------------- Movement & Mechanics

        private void HandlePointerMovement()
        {
            foreach (var p in _pointers)
            {
                Vector2 moveDir = Vector2.zero;
                bool isSpeedBoost = false;

                if (p.InputSource != null)
                {
                    // Live network input
                    moveDir = p.InputSource.Joystick;
                    isSpeedBoost = p.InputSource.JoystickB;
                }
                else
                {
                    // Editor Keyboard fallback using modern New Input System API
                    float h = 0f;
                    float v = 0f;
                    var keyboard = UnityEngine.InputSystem.Keyboard.current;
                    if (keyboard != null)
                    {
                        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) v = 1f;
                        else if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) v = -1f;

                        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h = 1f;
                        else if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) h = -1f;

                        // Allow B key or Shift keys for speed boost fallback in the Unity Editor
                        isSpeedBoost = keyboard.bKey.isPressed || keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                    }
                    moveDir = new Vector2(h, v).normalized;
                }

                float currentSpeed = isSpeedBoost ? pointerSpeed * 2f : pointerSpeed;

                // Update position
                p.Position += moveDir * currentSpeed * Time.deltaTime;

                // Restrict within 16:9 viewport boundaries (Width: 1920, Height: 1080)
                // playField bounds are -960..960 X, -540..540 Y. Clamp with radius padding
                float limitX = 960f - pointerRadius;
                float limitY = 540f - pointerRadius;

                p.Position.x = Mathf.Clamp(p.Position.x, -limitX, limitX);
                p.Position.y = Mathf.Clamp(p.Position.y, -limitY, limitY);

                // Apply to RectTransform
                p.Rect.anchoredPosition = p.Position;
            }
        }

        // -------------------------------------------------------------- UI & Players Initialization

        private void InitializePlayersAndPointers()
        {
            _pointers.Clear();

            if (_connections != null && _connections.PlayerCount > 0)
            {
                // Network players
                for (int i = 0; i < _connections.PlayerCount; i++)
                {
                    var p = _connections.Players[i];
                    CreatePointer(p.ClientId, p.Nickname, p.JoinOrder, p);
                }
            }
            else
            {
                // Local Editor test pointer
                CreatePointer(999, "TESTER", 1, null);
            }
        }

        private void CreatePointer(int clientId, string nickname, int order, PlayerInputData input)
        {
            var pointerGo = new GameObject("Pointer_" + nickname, typeof(RectTransform), typeof(Image));
            pointerGo.transform.SetParent(playField, false);
            pointerGo.tag = "playerpointer"; // AS REQUIRED BY USER!

            var rt = pointerGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(pointerRadius * 2f, pointerRadius * 2f);

            // Centered starting positions
            Vector2 startPos = new Vector2(-150f + (order - 1) * 100f, 0f);
            rt.anchoredPosition = startPos;

            var img = pointerGo.GetComponent<Image>();
            img.sprite = CircleSpriteFactory.WhiteCircle;
            Color col = PlayerColors[(order - 1) % PlayerColors.Length];
            img.color = col;

            // Nickname initial label inside pointer
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(pointerGo.transform, false);
            var rtLabel = labelGo.GetComponent<RectTransform>();
            rtLabel.anchorMin = Vector2.zero;
            rtLabel.anchorMax = Vector2.one;
            rtLabel.sizeDelta = Vector2.zero;

            var txt = labelGo.GetComponent<Text>();
            txt.font = _font;
            txt.text = nickname.Length > 0 ? nickname.Substring(0, 1).ToUpper() : "P";
            txt.fontSize = 24;
            txt.fontStyle = FontStyle.Bold;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            _pointers.Add(new PointerInstance {
                ClientId = clientId,
                Nickname = nickname,
                Order = order,
                Color = col,
                Rect = rt,
                InputSource = input,
                Position = startPos
            });
        }

        private void SetMobileTemplates(MobileTemplate t)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.SetMobileTemplate(t);
            else if (_connections != null)
                _connections.SetTemplateForAll(t);
        }

        // -------------------------------------------------------------- UI procedural assembly

        private void BuildUI()
        {
            // Turn Header Label
            _turnText = CreateText("TurnText", playField, new Vector2(0, 320), new Vector2(1000, 80), 48, FontStyle.Bold);
            _turnText.text = "Cooperative Pointer Room";

            // Status message
            _statusText = CreateText("StatusText", playField, new Vector2(0, 0), new Vector2(1400, 300), 64, FontStyle.Bold);
            _statusText.text = "";
            _statusText.gameObject.SetActive(false);

            // Exit Instruction text
            var instructionText = CreateText("ExitInstruction", playField, new Vector2(0, -420), new Vector2(1000, 50), 28, FontStyle.Bold);
            instructionText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            instructionText.text = "PRESS ESC ON KEYBOARD TO RETURN";
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
