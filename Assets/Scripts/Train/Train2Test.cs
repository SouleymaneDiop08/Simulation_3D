using UnityEngine;
using UnityEngine.InputSystem;

public class Train2Test : MonoBehaviour
{
    public TrainController train2;

    public TrackSystem voiePrincipale;
    public TrackSystem voieDeviation;


    void Update()
    {

        if(Keyboard.current == null)
            return;


        // Touche D = Déviation
        if(Keyboard.current.dKey.wasPressedThisFrame)
        {
            if(train2 != null && voieDeviation != null)
            {
                train2.DemanderChangementVoie(
                    voieDeviation
                );

                Debug.Log(
                    "Train 2 : changement vers déviation demandé"
                );
            }
        }



        // Touche P = Principale
        if(Keyboard.current.pKey.wasPressedThisFrame)
        {
            if(train2 != null && voiePrincipale != null)
            {
                train2.DemanderChangementVoie(
                    voiePrincipale
                );

                Debug.Log(
                    "Train 2 : retour voie principale demandé"
                );
            }
        }

    }
}