using UnityEngine;


public class TrainDamageController : MonoBehaviour
{

    [Header("Train associé")]
    public TrainController train;


    [Header("Distance des dégâts")]
    public float distanceDegatsForts = 5f;
    public float distanceDegatsLegers = 15f;



    public void AppliquerDegats(Vector3 pointImpact)
    {

        if(train == null)
            return;


        Debug.Log(
            "DÉGÂTS APPELÉS SUR : " + train.name
        );


        foreach(WagonController wagon in train.wagons)
        {

            if(wagon == null)
                continue;


            Debug.Log(
                "Analyse wagon : " + wagon.name
            );


            float distance =
                Vector3.Distance(
                    wagon.transform.position,
                    pointImpact
                );


            if(distance <= distanceDegatsForts)
            {

                Debug.Log(
                    wagon.name +
                    " : DEGATS FORTS"
                );


                wagon.AppliquerDegatVisuel();

            }


            else if(distance <= distanceDegatsLegers)
            {

                Debug.Log(
                    wagon.name +
                    " : DEGATS LEGERS"
                );

            }

        }

    }

} 