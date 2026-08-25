using UnityEngine;


public class TrainPhysicsController : MonoBehaviour
{

    public TrainController train;


    [Header("Vitesse")]
    public float vitesseMax = 70f;

    public float vitesseDemandee = 0f;

    public float vitesseActuelle = 0f;



    [Header("Physique")]
    public float acceleration = 5f;

    public float freinServicePuissance = 10f;

    public float freinUrgencePuissance = 25f;






    private enum EtatFrein
    {
        Relache,
        Service,
        Urgence
    }


    private EtatFrein etatFrein = EtatFrein.Relache;



void Start()
{

    etatFrein = EtatFrein.Relache;
    vitesseDemandee = 0;
    vitesseActuelle = 0;
}

    void Update()
    {

        if(train == null)
            return;



        if(etatFrein == EtatFrein.Relache)
        {

            vitesseActuelle = Mathf.MoveTowards(
                vitesseActuelle,
                vitesseDemandee,
                acceleration * Time.deltaTime
            );

        }



        if(etatFrein == EtatFrein.Service)
        {

            vitesseActuelle = Mathf.MoveTowards(
                vitesseActuelle,
                0,
                freinServicePuissance * Time.deltaTime
            );

        }



        if(etatFrein == EtatFrein.Urgence)
        {

            vitesseActuelle = Mathf.MoveTowards(
                vitesseActuelle,
                0,
                freinUrgencePuissance * Time.deltaTime
            );

        }



train.vitesse = vitesseActuelle;    
    }







    public void ChangerTraction(float valeur)
    {

  


        valeur = Mathf.Clamp01(valeur);



        vitesseDemandee =
            valeur * vitesseMax;



        etatFrein = EtatFrein.Relache;



        Debug.Log(
            "Consigne : "
            + vitesseDemandee
        );

    }







    public void FreinService()
    {
        etatFrein = EtatFrein.Service;
    }







    public void FreinUrgence()
    {

        vitesseDemandee = 0;


        etatFrein = EtatFrein.Urgence;

    }





public void RelacherFrein()
{
    etatFrein = EtatFrein.Relache;

    Debug.Log(
        "Frein relâché"
    );
}

    








}