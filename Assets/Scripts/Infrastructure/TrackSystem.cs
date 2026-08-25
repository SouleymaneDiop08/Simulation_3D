using UnityEngine;
using UnityEngine.Splines;


public class TrackSystem : MonoBehaviour
{
    public SplineContainer splineContainer;


    public float longueur = 4000f;



    public Vector3 GetPosition(float distance)
    {
        if (splineContainer == null)
        {
            Debug.LogError("SplineContainer manquant");
            return transform.position;
        }


        float t =
            Mathf.Clamp01(
                distance / longueur
            );


        Vector3 localPosition =
            splineContainer.Spline.EvaluatePosition(t);



        Vector3 worldPosition =
            splineContainer.transform.TransformPoint(
                localPosition
            );


        return worldPosition;
    }
}