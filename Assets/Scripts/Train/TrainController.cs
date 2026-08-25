using System.Collections.Generic;
using UnityEngine;


public class TrainController : MonoBehaviour
{

    public enum SensTrain
    {
        Avant,
        Neutre,
        Arriere
    }


public enum EtatTrain
{
    Normal,
    Impact,
    Bloque
}

    public SensTrain sens = SensTrain.Avant;

    public EtatTrain etat = EtatTrain.Normal;



    [Header("Wagons")]
    public WagonController[] wagons;



    [Header("Voie actuelle")]
    public TrackSystem trackSystem;



    [Header("Physique")]
    public TrainPhysicsController physics;



    [HideInInspector]
    public float vitesse = 0f;
// ==========================
// LIMITATION DE VITESSE
// ==========================

[HideInInspector]
// ==========================
// LIMITATIONS DE VITESSE ACTIVES
// ==========================

public List<SpeedLimitZone> zonesVitesseActives =
    new List<SpeedLimitZone>();
public float vitesseAutorisee = 999f;


    [HideInInspector]
    public float distanceTrain = 0f;



    public float distanceEntreWagons = 15f;



    // ==========================
    // IMPACT
    // ==========================

    private float vitesseImpact = 0f;

    private float tempsImpact = 0f;

    private float dureeImpact = 0f;



    // ==========================
    // AIGUILLAGE
    // ==========================

    public TrackSystem voiePrincipale;

    public TrackSystem voieDeviation;


    private TrackSystem prochaineVoie;



    void Start()
    {

        if(trackSystem != null)
        {
            AppliquerVoie(trackSystem);
        }
    foreach (WagonController wagon in wagons)
    {
        if (wagon != null)
        {
            wagon.train = this;
        }
    }
    }





    void Update()
    {

        if(prochaineVoie != null)
        {
            AppliquerVoie(prochaineVoie);

            prochaineVoie = null;
        }



        // ==========================
        // ETAT IMPACT
        // ==========================

if(etat == EtatTrain.Impact)
{

    distanceTrain += vitesseImpact * Time.deltaTime;


    // ralentissement progressif du recul
    vitesseImpact = Mathf.MoveTowards(
        vitesseImpact,
        0f,
        20f * Time.deltaTime
    );


    if(Mathf.Abs(vitesseImpact) < 0.1f)
    {

        vitesseImpact = 0f;


        etat = EtatTrain.Bloque;


        vitesse = 0f;


        if(physics != null)
        {
            physics.FreinUrgence();
        }


        Debug.Log(
            name + " arrêt après impact"
        );

    }

}


        // ==========================
        // DEPLACEMENT NORMAL
        // ==========================

        else if(etat == EtatTrain.Normal)
        {

            float vitesseReelle = 0f;



            switch(sens)
            {

                case SensTrain.Avant:

                    vitesseReelle = vitesse;

                    break;



                case SensTrain.Arriere:

                    vitesseReelle = -vitesse;

                    break;



                case SensTrain.Neutre:

                    vitesseReelle = 0f;

                    break;

            }



            distanceTrain +=
                vitesseReelle *
                Time.deltaTime;

        }



        if(trackSystem != null)
        {

            distanceTrain =
                Mathf.Clamp(
                    distanceTrain,
                    0,
                    trackSystem.longueur
                );

        }




        // ==========================
        // POSITION WAGONS
        // ==========================
// ==========================
// POSITION WAGONS
// ==========================

for(int i = 0; i < wagons.Length; i++)
{

    if(wagons[i] != null)
    {

        float distanceWagon =
            distanceTrain -
            (i * distanceEntreWagons);

        wagons[i].Move(distanceWagon);

    }

}
 }




    // ==========================
    // IMPACT
    // ==========================

    public void AppliquerImpact(
        float forceRecul,
        float duree
    )
    {

        if(etat == EtatTrain.Bloque)
            return;



        etat = EtatTrain.Impact;



       vitesseImpact = -forceRecul * 2f;


        dureeImpact = duree;


        tempsImpact = 0f;



        vitesse = 0f;



        Debug.Log(
            name + " réaction choc"
        );

    }






    // ==========================
    // RECUL MANUEL
    // ==========================

    public void ReculerTrain(float distance)
    {

        distanceTrain -= distance;


        if(trackSystem != null)
        {

            distanceTrain =
                Mathf.Clamp(
                    distanceTrain,
                    0,
                    trackSystem.longueur
                );

        }

    }





    // ==========================
    // AIGUILLAGE
    // ==========================

    public void DemanderChangementVoie(
        TrackSystem nouvelleVoie
    )
    {

        if(nouvelleVoie == null)
            return;


        prochaineVoie = nouvelleVoie;

    }





    public void AppliquerVoie(
        TrackSystem nouvelleVoie
    )
    {

        if(nouvelleVoie == null)
            return;



        trackSystem = nouvelleVoie;



        foreach(WagonController wagon in wagons)
        {

            if(wagon != null)
            {
                wagon.SetTrack(trackSystem);
            }

        }

    }






    // ==========================
    // SENS
    // ==========================

    public void MettreAvant()
    {
        sens = SensTrain.Avant;
    }



    public void MettreNeutre()
    {
        sens = SensTrain.Neutre;
    }



    public void MettreArriere()
    {
        sens = SensTrain.Arriere;
    }
// ==========================
// ZONES DE VITESSE
// ==========================

// ==========================
// GESTION DES ZONES DE VITESSE
// ==========================


public void EntrerZoneVitesse(
    SpeedLimitZone zone
)
{

    if(!zonesVitesseActives.Contains(zone))
    {
        zonesVitesseActives.Add(zone);
    }


    RecalculerVitesseAutorisee();

}



public void SortirZoneVitesse(
    SpeedLimitZone zone
)
{

    if(zonesVitesseActives.Contains(zone))
    {
        zonesVitesseActives.Remove(zone);
    }


    RecalculerVitesseAutorisee();

}



void RecalculerVitesseAutorisee()
{

    vitesseAutorisee = 999f;


    foreach(SpeedLimitZone zone in zonesVitesseActives)
    {

        if(zone.vitesseMax < vitesseAutorisee)
        {
            vitesseAutorisee =
                zone.vitesseMax;
        }

    }

}
    
}
