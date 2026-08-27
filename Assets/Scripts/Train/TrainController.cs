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

    [Header("Quais")]
    [Tooltip("Distance de garde conservée entre le convoi et chaque extrémité " +
             "de la voie, en mètres. Empêche le convoi d'entrer sous le " +
             "bâtiment de la gare, quoi que commande l'automate.")]
    public float margeQuai = 25f;


    [Header("Voie actuelle")]
    public TrackSystem trackSystem;


    [Header("Physique")]
    public TrainPhysicsController physics;

    [Header("Pilotage")]
    [Tooltip("Vitesse de rattrapage de la position commandée par l'automate, " +
             "en fraction par seconde. Lisse les paliers de 10 cm reçus à 20 Hz.")]
    [Min(1f)]
    public float rattrapagePosition = 12f;


    [Header("Impact")]
    [Tooltip("Multiplicateur appliqué à la force de recul reçue lors d'un choc. " +
             "Volontairement modeste : un choc doit se lire comme un arrêt " +
             "brutal, pas comme un rebond.")]
    public float coefficientRecul = 0.5f;

    [Tooltip("Amortissement du recul après un choc, en m/s². Élevé pour que le " +
             "convoi s'immobilise en une fraction de seconde.")]
    public float amortissementRecul = 40f;


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

    /// <summary>Longueur du convoi, du premier au dernier wagon, en mètres.</summary>
    public float LongueurConvoi =>
        wagons == null || wagons.Length < 2 ? 0f : (wagons.Length - 1) * distanceEntreWagons;

    /// <summary>Position la plus reculée admise pour la tête du convoi.</summary>
    public float LimiteBasse => margeQuai + LongueurConvoi;

    /// <summary>Position la plus avancée admise pour la tête du convoi.</summary>
    public float LimiteHaute =>
        trackSystem != null && trackSystem.Pret
            ? Mathf.Max(trackSystem.Longueur - margeQuai, LimiteBasse)
            : LimiteBasse;


    // ==========================================================
    // INTERNE
    // ==========================================================

    private float vitesseImpact = 0f;
    private float tempsImpact = 0f;
    private float dureeImpact = 0f;

    private TrackSystem prochaineVoie;

    private float _positionCommandee = -1f;

    /// <summary>Vrai lorsque l'automate fournit la position du convoi.</summary>
    public bool PiloteEnPosition => _positionCommandee >= 0f;


    /// <summary>
    /// Position imposée par l'automate, en mètres. Dès qu'elle est fournie, la
    /// simulation cesse d'intégrer la vitesse : elle se contente de suivre.
    ///
    /// C'est ce qui rend la vue reproductible — un rechargement de page replace
    /// le convoi exactement où l'automate le situe, sans dérive possible entre
    /// deux intégrations indépendantes.
    /// </summary>
    public void DefinirPositionCommandee(float metres)
    {
        _positionCommandee = metres;
    }


    private void Start()
    {
        if (wagons == null)
            wagons = new WagonController[0];

        foreach (WagonController wagon in wagons)
        {
            if (wagon != null)
                wagon.train = this;
        }

        if (trackSystem != null)
            AppliquerVoie(trackSystem);

        // Le convoi est posé au-delà du quai : sa queue se trouve à margeQuai
        // de l'origine de la voie, et non à la distance 0 — qui tombe sous le
        // bâtiment de la gare.
        if (distanceTrain < LimiteBasse)
            distanceTrain = LimiteBasse;
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
            case EtatTrain.FinDeVoie:
                // FinDeVoie doit continuer d'être évalué : c'est dans
                // MettreAJourDeplacement que la butée se lève, dès que le sens
                // commandé éloigne le convoi. L'exclure ici l'immobiliserait
                // définitivement.
                MettreAJourDeplacement();
                break;

            // Bloque : le convoi ne se déplace plus.
        }

        PositionnerWagons();
    }


    // ==========================================================
    // DÉPLACEMENT
    // ==========================================================

    private void MettreAJourDeplacement()
    {
        if (PiloteEnPosition)
        {
            SuivrePositionCommandee();
            return;
        }

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

        // Butées de quai. Volontairement NON verrouillantes : sur une navette,
        // l'automate commandera le sens inverse quelques secondes plus tard.
        // Un état terminal immobiliserait le convoi pour de bon dès le moindre
        // écart de calibrage.
        if (distanceTrain >= LimiteHaute && vitesseReelle > 0f)
        {
            distanceTrain = LimiteHaute;
            Buter();
        }
        else if (distanceTrain <= LimiteBasse && vitesseReelle < 0f)
        {
            distanceTrain = LimiteBasse;
            Buter();
        }
        else if (etat == EtatTrain.FinDeVoie)
        {
            // Le convoi repart dans l'autre sens : la butée est levée
            etat = EtatTrain.Normal;
        }
    }


    /// <summary>
    /// Rejoint la position dictée par l'automate. Le lissage évite les paliers
    /// visibles : la position arrive en décimètres, vingt fois par seconde,
    /// alors que l'image est rendue bien plus souvent.
    /// </summary>
    private void SuivrePositionCommandee()
    {
        float cible = _positionCommandee;

        if (trackSystem != null && trackSystem.Pret)
            cible = Mathf.Clamp(cible, LimiteBasse, LimiteHaute);

        // Un écart important vient d'un rechargement ou d'une reprise de
        // liaison : on s'y place d'un coup plutôt que de traverser la ligne.
        if (Mathf.Abs(cible - distanceTrain) > 50f)
            distanceTrain = cible;
        else
            distanceTrain = Mathf.Lerp(distanceTrain, cible,
                                       1f - Mathf.Exp(-rattrapagePosition * Time.deltaTime));

        if (etat == EtatTrain.FinDeVoie)
            etat = EtatTrain.Normal;
    }


    /// <summary>
    /// Arrêt en butée de quai. La vitesse est annulée mais aucun état bloquant
    /// n'est posé : le convoi repartira dès que le sens commandé l'éloignera
    /// de la butée.
    /// </summary>
    private void Buter()
    {
        vitesse = 0f;

        if (physics != null)
            physics.ArreterNet();

        if (etat == EtatTrain.FinDeVoie)
            return;

        etat = EtatTrain.FinDeVoie;
        Debug.Log($"[Train] {name} : butée de quai atteinte.", this);
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
            distanceTrain = Mathf.Clamp(distanceTrain, LimiteBasse, LimiteHaute);

        // Amortissement du recul
        vitesseImpact = Mathf.MoveTowards(vitesseImpact, 0f, amortissementRecul * Time.deltaTime);

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


    /// <summary>
    /// Réaction au choc : léger recul, puis immobilisation définitive.
    ///
    /// L'état Bloque n'est pas levé par la suite : un convoi accidenté reste
    /// sur place, et cesse de suivre la position dictée par l'automate. C'est
    /// voulu — sans cela les deux convois se traverseraient et repartiraient
    /// comme si de rien n'était.
    /// </summary>
    public void AppliquerImpact(float forceRecul, float duree)
    {
        if (etat == EtatTrain.Bloque || etat == EtatTrain.Impact)
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
            distanceTrain = Mathf.Clamp(distanceTrain, LimiteBasse, LimiteHaute);
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
