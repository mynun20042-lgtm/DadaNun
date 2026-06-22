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

        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            SceneManager.LoadScene(sceneName);
        }

        public void LoadGameSelect() => LoadScene(GameSelectScene);
        public void LoadCoinGame() => LoadScene(CoinGameScene);
        public void LoadMain() => LoadScene(MainScene);
    }
}
