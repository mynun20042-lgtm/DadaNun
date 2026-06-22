namespace PartyGame
{
    /// <summary>
    /// The set of controller layouts a connected mobile device can display.
    /// The integer values are part of the network protocol (sent as NetMessage.template),
    /// so do NOT reorder existing entries.
    /// </summary>
    public enum MobileTemplate
    {
        /// <summary>No controller shown yet (lobby / waiting screen on the phone).</summary>
        None = 0,

        /// <summary>Analog joystick plus A and B buttons.</summary>
        JoystickAB = 1,

        /// <summary>Four directional buttons (up / down / left / right).</summary>
        DPadFour = 2,

        /// <summary>A single A button.</summary>
        SingleButton = 3,

        /// <summary>Four numbered choice buttons (1 / 2 / 3 / 4), used for quiz-style answers.</summary>
        FourChoice = 4,
    }
}
