using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Runtime object created for each connected player. It stores that player's identity
    /// and the live input values for every controller template. A custom inspector
    /// (PlayerInputDataEditor) shows only the fields for the player's current template.
    /// </summary>
    public class PlayerInputData : MonoBehaviour
    {
        [Header("Identity")]
        public int ClientId;
        public string Nickname;
        [Tooltip("1-based order in which this player joined.")]
        public int JoinOrder;

        [Tooltip("Accumulated game score for this player.")]
        public int Score;

        [Tooltip("Which controller layout this player's phone is currently showing.")]
        public MobileTemplate CurrentTemplate = MobileTemplate.None;

        [Header("JoystickAB template")]
        public Vector2 Joystick;
        public bool JoystickA;
        public bool JoystickB;

        [Header("DPadFour template")]
        public bool DpadUp;
        public bool DpadDown;
        public bool DpadLeft;
        public bool DpadRight;

        [Header("SingleButton template")]
        public bool SingleA;

        [Header("FourChoice template")]
        public bool Choice1;
        public bool Choice2;
        public bool Choice3;
        public bool Choice4;

        /// <summary>
        /// Returns which of the four choice buttons is currently held (1-4),
        /// or 0 if none. If multiple are held, the lowest index wins.
        /// </summary>
        public int PressedChoice
        {
            get
            {
                if (Choice1) return 1;
                if (Choice2) return 2;
                if (Choice3) return 3;
                if (Choice4) return 4;
                return 0;
            }
        }

        /// <summary>Apply an incoming input message according to the active template.</summary>
        public void ApplyInput(NetMessage m)
        {
            switch (CurrentTemplate)
            {
                case MobileTemplate.JoystickAB:
                    Joystick = new Vector2(m.jx, m.jy);
                    JoystickA = m.a;
                    JoystickB = m.b;
                    break;
                case MobileTemplate.DPadFour:
                    DpadUp = m.up;
                    DpadDown = m.down;
                    DpadLeft = m.left;
                    DpadRight = m.right;
                    break;
                case MobileTemplate.SingleButton:
                    SingleA = m.a;
                    break;
                case MobileTemplate.FourChoice:
                    Choice1 = m.c1;
                    Choice2 = m.c2;
                    Choice3 = m.c3;
                    Choice4 = m.c4;
                    break;
                case MobileTemplate.ThreeChoice:
                    Choice1 = m.c1;
                    Choice2 = m.c2;
                    Choice3 = m.c3;
                    Choice4 = false;
                    break;
            }
        }

        /// <summary>Clear all input values (used when the template changes).</summary>
        public void ResetInputs()
        {
            Joystick = Vector2.zero;
            JoystickA = JoystickB = false;
            DpadUp = DpadDown = DpadLeft = DpadRight = false;
            SingleA = false;
            Choice1 = Choice2 = Choice3 = Choice4 = false;
        }
    }
}
