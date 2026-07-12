using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PartyGame
{
    /// <summary>
    /// 'Escape2' - Clean 3D Projectile Cooperative Shooting Game Template.
    /// Use this as a solid starting point for any cooperative shooter featuring 3D projectiles
    /// and 2D canvas-space aiming.
    /// Features:
    /// - Pointers: Moves 2D UI reticles (tagged 'playerpointer') parallel to the screen.
    /// - Input: Automatically handles mobile joysticks or local keyboard controls.
    /// - Shooting: Pressing A/Spacebar charges a shot, releasing fires a real 3D Sphere projectile at constant speed.
    /// - 3D Physics: Projectiles use 3D overlap spheres to detect collisions against standard 3D Colliders.
    /// - 3D FX: Spawns spectacular 3D physics-based cube shards upon impact.
    /// - Clean & commented hook handlers for custom gameplay (e.g., scoring, enemy damage).
    /// </summary>
    public class Escape2Game : MonoBehaviour
    {
        [Header("References")]
        public RectTransform playField;
        public PlayerScoreboard scoreboard;

        [Header("Template Settings")]
        [Tooltip("Speed of screen-space reticle (pixels per second)")]
        public float reticleSpeed = 825f;
        [Tooltip("Boundary padding on screen")]
        public float pointerRadius = 32f;
        [Tooltip("Movement speed of 3D straight projectiles (units per second)")]
        public float bulletSpeed = 25f;

        private Font _font;
        private PlayerConnectionManager _connections;
        private Camera _mainCam;

        // UI references
        private Text _statusText;
        private Text _instructionText;
        private Text _turnText;

        // Player colors
        private static readonly Color[] PlayerColors = {
            new Color(0f, 0.94f, 1f, 1f),       // Cyber Cyan
            new Color(0.75f, 0.33f, 0.93f, 1f),  // Electric Purple
            new Color(1f, 0.16f, 0.43f, 1f),    // Neon Pink
            new Color(0.96f, 0.84f, 0.43f, 1f)   // Solar Yellow
        };

        // Players & Bullets
        private class PlayerReticleInstance
        {
            public int ClientId;
            public string Nickname;
            public int Order;
            public Color Color;
            public GameObject ReticleGo;
            public RectTransform Rect;
            public Image InnerDot;
            public Vector2 Position;
            public PlayerInputData InputSource;
            public float ChargeAmount;
            public bool PrevButtonA;
        }

        private readonly List<PlayerReticleInstance> _reticles = new List<PlayerReticleInstance>();

        private class Bullet3D
        {
            public GameObject go;
            public Vector3 start;
            public Vector3 target;
            public float elapsed;
            public float travelDuration;
            public PlayerInputData shooter;
            public Color playerColor;
            public float damage;
            public float charge;
        }

        private readonly List<Bullet3D> _bullets = new List<Bullet3D>();

        private void Awake()
        {
            if (playField == null) playField = transform as RectTransform;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _connections = PlayerConnectionManager.Instance;

            SetupCamera();
            BuildUI();
        }

        private void Start()
        {
            InitializePlayersAndReticles();
            StartCoroutine(GameplayLoop());
        }

        private void Update()
        {
            HandlePlayerAiming();
            UpdateBullets3D();

            // Handle Escape key to return
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ReturnToLobby();
            }
        }

        private void OnDestroy()
        {
            foreach (var r in _reticles)
            {
                if (r.ReticleGo != null) Destroy(r.ReticleGo);
            }
            foreach (var b in _bullets)
            {
                if (b.go != null) Destroy(b.go);
            }
        }

        private void SetupCamera()
        {
            _mainCam = Camera.main;
            if (_mainCam == null)
            {
                var camGo = GameObject.FindWithTag("MainCamera");
                if (camGo != null) _mainCam = camGo.GetComponent<Camera>();
            }

            if (_mainCam != null)
            {
                _mainCam.orthographic = true;
                _mainCam.orthographicSize = 5f;
                _mainCam.clearFlags = CameraClearFlags.SolidColor;
                _mainCam.backgroundColor = new Color(0.72f, 0.36f, 0.26f, 1f); // Sunset background color
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

        // -------------------------------------------------------------- Aiming & 3D Projectile Fire

        private Sprite CreateCircleOutlineSprite(int size, int thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            float rOuter = size * 0.5f;
            float rInner = rOuter - thickness;
            Vector2 center = new Vector2(rOuter, rOuter);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(0, 0, 0, 0);
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    if (d >= rInner && d <= rOuter)
                    {
                        pixels[y * size + x] = new Color32(255, 255, 255, 255);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private void InitializePlayersAndReticles()
        {
            _reticles.Clear();

            if (_connections != null && _connections.PlayerCount > 0)
            {
                for (int i = 0; i < _connections.PlayerCount; i++)
                {
                    var p = _connections.Players[i];
                    CreateReticle(p.ClientId, p.Nickname, p.JoinOrder, p);
                }
            }
            else
            {
                CreateReticle(999, "TEMPLATE_TEST", 1, null);
            }
        }

        private void CreateReticle(int clientId, string nickname, int order, PlayerInputData input)
        {
            var reticleColor = PlayerColors[(order - 1) % PlayerColors.Length];

            var reticleGo = new GameObject("Reticle_" + nickname, typeof(RectTransform));
            reticleGo.transform.SetParent(playField, false);
            reticleGo.tag = "playerpointer";

            var rt = reticleGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 100f);

            Vector2 startCanvasPos = new Vector2(-200f + (order - 1) * 150f, 50f);
            rt.anchoredPosition = startCanvasPos;

            var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(reticleGo.transform, false);
            var rtRing = ringGo.GetComponent<RectTransform>();
            rtRing.anchorMin = rtRing.anchorMax = new Vector2(0.5f, 0.5f);
            rtRing.sizeDelta = new Vector2(90f, 90f);
            var ringRenderer = ringGo.GetComponent<Image>();
            ringRenderer.sprite = CreateCircleOutlineSprite(128, 12);
            ringRenderer.color = reticleColor;

            var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(reticleGo.transform, false);
            var rtDot = dotGo.GetComponent<RectTransform>();
            rtDot.anchorMin = rtDot.anchorMax = new Vector2(0.5f, 0.5f);
            rtDot.sizeDelta = new Vector2(25f, 25f);
            var dotRenderer = dotGo.GetComponent<Image>();
            dotRenderer.sprite = CircleSpriteFactory.WhiteCircle;
            dotRenderer.color = Color.white;

            _reticles.Add(new PlayerReticleInstance {
                ClientId = clientId,
                Nickname = nickname,
                Order = order,
                Color = reticleColor,
                ReticleGo = reticleGo,
                Rect = rt,
                InnerDot = dotRenderer,
                Position = startCanvasPos,
                InputSource = input,
                ChargeAmount = 0f,
                PrevButtonA = false
            });
        }

        private void HandlePlayerAiming()
        {
            foreach (var r in _reticles)
            {
                Vector2 moveDir = Vector2.zero;
                bool isSpeedBoost = false;

                if (r.InputSource != null)
                {
                    moveDir = r.InputSource.Joystick;
                    isSpeedBoost = r.InputSource.JoystickB;
                }
                else
                {
                    float h = 0f;
                    float v = 0f;
                    var keyboard = UnityEngine.InputSystem.Keyboard.current;
                    if (keyboard != null)
                    {
                        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) v = 1f;
                        else if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) v = -1f;

                        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h = 1f;
                        else if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) h = -1f;

                        isSpeedBoost = keyboard.bKey.isPressed || keyboard.leftShiftKey.isPressed;
                    }
                    moveDir = new Vector2(h, v).normalized;
                }

                float speed = isSpeedBoost ? reticleSpeed * 2f : reticleSpeed;
                r.Position.x += moveDir.x * speed * Time.deltaTime;
                r.Position.y += moveDir.y * speed * Time.deltaTime;

                float halfWidth = playField.rect.width * 0.5f;
                float halfHeight = playField.rect.height * 0.5f;
                float limitX = halfWidth - pointerRadius;
                float limitY = halfHeight - pointerRadius;
                r.Position.x = Mathf.Clamp(r.Position.x, -limitX, limitX);
                r.Position.y = Mathf.Clamp(r.Position.y, -limitY, limitY);

                r.Rect.anchoredPosition = r.Position;

                // Handle A Button charging
                bool btnA = (r.InputSource != null) ? r.InputSource.JoystickA : (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed);
                if (btnA)
                {
                    r.ChargeAmount = Mathf.Clamp01(r.ChargeAmount + Time.deltaTime / 1.0f);
                    float innerScale = 25f + r.ChargeAmount * 55f;
                    r.InnerDot.rectTransform.sizeDelta = new Vector2(innerScale, innerScale);
                    r.InnerDot.color = Color.Lerp(Color.white, r.Color, r.ChargeAmount);
                }
                else
                {
                    if (r.ChargeAmount > 0f)
                    {
                        FireBullet3D(r, r.ChargeAmount);
                        r.ChargeAmount = 0f;
                        r.InnerDot.rectTransform.sizeDelta = new Vector2(25f, 25f);
                        r.InnerDot.color = Color.white;
                    }
                }
                r.PrevButtonA = btnA;
            }
        }

        private void FireBullet3D(PlayerReticleInstance player, float charge)
        {
            if (_mainCam == null) return;

            // Instantiate a real 3D Sphere in world space instead of a 2D UI Image
            var bulletGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulletGo.name = $"Bullet3D_{player.Nickname}";

            // Map bottom-center of Screen-space to 3D world space
            Vector3 startPos = _mainCam.ViewportToWorldPoint(new Vector3(0.5f, 0.05f, 10f));
            startPos.z = 0f; // Lock strictly to gameplay plane

            bulletGo.transform.position = startPos;

            // Scale 3D sphere according to charge
            float bScale = 0.3f + charge * 0.5f;
            bulletGo.transform.localScale = new Vector3(bScale, bScale, bScale);

            // Configure Trigger Collider
            var col = bulletGo.GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }

            var r = bulletGo.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(Shader.Find("Unlit/Color"));
                r.material.color = Color.Lerp(new Color(1f, 0.85f, 0.15f), Color.white, charge * 0.5f);
            }

            // Convert player's 2D canvas pointer position to 3D world target position
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, player.Rect.position);
            Vector3 targetPos = _mainCam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f));
            targetPos.z = 0f; // Lock strictly to gameplay plane

            // Calculate dynamic travel duration based on constant bulletSpeed
            float dist = Vector3.Distance(startPos, targetPos);
            float duration = dist / bulletSpeed;
            if (duration <= 0f) duration = 0.01f;

            _bullets.Add(new Bullet3D {
                go = bulletGo,
                start = startPos,
                target = targetPos,
                elapsed = 0f,
                travelDuration = duration,
                shooter = player.InputSource,
                playerColor = player.Color,
                damage = (charge >= 0.9f) ? 50f : 20f,
                charge = charge
            });
        }

        private void UpdateBullets3D()
        {
            for (int i = _bullets.Count - 1; i >= 0; i--)
            {
                var b = _bullets[i];
                if (b.go == null)
                {
                    _bullets.RemoveAt(i);
                    continue;
                }

                b.elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(b.elapsed / b.travelDuration);

                // Travel in a perfect straight 3D line (projectile style)
                b.go.transform.position = Vector3.Lerp(b.start, b.target, t);

                // Collision check against active targets in the scene (Physics 3D)
                bool hitTarget = CheckPhysicsCollisions(b);

                if (hitTarget || t >= 1.0f)
                {
                    if (!hitTarget)
                    {
                        // Landed on floor - spawn satisfying 3D spark debris
                        SpawnDebris3D(b.go.transform.position, b.playerColor, 6, b.charge);
                    }
                    Destroy(b.go);
                    _bullets.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Robust 3D target collision detection template.
        /// Detects any 3D Colliders (Collider) in the scene corresponding to the bullet position.
        /// </summary>
        private bool CheckPhysicsCollisions(Bullet3D b)
        {
            if (b.go == null) return false;

            float radius = b.go.transform.localScale.x * 0.5f;
            Collider[] hitCols = Physics.OverlapSphere(b.go.transform.position, radius);

            foreach (var hitCol in hitCols)
            {
                // Ignore self and UI pointer objects
                if (hitCol.gameObject == b.go || hitCol.CompareTag("playerpointer")) continue;

                OnProjectileHit(b.go.transform.position, hitCol, b.shooter, b.playerColor, b.damage, b.charge);
                return true;
            }

            return false;
        }

        /// <summary>
        /// EXTENSIBILITY HOOK: Executes when a 3D projectile hits any 3D collider (enemy, box, obstacle, etc.).
        /// Implement scoring, enemy damage, health reductions, and visual feedback here!
        /// </summary>
        public void OnProjectileHit(Vector3 worldHitPos, Collider hitCollider, PlayerInputData shooter, Color color, float damage, float charge)
        {
            Debug.Log($"[Template Hit] Projectile hit 3D object '{hitCollider.name}' with {damage} damage!");

            // Trigger visual 3D explosion shards
            SpawnDebris3D(worldHitPos, color, 20, charge);

            // Template Score / Scoreboard Update
            int pts = 3;
            if (shooter != null)
            {
                shooter.Score += pts;
                if (scoreboard != null) scoreboard.Refresh();
            }

            // Show satisfying target hit UI toast
            if (_statusText != null)
            {
                string shooterName = shooter != null ? shooter.Nickname.ToUpper() : "PLAYER";
                _statusText.text = $"{shooterName} HIT {hitCollider.name.ToUpper()}! +3 PTS";
                StopCoroutine("ShowHitStatus");
                StartCoroutine(ShowHitStatus());
            }

            // Example: If the target has a custom script or interface, call it!
            // e.g., hitCollider.GetComponent<EnemyHealth>()?.TakeDamage(damage);
        }

        // -------------------------------------------------------------- FX & Particles (3D Cube Shards)

        private void SpawnDebris3D(Vector3 worldPos, Color color, int count, float charge)
        {
            // Scale particle count and speed based on distance (25% close, 100% far)
            float dist = Vector3.Distance(Vector3.zero, worldPos);
            float maxExpectedDistance = 15f;
            float tDist = Mathf.Clamp01(dist / maxExpectedDistance);
            float effectScale = Mathf.Lerp(0.25f, 1.0f, tDist);

            int finalCount = Mathf.RoundToInt(count * effectScale);
            finalCount = Mathf.Max(2, finalCount);

            for (int i = 0; i < finalCount; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                p.name = "ImpactShard3D";
                p.transform.position = worldPos;

                float shardSize = Random.Range(0.08f, 0.2f) * (1f + charge * 0.8f) * effectScale;
                p.transform.localScale = new Vector3(shardSize, shardSize, shardSize);
                Destroy(p.GetComponent<Collider>());

                var r = p.GetComponent<Renderer>();
                if (r != null)
                {
                    r.material = new Material(Shader.Find("Unlit/Color"));
                    r.material.color = Color.Lerp(color, Color.white, Random.Range(0f, 0.4f));
                }

                var physics = p.AddComponent<ShardPhysics3D>();
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float spd = Random.Range(3f, 8f) * (1f + charge * 1.2f) * effectScale;
                physics.velocity = new Vector3(Mathf.Cos(angle) * spd, Random.Range(3f, 8f) * (1f + charge) * effectScale, Mathf.Sin(angle) * spd);
            }
        }

        private class ShardPhysics3D : MonoBehaviour
        {
            public Vector3 velocity;
            private float _age = 0f;
            private float _lifetime = 0.65f;
            private Renderer _renderer;

            private void Awake()
            {
                _renderer = GetComponent<Renderer>();
                _lifetime = Random.Range(0.45f, 0.75f);
            }

            private void Update()
            {
                _age += Time.deltaTime;
                if (_age >= _lifetime)
                {
                    Destroy(gameObject);
                    return;
                }

                // Gravity pull downwards
                velocity.y -= 9.81f * Time.deltaTime;
                transform.position += velocity * Time.deltaTime;

                if (_renderer != null)
                {
                    float t = 1f - (_age / _lifetime);
                    _renderer.material.color = new Color(_renderer.material.color.r, _renderer.material.color.g, _renderer.material.color.b, t);
                }
            }
        }

        // -------------------------------------------------------------- Gameplay Loop

        private IEnumerator GameplayLoop()
        {
            SetMobileTemplates(MobileTemplate.JoystickAB);

            _statusText.text = "3D Co-op Shooting Template Ready!\nUse Joystick/WSAD to move, A/Space to fire.";
            _statusText.gameObject.SetActive(true);
            yield return new WaitForSeconds(3.0f);
            _statusText.gameObject.SetActive(false);

            while (true)
            {
                yield return null;
            }
        }

        private void SetMobileTemplates(MobileTemplate t)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.SetMobileTemplate(t);
            else if (_connections != null)
                _connections.SetTemplateForAll(t);
        }

        private IEnumerator ShowHitStatus()
        {
            if (_statusText != null)
            {
                _statusText.gameObject.SetActive(true);
                yield return new WaitForSeconds(1.8f);
                _statusText.gameObject.SetActive(false);
            }
        }

        private void BuildUI()
        {
            _turnText = CreateText("TurnText", playField, new Vector2(0, 320), new Vector2(1000, 80), 48, FontStyle.Bold);
            _turnText.text = "3D Co-Op Shooting Template";

            _statusText = CreateText("StatusText", playField, new Vector2(0, 0), new Vector2(1400, 300), 56, FontStyle.Bold);
            _statusText.text = "";
            _statusText.gameObject.SetActive(false);

            _instructionText = CreateText("ExitInstruction", playField, new Vector2(0, -420), new Vector2(1000, 50), 28, FontStyle.Bold);
            _instructionText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            _instructionText.text = "PRESS ESC ON KEYBOARD TO RETURN";
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