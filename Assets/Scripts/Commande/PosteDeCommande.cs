using UnityEngine;


/// <summary>
/// Point d'entrée unique des ordres extérieurs dans la simulation.
///
/// Toute source de commande — aujourd'hui l'automate OpenPLC via la passerelle,
/// demain autre chose — passe par ici et par rien d'autre. Le transport n'a
/// donc à connaître ni TrainController, ni TrainPhysicsController, ni la
/// convention d'unités : il traduit sa trame en CommandeTrain et appelle.
///
/// Ce qui vit ici, et pas dans le transport :
///   - la conversion km/h → m/s ;
///   - la détection de front, pour ne pas rappeler les commandes à 50 Hz ;
///   - le chien de garde et le repli en sécurité.
///
/// Ainsi le repli protège quel que soit le transport, y compris si celui-ci
/// plante : c'est PosteDeCommande qui compte le temps écoulé, pas la liaison.
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


    [Header("Sécurité")]
    [Tooltip("Secondes sans ordre reçu avant serrage automatique des freins.")]
    public float delaiSansOrdre = 2f;


    [Header("Diagnostic (lecture seule)")]
    public bool liaisonActive;
    public string sourceCommande = "(aucune)";
    public float secondesDepuisDernierOrdre;
    public string dernierMotifSecurite = "";


    private float _dernierOrdre;
    private bool _replieEnSecurite;

    private CommandeTrain[] _precedentes;
    private bool[] _aiguillesPrecedentes;
    private bool[] _aiguillesConnues;
    private bool _premiereApplication = true;


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

        _precedentes = new CommandeTrain[trains.Length];
        _aiguillesPrecedentes = new bool[aiguillages.Length];
        _aiguillesConnues = new bool[aiguillages.Length];

        // Aucun ordre n'a encore été reçu : le chien de garde part expiré.
        _dernierOrdre = float.NegativeInfinity;
    }


    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }


    private void Start()
    {
        // Tant qu'aucune source ne s'est manifestée, les convois restent
        // freinés. Un train immobile au démarrage n'est pas un défaut : c'est
        // l'absence d'automate.
        ReplierEnSecurite("aucune source de commande");
    }


    private void FixedUpdate()
    {
        secondesDepuisDernierOrdre = float.IsNegativeInfinity(_dernierOrdre)
            ? float.PositiveInfinity
            : Time.unscaledTime - _dernierOrdre;

        if (secondesDepuisDernierOrdre > delaiSansOrdre)
        {
            if (liaisonActive)
                Debug.LogWarning($"[Poste] Plus d'ordre depuis {delaiSansOrdre} s.", this);

            liaisonActive = false;
            ReplierEnSecurite("chien de garde : plus d'ordre reçu");
        }
    }


    // ======================================================================
    // INTERFACE POUR LES SOURCES DE COMMANDE
    // ======================================================================

    /// <summary>Annonce la source active. Purement informatif, pour le diagnostic.</summary>
    public void DeclarerSource(string nom)
    {
        sourceCommande = string.IsNullOrEmpty(nom) ? "(aucune)" : nom;
        Debug.Log($"[Poste] Source de commande : {sourceCommande}", this);
    }


    /// <summary>
    /// À appeler à chaque trame reçue, même si aucun ordre n'a changé : c'est
    /// ce qui réarme le chien de garde.
    /// </summary>
    public void SignalerVie()
    {
        _dernierOrdre = Time.unscaledTime;

        if (!liaisonActive)
        {
            liaisonActive = true;
            dernierMotifSecurite = "";

            // Au retour de la liaison, tout est réappliqué : l'état mémorisé
            // pour la détection de front n'est plus fiable.
            _premiereApplication = true;
            _replieEnSecurite = false;

            Debug.Log("[Poste] Liaison rétablie.", this);
        }
    }


    /// <summary>Applique un jeu d'ordres à un convoi.</summary>
    public void AppliquerTrain(int index, CommandeTrain commande)
    {
        if (index < 0 || index >= trains.Length)
            return;

        TrainController train = trains[index];

        if (train == null || train.physics == null)
            return;

        // ---- Sens ----
        if (commande.sensAvant && !commande.sensArriere)
            train.sens = TrainController.SensTrain.Avant;
        else if (commande.sensArriere && !commande.sensAvant)
            train.sens = TrainController.SensTrain.Arriere;
        else
            train.sens = TrainController.SensTrain.Neutre;

        // ---- Vitesse autorisée : km/h reçus → m/s internes ----
        train.vitesseAutorisee = commande.vitesseLimiteKmh / TrainController.MS_VERS_KMH;

        // ---- Freins et traction, sur changement uniquement ----
        CommandeTrain precedente = _precedentes[index];

        bool changement = _premiereApplication
                          || commande.freinUrgence != precedente.freinUrgence
                          || commande.freinService != precedente.freinService
                          || commande.tractionPourMille != precedente.tractionPourMille
                          || commande.sensAvant != precedente.sensAvant
                          || commande.sensArriere != precedente.sensArriere;

        if (changement)
        {
            if (commande.freinUrgence)
            {
                train.physics.FreinUrgence();
            }
            else if (commande.freinService)
            {
                train.physics.FreinService();
            }
            else
            {
                // Le desserrage est explicite : ChangerTraction ne touche pas
                // à l'état de frein, pour qu'une consigne de traction ne puisse
                // pas annuler un freinage d'urgence par inadvertance.
                train.physics.RelacherFrein();
                train.physics.ChangerTraction(commande.TractionEffective);
            }
        }

        _precedentes[index] = commande;
    }


    public void CommanderAiguille(int index, bool deviation)
    {
        if (index < 0 || index >= aiguillages.Length)
            return;

        AiguillageController aiguillage = aiguillages[index];

        if (aiguillage == null)
            return;

        if (_aiguillesConnues[index] && deviation == _aiguillesPrecedentes[index])
            return;

        if (deviation)
            aiguillage.ActiverDeviation();
        else
            aiguillage.ActiverPrincipale();

        _aiguillesPrecedentes[index] = deviation;
        _aiguillesConnues[index] = true;
    }


    public void CommanderSignal(int index, AspectSignal aspect)
    {
        if (index < 0 || index >= signaux.Length)
            return;

        if (signaux[index] != null)
            signaux[index].DefinirAspect(aspect);
    }


    /// <summary>Fin d'une trame : valide l'application et lève le drapeau de première passe.</summary>
    public void TerminerTrame()
    {
        _premiereApplication = false;
    }


    // ======================================================================
    // REPLI EN SÉCURITÉ
    // ======================================================================

    /// <summary>
    /// Serre les freins de tous les convois et ferme tous les signaux.
    /// Appelable par une source qui détecte elle-même une anomalie.
    /// </summary>
    public void ReplierEnSecurite(string motif)
    {
        if (_replieEnSecurite)
            return;

        _replieEnSecurite = true;
        dernierMotifSecurite = motif;

        Debug.LogWarning($"[Poste] REPLI EN SÉCURITÉ — {motif}", this);

        for (int i = 0; i < trains.Length; i++)
        {
            if (trains[i] != null && trains[i].physics != null)
                trains[i].physics.FreinUrgence();

            if (_precedentes != null && i < _precedentes.Length)
                _precedentes[i] = CommandeTrain.Securite;
        }

        for (int i = 0; i < signaux.Length; i++)
        {
            if (signaux[i] != null)
                signaux[i].DefinirAspect(AspectSignal.Carre);
        }

        // La liaison retrouvée devra tout réappliquer
        _premiereApplication = true;
    }


    // ======================================================================
    // ESSAIS
    // Permet de vérifier le câblage de l'inspecteur sans automate ni
    // passerelle. Clic droit sur le composant → menu contextuel.
    // ======================================================================

    [ContextMenu("Essai — traction 50 % sur tous les convois")]
    private void EssaiTraction()
    {
        SignalerVie();

        CommandeTrain c = new CommandeTrain
        {
            tractionPourMille = 500,
            sensAvant = true,
            vitesseLimiteKmh = 70
        };

        for (int i = 0; i < trains.Length; i++)
            AppliquerTrain(i, c);

        TerminerTrame();
    }


    [ContextMenu("Essai — frein d'urgence")]
    private void EssaiFreinUrgence()
    {
        ReplierEnSecurite("essai manuel");
    }
}
