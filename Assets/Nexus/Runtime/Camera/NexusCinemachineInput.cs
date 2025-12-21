using UnityEngine;
using Cinemachine;

public class NexusCinemachineInput : MonoBehaviour
{
    void Awake()
    {
        CinemachineCore.GetInputAxis = GetAxis;
    }

    float GetAxis(string axisName)
    {
        if (TokenDraggable.IsAnyDragging && axisName == "Mouse ScrollWheel")
            return 0f;
        return Input.GetAxis(axisName);
    }
}
