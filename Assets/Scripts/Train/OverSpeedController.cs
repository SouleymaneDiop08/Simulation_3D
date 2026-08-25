using UnityEngine;

public class OverspeedController : MonoBehaviour
{

    public TrainController train;


    [Header("Temps avant réaction")]
    public float tempsAlerte = 3f;
    public float tempsFreinage = 8f;
    public float tempsCritique = 12f;



    private float tempsSurvitesse = 0f;



    public int niveauSurvitesse = 0;



    void Update()
    {

        if(train == null)
            return;



        float depassement =
            train.vitesse - train.vitesseAutorisee;



        // Pas de survitesse

        if(depassement <= 0)
        {

            tempsSurvitesse = 0f;

            niveauSurvitesse = 0;

            return;

        }



        tempsSurvitesse += Time.deltaTime;



        // Niveau 1 : alerte

        if(tempsSurvitesse >= tempsAlerte)
        {

            niveauSurvitesse = 1;


            Debug.Log(
                train.name +
                " : ALERTE SURVITESSE (" +
                train.vitesse +
                "/" +
                train.vitesseAutorisee +
                ")"
            );

        }



        // Niveau 2 : freinage automatique

        if(tempsSurvitesse >= tempsFreinage)
        {

            niveauSurvitesse = 2;


            Debug.Log(
                train.name +
                " : FREINAGE AUTOMATIQUE"
            );


            if(train.physics != null)
            {
                train.physics.FreinService();
            }

        }



        // Niveau 3 : critique

        if(tempsSurvitesse >= tempsCritique)
        {

            niveauSurvitesse = 3;


            Debug.Log(
                train.name +
                " : SURVITESSE CRITIQUE"
            );

        }

    }

}