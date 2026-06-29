using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Component attached to robotSphere and its child colliders.
    /// Handles independent health, 0.5s invincibility, red flashing, and part-specific destruction.
    /// </summary>
    public class RobotHealth : MonoBehaviour
    {
        [Header("Health Settings")]
        public float maxHealth = 100f;
        public float currentHealth = 100f;
        public float invincibilityDuration = 0.5f;

        private float _invincibilityTimer = 0f;
        private bool _isDestroyed = false;

        void Update()
        {
            if (_invincibilityTimer > 0f)
            {
                _invincibilityTimer -= Time.deltaTime;
            }
        }

        public bool IsInvincible()
        {
            return _invincibilityTimer > 0f;
        }

        private Dictionary<Renderer, Color> _cachedColors = new Dictionary<Renderer, Color>();
        private Coroutine _flashCoroutine;

        public void TakeDamage(float damage, PlayerInputData shooter, Color playerColor)
        {
            if (_isDestroyed) return;
            if (_invincibilityTimer > 0f) return;

            currentHealth -= damage;
            _invincibilityTimer = invincibilityDuration;

            Debug.Log(name + " took " + damage + " damage. Current HP: " + currentHealth);

            // Visual feedback: Flash red safely with concurrent/overlapping guard
            if (_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
            }
            _flashCoroutine = StartCoroutine(FlashRedRoutine());

            if (currentHealth <= 0f)
            {
                Die(shooter, playerColor);
            }
        }

        private IEnumerator FlashRedRoutine()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            
            // Cache only the true original sharedMaterial colors before any instantiation or overlap occurs
            foreach (var r in renderers)
            {
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color"))
                {
                    if (!_cachedColors.ContainsKey(r))
                    {
                        _cachedColors[r] = r.sharedMaterial.color;
                    }
                }
            }

            // Apply solid red flash
            foreach (var r in renderers)
            {
                if (r != null && _cachedColors.ContainsKey(r))
                {
                    r.material.color = Color.red;
                }
            }

            yield return new WaitForSeconds(0.15f);

            // Restore cleanly back to original cached colors
            foreach (var r in renderers)
            {
                if (r != null && _cachedColors.ContainsKey(r))
                {
                    r.material.color = _cachedColors[r];
                }
            }

            _flashCoroutine = null;
        }

        private void Die(PlayerInputData shooter, Color playerColor)
        {
            _isDestroyed = true;
            Debug.Log(name + " has been eliminated!");

            // Notify EscapeGame about the elimination to award scores
            var game = Object.FindFirstObjectByType<EscapeGame>();
            if (game != null)
            {
                game.OnRobotKilled(transform.position, shooter, playerColor);
            }

            // If the destroyed part is the main 'body' core or the root 'robotSphere', destroy the entire robot.
            // Otherwise, just destroy this specific child part (e.g. arm, lid) to visually detach/disarm it!
            bool isCorePart = (name.ToLower() == "body" || name.ToLower().Contains("robotsphere"));

            if (isCorePart)
            {
                // Find the root robotSphere object and destroy it entirely
                Transform root = transform;
                while (root.parent != null && (root.parent.name.Contains("robotSphere") || root.parent.name.Contains("Robot") || root.parent.name.Contains("Prototype")))
                {
                    root = root.parent;
                }
                Destroy(root.gameObject);
            }
            else
            {
                // Just destroy this specific part/limb!
                Destroy(gameObject);
            }
        }
    }
}