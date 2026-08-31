using UnityEngine;


/// <summary>
/// Frontière unique entre la simulation et le monde extérieur, dans les deux
/// sens : elle applique les ordres reçus, et collecte l'état à remonter.
///
/// Le transport n'a donc à connaître ni TrainController, ni NavetteController,
/// ni la convention d'unités. Il traduit sa trame en CommandeTrain, appelle, et
/// récupère un EtatTrainMesure.
///
/// Différence de fond avec la version précédente : la simulation N'ATTEND PLUS
/// d'ordres pour fonctionner. Les navettes roulent seules ; l'automate ne fait
/// qu'intervenir. Sans liaison, le procédé continue sur ses valeurs nominales —
/// couper le réseau perturbe la supervision, pas l'installation.
/// </summary>
[DefaultExecutionOrder(-20)]
public class PosteDeCommande : MonoBehaviour
{
    public static PosteDeCommande Instance { get; private set; }


    [Header("Convois commandés")]
    [Tooltip("L'ordre compte : l'indice 0 correspond au train 1 du programme ST.")]
    public TrainController[] trains;


    [Header("Aiguillages commandés")]
    [Tooltip("Indice 0 = AIG1 du programme ST.")]
    public AiguillageController[] aiguillages;


    [Header("Signaux commandés")]
    [Tooltip("Indice 0 = SIG1 du programme ST.")]
    public SignalController[] signaux;


    [Header("Cantonnement")]
    [Tooltip("Nombre de sections découpant chaque voie. Sert à remonter " +
             "l'occupation, matière première d'un enclenchement.")]
    [Min(1)]
    public int nombreCantons = 8;


    [Header("Supervision")]
    [Tooltip("Secondes sans ordre avant de considérer l'automate absent. Les " +
             "navettes ne s'arrêtent pas pour autant : elles reprennent leurs " +
             "valeurs nominales.")]
    public float delaiSansOrdre = 3f;


    [Header("Voie commune")]
    [Tooltip("Distance en deçà de laquelle une caisse est considérée posée " +
             "sur une voie, en mètres.")]
    public float gabaritVoie = 4f;

    [Tooltip("Secondes entre deux évaluations. Le test parcourt les tracés " +
             "échantillonnés : inutile de le refaire à chaque image.")]
    public float periodeControleVoie = 0.25f;


    [Header("Diagnostic (lecture seule)")]
    [Tooltip("Vrai lorsque deux convois circulent sur le même rail. PURE " +
             "INFORMATION : rien n'est arrêté, ralenti ni refusé. Remonte à " +
             "l'automate par le bit 3 des alarmes.")]
    public bool deuxTrainsMemeVoie;

    public bool automatePresent;
    public string sourceCommande = "(aucune)";
    public float secondesDepuisDernierOrdre;


    private float _dernierOrdre;
    private NavetteController[] _navettes;
    private float _prochainControleVoie;


    // ======================================================================
    // CYCLE DE VIE
    // ======================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning(
                $"[Poste] Un second PosteDeCommande existe sur {name}, il est désactivé.", this);
            enabled = false;
            return;
        }

        Instance = this;

        if (trains == null) trains = new TrainController[0];
        if (aiguillages == null) aiguillages = new AiguillageController[0];
        if (signaux == null) signaux = new SignalController[0];

        _navettes = new NavetteController[trains.Length];

        for (int i = 0; i < trains.Length; i++)
        {
            if (trains[i] != null)
                _navettes[i] = trains[i].GetComponent<NavetteController>();
        }

        _dernierOrdre = float.NegativeInfinity;
    }


    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }


    private void FixedUpdate()
    {
        ControlerVoieCommune();

        secondesDepuisDernierOrdre = float.IsNegativeInfinity(_dernierOrdre)
            ? float.PositiveInfinity
            : Time.unscaledTime - _dernierOrdre;

        bool present = secondesDepuisDernierOrdre <= delaiSansOrdre;

        if (present == automatePresent)
            return;

        automatePresent = present;

        if (present)
        {
            Debug.Log("[Poste] Automate présent.", this);
            return;
        }

        // L'automate a disparu : on rend la main aux valeurs nominales plutôt
        // que de freiner. Le procédé doit survivre à la perte de supervision.
        Debug.LogWarning("[Poste] Automate absent — retour aux valeurs nominales.", this);

        for (int i = 0; i < trains.Length; i++)
            AppliquerTrain(i, CommandeTrain.Nominale);
    }


    /// <summary>
    /// Renseigne le témoin de voie commune. Ce n'est qu'un constat : deux
    /// convois sur le même rail ne sont ni arrêtés ni ralentis ici. La suite
    /// appartient à l'automate, ou au choc.
    /// </summary>
    private void ControlerVoieCommune()
    {
        if (Time.unscaledTime < _prochainControleVoie)
            return;

        _prochainControleVoie = Time.unscaledTime + Mathf.Max(0.05f, periodeControleVoie);

        bool commune = false;

        for (int i = 0; i < trains.Length && !commune; i++)
        {
            for (int j = i + 1; j < trains.Length && !commune; j++)
            {
                commune =
                    TrainCollisionDetector.SurLaMemeVoie(trains[i], trains[j], gabaritVoie) ||
                    TrainCollisionDetector.SurLaMemeVoie(trains[j], trains[i], gabaritVoie);
            }
        }

        if (commune != deuxTrainsMemeVoie)
        {
            deuxTrainsMemeVoie = commune;

            Debug.Log(commune
                ? "[Poste] Deux convois sur la même voie."
                : "[Poste] Les convois ont retrouvé des voies distinctes.", this);
        }
    }


    // ======================================================================
    // ORDRES REÇUS
    // ======================================================================

    public void DeclarerSource(string nom)
    {
        sourceCommande = string.IsNullOrEmpty(nom) ? "(aucune)" : nom;
        Debug.Log($"[Poste] Source de commande : {sourceCommande}", this);
    }


    /// <summary>À appeler à chaque trame reçue : c'est ce qui atteste la présence.</summary>
    public void SignalerVie()
    {
        _dernierOrdre = Time.unscaledTime;
    }


    /// <summary>Applique les leviers de l'automate à une navette.</summary>
    public void AppliquerTrain(int index, CommandeTrain commande)
    {
        if (index < 0 || index >= trains.Length)
            return;

        NavetteController navette = _navettes[index];

        if (navette == null)
            return;

        navette.consigneVitesseKmh = commande.consigneVitesseKmh;
        navette.arretUrgence = commande.arretUrgence;
        navette.autorisee = commande.autorisee;
    }


    public void CommanderAiguille(int index, bool deviation)
    {
        if (index < 0 || index >= aiguillages.Length || aiguillages[index] == null)
            return;

        // Pas de détection de front ici : CommanderDeviation ignore d'elle-même
        // une commande déjà satisfaite ou une manœuvre en cours.
        aiguillages[index].CommanderDeviation(deviation);
    }


    public void CommanderSignal(int index, AspectSignal aspect)
    {
        if (index < 0 || index >= signaux.Length || signaux[index] == null)
            return;

        signaux[index].DefinirAspect(aspect);
    }


    // ======================================================================
    // ÉTAT REMONTÉ
    // ======================================================================

    /// <summary>Mesures d'un convoi, telles que l'automate les recevra.</summary>
    public EtatTrainMesure MesurerTrain(int index)
    {
        EtatTrainMesure mesure = new EtatTrainMesure();

        if (index < 0 || index >= trains.Length || trains[index] == null)
            return mesure;

        TrainController train = trains[index];

        mesure.positionDecimetres = Mathf.RoundToInt(train.distanceTrain * 10f);
        mesure.vitesseKmhDix = Mathf.RoundToInt(train.VitesseKmh * 10f);
        mesure.canton = CantonDe(train);

        NavetteController navette = _navettes[index];

        if (train.etat == TrainController.EtatTrain.Bloque ||
            train.etat == TrainController.EtatTrain.Impact)
            mesure.etat = EtatTrainMesure.ETAT_ACCIDENTE;
        else if (Deraille(train))
            mesure.etat = EtatTrainMesure.ETAT_DERAILLE;
        else if (navette != null && navette.AQuaiMaintenant)
            mesure.etat = EtatTrainMesure.ETAT_A_QUAI;
        else
            mesure.etat = EtatTrainMesure.ETAT_EN_LIGNE;

        return mesure;
    }


    /// <summary>Position réelle d'une aiguille : 0 principale, 1 déviation, 2 en manœuvre.</summary>
    public int ControleAiguille(int index)
    {
        if (index < 0 || index >= aiguillages.Length || aiguillages[index] == null)
            return 0;

        return (int)aiguillages[index].controle;
    }


    /// <summary>
    /// Occupation des cantons, un bit par section. C'est la donnée dont un
    /// enclenchement a besoin pour refuser une manœuvre sous circulation.
    /// </summary>
    public int OccupationCantons()
    {
        int masque = 0;

        for (int i = 0; i < trains.Length; i++)
        {
            int canton = CantonDe(trains[i]);

            if (canton >= 1 && canton <= 16)
                masque |= 1 << (canton - 1);
        }

        return masque;
    }


    /// <summary>
    /// Alarmes : bit 0 accident, bit 1 déraillement, bit 2 automate absent,
    /// bit 3 deux convois sur la même voie.
    /// </summary>
    public int Alarmes()
    {
        int masque = 0;

        foreach (TrainController train in trains)
        {
            if (train == null)
                continue;

            if (train.etat == TrainController.EtatTrain.Bloque ||
                train.etat == TrainController.EtatTrain.Impact)
                masque |= 1;

            if (Deraille(train))
                masque |= 2;
        }

        if (!automatePresent)
            masque |= 4;

        // Bit 3 : deux convois sur la même voie. Information, pas verrou.
        if (deuxTrainsMemeVoie)
            masque |= 8;

        return masque;
    }


    // ======================================================================
    // OUTILS
    // ======================================================================

    private int CantonDe(TrainController train)
    {
        if (train == null || train.trackSystem == null || !train.trackSystem.Pret)
            return 0;

        float longueur = train.trackSystem.Longueur;

        if (longueur <= 0f)
            return 0;

        int canton = Mathf.FloorToInt(train.distanceTrain / longueur * nombreCantons) + 1;

        return Mathf.Clamp(canton, 1, nombreCantons);
    }


    private static bool Deraille(TrainController train)
    {
        if (train == null)
            return false;

        TrainDerailmentController derail = train.GetComponent<TrainDerailmentController>();

        return derail != null && derail.deraille;
    }
}
