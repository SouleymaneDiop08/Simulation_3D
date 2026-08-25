using UnityEngine;
using UnityEngine.Splines;

public class WagonFollower : MonoBehaviour
{
    public SplineContainer track;

    public float distanceOffset = 20f;
    public float speed = 10f;

    private float distance = 0f;

    void Update()
    {
        if (track == null)
            return;

        distance += speed * Time.deltaTime;

        float wagonDistance = distance - distanceOffset;

        if (wagonDistance < 0)
            wagonDistance = 0;

        float length = track.CalculateLength();

        float t = wagonDistance / length;

        Vector3 position = track.transform.TransformPoint(
            track.EvaluatePosition(t)
        );

        Vector3 tangent = track.transform.TransformDirection(
            track.EvaluateTangent(t)
        );

        transform.position = position;

        if (tangent != Vector3.zero)
        {
            transform.rotation =
                Quaternion.LookRotation(tangent, Vector3.up);
        }
    }
}