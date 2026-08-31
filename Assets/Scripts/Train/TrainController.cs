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

    // ==========================================================
    // PLACEMENT DES CAISSES
    //
    // La position de chaque caisse est DÉDUITE de celle de la tête, en
    // remontant de son recul le long du chemin parcouru — jamais accumulée
    // image par image. Une distance propre à chaque caisse, avancée pas à pas,
    // dérivait au moindre changement de voie et ne savait pas revenir en
    // arrière : le convoi basculait alors d'un bloc au lieu de s'engager
    // caisse par caisse.
    // ==========================================================

    // Voie quittée, le temps que tout le convoi franchisse l'appareil.
    // Null dès que la dernière caisse est passée.
    private TrackSystem _voieQuittee;

    // Point de bascule, exprimé sur chacune des deux voies : les deux tracés
    // n'ont ni la même origine ni le même paramétrage.
    private float _distanceAiguille;        // sur la voie quittée
    private float _distanceAiguilleNeuve;   // sur la voie courante

    // +1 si les deux tracés sont parcourus dans le même sens au point de
    // bascule, -1 s'ils s'opposent. Sert à convertir un recul exprimé sur la
    // voie courante en distance sur la voie quittée.
    private int _signeAncienne = 1;

    // Orientation de la caisse par rapport au sens croissant du tracé. Se
    // retourne en passant sur une voie opposée, jamais lors d'un simple
    // changement de sens de marche : un convoi qui rebrousse chemin recule,
    // il ne pivote pas.
    private int _orientation = 1;

    // Sens de marche au moment où le convoi s'est engagé sur l'appareil, et
    // recul de l'extrémité engagée derrière le repère de tête (0 si c'est la
    // tête qui mène, la longueur du convoi si c'est la queue). Tous deux sont
    // FIGÉS pour la durée du franchissement : c'est toujours la même extrémité
    // qui entre sur la nouvelle voie, même si la marche s'inverse entre-temps.
    private int _sensEngagement = 1;
    private float _reculEngagement = 0f;

    // Sens de marche courant, mémorisé quand il est franc. Sert à savoir quelle
    // extrémité du convoi mène, donc laquelle s'engage sur l'appareil.
    private int _marche = 1;

    // Résolue une fois : l'inversion de sens doit lui être répercutée.
    private NavetteController _navette;

    private void Start()
    {
        _navette = GetComponent<NavetteController>();

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
        _voieQuittee = null;

        for (int i = 0; i < wagons.Length; i++)
        {
            if (wagons[i] != null)
                wagons[i].SetTrack(trackSystem);
        }
    }


    /// <summary>
    /// Où se trouve la caisse d'indice i : sur quelle voie, à quelle distance,
    /// et dans quelle orientation.
    ///
    /// Le raisonnement se fait en une seule grandeur : le recul de la caisse
    /// derrière la tête. Tant que ce recul est inférieur au chemin parcouru
    /// depuis l'appareil, la caisse est sur la voie courante. Au-delà, elle
    /// est encore sur la voie quittée, du surplus exact.
    ///
    /// Rien n'est accumulé : le convoi ne peut donc ni dériver, ni se
    /// disloquer, et il revient de lui-même sur ses pas si la marche
    /// s'inverse au milieu d'un franchissement.
    /// </summary>
    /// <summary>
    /// Place de la caisse i dans le convoi, comptée depuis l'extrémité de plus
    /// grande distance. Le convoi occupe toujours [distanceTrain -
    /// LongueurConvoi ; distanceTrain] : c'est ce qui permet aux butées de
    /// quai de garantir que TOUTES les caisses restent sur la voie.
    ///
    /// L'ordre s'inverse en passant sur un tracé opposé — la caisse de tête
    /// se retrouve du côté des petites distances — exactement comme
    /// l'orientation. C'est la même bascule, elle est donc lue au même endroit.
    /// </summary>
    private float PlaceDansConvoi(int i)
    {
        float depuisTete = i * distanceEntreWagons;

        return _orientation > 0 ? depuisTete : LongueurConvoi - depuisTete;
    }


    private void SituerCaisse(int i, out TrackSystem voie, out float distance, out int orientation)
    {
        // Le convoi occupe toujours [distanceTrain - LongueurConvoi ;
        // distanceTrain] sur son tracé. Cet ordre ne dépend PAS du sens de
        // marche : un train qui rebrousse chemin recule, ses caisses ne
        // changent pas de côté. C'est aussi ce qui garantit que les butées de
        // quai suffisent à maintenir tout le convoi sur la voie.
        float place = PlaceDansConvoi(i);
        float surVoie = distanceTrain - place;

        if (_voieQuittee == null || !_voieQuittee.Pret)
        {
            voie = trackSystem;
            distance = surVoie;
            orientation = _orientation;
            return;
        }

        // Chemin parcouru par l'extrémité engagée depuis l'appareil.
        float parcouru =
            (distanceTrain - _reculEngagement - _distanceAiguilleNeuve) * _sensEngagement;

        // Recul de cette caisse derrière l'extrémité engagée.
        float recul = Mathf.Abs(place - _reculEngagement);

        if (recul <= parcouru)
        {
            voie = trackSystem;
            distance = surVoie;
            orientation = _orientation;
            return;
        }

        // La caisse n'a pas encore atteint l'appareil : elle est en arrière du
        // point de bascule, du surplus exact, sur la voie quittée.
        voie = _voieQuittee;
        distance = _distanceAiguille - _sensEngagement * _signeAncienne * (recul - parcouru);
        orientation = _orientation * _signeAncienne;
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

        if (pas > 0f) _marche = 1;
        else if (pas < 0f) _marche = -1;

        FranchirAiguille();
    }


    /// <summary>
    /// Clôt le franchissement dès que la dernière caisse a dépassé l'appareil.
    ///
    /// Rien n'est à basculer ici : SituerCaisse place déjà chaque caisse sur
    /// la voie qui lui revient, selon son recul. Il ne reste qu'à libérer la
    /// voie quittée quand plus personne ne s'y trouve — et à la conserver tant
    /// qu'une caisse y est encore, y compris si le convoi rebrousse chemin au
    /// milieu de l'appareil.
    /// </summary>
    private void FranchirAiguille()
    {
        if (_voieQuittee == null || trackSystem == null || !trackSystem.Pret)
            return;

        float parcouru =
            (distanceTrain - _reculEngagement - _distanceAiguilleNeuve) * _sensEngagement;

        if (parcouru < LongueurConvoi)
            return;

        Debug.Log($"[Train] {name} : convoi entièrement passé sur {trackSystem.name}.", this);

        _voieQuittee = null;

        foreach (WagonController wagon in wagons)
        {
            if (wagon != null)
                wagon.SetTrack(trackSystem);
        }
    }


    private void PositionnerWagons()
    {
        if (wagons == null)
            return;

        for (int i = 0; i < wagons.Length; i++)
        {
            if (wagons[i] == null)
                continue;

            SituerCaisse(i, out TrackSystem voie, out float distance, out int orientation);

            if (voie == null || !voie.Pret)
                continue;

            wagons[i].SetTrack(voie);
            wagons[i].Move(distance, orientation);
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

        // C'est l'extrémité qui MÈNE qui s'engage sur l'appareil, et non le
        // repère de tête : un convoi en marche arrière entre par sa queue.
        float reculAncien = _marche > 0 ? 0f : LongueurConvoi;
        float engageAncien = distanceTrain - reculAncien;

        Vector3 pointBascule = ancienne.GetPosition(engageAncien);
        Vector3 sensAncien = ancienne.GetDirection(engageAncien);

        float dNeuve = nouvelleVoie.ProjeterDistance(pointBascule);
        Vector3 sensNouveau = nouvelleVoie.GetDirection(dNeuve);

        // Une traversée unique est parcourue dans un sens par un convoi et
        // dans l'autre par celui d'en face. Si le nouveau tracé s'oppose à
        // l'ancien, on inverse le sens de marche : le convoi continue alors
        // dans la même direction du monde, sa distance progressant à l'envers
        // sur la nouvelle voie.
        bool opposes = Vector3.Dot(sensAncien, sensNouveau) < 0f;

        _voieQuittee = ancienne;
        _distanceAiguille = engageAncien;
        _signeAncienne = opposes ? -1 : 1;

        _sensEngagement = opposes ? -_marche : _marche;
        _reculEngagement = _sensEngagement > 0 ? 0f : LongueurConvoi;

        trackSystem = nouvelleVoie;
        _distanceAiguilleNeuve = dNeuve;

        // Le repère de tête se déduit de l'extrémité engagée : le convoi
        // conserve ainsi exactement les positions qu'il occupait.
        distanceTrain = dNeuve + _reculEngagement;

        if (opposes)
        {
            InverserSens();
            _marche = -_marche;

            // La caisse ne pivote pas pour autant : elle garde son orientation
            // dans le monde, et c'est sa référence au tracé qui se retourne —
            // ce qui retourne aussi l'ordre des caisses le long du tracé.
            _orientation = -_orientation;

            Debug.Log($"[Train] {name} : tracé opposé, sens de marche inversé.", this);
        }

        Debug.Log($"[Train] {name} : convoi engagé sur {nouvelleVoie.name} " +
                  $"depuis {ancienne.name}.", this);
    }


    // ==========================================================
    // SENS
    // ==========================================================

    /// <summary>Permute avant et arrière, en laissant le point mort inchangé.</summary>
    private void InverserSens()
    {
        if (sens == SensTrain.Avant)
            sens = SensTrain.Arriere;
        else if (sens == SensTrain.Arriere)
            sens = SensTrain.Avant;

        // La navette raisonne en extrémité visée, pas en signe de progression :
        // il faut lui retourner son objectif, sinon elle roulerait vers la
        // butée opposée à celle qu'elle croit surveiller.
        if (_navette != null)
            _navette.InverserObjectif();
    }


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
