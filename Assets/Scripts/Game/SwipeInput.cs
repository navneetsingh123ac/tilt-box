using System;
using TiltBox.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TiltBox.Game
{
    /// <summary>
    /// Turns touch drags, mouse drags, arrow keys and WASD into swipe directions (GDD §14).
    /// A drag fires as soon as it passes the threshold (no need to lift the finger); one swipe per drag.
    /// Uses the legacy Input Manager (project setting activeInputHandler = Input Manager).
    /// </summary>
    public sealed class SwipeInput : MonoBehaviour
    {
        [Tooltip("Minimum drag distance in screen pixels (GDD §14: 40 px).")]
        [SerializeField] private float thresholdPixels = 40f;

        public event Action<Direction> Swiped;

        private bool _tracking;
        private Vector2 _start;

        private void Update()
        {
            ReadKeys();
            if (Input.touchCount > 0) ReadTouch(Input.GetTouch(0));
            else ReadMouse();
        }

        private void ReadKeys()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) Fire(Direction.Up);
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) Fire(Direction.Down);
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) Fire(Direction.Left);
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) Fire(Direction.Right);
        }

        private void ReadTouch(Touch t)
        {
            switch (t.phase)
            {
                case TouchPhase.Began:
                    Begin(t.position, IsOverUi(t.fingerId));
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    Track(t.position);
                    break;
                default:
                    _tracking = false;
                    break;
            }
        }

        private void ReadMouse()
        {
            if (Input.GetMouseButtonDown(0)) Begin(Input.mousePosition, IsOverUi(-1));
            else if (Input.GetMouseButton(0)) Track(Input.mousePosition);
            else _tracking = false;
        }

        private void Begin(Vector2 pos, bool overUi)
        {
            // Drags that start on a button belong to the button, not the board.
            _tracking = !overUi;
            _start = pos;
        }

        private void Track(Vector2 pos)
        {
            if (!_tracking) return;
            Vector2 d = pos - _start;
            if (d.magnitude < thresholdPixels) return;
            _tracking = false; // one swipe per drag
            if (Mathf.Abs(d.x) > Mathf.Abs(d.y)) Fire(d.x > 0 ? Direction.Right : Direction.Left);
            else Fire(d.y > 0 ? Direction.Up : Direction.Down); // screen y grows upwards
        }

        private static bool IsOverUi(int pointerId)
        {
            EventSystem es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject(pointerId);
        }

        private void Fire(Direction dir) => Swiped?.Invoke(dir);
    }
}
