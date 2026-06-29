#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace PartyGame.GameViewSettings
{
    [InitializeOnLoad]
    public static class GameViewScaleKeeper
    {
        static GameViewScaleKeeper()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                SetGameViewScale(0.72f);
            }
        }

        public static void SetGameViewScale(float scaleValue)
        {
            try
            {
                var gameViewType = System.Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType == null) return;

                var gameViewWindow = EditorWindow.GetWindow(gameViewType, false, null, false);
                if (gameViewWindow == null) return;

                var scaleField = gameViewType.GetField("m_Scale", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (scaleField != null)
                {
                    if (scaleField.FieldType == typeof(float))
                    {
                        scaleField.SetValue(gameViewWindow, scaleValue);
                        gameViewWindow.Repaint();
                    }
                    else if (scaleField.FieldType == typeof(Vector2))
                    {
                        scaleField.SetValue(gameViewWindow, new Vector2(scaleValue, scaleValue));
                        gameViewWindow.Repaint();
                    }
                }
                else
                {
                    var scaleProperty = gameViewType.GetProperty("scale", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (scaleProperty != null)
                    {
                        scaleProperty.SetValue(gameViewWindow, scaleValue, null);
                        gameViewWindow.Repaint();
                    }
                }
            }
            catch (System.Exception)
            {
                // Suppress any reflection exceptions to keep editor completely stable
            }
        }
    }
}
#endif