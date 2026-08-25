// using UnityEngine;
// using UnityEngine.Splines;

// public class TrainSplineFollower : MonoBehaviour
// {
//     public SplineContainer track;
//     public float speed = 10f;

//     private float distance = 0f;

//     void Update()
//     {
//         if (track == null)
//             return;

//         // Avance sur la spline
//         distance += speed * Time.deltaTime;

//         // Longueur totale de la spline
//         float length = track.CalculateLength();

//         if (length <= 0)
//             return;

//         // Position normalisée 0 -> 1
//         float t = distance / length;

//         // Arrêt à la fin de la spline
//         if (t > 1f)
//             t = 1f;

//         // Position et direction dans le monde
//         Vector3 localPosition = track.EvaluatePosition(t);
//         Vector3 worldPosition = track.transform.TransformPoint(localPosition);

//         Vector3 localTangent = track.EvaluateTangent(t);
//         Vector3 worldTangent = track.transform.TransformDirection(localTangent);

//         // Déplacement du train
//         transform.position = worldPosition;

//         // Orientation du train
//         if (worldTangent != Vector3.zero)
//         {
//             transform.rotation = Quaternion.LookRotation(worldTangent, Vector3.up);
//         }
//     }
// }