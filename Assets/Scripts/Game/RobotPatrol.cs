using System.Collections;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Script attached to robotSphere to make it slowly move around the map 
    /// and play walk/roll/open/close animations automatically.
    /// </summary>
    public class RobotPatrol : MonoBehaviour
    {
        [Header("Movement Settings")]
        public float moveSpeed = 1.8f;
        public float rotationSpeed = 6f;
        public float minIdleTime = 1.5f;
        public float maxIdleTime = 4f;

        [Header("Patrol Area Bounds")]
        public float minX = -9f;
        public float maxX = 9f;
        public float minZ = -5f;
        public float maxZ = 7f;

        private Animator anim;
        private Vector3 targetPos;

        void Start()
        {
            anim = GetComponent<Animator>();
            if (anim != null)
            {
                anim.applyRootMotion = false; // Disable root motion so custom scripting movement works flawlessly!
            }
            PickNewTarget();
            StartCoroutine(PatrolRoutine());
        }

        void PickNewTarget()
        {
            float randX = Random.Range(minX, maxX);
            float randZ = Random.Range(minZ, maxZ);
            targetPos = new Vector3(randX, transform.position.y, randZ);
        }

        IEnumerator PatrolRoutine()
        {
            while (true)
            {
                // 1. Walk to target
                if (anim != null)
                {
                    anim.SetBool("Walk_Anim", true);
                }

                while (Vector3.Distance(new Vector3(transform.position.x, 0f, transform.position.z), new Vector3(targetPos.x, 0f, targetPos.z)) > 0.4f)
                {
                    // Rotate towards target smoothly
                    Vector3 dir = (targetPos - transform.position).normalized;
                    dir.y = 0f; // Keep rotation strictly horizontal
                    if (dir != Vector3.zero)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
                    }

                    // Move towards target
                    transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
                    yield return null;
                }

                // 2. Arrived at target - transition to idle
                if (anim != null)
                {
                    anim.SetBool("Walk_Anim", false);
                }

                // Randomly trigger other animations (Roll or Open/Close) during idle
                float idleChoice = Random.value;
                if (anim != null)
                {
                    if (idleChoice < 0.25f)
                    {
                        // Roll animation
                        anim.SetBool("Roll_Anim", true);
                        yield return new WaitForSeconds(1.5f);
                        anim.SetBool("Roll_Anim", false);
                    }
                    else if (idleChoice < 0.5f)
                    {
                        // Toggle Open animation
                        bool isOpen = anim.GetBool("Open_Anim");
                        anim.SetBool("Open_Anim", !isOpen);
                    }
                }

                // Stand idle for a random duration
                float idleTime = Random.Range(minIdleTime, maxIdleTime);
                yield return new WaitForSeconds(idleTime);

                // 3. Pick next destination
                PickNewTarget();
            }
        }
    }
}