using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Rotates the object (like an airplane or parent pivot) once every 5 seconds (72 degrees/sec) around the Y axis.
    /// </summary>
    public class RotatePlane : MonoBehaviour
    {
        [Header("Rotation Settings")]
        [Tooltip("Seconds required to complete one full 360-degree rotation")]
        public float rotationPeriod = 5.0f;

        [Tooltip("Local axis of rotation")]
        public Vector3 rotationAxis = Vector3.up;

        private void Update()
        {
            if (rotationPeriod <= 0f) return;

            // Calculate rotation speed in degrees per second (360 / period)
            float speed = 360f / rotationPeriod;
            // Rotate the object smoothly over time
            transform.Rotate(rotationAxis, -1*speed * Time.deltaTime, Space.Self);
        }
    }
}