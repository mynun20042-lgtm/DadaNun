using System;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// A single flat message used for all PC &lt;-&gt; mobile communication.
    /// It is intentionally flat so that <see cref="UnityEngine.JsonUtility"/> can
    /// (de)serialize it directly. Unused fields simply keep their default value.
    ///
    /// Message types (the <see cref="type"/> field):
    ///   Mobile -&gt; PC:
    ///     "join"  : player requests to join. Uses <see cref="nick"/>.
    ///     "input" : controller input. Uses the input fields below depending on the active template.
    ///   PC -&gt; Mobile:
    ///     "welcome"  : connection accepted. Uses <see cref="id"/>.
    ///     "joinResult": result of a join request. Uses <see cref="ok"/> and optionally <see cref="message"/>.
    ///     "template" : switch the controller layout. Uses <see cref="template"/> (a <see cref="MobileTemplate"/> value).
    /// </summary>
    [Serializable]
    public struct NetMessage
    {
        public string type;

        // --- join ---
        public string nick;

        // --- welcome / identity ---
        public int id;

        // --- template switch ---
        public int template; // cast to/from MobileTemplate

        // --- input: joystick (JoystickAB) ---
        public float jx; // -1..1
        public float jy; // -1..1

        // --- input: buttons ---
        public bool a;
        public bool b;

        // --- input: dpad (DPadFour) ---
        public bool up;
        public bool down;
        public bool left;
        public bool right;

        // --- joinResult ---
        public bool ok;

        // --- misc / status text ---
        public string message;

        public string ToJson() => JsonUtility.ToJson(this);

        public static NetMessage FromJson(string json) => JsonUtility.FromJson<NetMessage>(json);

        public static NetMessage Welcome(int id) => new NetMessage { type = "welcome", id = id };

        public static NetMessage JoinResult(bool ok, string message = null) =>
            new NetMessage { type = "joinResult", ok = ok, message = message };

        public static NetMessage Template(MobileTemplate t) =>
            new NetMessage { type = "template", template = (int)t };
    }
}
