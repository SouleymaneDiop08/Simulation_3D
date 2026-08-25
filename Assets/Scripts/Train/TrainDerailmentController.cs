using UnityEngine;

public class TrainDerailmentController : MonoBehaviour
{
    public TrainController train;

    public bool deraille = false;
[Header("Glissement")]

    [Header("Déraillement")]
    public float ralentissement = 20f;
public float tempsEntreWagons = 0.4f;
[Header("Bascule")]
public float angleMax = 70f;

private bool[] wagonsActifs;
private float tempsDeraillement = 0f;
[Header("Torsion wagons")]
public float rotationMaxAvant = 90f;
public float rotationArriere = 50f;
private int dernierWagonDeraille = 0;
[Header("Bascule")]
public float vitesseBascule = 30f;
    public void Derail()
    {
        if (deraille)
            return;

        deraille = true;

        Debug.Log(train.name + " DÉRAILLEMENT");
        wagonsActifs = new bool[train.wagons.Length];
    }

void Update()
{
    if (!deraille)
        return;


    // ==========================
    // RALENTISSEMENT
    // ==========================

    train.physics.vitesseActuelle =
        Mathf.MoveTowards(
            train.physics.vitesseActuelle,
            0f,
            ralentissement * Time.deltaTime
        );

    // ==========================
    // PROPAGATION WAGON PAR WAGON
    // ==========================

    tempsDeraillement += Time.deltaTime;


    if(tempsDeraillement >= tempsEntreWagons)
    {
        tempsDeraillement = 0f;


        if(dernierWagonDeraille < train.wagons.Length)
        {
            WagonController wagon =
                train.wagons[dernierWagonDeraille];


            if(wagon != null)
            {
wagon.derailOffset =
    new Vector3(-2f,0f,0f);

                float intensite =
                    Mathf.Lerp(
                        rotationMaxAvant,
                        rotationArriere,
                        (float)dernierWagonDeraille / train.wagons.Length
                    );


                wagonsActifs[dernierWagonDeraille] = true;


                Debug.Log(
                    "WAGON DÉRAILLÉ : " + wagon.name
                );

                Debug.Log(
                    "ROTATION CIBLE : " + intensite
                );
            }


            dernierWagonDeraille++;
        }
    }


    // ==========================
    // BASCULE PROGRESSIVE
    // ==========================

    for(int i = 0; i < train.wagons.Length; i++)
    {
        if(!wagonsActifs[i])
            continue;


        WagonController wagon =
            train.wagons[i];


        if(wagon == null)
            continue;


        float intensite =
            Mathf.Lerp(
                rotationMaxAvant,
                rotationArriere,
                (float)i / train.wagons.Length
            );


        Quaternion rotationCible =
            Quaternion.Euler(
                0,
                0,
                -intensite
            );


        wagon.derailRotation =
            Quaternion.RotateTowards(
                wagon.derailRotation,
                rotationCible,
                vitesseBascule * Time.deltaTime
            );
    }
}
}