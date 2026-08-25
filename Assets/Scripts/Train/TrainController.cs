using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Cinématique du convoi : une distance curviligne le long de la voie, et des
/// wagons répartis en arrière de cette distance.
///
/// Unités : distances en mètres, vitesses en mètres par seconde.
/// </summary>
public class TrainController : MonoBehaviour
{
    /// <summary>Facteur de conversion pour l'affichage et l'échange automate.</summary>
    public const float MS_VERS_KMH = 3.6f;


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
        Bloque,
        FinDeVoie
    }


    [Header("État")]
    public SensTrain sens = SensTrain.Avant;
    public EtatTrain etat = EtatTrain.Normal;


    [Header("Wagons")]
    public WagonController[] wagons;

    [Tooltip("Écart entre deux wagons consécutifs, en mètres.")]
    public float distanceEntreWagons = 15f;


    [Header("Voie actuelle")]
    public TrackSystem trackSystem;


    [Header("Physique")]
    public TrainPhysicsController physics;


    [Header("Impact")]
    [Tooltip("Multiplicateur appliqué à la force de recul reçue lors d'un choc.")]
    public float coefficientRecul = 2f;


    // ==========================================================
    // ÉTAT COURANT
    // ==========================================================

    /// <summary>Vitesse instantanée, en m/s. Écrite par TrainPhysicsController.</summary>
    [HideInInspector]
    public float vitesse = 0f;

    /// <summary>Vitesse maximale autorisée, en m/s.</summary>
    [HideInInspector]
    public float vitesseAutorisee = 999f;

    /// <summary>Position de la tête du convoi sur la voie, en mètres.</summary>
    [HideInInspector]
    public float distanceTrain = 0f;

    /// <summary>Zones de limitation dans lesquelles le convoi se trouve.</summary>
    [HideInInspector]
    public List<SpeedLimitZone> zonesVitesseActives = new List<SpeedLimitZone>();


    /// <summary>Vitesse en km/h, pour l'affichage et l'échange automate.</summary>
    public float VitesseKmh => vitesse * MS_VERS_KMH;


    // ==========================================================
    // INTERNE
    // ==========================================================

    private float vitesseImpact = 0f;
    private float tempsImpact = 0f;
    private float dureeImpact = 0f;

    private TrackSystem prochaineVoie;


    private void Start()
    {
        if (wagons == null)
            wagons = new WagonController[0];

        foreach (WagonController wagon in wagons)
        {
            if (wagon != null)
                wagon.train = this;
        }

        // Sans décalage initial, tous les wagons seraient ramenés à la
        // distance 0 par le Clamp et se superposeraient au démarrage.
        float minimum = Mathf.Max(0f, (wagons.Length - 1) * distanceEntreWagons);

        if (distanceTrain < minimum)
            distanceTrain = minimum;

        if (trackSystem != null)
            AppliquerVoie(trackSystem);
    }


    private void Update()
    {
        // Le changement de voie est différé d'une image : il est demandé
        // depuis un OnTriggerEnter, en plein parcours de la physique.
        if (prochaineVoie != null)
        {
            AppliquerVoie(prochaineVoie);
            prochaineVoie = null;
        }

        switch (etat)
        {
            case EtatTrain.Impact:
                MettreAJourImpact();
                break;

            case EtatTrain.Normal:
                MettreAJourDeplacement();
                break;

            // Bloque et FinDeVoie : le convoi ne se déplace plus.
        }

        PositionnerWagons();
    }


    // ==========================================================
    // DÉPLACEMENT
    // ==========================================================

    private void MettreAJourDeplacement()
    {
        float vitesseReelle;

        switch (sens)
        {
            case SensTrain.Avant:
                vitesseReelle = vitesse;
                break;

            case SensTrain.Arriere:
                vitesseReelle = -vitesse;
                break;

            default:
                vitesseReelle = 0f;
                break;
        }

        distanceTrain += vitesseReelle * Time.deltaTime;

        if (trackSystem == null || !trackSystem.Pret)
            return;

        // En bout de voie, on change d'état au lieu de figer silencieusement
        // la distance : sinon le train reste immobile alors que la physique
        // continue d'annoncer une vitesse.
        if (distanceTrain >= trackSystem.Longueur)
        {
            distanceTrain = trackSystem.Longueur;
            ArriverEnBoutDeVoie();
        }
        else if (distanceTrain <= 0f)
        {
            distanceTrain = 0f;
            ArriverEnBoutDeVoie();
        }
    }


    private void ArriverEnBoutDeVoie()
    {
        if (etat == EtatTrain.FinDeVoie)
            return;

        etat = EtatTrain.FinDeVoie;
        vitesse = 0f;

        if (physics != null)
            physics.FreinUrgence();

        Debug.Log($"[Train] {name} : bout de voie atteint.", this);
    }


    private void PositionnerWagons()
    {
        if (wagons == null)
            return;

        for (int i = 0; i < wagons.Length; i++)
        {
            if (wagons[i] != null)
                wagons[i].Move(distanceTrain - i * distanceEntreWagons);
        }
    }


    // ==========================================================
    // IMPACT
    // ==========================================================

    private void MettreAJourImpact()
    {
        tempsImpact += Time.deltaTime;

        distanceTrain += vitesseImpact * Time.deltaTime;

        if (trackSystem != null && trackSystem.Pret)
            distanceTrain = Mathf.Clamp(distanceTrain, 0f, trackSystem.Longueur);

        // Amortissement du recul
        vitesseImpact = Mathf.MoveTowards(vitesseImpact, 0f, 20f * Time.deltaTime);

        bool recultermine = Mathf.Abs(vitesseImpact) < 0.1f;
        bool dureeEcoulee = dureeImpact > 0f && tempsImpact >= dureeImpact;

        if (!recultermine && !dureeEcoulee)
            return;

        vitesseImpact = 0f;
        vitesse = 0f;
        etat = EtatTrain.Bloque;

        if (physics != null)
            physics.FreinUrgence();

        Debug.Log($"[Train] {name} : arrêt après impact.", this);
    }


    public void AppliquerImpact(float forceRecul, float duree)
    {
        if (etat == EtatTrain.Bloque)
            return;

        etat = EtatTrain.Impact;

        vitesseImpact = -forceRecul * coefficientRecul;
        dureeImpact = duree;
        tempsImpact = 0f;

        vitesse = 0f;

        if (physics != null)
            physics.FreinUrgence();

        Debug.Log($"[Train] {name} : réaction au choc.", this);
    }


    /// <summary>
    /// Sort le convoi d'un état bloquant (impact, bout de voie) et le remet en
    /// service. Sans cela, l'état Bloque était définitif : aucun code ne
    /// permettait d'en sortir.
    /// </summary>
    public void Reinitialiser()
    {
        etat = EtatTrain.Normal;

        vitesse = 0f;
        vitesseImpact = 0f;
        tempsImpact = 0f;
        dureeImpact = 0f;

        if (physics != null)
            physics.Reinitialiser();

        if (wagons != null)
        {
            foreach (WagonController wagon in wagons)
            {
                if (wagon != null)
                    wagon.Reinitialiser();
            }
        }

        Debug.Log($"[Train] {name} : réinitialisé.", this);
    }


    public void ReculerTrain(float distance)
    {
        distanceTrain -= distance;

        if (trackSystem != null && trackSystem.Pret)
            distanceTrain = Mathf.Clamp(distanceTrain, 0f, trackSystem.Longueur);
    }


    // ==========================================================
    // AIGUILLAGE
    // ==========================================================

    public void DemanderChangementVoie(TrackSystem nouvelleVoie)
    {
        if (nouvelleVoie == null || nouvelleVoie == trackSystem)
            return;

        prochaineVoie = nouvelleVoie;
    }


    public void AppliquerVoie(TrackSystem nouvelleVoie)
    {
        if (nouvelleVoie == null || !nouvelleVoie.Pret)
            return;

        // La distance est reprojetée sur le nouveau tracé. La conserver telle
        // quelle ferait sauter le convoi, les deux voies n'ayant ni la même
        // origine ni la même longueur.
        if (trackSystem != null && trackSystem != nouvelleVoie && trackSystem.Pret)
        {
            Vector3 positionActuelle = trackSystem.GetPosition(distanceTrain);
            distanceTrain = nouvelleVoie.ProjeterDistance(positionActuelle);
        }

        trackSystem = nouvelleVoie;

        if (wagons == null)
            return;

        foreach (WagonController wagon in wagons)
        {
            if (wagon != null)
                wagon.SetTrack(trackSystem);
        }
    }


    // ==========================================================
    // SENS
    // ==========================================================

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


    // ==========================================================
    // ZONES DE VITESSE
    // ==========================================================

    public void EntrerZoneVitesse(SpeedLimitZone zone)
    {
        if (zone == null || zonesVitesseActives.Contains(zone))
            return;

        zonesVitesseActives.Add(zone);
        RecalculerVitesseAutorisee();
    }


    public void SortirZoneVitesse(SpeedLimitZone zone)
    {
        if (zone == null || !zonesVitesseActives.Remove(zone))
            return;

        RecalculerVitesseAutorisee();
    }


    private void RecalculerVitesseAutorisee()
    {
        float limite = 999f;

        // Parcours à l'envers : permet de purger au passage les zones
        // détruites, qui bloqueraient sinon la limitation indéfiniment.
        for (int i = zonesVitesseActives.Count - 1; i >= 0; i--)
        {
            SpeedLimitZone zone = zonesVitesseActives[i];

            if (zone == null)
            {
                zonesVitesseActives.RemoveAt(i);
                continue;
            }

            if (zone.VitesseMaxMs < limite)
                limite = zone.VitesseMaxMs;
        }

        vitesseAutorisee = limite;
    }
}
