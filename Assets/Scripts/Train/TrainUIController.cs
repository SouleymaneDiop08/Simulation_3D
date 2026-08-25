using UnityEngine;
using UnityEngine.UI;


public class TrainUIController : MonoBehaviour
{

    // ==========================
    // TRAINS
    // ==========================

    [Header("Trains")]

    public TrainController train1;

    public TrainController train2;


    private TrainController trainActuel;



    // ==========================
    // AIGUILLAGES
    // ==========================

    [Header("Aiguillages")]

    public AiguillageController aiguillageTrain1;

    public AiguillageController aiguillageTrain2;


    private AiguillageController aiguillageActuel;



    // ==========================
    // SLIDER
    // ==========================

    [Header("Vitesse")]

    public Slider sliderVitesse;



    // ==========================
    // START
    // ==========================

    void Start()
    {
        SelectionnerTrain1();
    }





    // ==========================
    // SELECTION TRAIN
    // ==========================


    public void SelectionnerTrain1()
    {

        trainActuel = train1;

        aiguillageActuel = aiguillageTrain1;


        Debug.Log(
            "Train 1 sélectionné"
        );

    }





    public void SelectionnerTrain2()
    {

        trainActuel = train2;

        aiguillageActuel = aiguillageTrain2;


        Debug.Log(
            "Train 2 sélectionné"
        );

    }





    // ==========================
    // VITESSE
    // ==========================


    public void ModifierVitesse()
    {

        if(trainActuel == null)
            return;


        if(trainActuel.physics == null)
        {
            Debug.Log(
                "TrainPhysicsController absent"
            );

            return;
        }


        trainActuel.physics.ChangerTraction(
            sliderVitesse.value
        );

    }





    // ==========================
    // FREINS
    // ==========================


    public void FreinService()
    {

        if(trainActuel == null)
            return;


        trainActuel.physics.FreinService();

    }




    public void FreinUrgence()
    {

        if(trainActuel == null)
            return;


        trainActuel.physics.FreinUrgence();

    }




    public void RelacherFrein()
    {

        if(trainActuel == null)
            return;


        trainActuel.physics.RelacherFrein();

    }





    // ==========================
    // SENS
    // ==========================


    public void SensAvant()
    {

        if(trainActuel == null)
            return;


        trainActuel.sens =
            TrainController.SensTrain.Avant;

    }




    public void SensNeutre()
    {

        if(trainActuel == null)
            return;


        trainActuel.sens =
            TrainController.SensTrain.Neutre;

    }




    public void SensArriere()
    {

        if(trainActuel == null)
            return;


        trainActuel.sens =
            TrainController.SensTrain.Arriere;

    }





    // ==========================
    // AIGUILLAGE
    // ==========================


    public void ActiverDeviation()
    {

        if(aiguillageActuel == null)
        {
            Debug.Log(
                "Aucun aiguillage sélectionné"
            );

            return;
        }


        aiguillageActuel.ActiverDeviation();


        Debug.Log(
            "Déviation activée"
        );

    }





    public void RetourVoiePrincipale()
    {

        if(aiguillageActuel == null)
        {
            Debug.Log(
                "Aucun aiguillage sélectionné"
            );

            return;
        }


        aiguillageActuel.ActiverPrincipale();


        Debug.Log(
            "Voie principale activée"
        );

    }

}