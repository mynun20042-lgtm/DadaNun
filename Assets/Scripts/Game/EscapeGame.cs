using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PartyGame
{
    /// <summary>
    /// "Escape" transformed into a 3D perspective shooter game.
    /// Features:
    /// - 3D Perspective desert sunset arena (floor, skybox color matching). No canyons/walls.
    /// - Turret gun at bottom-center of the 3D space.
    /// - Players control 2D UI reticles (tagged "playerpointer") moving parallel to the screen (상하좌우 화면 기준).
    /// - The 3D target point is determined by converting screen cursor position to world coordinates, representing the EXACT peak (apex) of the parabolic trajectory.
    /// - No red target bots are spawned ("빨간 공들은 소환하지마" as requested).
    /// - Dotted 3D parabolic aiming arc from the turret up to the screen-space pointer"s 3D position (which is the peak), then landing symmetrically on the ground.
    /// - Holding the A button charges a shot up to 1 second. On release, launches a scaling bullet along the 3D parabolic trajectory.
    /// </summary>
    public class EscapeGame : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Full-screen RectTransform that 2D overlays are parented to. Defaults to this object.")]
        public RectTransform playField;
        public PlayerScoreboard scoreboard;

        [Header("3D Scene References (Assigned or Auto-linked)")]
        public Transform desertGround;
        public Transform centralTurret;

        [Header("3D Scene Colors")]
        public Color skyColor = new Color(0.72f, 0.36f, 0.26f, 1f);       // Sunset orange (#B85B42)
        public Color groundColor = new Color(0.78f, 0.53f, 0.26f, 1f);    // Sandy gold (#C78742)
        public Color bulletColor = new Color(1f, 0.85f, 0.15f, 1f);       // Glowing yellow

        [Header("Shooting Settings")]
        public float reticleSpeed = 825f; // Speed of screen-space reticle (pixels per second)
        public float pointerRadius = 32f; // Boundary padding size on screen
        public float bulletFlightDuration = 0.75f;

        private Font _font;
        private PlayerConnectionManager _connections;
        private Camera _mainCam;

        // Visual 3D elements
        private GameObject _groundGo;
        private GameObject _turretGo;
        private GameObject _robotPrototype;
        private readonly List<PlayerReticleInstance> _reticles = new List<PlayerReticleInstance>();

        // UI references
        private Text _statusText;
        private Text _instructionText;
        private Text _turnText;

        // Player colors
        private static readonly Color[] PlayerColors = {
            new Color(0f, 0.94f, 1f, 1f),       // Cyber Cyan (#00F0FF)
            new Color(0.75f, 0.33f, 0.93f, 1f),  // Electric Purple (#BF55EC)
            new Color(1f, 0.16f, 0.43f, 1f),    // Neon Pink (#FF2A6D)
            new Color(0.96f, 0.84f, 0.43f, 1f)   // Solar Yellow (#F5D76E)
        };

        private class PlayerReticleInstance
        {
            public int ClientId;
            public string Nickname;
            public int Order;
            public Color Color;
            public GameObject ReticleGo; // 2D UI GameObject on the canvas
            public RectTransform Rect;   // 2D UI RectTransform
            public UnityEngine.UI.Image InnerDot; // Reference to inner dot to scale & flash during charging
            public Vector2 Position;     // 2D Canvas Position (-960..960, -540..540)
            public Vector3 WorldPosition; // Corresponding 3D world position (apex/peak of the trajectory)
            public Vector3 GroundTarget;  // Corresponding 3D intersection point on the ground (Y = 0)
            public PlayerInputData InputSource;
            public List<GameObject> AimDots;
            public float ChargeAmount; // 0.0f to 1.0f (charges over 1.0 second)
            public bool PrevButtonA;
        }

        private void Awake()
        {
            if (playField == null) playField = transform as RectTransform;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _connections = PlayerConnectionManager.Instance;

            // Force programmatically override any serialized inspector values to guarantee exact speed requested (half speed)
            reticleSpeed = 825f;
            pointerRadius = 32f;

            Setup3DCamera();
            Build3DEnvironment();
            BuildUI();
        }

        private void Start()
        {
            InitializePlayersAndReticles();

            // Find and clone the scene's robotSphere as a prototype template
            GameObject originalRobot = GameObject.Find("robotSphere");
            if (originalRobot != null)
            {
                _robotPrototype = Instantiate(originalRobot);
                _robotPrototype.name = "RobotPrototype_Template";
                _robotPrototype.SetActive(false);
            }

            StartCoroutine(GameplayLoop());
        }

        private void Update()
        {
            HandlePlayerAiming();
            UpdateTurretAimRotation();
            UpdateAimDottedLines();

            // Allow host to return by pressing Escape
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ReturnToLobby();
            }
        }

        private void OnDestroy()
        {
            // Clean up created runtime visual aids
            if (_robotPrototype != null) Destroy(_robotPrototype);
            foreach (var r in _reticles)
            {
                if (r.ReticleGo != null) Destroy(r.ReticleGo);
                if (r.AimDots != null)
                {
                    foreach (var dot in r.AimDots) if (dot != null) Destroy(dot);
                }
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

        // -------------------------------------------------------------- Scene Setup

        private void Setup3DCamera()
        {
            _mainCam = Camera.main;
            if (_mainCam == null)
            {
                var camGo = GameObject.FindWithTag("MainCamera");
                if (camGo != null) _mainCam = camGo.GetComponent<Camera>();
            }

            if (_mainCam != null)
            {
                _mainCam.orthographic = false; // Perspective
                _mainCam.fieldOfView = 60f;
                _mainCam.clearFlags = CameraClearFlags.SolidColor;
                _mainCam.backgroundColor = skyColor;
            }
        }

        private void Build3DEnvironment()
        {
            // 1. Check if desertGround already exists natively in scene
            if (desertGround != null)
            {
                _groundGo = desertGround.gameObject;
            }
            else
            {
                var existing = GameObject.Find("DesertGround");
                if (existing != null)
                {
                    _groundGo = existing;
                    desertGround = existing.transform;
                }
                else
                {
                    // Fallback runtime spawn if not found
                    _groundGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _groundGo.name = "DesertGround";
                    _groundGo.transform.position = new Vector3(0f, -0.5f, 0f);
                    _groundGo.transform.localScale = new Vector3(45f, 1f, 45f);
                    desertGround = _groundGo.transform;

                    var groundRenderer = _groundGo.GetComponent<Renderer>();
                    if (groundRenderer != null)
                    {
                        groundRenderer.material = new Material(Shader.Find("Unlit/Color"));
                        groundRenderer.material.color = groundColor;
                    }
                }
            }

            // 2. Check if centralTurret already exists natively in scene
            if (centralTurret != null)
            {
                _turretGo = centralTurret.gameObject;
            }
            else
            {
                var existing = GameObject.Find("CentralTurret");
                if (existing != null)
                {
                    _turretGo = existing;
                    centralTurret = existing.transform;
                }
                else
                {
                    // Fallback runtime spawn if not found
                    _turretGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    _turretGo.name = "CentralTurret";
                    _turretGo.transform.position = new Vector3(0f, 0.5f, -9.5f);
                    _turretGo.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
                    centralTurret = _turretGo.transform;

                    var turretRenderer = _turretGo.GetComponent<Renderer>();
                    if (turretRenderer != null)
                    {
                        turretRenderer.material = new Material(Shader.Find("Unlit/Color"));
                        turretRenderer.material.color = Color.gray;
                    }

                    // Add barrel to turret fallback
                    var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    barrel.name = "Barrel";
                    barrel.transform.SetParent(_turretGo.transform, false);
                    barrel.transform.localPosition = new Vector3(0f, 0.8f, 0.5f);
                    barrel.transform.localScale = new Vector3(0.4f, 0.4f, 1.8f);

                    var barrelRenderer = barrel.GetComponent<Renderer>();
                    if (barrelRenderer != null)
                    {
                        barrelRenderer.material = new Material(Shader.Find("Unlit/Color"));
                        barrelRenderer.material.color = Color.black;
                    }
                }
            }

            // Hide all renderers in centralTurret to make it completely invisible as requested!
            if (_turretGo != null)
            {
                var renderers = _turretGo.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (r != null) r.enabled = false;
                }
            }
        }

        // -------------------------------------------------------------- Players & Aiming

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
                // Local testing reticle
                CreateReticle(999, "TESTER", 1, null);
            }
        }

        private void CreateReticle(int clientId, string nickname, int order, PlayerInputData input)
        {
            var reticleColor = PlayerColors[(order - 1) % PlayerColors.Length];

            // Reticle container: 2D UI on the canvas
            var reticleGo = new GameObject("Reticle_" + nickname, typeof(RectTransform));
            reticleGo.transform.SetParent(playField, false);
            reticleGo.tag = "playerpointer"; // AS EXPLICITLY REQUIRED!

            var rt = reticleGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 100f);

            // Centered starting coordinates on canvas
            Vector2 startCanvasPos = new Vector2(-200f + (order - 1) * 150f, 50f);
            rt.anchoredPosition = startCanvasPos;

            // Outer ring Image: hollow circle facing camera
            var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            ringGo.transform.SetParent(reticleGo.transform, false);
            var rtRing = ringGo.GetComponent<RectTransform>();
            rtRing.anchorMin = rtRing.anchorMax = new Vector2(0.5f, 0.5f);
            rtRing.pivot = new Vector2(0.5f, 0.5f);
            rtRing.sizeDelta = new Vector2(90f, 90f);

            var ringRenderer = ringGo.GetComponent<UnityEngine.UI.Image>();
            ringRenderer.sprite = CreateCircleOutlineSprite(128, 12);
            ringRenderer.color = reticleColor;

            // Inner dot Image
            var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            dotGo.transform.SetParent(reticleGo.transform, false);
            var rtDot = dotGo.GetComponent<RectTransform>();
            rtDot.anchorMin = rtDot.anchorMax = new Vector2(0.5f, 0.5f);
            rtDot.pivot = new Vector2(0.5f, 0.5f);
            rtDot.sizeDelta = new Vector2(25f, 25f);

            var dotRenderer = dotGo.GetComponent<UnityEngine.UI.Image>();
            dotRenderer.sprite = CircleSpriteFactory.WhiteCircle;
            dotRenderer.color = Color.white;

            // Spawn aiming dots along the 3D trajectory line (12 dots)
            var aimDots = new List<GameObject>();
            for (int i = 0; i < 12; i++)
            {
                var dotGo3D = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dotGo3D.name = "AimDot_" + i;
                dotGo3D.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
                Destroy(dotGo3D.GetComponent<Collider>());

                var r = dotGo3D.GetComponent<Renderer>();
                if (r != null)
                {
                    r.material = new Material(Shader.Find("Unlit/Color"));
                    r.material.color = new Color(reticleColor.r, reticleColor.g, reticleColor.b, 0.6f);
                }
                aimDots.Add(dotGo3D);
            }

            _reticles.Add(new PlayerReticleInstance {
                ClientId = clientId,
                Nickname = nickname,
                Order = order,
                Color = reticleColor,
                ReticleGo = reticleGo,
                Rect = rt,
                InnerDot = dotRenderer,
                Position = startCanvasPos,
                WorldPosition = Vector3.zero,
                GroundTarget = Vector3.zero,
                InputSource = input,
                AimDots = aimDots,
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
                    // Fallback local key controls
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

                // Move 2D UI Reticle on Canvas (상하좌우 화면 기준)
                float speed = isSpeedBoost ? reticleSpeed * 2f : reticleSpeed;
                r.Position.x += moveDir.x * speed * Time.deltaTime;
                r.Position.y += moveDir.y * speed * Time.deltaTime;

                // Robust clamping matching the actual stretched playField width/height automatically!
                float halfWidth = playField.rect.width * 0.5f;
                float halfHeight = playField.rect.height * 0.5f;
                float limitX = halfWidth - pointerRadius;
                float limitY = halfHeight - pointerRadius;
                r.Position.x = Mathf.Clamp(r.Position.x, -limitX, limitX);
                r.Position.y = Mathf.Clamp(r.Position.y, -limitY, limitY);

                r.Rect.anchoredPosition = r.Position;

                // ----------------------------------------------------
                // Calculate corresponding 3D world position and ground target
                // ----------------------------------------------------
                if (_mainCam != null)
                {
                    Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, r.Rect.position);
                    
                    // 1. Apex position in the air (Z depth 12 units)
                    r.WorldPosition = _mainCam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 12f));
                    
                    // 2. Intersection with ground plane Y = 0 to allow deep straight-line aiming below starting barrel
                    Ray ray = _mainCam.ScreenPointToRay(screenPos);
                    Plane groundPlane = new Plane(Vector3.up, Vector3.zero); // Y = 0
                    if (groundPlane.Raycast(ray, out float enter))
                    {
                        r.GroundTarget = ray.GetPoint(enter);
                    }
                    else
                    {
                        r.GroundTarget = r.WorldPosition;
                        r.GroundTarget.y = 0f;
                    }
                }

                // Handle A Button Bullet charging & firing
                bool btnA = (r.InputSource != null) ? r.InputSource.JoystickA : (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed);
                if (btnA)
                {
                    // Charge up to 1.0 second max
                    r.ChargeAmount = Mathf.Clamp01(r.ChargeAmount + Time.deltaTime / 1.0f);

                    // Grow inner dot size (base 25px, goes up to 80px)
                    float innerScale = 25f + r.ChargeAmount * 55f;
                    r.InnerDot.rectTransform.sizeDelta = new Vector2(innerScale, innerScale);

                    // Smoothly blend color from white to the player"s specific color without flashing/blinking
                    r.InnerDot.color = Color.Lerp(Color.white, r.Color, r.ChargeAmount);
                }
                else
                {
                    // Released A button - fire!
                    if (r.ChargeAmount > 0f)
                    {
                        FireParabolicBullet(r, r.ChargeAmount);
                        r.ChargeAmount = 0f;

                        // Reset inner dot
                        r.InnerDot.rectTransform.sizeDelta = new Vector2(25f, 25f);
                        r.InnerDot.color = Color.white;
                    }
                }
                r.PrevButtonA = btnA;
            }
        }

        private void FireParabolicBullet(PlayerReticleInstance player, float charge)
        {
            var bulletGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulletGo.name = "Bullet_" + player.Nickname;
            bulletGo.transform.position = _turretGo.transform.position + new Vector3(0f, 0.6f, 0.5f);
            
            // Bullet size scales with charge (base 0.4 up to 1.4)
            float bScale = 0.4f + charge * 1.0f;
            bulletGo.transform.localScale = new Vector3(bScale, bScale, bScale);

            // Configure Trigger Collider for robot impact detection
            var col = bulletGo.GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }

            // Add kinematic Rigidbody so OnTriggerEnter reliably fires in Unity
            var rb = bulletGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // Add trigger handler
            var handler = bulletGo.AddComponent<BulletTriggerHandler>();
            handler.shooter = player.InputSource;
            handler.playerColor = player.Color;
            handler.damage = (charge >= 0.9f) ? 50f : 20f; // 50 damage if fully charged (charge >= 0.9s), otherwise 20 damage!

            var r = bulletGo.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(Shader.Find("Unlit/Color"));
                r.material.color = Color.Lerp(bulletColor, Color.white, charge * 0.5f);
            }

            StartCoroutine(AnimateParabolicBullet(bulletGo, bulletGo.transform.position, player.WorldPosition, player, charge));
        }

        private IEnumerator AnimateParabolicBullet(GameObject bullet, Vector3 start, Vector3 target, PlayerReticleInstance player, float charge)
        {
            float elapsed = 0f;
            while (elapsed < bulletFlightDuration)
            {
                if (bullet == null) yield break;

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / bulletFlightDuration);

                // Sample symmetric parabolic trajectory where target represents the apex (t=0.5)
                bullet.transform.position = EvaluateSymmetricParabola(start, target, t);

                yield return null;
            }

            // Explode / Land impact
            if (bullet != null)
            {
                Vector3 finalPos = bullet.transform.position;
                Destroy(bullet);

                SpawnBulletImpactParticles(finalPos, player.Color, charge);
            }
        }

        /// <summary>
        /// Hybrid parabolic and linear trajectory math.
        /// Below the turret barrel's horizon line (target.y <= start.y), the bullet travels in a pure straight line to the ground target.
        /// At the horizon line (target.y == start.y), it is a straight line.
        /// Above the horizon line, it smoothly blends into a beautiful symmetrical parabolic arc where the target represents the exact apex.
        /// </summary>
        private Vector3 EvaluateSymmetricParabola(Vector3 start, Vector3 target, float t)
        {
            float startY = start.y;

            // Calculate the ground raycast intersection point for this aiming direction
            Vector3 groundPoint = target;
            if (_mainCam != null)
            {
                Ray ray = new Ray(_mainCam.transform.position, (target - _mainCam.transform.position).normalized);
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero); // Y = 0 plane
                if (groundPlane.Raycast(ray, out float enter))
                {
                    groundPoint = ray.GetPoint(enter);
                }
            }

            // Below or at the barrel's horizontal line: Shoot in a pure straight line to the ground
            if (target.y <= startY)
            {
                return Vector3.Lerp(start, groundPoint, t);
            }
            else
            {
                // Symmetrical end point mirroring the start relative to the peak horizontally
                Vector3 end = start + 2f * (target - start);
                end.y = 0f; // Forces landing on the ground plane (Y = 0)

                float x = Mathf.Lerp(start.x, end.x, t);
                float z = Mathf.Lerp(start.z, end.z, t);

                // Vertical calculations
                float baseHeight = Mathf.Lerp(startY, 0f, t);
                float offsetMultiplier = target.y - Mathf.Lerp(startY, 0f, 0.5f);
                float yParabola = baseHeight + offsetMultiplier * 4f * t * (1f - t);

                // Pure straight-line height to 'end'
                float yStraight = baseHeight;

                // Smoothly blend from straight line to parabola over a 0.5 unit vertical transition range
                float blend = Mathf.Clamp01((target.y - startY) / 0.5f);
                float y = Mathf.Lerp(yStraight, yParabola, blend);

                return new Vector3(x, y, z);
            }
        }

        // -------------------------------------------------------------- Sandbox Loop

        private IEnumerator GameplayLoop()
        {
            SetMobileTemplates(MobileTemplate.JoystickAB);

            _statusText.text = "Hold A to Charge, Release to Shoot!";
            _statusText.gameObject.SetActive(true);
            yield return new WaitForSeconds(2.5f);
            _statusText.gameObject.SetActive(false);

            while (true)
            {
                yield return null;
            }
        }

        private void UpdateTurretAimRotation()
        {
            if (_reticles.Count == 0) return;

            // Rotate turret barrel to face the first player"s world target position
            Vector3 target = _reticles[0].WorldPosition;
            Vector3 direction = (target - _turretGo.transform.position).normalized;

            if (direction != Vector3.zero)
            {
                _turretGo.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }

        private void UpdateAimDottedLines()
        {
            foreach (var r in _reticles)
            {
                Vector3 start = _turretGo.transform.position + new Vector3(0f, 0.6f, 0.5f);
                Vector3 target = r.WorldPosition; // Apex target coordinate

                // Distribute dotted sphere visual markers along the parabola path
                for (int i = 0; i < r.AimDots.Count; i++)
                {
                    float t = (float)(i + 1) / (float)(r.AimDots.Count + 1);
                    r.AimDots[i].transform.position = EvaluateSymmetricParabola(start, target, t);
                }
            }
        }

        // -------------------------------------------------------------- FX

        private void SpawnBulletImpactParticles(Vector3 pos, Color color, float charge)
        {
            // Calculate distance from turret to the impact point
            float dist = 0f;
            if (_turretGo != null)
            {
                dist = Vector3.Distance(_turretGo.transform.position, pos);
            }
            else
            {
                var existingTurret = GameObject.Find("CentralTurret");
                if (existingTurret != null)
                {
                    dist = Vector3.Distance(existingTurret.transform.position, pos);
                }
            }

            // Map distance to scale factor: 25% at 0 distance, 100% at 20+ distance
            float maxExpectedDistance = 20f;
            float tDist = Mathf.Clamp01(dist / maxExpectedDistance);
            float effectScale = Mathf.Lerp(0.25f, 1.0f, tDist);

            // Scale particle count
            int pCount = Mathf.RoundToInt((8 + charge * 16) * effectScale);
            pCount = Mathf.Max(2, pCount); // Ensure at least a couple of shards spawn

            for (int i = 0; i < pCount; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                p.name = "ImpactShard";
                p.transform.position = pos;

                // Scale individual particle size
                float shardSize = 0.2f * effectScale;
                p.transform.localScale = new Vector3(shardSize, shardSize, shardSize);
                Destroy(p.GetComponent<Collider>());

                var r = p.GetComponent<Renderer>();
                if (r != null)
                {
                    r.material = new Material(Shader.Find("Unlit/Color"));
                    r.material.color = color;
                }

                var physics = p.AddComponent<ShardPhysics>();
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float spd = Random.Range(2f, 6f) * (1f + charge * 1.5f) * effectScale; // Scale speed too!
                physics.Velocity = new Vector3(Mathf.Cos(angle) * spd, Random.Range(2f, 6f) * (1f + charge) * effectScale, Mathf.Sin(angle) * spd);
            }
        }

        private class ShardPhysics : MonoBehaviour
        {
            public Vector3 Velocity;
            private float _age = 0f;
            private float _lifetime = 0.6f;
            private Renderer _renderer;

            private void Awake()
            {
                _renderer = GetComponent<Renderer>();
            }

            private void Update()
            {
                _age += Time.deltaTime;
                if (_age >= _lifetime)
                {
                    Destroy(gameObject);
                    return;
                }

                // Add simple gravity
                Velocity.y -= 9.8f * Time.deltaTime;
                transform.Translate(Velocity * Time.deltaTime, Space.World);

                if (_renderer != null)
                {
                    // Fade color
                    float t = 1f - (_age / _lifetime);
                    _renderer.material.color = new Color(_renderer.material.color.r, _renderer.material.color.g, _renderer.material.color.b, t);
                }
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
                yield return new WaitForSeconds(1.5f);
                _statusText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Award score (same 3 points for normal and full-charge) and play grand death explosion!
        /// </summary>
        public void OnRobotKilled(Vector3 hitPos, PlayerInputData shooter, Color playerColor)
        {
            // Play a highly spectacular impact/death explosion!
            SpawnBulletImpactParticles(hitPos, playerColor, 1.0f);

            // Award points (both normal/full-charge give the SAME points, let's say +3 pts)
            int pts = 3;
            if (shooter != null)
            {
                shooter.Score += pts;
                if (scoreboard != null) scoreboard.Refresh();
            }

            // Display UI status notification
            if (_statusText != null)
            {
                string nickname = shooter != null ? shooter.Nickname.ToUpper() : "A PLAYER";
                _statusText.text = nickname + " ELIMINATED THE ROBOT! +3 PTS";
                StopCoroutine("ShowHitStatus");
                StartCoroutine(ShowHitStatus());
            }
        }

        // -------------------------------------------------------------- UI Setup

        private void BuildUI()
        {
            _turnText = CreateText("TurnText", playField, new Vector2(0, 320), new Vector2(1000, 80), 48, FontStyle.Bold);
            _turnText.text = "Sunset Shooting Gallery";

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

    /// <summary>
    /// Handles trigger-based impact detection with 'robot' tagged colliders, routing to RobotHealth.
    /// </summary>
    public class BulletTriggerHandler : MonoBehaviour
    {
        public PlayerInputData shooter;
        public Color playerColor;
        public float damage = 20f;

        private void OnTriggerEnter(Collider other)
        {
            // First check if the other object has a RobotHealth component
            var health = other.GetComponent<RobotHealth>();
            if (health != null)
            {
                health.TakeDamage(damage, shooter, playerColor);
                Destroy(gameObject); // Consume bullet
            }
            else if (other.CompareTag("robot"))
            {
                // Fallback direct kill if health component is somehow missing but tagged 'robot'
                var game = Object.FindFirstObjectByType<EscapeGame>();
                if (game != null)
                {
                    game.OnRobotKilled(transform.position, shooter, playerColor);
                }
                Destroy(other.gameObject);
                Destroy(gameObject);
            }
        }
    }
}
