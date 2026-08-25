// using UnityEngine;
// using UnityEngine.Splines;

// public class BogieFollower : MonoBehaviour
// {
//     public SplineContainer track;

//     public float distance = 0f;
//     public float speed = 10f;

//     void Update()
//     {
//         if (track == null)
//             return;


//         float length = track.CalculateLength();

//         if (length <= 0)
//             return;


//         float t = distance / length;


//         if (t > 1f)
//             t = 1f;


//         Vector3 localPos = track.EvaluatePosition(t);

//         Vector3 worldPos =
//             track.transform.TransformPoint(localPos);


//         Vector3 localDir = track.EvaluateTangent(t);

//         Vector3 worldDir =
//             track.transform.TransformDirection(localDir);


//         transform.position = worldPos;


//         if (worldDir != Vector3.zero)
//         {
//             transform.rotation =
//                 Quaternion.LookRotation(
//                     worldDir,
//                     Vector3.up
//                 );
//         }
//     }
// }