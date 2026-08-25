using UnityEngine;


public class DerailmentZone : MonoBehaviour
{

    public float vitesseDeraillement = 100f;


    private void OnTriggerEnter(Collider other)
    {

        Debug.Log(
            "QUELQUE CHOSE ENTRE DANS LA ZONE : " + other.name
        );


        WagonController wagon =
            other.GetComponentInParent<WagonController>();


        if(wagon == null)
        {
            Debug.Log(
                "Aucun WagonController trouvé"
            );

            return;
        }


        TrainController train = wagon.train;


        if(train == null)
        {
            Debug.Log(
                "Wagon trouvé mais aucun train associé"
            );

            return;
        }


        Debug.Log(
            "Train trouvé : " + train.name
        );


        if(train.vitesse > vitesseDeraillement)
        {

            Debug.Log(
                "SURVITESSE DÉRAILLEMENT : " +
                train.vitesse +
                " > " +
                vitesseDeraillement
            );


            TrainDerailmentController derail =
                train.GetComponent<TrainDerailmentController>();


            if(derail != null)
            {
                derail.Derail();
            }
            else
            {
                Debug.Log(
                    "TrainDerailmentController introuvable"
                );
            }

        }
        else
        {

            Debug.Log(
                "Vitesse correcte : " +
                train.vitesse
            );

        }

    }

}