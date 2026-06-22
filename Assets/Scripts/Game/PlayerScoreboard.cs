using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PartyGame
{
    /// <summary>
    /// Builds and maintains a horizontal row of player cards (nickname + score) along the top
    /// of the screen. Reads from <see cref="PlayerConnectionManager"/> and refreshes each frame.
    /// Cards are created at runtime, so the scene only needs an empty RectTransform host.
    /// </summary>
    public class PlayerScoreboard : MonoBehaviour
    {
        [Tooltip("Parent RectTransform that holds the player cards. Defaults to this object.")]
        public RectTransform container;

        public Color cardColor = new Color(0.16f, 0.18f, 0.25f, 1f);
        public Color textColor = Color.white;
        public int fontSize = 28;

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private Font _font;
        private PlayerConnectionManager _connections;

        private class Entry
        {
            public GameObject root;
            public Text nameText;
            public Text scoreText;
        }

        private void Awake()
        {
            if (container == null) container = transform as RectTransform;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            EnsureLayout();
        }

        private void EnsureLayout()
        {
            var hlg = container.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) hlg = container.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.padding = new RectOffset(12, 12, 8, 8);
        }

        private void Update()
        {
            if (_connections == null)
                _connections = PlayerConnectionManager.Instance;
            Refresh();
        }

        public void Refresh()
        {
            if (_connections == null) return;
            var players = _connections.Players;

            // Track which clients are present this frame.
            var seen = new HashSet<int>();
            foreach (var p in players)
            {
                seen.Add(p.ClientId);
                if (!_entries.TryGetValue(p.ClientId, out var e))
                {
                    e = CreateCard();
                    _entries[p.ClientId] = e;
                }
                e.nameText.text = p.Nickname;
                e.scoreText.text = p.Score.ToString();
            }

            // Remove cards for players that left.
            if (_entries.Count > seen.Count)
            {
                var toRemove = new List<int>();
                foreach (var kv in _entries)
                    if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);
                foreach (var id in toRemove)
                {
                    if (_entries[id].root != null) Destroy(_entries[id].root);
                    _entries.Remove(id);
                }
            }
        }

        private Entry CreateCard()
        {
            var go = new GameObject("PlayerCard", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(container, false);

            var img = go.GetComponent<Image>();
            img.color = cardColor;

            var le = go.GetComponent<LayoutElement>();
            le.minWidth = 150;
            le.minHeight = 80;

            var vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.padding = new RectOffset(14, 14, 8, 8);
            vlg.spacing = 2;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var nameText = MakeText("Name", go.transform, fontSize, FontStyle.Bold);
            var scoreText = MakeText("0", go.transform, fontSize + 8, FontStyle.Bold);

            return new Entry { root = go, nameText = nameText, scoreText = scoreText };
        }

        private Text MakeText(string content, Transform parent, int size, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = textColor;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
