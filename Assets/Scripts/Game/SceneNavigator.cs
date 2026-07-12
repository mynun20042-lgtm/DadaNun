using UnityEngine;
using UnityEngine.SceneManagement;

namespace PartyGame
{
    /// <summary>
    /// Thin helper that loads scenes by name. Intended to be hooked to uGUI Button OnClick
    /// events. Scene names must be registered in Build Settings.
    /// </summary>
    public class SceneNavigator : MonoBehaviour
    {
        public const string MainScene = "Main";
        public const string GameSelectScene = "GameSelect";
        public const string CoinGameScene = "CoinGame";
        public const string CardGameScene = "CardGame";
        public const string RpsGameScene = "RpsGame";
        public const string BoardGameScene = "BoardGame";
        public const string EscapeGameScene = "EscapeGame";
        public const string Escape2Scene = "Escape2";
        public const string BaskinRobbins31Scene = "BaskinRobbins31";

        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            SceneManager.LoadScene(sceneName);
        }

        public void LoadGameSelect() => LoadScene(GameSelectScene);
        public void LoadCoinGame() => LoadScene(CoinGameScene);
        public void LoadCardGame() => LoadScene(CardGameScene);
        public void LoadRpsGame() => LoadScene(RpsGameScene);
        public void LoadBoardGame() => LoadScene(BoardGameScene);
        public void LoadEscapeGame() => LoadScene(EscapeGameScene);
        public void LoadEscape2() => LoadScene(Escape2Scene);
        public void LoadBaskinRobbins31() => LoadScene(BaskinRobbins31Scene);
        public void LoadMain() => LoadScene(MainScene);
    }
}
