using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Central coordinator. Holds references to the server and the connection manager and
    /// exposes the high-level functions the rest of the game uses — in particular the
    /// ability to change which controller template the phones display.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public MobileServer server;
        public PlayerConnectionManager connections;

        [Tooltip("The controller template currently broadcast to all players. " +
                 "Changing this in the Inspector while playing will update all phones automatically.")]
        public MobileTemplate currentTemplate = MobileTemplate.None;

        // Tracks the last value actually pushed to phones, so we can detect Inspector edits at runtime.
        private MobileTemplate _appliedTemplate = MobileTemplate.None;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (server == null) server = FindAnyObjectByType<MobileServer>();
            if (connections == null) connections = FindAnyObjectByType<PlayerConnectionManager>();
        }

        private void Start()
        {
            // Keep new players in sync with the current global template.
            if (connections != null)
                connections.defaultTemplate = currentTemplate;
            _appliedTemplate = currentTemplate;
        }

        private void Update()
        {
            // If the value was changed directly in the Inspector (or by any other code path
            // that bypassed SetMobileTemplate), detect it here and broadcast to all phones.
            if (currentTemplate != _appliedTemplate)
                SetMobileTemplate(currentTemplate);
        }

        /// <summary>Change the controller layout shown on every connected phone.</summary>
        public void SetMobileTemplate(MobileTemplate template)
        {
            currentTemplate = template;
            _appliedTemplate = template;
            if (connections != null)
            {
                connections.defaultTemplate = template;
                connections.SetTemplateForAll(template);
            }
        }

        /// <summary>Change the controller layout for a single player only.</summary>
        public void SetMobileTemplate(int clientId, MobileTemplate template)
        {
            if (connections != null)
                connections.SetTemplate(clientId, template);
        }

        // Convenience wrappers (handy for UI Button OnClick hooks, which can't pass enums).
        public void ShowJoystickAB() => SetMobileTemplate(MobileTemplate.JoystickAB);
        public void ShowDPad() => SetMobileTemplate(MobileTemplate.DPadFour);
        public void ShowSingleButton() => SetMobileTemplate(MobileTemplate.SingleButton);
        public void ShowNone() => SetMobileTemplate(MobileTemplate.None);
    }
}
