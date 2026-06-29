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
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            // Only force it if we are in Play Mode and the game is running
            if (EditorApplication.isPlaying)
            {
                SetGameViewScale(0.7f);
            }
        }

        public static void SetGameViewScale(float scaleValue)
        {
            try
            {
                var gameViewType = System.Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType == null) return;

                // Find the active GameView window without creating a new window or focusing if it doesn't exist
                var gameViewWindow = EditorWindow.GetWindow(gameViewType, false, null, false);
                if (gameViewWindow == null) return;

                var scaleField = gameViewType.GetField("m_Scale", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (scaleField != null)
                {
                    if (scaleField.FieldType == typeof(float))
                    {
                        float currentScale = (float)scaleField.GetValue(gameViewWindow);
                        if (!Mathf.Approximately(currentScale, scaleValue))
                        {
                            scaleField.SetValue(gameViewWindow, scaleValue);
                            gameViewWindow.Repaint();
                        }
                    }
                    else if (scaleField.FieldType == typeof(Vector2))
                    {
                        Vector2 currentScale = (Vector2)scaleField.GetValue(gameViewWindow);
                        if (!Mathf.Approximately(currentScale.x, scaleValue))
                        {
                            scaleField.SetValue(gameViewWindow, new Vector2(scaleValue, scaleValue));
                            gameViewWindow.Repaint();
                        }
                    }
                }
                else
                {
                    var scaleProperty = gameViewType.GetProperty("scale", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (scaleProperty != null)
                    {
                        float currentScale = (float)scaleProperty.GetValue(gameViewWindow, null);
                        if (!Mathf.Approximately(currentScale, scaleValue))
                        {
                            scaleProperty.SetValue(gameViewWindow, scaleValue, null);
                            gameViewWindow.Repaint();
                        }
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