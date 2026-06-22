using System;
using System.Collections.Generic;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Owns the set of connected players. Subscribes to <see cref="MobileServer"/> events,
    /// creates a <see cref="PlayerInputData"/> object per joined player, keeps them ordered
    /// by join time, and routes input to the right player. Raises <see cref="PlayersChanged"/>
    /// whenever the roster changes so UI can refresh.
    /// </summary>
    public class PlayerConnectionManager : MonoBehaviour
    {
        [Tooltip("The server to listen to. If left empty, one is searched for in the scene.")]
        public MobileServer server;

        [Tooltip("Optional parent for the spawned per-player objects. Defaults to this transform.")]
        public Transform playerContainer;

        [Tooltip("Template assigned to a player the moment they join.")]
        public MobileTemplate defaultTemplate = MobileTemplate.None;

        /// <summary>Raised on the main thread whenever the player roster changes.</summary>
        public event Action PlayersChanged;

        private readonly Dictionary<int, PlayerInputData> _byClient = new Dictionary<int, PlayerInputData>();
        private readonly List<PlayerInputData> _ordered = new List<PlayerInputData>();
        private int _joinCounter;

        /// <summary>All connected (joined) players, in connection order.</summary>
        public IReadOnlyList<PlayerInputData> Players => _ordered;

        public int PlayerCount => _ordered.Count;

        public static PlayerConnectionManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (server == null) server = MobileServer.Instance != null ? MobileServer.Instance : FindAnyObjectByType<MobileServer>();
            if (playerContainer == null) playerContainer = transform;
        }

        private void OnEnable()
        {
            if (server == null) return;
            server.ClientConnected += OnClientConnected;
            server.ClientDisconnected += OnClientDisconnected;
            server.MessageReceived += OnMessageReceived;
        }

        private void OnDisable()
        {
            if (server == null) return;
            server.ClientConnected -= OnClientConnected;
            server.ClientDisconnected -= OnClientDisconnected;
            server.MessageReceived -= OnMessageReceived;
        }

        public PlayerInputData GetPlayer(int clientId)
            => _byClient.TryGetValue(clientId, out var p) ? p : null;

        // -------------------------------------------------------------- server events

        private void OnClientConnected(int clientId)
        {
            // Socket connected, but not joined yet. Tell the phone its id.
            server.Send(clientId, NetMessage.Welcome(clientId));
        }

        private void OnClientDisconnected(int clientId)
        {
            if (!_byClient.TryGetValue(clientId, out var player)) return;

            _byClient.Remove(clientId);
            _ordered.Remove(player);
            if (player != null) Destroy(player.gameObject);

            PlayersChanged?.Invoke();
        }

        private void OnMessageReceived(int clientId, string json)
        {
            NetMessage msg;
            try { msg = NetMessage.FromJson(json); }
            catch { return; }

            switch (msg.type)
            {
                case "join":
                    HandleJoin(clientId, msg);
                    break;
                case "input":
                    if (_byClient.TryGetValue(clientId, out var p))
                        p.ApplyInput(msg);
                    break;
            }
        }

        private void HandleJoin(int clientId, NetMessage msg)
        {
            string nick = string.IsNullOrWhiteSpace(msg.nick) ? $"Player{clientId}" : msg.nick.Trim();
            if (nick.Length > 16) nick = nick.Substring(0, 16);

            PlayerInputData player;
            if (!_byClient.TryGetValue(clientId, out player))
            {
                var go = new GameObject($"Player_{nick}");
                go.transform.SetParent(playerContainer, false);
                player = go.AddComponent<PlayerInputData>();
                player.ClientId = clientId;
                player.JoinOrder = ++_joinCounter;
                player.CurrentTemplate = defaultTemplate;

                _byClient[clientId] = player;
                _ordered.Add(player);
            }

            player.Nickname = nick;
            player.gameObject.name = $"Player_{nick}";

            server.Send(clientId, NetMessage.JoinResult(true));
            server.Send(clientId, NetMessage.Template(player.CurrentTemplate));

            PlayersChanged?.Invoke();
        }

        // -------------------------------------------------------------- template control

        /// <summary>Switch one player's controller layout and notify their phone.</summary>
        public void SetTemplate(int clientId, MobileTemplate template)
        {
            if (!_byClient.TryGetValue(clientId, out var player)) return;
            player.CurrentTemplate = template;
            player.ResetInputs();
            server.Send(clientId, NetMessage.Template(template));
        }

        /// <summary>Switch every connected player's controller layout.</summary>
        public void SetTemplateForAll(MobileTemplate template)
        {
            foreach (var player in _ordered)
            {
                player.CurrentTemplate = template;
                player.ResetInputs();
            }
            server.Broadcast(NetMessage.Template(template));
        }
    }
}
