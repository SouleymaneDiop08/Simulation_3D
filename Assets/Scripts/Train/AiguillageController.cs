using UnityEngine;


public class AiguillageController : MonoBehaviour
{

    [Header("Voies")]
    public TrackSystem voiePrincipale;
    public TrackSystem voieDeviation;


    private bool deviationActive = false;



    public TrackSystem GetVoieActive()
    {

        if(deviationActive)
        {
            return voieDeviation;
        }


        return voiePrincipale;

    }




    public void ActiverDeviation()
    {

        deviationActive = true;


        Debug.Log(
            "Aiguillage : DEVIATION"
        );

    }




    public void ActiverPrincipale()
    {

        deviationActive = false;


        Debug.Log(
            "Aiguillage : PRINCIPALE"
        );

    }

}