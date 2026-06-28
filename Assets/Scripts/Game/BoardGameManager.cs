using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Persistent Singleton that manages the master board game state.
    /// Tracks player board tile positions, active turns, and rewards for minigame winners.
    /// </summary>
    public class BoardGameManager : MonoBehaviour
    {
        private static BoardGameManager _instance;
        public static BoardGameManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<BoardGameManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("BoardGameManager");
                        _instance = go.AddComponent<BoardGameManager>();
                    }
                }
                return _instance;
            }
        }

        [Header("Board Settings")]
        public const int MaxTiles = 41; // 0 to 40

        // Tracks player positions: ClientId -> Tile Index
        private readonly Dictionary<int, int> _playerPositions = new Dictionary<int, int>();
        
        public bool IsGameActive { get; private set; } = false;
        public int ActivePlayerIndex { get; set; } = 0;
        
        // Minigame return state
        public bool IsReturningFromMinigame { get; private set; } = false;
        public int MinigameWinnerClientId { get; private set; } = -1;
        public string LastMinigamePlayed { get; private set; } = "";

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void StartBoardGame()
        {
            _playerPositions.Clear();
            IsGameActive = true;
            ActivePlayerIndex = 0;
            IsReturningFromMinigame = false;
            MinigameWinnerClientId = -1;
            LastMinigamePlayed = "";

            if (PlayerConnectionManager.Instance != null)
            {
                // Reset all scores
                foreach (var p in PlayerConnectionManager.Instance.Players)
                {
                    p.Score = 0;
                    _playerPositions[p.ClientId] = 0; // All start at tile 0
                }
            }
        }

        public void EndBoardGame()
        {
            IsGameActive = false;
            IsReturningFromMinigame = false;
            MinigameWinnerClientId = -1;
        }

        public int GetPlayerPosition(int clientId)
        {
            if (_playerPositions.TryGetValue(clientId, out int pos))
            {
                return pos;
            }
            _playerPositions[clientId] = 0;
            return 0;
        }

        public void SetPlayerPosition(int clientId, int pos)
        {
            _playerPositions[clientId] = Mathf.Clamp(pos, 0, MaxTiles - 1);
        }

        public void MovePlayer(int clientId, int steps)
        {
            int current = GetPlayerPosition(clientId);
            SetPlayerPosition(clientId, current + steps);
        }

        /// <summary>
        /// Triggered by minigame controllers to signal that a minigame completed
        /// and we should return to the board with the specified winner.
        /// </summary>
        public void ReportMinigameWinner(int winnerClientId, string minigameName)
        {
            if (!IsGameActive) return;

            IsReturningFromMinigame = true;
            MinigameWinnerClientId = winnerClientId;
            LastMinigamePlayed = minigameName;
        }

        public void ClearMinigameReturnState()
        {
            IsReturningFromMinigame = false;
            MinigameWinnerClientId = -1;
        }
    }
}
