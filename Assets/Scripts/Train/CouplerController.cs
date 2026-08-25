using UnityEngine;

public class CouplerController : MonoBehaviour
{
    public Transform wagonAvant;
    public Transform wagonArriere;

    public float distanceMax = 16f;
    public float distanceRepos = 15f;

    public float correction = 5f;


    void FixedUpdate()
    {
        if(wagonAvant == null || wagonArriere == null)
            return;


        float distance = Vector3.Distance(
            wagonAvant.position,
            wagonArriere.position
        );


        // Trop éloigné : la corde tire
        if(distance > distanceMax)
        {
            Vector3 direction =
                (wagonAvant.position -
                 wagonArriere.position)
                 .normalized;


            wagonArriere.position +=
                direction *
                correction *
                Time.fixedDeltaTime;
        }


        // Trop rapproché : on empêche le chevauchement
        if(distance < distanceRepos)
        {
            Vector3 direction =
                (wagonArriere.position -
                 wagonAvant.position)
                 .normalized;


            wagonArriere.position +=
                direction *
                correction *
                Time.fixedDeltaTime;
        }
    }
}