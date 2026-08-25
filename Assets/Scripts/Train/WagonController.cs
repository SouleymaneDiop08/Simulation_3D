using UnityEngine;


public class WagonController : MonoBehaviour
{

    public TrackSystem trackSystem;


    public float longueurWagon = 10f;

public TrainController train;
[Header("Dégâts")]
[Header("Dégâts")]
public ParticleSystem suieChoc;
// ==========================
// DÉRAILLEMENT
// ==========================

[HideInInspector]
public Vector3 derailOffset = Vector3.zero;

[HideInInspector]
public Quaternion derailRotation = Quaternion.identity;

    public void SetTrack(TrackSystem track)
    {
        trackSystem = track;
    }





    public void Move(float distanceSurVoie)
    {

        if(trackSystem == null)
            return;



        distanceSurVoie =
            Mathf.Clamp(
                distanceSurVoie,
                0,
                trackSystem.longueur
            );



        Vector3 position =
            trackSystem.GetPosition(
                distanceSurVoie
            );



        Vector3 avant =
            trackSystem.GetPosition(
                Mathf.Min(
                    distanceSurVoie + longueurWagon,
                    trackSystem.longueur
                )
            );



        Vector3 arriere =
            trackSystem.GetPosition(
                Mathf.Max(
                    distanceSurVoie - longueurWagon,
                    0
                )
            );



        Vector3 direction =
            avant - arriere;



  



     Quaternion rotationNormale = transform.rotation;

if(direction.sqrMagnitude > 0.001f)
{
    rotationNormale =
        Quaternion.LookRotation(
            direction.normalized,
            Vector3.up
        );
}
Debug.Log(
    name + " offset = " + derailOffset
);
transform.position =
    position + derailOffset;

transform.rotation =
    rotationNormale * derailRotation;

    }
public void AppliquerDegatVisuel()
{

    if(suieChoc != null)
    {

        suieChoc.transform.position =
            transform.position;


        suieChoc.transform.rotation =
            transform.rotation;


        suieChoc.Play();

    }


    Debug.Log(
        name + " : dégâts visuels activés"
    );

}

}