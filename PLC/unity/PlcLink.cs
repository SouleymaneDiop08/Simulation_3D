using System;
using System.Collections;
using UnityEngine;
using NativeWebSocket;


/// <summary>
/// Reception des ordres de l'automate OpenPLC, via la passerelle WebSocket.
///
/// Compatible WebGL : un build navigateur ne peut pas ouvrir de socket TCP,
/// donc pas de Modbus direct. C'est la passerelle Node.js (PLC/bridge) qui
/// interroge OpenPLC et rediffuse l'etat ici en JSON.
///
/// Remplace l'ancien PLCConnection.cs, qui s'appuyait sur websocket-sharp
/// (incompatible WebGL) et pointait sur le port de l'interface web d'OpenPLC.
/// </summary>
public class PlcLink : MonoBehaviour
{
    // ======================================================================
    // TRAME RECUE
    // Champs a plat : JsonUtility ne gere ni dictionnaires ni polymorphisme.
    // Les noms doivent correspondre exactement a ceux emis par bridge.js.
    // ======================================================================

    [Serializable]
    private class TramePlc
    {
        public int seq;
        public bool plc;            // false = automate injoignable
        public string err;

        public bool hb;             // heartbeat, bascule a 1 Hz
        public int etape;
        public bool scenario;

        public int t1_traction;     // 0..1000 pour mille
        public bool t1_fs;          // frein de service
        public bool t1_fu;          // frein d'urgence
        public bool t1_av;          // sens avant
        public bool t1_ar;          // sens arriere
        public int t1_vlim;         // km/h

        public int t2_traction;
        public bool t2_fs;
        public bool t2_fu;
        public bool t2_av;
        public bool t2_ar;
        public int t2_vlim;

        public bool aig1;           // true = deviation
        public bool aig2;

        public int sig1;            // 0 carre, 1 avertissement, 2 voie libre
        public int sig2;
    }


    // ======================================================================
    // CONFIGURATION
    // ======================================================================

    [Header("Passerelle")]
    [Tooltip("Laisser VIDE en production : l'adresse est alors deduite de la page " +
             "qui sert le build, donc un seul build fonctionne en local, sur le " +
             "reseau et derriere HTTPS. Ne remplir que pour forcer une adresse.")]
    public string urlPasserelle = "";

    [Tooltip("Secondes sans battement avant de considerer la liaison perdue.")]
    public float delaiChienDeGarde = 2f;

    public float delaiReconnexion = 3f;


    [Header("Trains")]
    public TrainController train1;
    public TrainController train2;


    [Header("Aiguillages")]
    public AiguillageController aiguillage1;
    public AiguillageController aiguillage2;


    [Header("Diagnostic (lecture seule)")]
    public bool connecte;
    public bool automateVivant;
    public int etapeCourante;
    public string derniereErreur = "";


    // ======================================================================
    // ETAT INTERNE
    // ======================================================================

    private WebSocket _ws;
    private TramePlc _trame;
    private bool _nouvelleTrame;

    private bool _hbPrecedent;
    private float _hbDernierChangement;

    // Memorisation pour n'agir que sur changement : les methodes des
    // controleurs journalisent, les appeler a 50 Hz noierait la console.
    private int _t1TractionPrec = -1;
    private int _t2TractionPrec = -1;
    private bool _t1FsPrec, _t1FuPrec, _t2FsPrec, _t2FuPrec;
    private bool _aig1Prec, _aig2Prec;
    private bool _premiereApplication = true;
    private bool _urgenceAppliquee;


    // ======================================================================
    // CYCLE DE VIE
    // ======================================================================

    private async void Start()
    {
        _hbDernierChangement = Time.unscaledTime;
        await Connecter();
    }


    private void Update()
    {
        // Hors WebGL, NativeWebSocket met les messages en file d'attente et
        // il faut les depiler soi-meme. En WebGL les rappels viennent
        // directement de JavaScript, sur le thread principal.
#if !UNITY_WEBGL || UNITY_EDITOR
        _ws?.DispatchMessageQueue();
#endif
    }


    private void FixedUpdate()
    {
        SurveillerLiaison();

        if (_nouvelleTrame && _trame != null)
        {
            _nouvelleTrame = false;
            AppliquerTrame(_trame);
        }
    }


    private async void OnDestroy()
    {
        if (_ws != null)
        {
            try { await _ws.Close(); }
            catch (Exception) { /* socket deja fermee */ }
        }
    }


    // ======================================================================
    // CONNEXION
    // ======================================================================

    /// <summary>
    /// Determine l'adresse de la passerelle, par ordre de priorite :
    ///
    ///   1. parametre d'URL ?plc=...  — permet de rediriger un build deja
    ///      compile, sans repasser par Unity ;
    ///   2. le champ de l'inspecteur, s'il est rempli ;
    ///   3. l'origine de la page qui sert le build (WebGL) — meme hote, meme
    ///      port, et wss:// automatiquement si la page est en HTTPS ;
    ///   4. localhost, pour l'editeur.
    ///
    /// Sans le point 3, un build compile avec "localhost" ne fonctionnerait que
    /// sur la machine qui l'heberge : tout poste distant chercherait la
    /// passerelle sur lui-meme.
    /// </summary>
    private string ResoudreUrl()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string page = Application.absoluteURL;

        if (!string.IsNullOrEmpty(page))
        {
            try
            {
                System.Uri uri = new System.Uri(page);

                // 1. Redirection explicite par la barre d'adresse
                foreach (string couple in uri.Query.TrimStart('?').Split('&'))
                {
                    if (couple.StartsWith("plc="))
                        return System.Uri.UnescapeDataString(couple.Substring(4));
                }

                // 2. Champ de l'inspecteur
                if (!string.IsNullOrEmpty(urlPasserelle))
                    return urlPasserelle;

                // 3. Meme origine que la page. Une page en HTTPS impose wss://,
                //    sinon le navigateur bloque la connexion.
                string schema = uri.Scheme == "https" ? "wss" : "ws";
                return $"{schema}://{uri.Host}:{uri.Port}";
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PLC] URL de page illisible : " + e.Message);
            }
        }
#endif

        if (!string.IsNullOrEmpty(urlPasserelle))
            return urlPasserelle;

        return "ws://localhost:8081";
    }


    private async System.Threading.Tasks.Task Connecter()
    {
        string url = ResoudreUrl();

        _ws = new WebSocket(url);

        _ws.OnOpen += () =>
        {
            connecte = true;
            derniereErreur = "";
            _hbDernierChangement = Time.unscaledTime;
            Debug.Log("[PLC] Connecte a la passerelle : " + url);
        };

        _ws.OnMessage += (octets) =>
        {
            try
            {
                _trame = JsonUtility.FromJson<TramePlc>(
                    System.Text.Encoding.UTF8.GetString(octets)
                );
                _nouvelleTrame = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PLC] Trame illisible : " + e.Message);
            }
        };

        _ws.OnError += (msg) =>
        {
            derniereErreur = msg;
            Debug.LogError("[PLC] Erreur WebSocket : " + msg);
        };

        _ws.OnClose += (code) =>
        {
            connecte = false;
            Debug.LogWarning("[PLC] Passerelle deconnectee (" + code + ")");
            AppliquerUrgence("liaison passerelle perdue");

            if (isActiveAndEnabled)
                StartCoroutine(Reconnecter());
        };

        await _ws.Connect();
    }


    private IEnumerator Reconnecter()
    {
        yield return new WaitForSecondsRealtime(delaiReconnexion);

        if (!connecte)
        {
            Debug.Log("[PLC] Tentative de reconnexion...");
            _ = Connecter();
        }
    }


    // ======================================================================
    // CHIEN DE GARDE
    // ======================================================================

    /// <summary>
    /// Le heartbeat vient du programme .st et traverse toute la chaine.
    /// S'il se fige, c'est qu'un maillon est tombe : automate arrete,
    /// passerelle coupee ou reseau perdu. Dans tous les cas, on freine.
    /// Sans ce mecanisme, le train reste lance sur le dernier ordre recu.
    /// </summary>
    private void SurveillerLiaison()
    {
        bool vivantAvant = automateVivant;

        if (!connecte)
        {
            automateVivant = false;
        }
        else
        {
            float silence = Time.unscaledTime - _hbDernierChangement;
            automateVivant = silence <= delaiChienDeGarde;
        }

        if (vivantAvant && !automateVivant)
        {
            AppliquerUrgence("chien de garde : plus de battement automate");
        }
    }


    private void AppliquerUrgence(string motif)
    {
        if (_urgenceAppliquee)
            return;

        _urgenceAppliquee = true;

        Debug.LogWarning("[PLC] FREIN D'URGENCE - " + motif);

        if (train1 != null && train1.physics != null) train1.physics.FreinUrgence();
        if (train2 != null && train2.physics != null) train2.physics.FreinUrgence();

        // Force la reapplication complete au retour de la liaison
        _premiereApplication = true;
    }


    // ======================================================================
    // APPLICATION DES ORDRES
    // ======================================================================

    private void AppliquerTrame(TramePlc t)
    {
        // Suivi du battement
        if (t.hb != _hbPrecedent)
        {
            _hbPrecedent = t.hb;
            _hbDernierChangement = Time.unscaledTime;
        }

        etapeCourante = t.etape;

        // La passerelle signale que l'automate est injoignable
        if (!t.plc)
        {
            derniereErreur = t.err;
            AppliquerUrgence("automate injoignable : " + t.err);
            return;
        }

        if (!automateVivant)
            return;

        _urgenceAppliquee = false;

        AppliquerTrain(train1, t.t1_traction, t.t1_fs, t.t1_fu, t.t1_av, t.t1_ar, t.t1_vlim,
                       ref _t1TractionPrec, ref _t1FsPrec, ref _t1FuPrec);

        AppliquerTrain(train2, t.t2_traction, t.t2_fs, t.t2_fu, t.t2_av, t.t2_ar, t.t2_vlim,
                       ref _t2TractionPrec, ref _t2FsPrec, ref _t2FuPrec);

        AppliquerAiguillage(aiguillage1, t.aig1, ref _aig1Prec);
        AppliquerAiguillage(aiguillage2, t.aig2, ref _aig2Prec);

        _premiereApplication = false;
    }


    private void AppliquerTrain(
        TrainController train,
        int traction, bool freinService, bool freinUrgence,
        bool sensAvant, bool sensArriere, int vitesseLimite,
        ref int tractionPrec, ref bool fsPrec, ref bool fuPrec)
    {
        if (train == null || train.physics == null)
            return;

        // ---- Sens ----
        if (sensAvant && !sensArriere)
            train.sens = TrainController.SensTrain.Avant;
        else if (sensArriere && !sensAvant)
            train.sens = TrainController.SensTrain.Arriere;
        else
            train.sens = TrainController.SensTrain.Neutre;

        // ---- Vitesse autorisee ----
        // L'automate emet des km/h, la simulation travaille en m/s.
        train.vitesseAutorisee = vitesseLimite / TrainController.MS_VERS_KMH;

        // ---- Freins et traction ----
        bool changement = _premiereApplication
                          || freinUrgence != fuPrec
                          || freinService != fsPrec
                          || traction != tractionPrec;

        if (changement)
        {
            if (freinUrgence)
            {
                train.physics.FreinUrgence();
            }
            else if (freinService)
            {
                train.physics.FreinService();
            }
            else
            {
                // Le desserrage est explicite : ChangerTraction() ne touche
                // plus a l'etat de frein, pour qu'une consigne de traction ne
                // puisse pas annuler un freinage d'urgence par inadvertance.
                train.physics.RelacherFrein();
                train.physics.ChangerTraction(traction / 1000f);
            }
        }

        fuPrec = freinUrgence;
        fsPrec = freinService;
        tractionPrec = traction;
    }


    private void AppliquerAiguillage(
        AiguillageController aiguillage, bool deviation, ref bool precedent)
    {
        if (aiguillage == null)
            return;

        if (!_premiereApplication && deviation == precedent)
            return;

        if (deviation)
            aiguillage.ActiverDeviation();
        else
            aiguillage.ActiverPrincipale();

        precedent = deviation;
    }
}
