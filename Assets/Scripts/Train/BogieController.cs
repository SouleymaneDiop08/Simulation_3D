// using UnityEngine;

// public class BogieController : MonoBehaviour
// {
//     [Header("Références")]
//     public TrackSystem trackSystem;

//     public Transform bogieAvant;
//     public Transform bogieArriere;


//     [Header("Position sur la voie")]
//     public float distanceAvant = 0f;
//     public float entraxe = 10f;


//     void Update()
//     {
//         if (trackSystem == null)
//             return;


//         UpdateBogie(
//             bogieAvant,
//             distanceAvant + entraxe / 2f
//         );


//         UpdateBogie(
//             bogieArriere,
//             distanceAvant - entraxe / 2f
//         );
//     }


//     void UpdateBogie(Transform bogie, float distance)
//     {
//         if (bogie == null)
//             return;


//         Vector3 worldPosition =
//             trackSystem.GetPosition(distance);


//         Vector3 worldDirection =
//             trackSystem.GetDirection(distance);


//         // Conversion monde -> local du parent
//         if (bogie.parent != null)
//         {
//             bogie.localPosition =
//                 bogie.parent.InverseTransformPoint(worldPosition);


//             Quaternion worldRotation =
//                 Quaternion.LookRotation(
//                     worldDirection,
//                     Vector3.up
//                 );


//             bogie.localRotation =
//                 Quaternion.Inverse(
//                     bogie.parent.rotation
//                 ) * worldRotation;
//         }
//         else
//         {
//             bogie.position = worldPosition;

//             if (worldDirection.sqrMagnitude > 0.001f)
//             {
//                 bogie.rotation =
//                     Quaternion.LookRotation(
//                         worldDirection,
//                         Vector3.up
//                     );
//             }
//         }
//     }
// }