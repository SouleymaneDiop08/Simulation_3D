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

    // Chaque wagon porte SA distance sur SA voie. Une seule distance partagée
    // ne permettrait pas de franchir une aiguille caisse par caisse : les
    // deux voies n'ont ni la même origine ni le même paramétrage.
    private float[] _distances;

    // Voie quittée et point de bascule, le temps que tout le convoi franchisse
    // l'appareil. Null dès que la dernière caisse est passée.
    private TrackSystem _voieQuittee;
    private float _distanceAiguille;

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

        RepartirWagons();
    }


    /// <summary>Aligne toutes les caisses derrière la tête, sur la voie courante.</summary>
    private void RepartirWagons()
    {
        _distances = new float[wagons.Length];

        for (int i = 0; i < wagons.Length; i++)
        {
            _distances[i] = distanceTrain - i * distanceEntreWagons;

            if (wagons[i] != null)
                wagons[i].SetTrack(trackSystem);
        }

        _voieQuittee = null;
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

        AvancerConvoi(vitesseReelle * Time.deltaTime);

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


    /// <summary>
    /// Fait avancer le convoi entier du même pas : la tête et chaque caisse,
    /// chacune sur sa propre voie. C'est ce qui permet à un convoi d'être à
    /// cheval sur deux voies pendant qu'il franchit un appareil.
    /// </summary>
    private void AvancerConvoi(float pas)
    {
        distanceTrain += pas;

        if (_distances == null || _distances.Length != wagons.Length)
            RepartirWagons();

        for (int i = 0; i < _distances.Length; i++)
            _distances[i] += pas;

        FranchirAiguille(pas);
    }


    /// <summary>
    /// Bascule sur la nouvelle voie les caisses qui viennent d'atteindre
    /// l'aiguille — et elles seules.
    ///
    /// Un train ne saute pas d'une voie à l'autre : ses caisses franchissent
    /// l'appareil l'une après l'autre, et le convoi reste à cheval sur les
    /// deux voies le temps du passage. Basculer tout le monde d'un coup, comme
    /// le faisait AppliquerVoie, déplaçait latéralement des caisses encore à
    /// plusieurs dizaines de mètres en amont.
    /// </summary>
    private void FranchirAiguille(float pas)
    {
        if (_voieQuittee == null || trackSystem == null || !trackSystem.Pret)
            return;

        bool versAvant = pas >= 0f;
        bool resteEnArriere = false;

        for (int i = 0; i < wagons.Length; i++)
        {
            WagonController wagon = wagons[i];

            if (wagon == null || wagon.trackSystem != _voieQuittee)
                continue;

            // La caisse a-t-elle atteint le point de bascule ?
            bool atteint = versAvant
                ? _distances[i] >= _distanceAiguille
                : _distances[i] <= _distanceAiguille;

            if (!atteint)
            {
                resteEnArriere = true;
                continue;
            }

            // Reprojection au point exact où elle se trouve : sans cela la
            // caisse sauterait, les deux voies n'ayant pas le même origine.
            Vector3 ou = _voieQuittee.GetPosition(_distances[i]);
            _distances[i] = trackSystem.ProjeterDistance(ou);
            wagon.SetTrack(trackSystem);
        }

        if (!resteEnArriere)
        {
            Debug.Log($"[Train] {name} : convoi entièrement passé sur {trackSystem.name}.", this);
            _voieQuittee = null;
        }
    }


    private void PositionnerWagons()
    {
        if (wagons == null)
            return;

        if (_distances == null || _distances.Length != wagons.Length)
            RepartirWagons();

        for (int i = 0; i < wagons.Length; i++)
        {
            if (wagons[i] != null)
                wagons[i].Move(_distances[i]);
        }
    }


    // ==========================================================
    // IMPACT
    // ==========================================================

    private void MettreAJourImpact()
    {
        tempsImpact += Time.deltaTime;

        AvancerConvoi(vitesseImpact * Time.deltaTime);

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


    /// <summary>
    /// Engage le convoi sur une nouvelle voie.
    ///
    /// Seule la TÊTE bascule ici. Les caisses suivantes restent sur l'ancienne
    /// voie et la quitteront chacune à son tour, en arrivant au point de
    /// bascule — c'est FranchirAiguille qui s'en charge. Le convoi est donc à
    /// cheval sur les deux voies le temps du passage, comme un vrai train sur
    /// un appareil.
    /// </summary>
    public void AppliquerVoie(TrackSystem nouvelleVoie)
    {
        if (nouvelleVoie == null || !nouvelleVoie.Pret)
            return;

        TrackSystem ancienne = trackSystem;

        // Première affectation, ou pose initiale : tout le convoi suit.
        if (ancienne == null || ancienne == nouvelleVoie || !ancienne.Pret)
        {
            trackSystem = nouvelleVoie;
            RepartirWagons();
            return;
        }

        // Un franchissement est déjà en cours : on attend qu'il s'achève
        // plutôt que d'empiler deux voies quittées.
        if (_voieQuittee != null)
            return;

        // Point de bascule : là où la tête se trouve à l'instant du
        // changement. Chaque caisse y passera à son tour.
        Vector3 pointBascule = ancienne.GetPosition(distanceTrain);

        _voieQuittee = ancienne;
        _distanceAiguille = distanceTrain;

        trackSystem = nouvelleVoie;
        distanceTrain = nouvelleVoie.ProjeterDistance(pointBascule);

        // La tête seule change de voie ; son écart avec les caisses restées
        // en arrière est conservé sur leur propre voie.
        if (wagons != null && wagons.Length > 0 && wagons[0] != null)
        {
            _distances[0] = distanceTrain;
            wagons[0].SetTrack(nouvelleVoie);
        }

        Debug.Log($"[Train] {name} : tête engagée sur {nouvelleVoie.name}, " +
                  $"{wagons.Length - 1} caisse(s) encore sur {ancienne.name}.", this);
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
