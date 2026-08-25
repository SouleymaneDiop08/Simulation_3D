using UnityEngine;


public class AiguillageTrigger : MonoBehaviour
{

    public AiguillageController aiguillage;


    public TrainController train;



    private void OnTriggerEnter(Collider other)
    {

        WagonController wagon =
            other.GetComponent<WagonController>();


        if(wagon == null)
            return;



        Debug.Log(
            "Train détecté par aiguillage"
        );



        TrackSystem voie =
            aiguillage.GetVoieActive();



        train.DemanderChangementVoie(
            voie
        );

    }

}