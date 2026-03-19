using UnityEngine;

namespace LibSM64
{
    public abstract class SM64InputProvider : MonoBehaviour
    {
        public enum Button
        {
            Jump,
            Kick,
            Stomp
        };

        public abstract Vector3 GetCameraLookDirection();
        public abstract Vector2 GetJoystickAxes();
        public abstract bool GetButtonHeld(Button button);
    }

    // This will be your class that reads the game's inputs and converts them to Mario inputs.
    public class SM64InputGame : SM64InputProvider
    {
        // Add a public field here that points to the player's input object

        public override Vector3 GetCameraLookDirection()
        {
            return new Vector3(-Camera.main.transform.forward.z, 0, Camera.main.transform.forward.x);
        }

        public override Vector2 GetJoystickAxes()
        {
            // Check for held button or left analog stick axis in the player's input object.
            // For analog stick: return new Vector2(axis.z, -axis.x);
            // For button held: return -((buttonLeft) ? Vector2.left : (buttonRight) ? Vector2.right : Vector2.zero);
            float vertical = 0f;
            if (Input.GetKey(KeyCode.W))
                vertical = 1f;
            else if (Input.GetKey(KeyCode.S))
                vertical = -1f;

            float horizontal = 0f;
            if (Input.GetKey(KeyCode.A))
                horizontal = -1f;
            else if (Input.GetKey(KeyCode.D))
                horizontal = 1f;
            return new Vector2(vertical, horizontal).normalized;
        }

        public override bool GetButtonHeld(Button button)
        {
            // Check against the game's button presses
            bool result = false;
            switch (button)
            {
                case Button.Jump:
                    return Input.GetKey(KeyCode.Space);

                case Button.Kick:
                    return Input.GetKey(KeyCode.LeftControl);

                case Button.Stomp:
                    return Input.GetKey(KeyCode.LeftShift);
            }

            return result;
        }
    }
}