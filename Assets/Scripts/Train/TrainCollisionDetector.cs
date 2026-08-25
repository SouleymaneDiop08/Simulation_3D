using UnityEngine;


public class TrainCollisionDetector : MonoBehaviour
{

    [Header("Train associé")]
    public TrainController train;



    [Header("Détection")]
    public float distanceDetection = 5f;



    [Header("Impact")]
 public float forceImpact = 35f;

public float dureeImpact = 1f;

[Header("Explosion")]
public GameObject explosionPrefab;

    private bool collisionEffectuee = false;



    void Update()
    {

        if(train == null)
            return;



        if(collisionEffectuee)
            return;




        TrainCollisionDetector[] trains =
            FindObjectsByType<TrainCollisionDetector>();



        foreach(TrainCollisionDetector autre in trains)
        {

            if(autre == this)
                continue;


            if(autre.train == null)
                continue;


            if(autre.train == train)
                continue;



            float distance =
                Vector3.Distance(
                    transform.position,
                    autre.transform.position
                );



            if(distance <= distanceDetection)
            {

                CollisionTrain(autre);

                break;

            }

        }

    }





    void CollisionTrain(
        TrainCollisionDetector autre
    )
    {

        if(collisionEffectuee)
            return;


        if(autre.collisionEffectuee)
            return;




        collisionEffectuee = true;

        autre.collisionEffectuee = true;



        Debug.Log(
            "CHOC ENTRE : "
            + train.name
            + " ET "
            + autre.train.name
        );

// ==========================
// EXPLOSION
// ==========================

if(explosionPrefab != null)
{

Vector3 pointImpact =
    (
        transform.position +
        autre.transform.position
    ) * 0.5f;
pointImpact.y += 2f;
Debug.Log("EXPLOSION CRÉÉE À : " + pointImpact);

GameObject explosion =
    Instantiate(
        explosionPrefab,
        pointImpact + Vector3.up ,
        Quaternion.identity
    );
    TrainDamageController degats =
    train.GetComponent<TrainDamageController>();


if(degats != null)
{
    degats.AppliquerDegats(pointImpact);
}



TrainDamageController degatsAutre =
    autre.train.GetComponent<TrainDamageController>();


if(degatsAutre != null)
{
    degatsAutre.AppliquerDegats(pointImpact);
}


explosion.transform.localScale =
    Vector3.one * 5f;


Debug.Log(
    "Explosion créée : "
    + explosion.name
);
}



        // ==========================
        // REACTION CHOC
        // ==========================

train.AppliquerImpact(
    forceImpact,
    dureeImpact
);


autre.train.AppliquerImpact(
    forceImpact,
    dureeImpact
);
    }

}