using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PartyGame
{
    /// <summary>
    /// Drives the PC main/connection screen: shows the QR code and server address for phones
    /// to connect to, the number of connected players, and the connected nicknames in join order.
    /// </summary>
    public class ConnectionScreenUI : MonoBehaviour
    {
        [Header("References")]
        public MobileServer server;
        public PlayerConnectionManager connections;

        [Header("UI Widgets")]
        public RawImage qrImage;
        public Text addressText;
        public Text countText;
        public Text playerListText;

        [Header("QR")]
        [Tooltip("Pixels per QR module. Higher = sharper/larger texture.")]
        public int qrPixelsPerModule = 8;

        private Texture2D _qrTexture;

        private void Awake()
        {
            if (server == null) server = FindAnyObjectByType<MobileServer>();
            if (connections == null) connections = FindAnyObjectByType<PlayerConnectionManager>();
        }

        private void OnEnable()
        {
            if (connections != null)
                connections.PlayersChanged += RefreshPlayers;
        }

        private void OnDisable()
        {
            if (connections != null)
                connections.PlayersChanged -= RefreshPlayers;
        }

        private void Start()
        {
            // Guarantee the server is up before we read its address / build the QR.
            if (server != null && !server.IsRunning)
                server.StartServer();

            string url = server != null ? server.GetConnectUrl() : "http://127.0.0.1:8080/";

            if (addressText != null)
                addressText.text = url;

            BuildQr(url);
            RefreshPlayers();
        }

        private void BuildQr(string url)
        {
            if (qrImage == null) return;
            try
            {
                _qrTexture = QrCodeGenerator.GenerateTexture(url, Mathf.Max(1, qrPixelsPerModule));
                qrImage.texture = _qrTexture;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ConnectionScreenUI] QR generation failed: {e.Message}");
            }
        }

        private void RefreshPlayers()
        {
            int count = connections != null ? connections.PlayerCount : 0;

            if (countText != null)
                countText.text = $"Players: {count}";

            if (playerListText != null)
            {
                var sb = new StringBuilder();
                if (connections != null)
                {
                    var players = connections.Players;
                    for (int i = 0; i < players.Count; i++)
                        sb.Append(i + 1).Append(". ").Append(players[i].Nickname).Append('\n');
                }
                playerListText.text = sb.ToString();
            }
        }

        private void OnDestroy()
        {
            if (_qrTexture != null)
                Destroy(_qrTexture);
        }
    }
}
