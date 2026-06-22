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
            }
        }

        /// <summary>Clear all input values (used when the template changes).</summary>
        public void ResetInputs()
        {
            Joystick = Vector2.zero;
            JoystickA = JoystickB = false;
            DpadUp = DpadDown = DpadLeft = DpadRight = false;
            SingleA = false;
        }
    }
}
