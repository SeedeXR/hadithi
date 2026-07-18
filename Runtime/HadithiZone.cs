using UnityEngine;
using UnityEngine.Events;

namespace SeedeXR.Hadithi
{
    /// <summary>
    /// A volume a Beat can wait on (end condition: Player Enters Zone). Uses a real
    /// (isTrigger) Collider for authoring and visualisation, but detects entry by polling
    /// Collider.ClosestPoint against the tracked target each frame: a precise, physics
    /// free containment test that needs no Rigidbody on the target and cannot miss a
    /// fast entry.
    ///
    /// HasEntered latches true on first entry (the story gate reads it). IsInside
    /// reflects live presence; OnEntered / OnExited fire on transitions for any other
    /// listeners.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    [AddComponentMenu("SeedeXR/Hadithi Zone")]
    public class HadithiZone : MonoBehaviour
    {
        [Tooltip("The transform whose position is tested, usually the player's head. Leave empty to auto resolve (Camera.main, then Target Name).")]
        public Transform Target;

        [Tooltip("Name used to auto resolve the target if Target is empty. For a Meta rig use CenterEyeAnchor.")]
        public string TargetName = "CenterEyeAnchor";

        public UnityEvent OnEntered;
        public UnityEvent OnExited;

        /// <summary>True once the target has entered at least once (latched).</summary>
        public bool HasEntered { get; private set; }

        /// <summary>True while the target is currently inside the volume.</summary>
        public bool IsInside { get; private set; }

        Collider _col;

        void Awake() => _col = GetComponent<Collider>();

        /// <summary>Clears the latch, so a looping story can gate on a fresh entry.</summary>
        public void ResetEntry()
        {
            HasEntered = false;
            IsInside = false;
        }

        void Update()
        {
            Transform target = ResolveTarget();
            if (target == null || _col == null) return;

            // ClosestPoint returns the query point itself when it lies inside (or on) a convex collider.
            bool inside = _col.ClosestPoint(target.position) == target.position;

            if (inside && !IsInside)
            {
                IsInside = true;
                HasEntered = true;
                OnEntered?.Invoke();
            }
            else if (!inside && IsInside)
            {
                IsInside = false;
                OnExited?.Invoke();
            }
        }

        Transform ResolveTarget()
        {
            if (Target != null) return Target;
            if (Camera.main != null) { Target = Camera.main.transform; return Target; }
            if (!string.IsNullOrEmpty(TargetName))
            {
                var go = GameObject.Find(TargetName);
                if (go != null) { Target = go.transform; return Target; }
            }
            return null;
        }
    }
}
