using System;
using System.Collections;
using UnityEngine;
using NativeWebSocket;


/// <summary>
/// Transport : reçoit les trames de la passerelle et les remet au
/// PosteDeCommande. Rien d'autre.
///
/// Toute la logique métier — conversion d'unités, détection de front, repli en
/// sécurité — vit dans PosteDeCommande. Ce script ne connaît ni TrainController
/// ni TrainPhysicsController : il traduit du JSON en CommandeTrain et délègue.
/// Changer de transport (HTTP, port série, autre protocole) ne demande donc que
/// de réécrire ce fichier.
///
/// Compatible WebGL : un build navigateur ne peut pas ouvrir de socket TCP,
/// donc pas de Modbus direct. C'est la passerelle Node.js (PLC/bridge) qui
/// interroge OpenPLC et rediffuse l'état ici.
///
/// PRÉREQUIS : le paquet NativeWebSocket doit être installé avant de placer ce
/// fichier dans Assets/ — sinon le projet ne compile pas.
///   Package Manager → + → Add package from git URL →
///   https://github.com/endel/NativeWebSocket.git#upm
/// </summary>
public class PlcLink : MonoBehaviour
{
    // ======================================================================
    // TRAME REÇUE
    // Champs à plat : JsonUtility ne gère ni dictionnaires ni polymorphisme.
    // Les noms doivent correspondre exactement à ceux émis par bridge.js.
    // ======================================================================

    [Serializable]
    private class TramePlc
    {
        public int seq;
        public bool plc;            // false = automate injoignable
        public string err;

        public bool hb;             // heartbeat, bascule à 1 Hz
        public int etape;
        public bool scenario;

        public int t1_traction;     // 0..1000 pour mille
        public bool t1_fs;          // frein de service
        public bool t1_fu;          // frein d'urgence
        public bool t1_av;          // sens avant
        public bool t1_ar;          // sens arrière
        public int t1_vlim;         // km/h

        public int t2_traction;
        public bool t2_fs;
        public bool t2_fu;
        public bool t2_av;
        public bool t2_ar;
        public int t2_vlim;

        public bool aig1;           // true = déviation
        public bool aig2;

        public int sig1;            // 0 carré, 1 avertissement, 2 voie libre
        public int sig2;
    }


    // ======================================================================
    // CONFIGURATION
    // ======================================================================

    [Header("Passerelle")]
    [Tooltip("Laisser VIDE en production : l'adresse est alors déduite de la page " +
             "qui sert le build, donc un seul build fonctionne en local, sur le " +
             "réseau et derrière HTTPS. Ne remplir que pour forcer une adresse.")]
    public string urlPasserelle = "";

    [Tooltip("Secondes sans battement avant de considérer l'automate perdu.")]
    public float delaiChienDeGarde = 2f;

    public float delaiReconnexion = 3f;


    [Header("Poste de commande")]
    [Tooltip("Laisser vide pour utiliser PosteDeCommande.Instance.")]
    public PosteDeCommande poste;


    [Header("Diagnostic (lecture seule)")]
    public bool connecte;
    public int etapeCourante;
    public string derniereErreur = "";


    // ======================================================================
    // ÉTAT INTERNE
    // ======================================================================

    private WebSocket _ws;
    private TramePlc _trame;
    private bool _nouvelleTrame;

    private bool _hbPrecedent;
    private float _hbDernierChangement;
    private bool _battementPerdu;


    // ======================================================================
    // CYCLE DE VIE
    // ======================================================================

    private async void Start()
    {
        if (poste == null)
            poste = PosteDeCommande.Instance;

        if (poste == null)
        {
            Debug.LogError("[PLC] Aucun PosteDeCommande dans la scène : liaison inutile.", this);
            enabled = false;
            return;
        }

        poste.DeclarerSource("OpenPLC (passerelle WebSocket)");

        _hbDernierChangement = Time.unscaledTime;
        await Connecter();
    }


    private void Update()
    {
        // Hors WebGL, NativeWebSocket met les messages en file d'attente et il
        // faut les dépiler soi-même. En WebGL les rappels viennent directement
        // de JavaScript, sur le thread principal.
#if !UNITY_WEBGL || UNITY_EDITOR
        _ws?.DispatchMessageQueue();
#endif
    }


    private void FixedUpdate()
    {
        SurveillerBattement();

        if (!_nouvelleTrame || _trame == null)
            return;

        _nouvelleTrame = false;
        AppliquerTrame(_trame);
    }


    private async void OnDestroy()
    {
        if (_ws == null)
            return;

        try { await _ws.Close(); }
        catch (Exception) { /* socket déjà fermée */ }
    }


    // ======================================================================
    // CONNEXION
    // ======================================================================

    /// <summary>
    /// Détermine l'adresse de la passerelle, par ordre de priorité :
    ///
    ///   1. paramètre d'URL ?plc=...  — permet de rediriger un build déjà
    ///      compilé, sans repasser par Unity ;
    ///   2. le champ de l'inspecteur, s'il est rempli ;
    ///   3. l'origine de la page qui sert le build (WebGL) — même hôte, même
    ///      port, et wss:// automatiquement si la page est en HTTPS ;
    ///   4. localhost, pour l'éditeur.
    ///
    /// Sans le point 3, un build compilé avec « localhost » ne fonctionnerait
    /// que sur la machine qui l'héberge : tout poste distant chercherait la
    /// passerelle sur lui-même.
    /// </summary>
    private string ResoudreUrl()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string page = Application.absoluteURL;

        if (!string.IsNullOrEmpty(page))
        {
            try
            {
                Uri uri = new Uri(page);

                // 1. Redirection explicite par la barre d'adresse
                foreach (string couple in uri.Query.TrimStart('?').Split('&'))
                {
                    if (couple.StartsWith("plc="))
                        return Uri.UnescapeDataString(couple.Substring(4));
                }

                // 2. Champ de l'inspecteur
                if (!string.IsNullOrEmpty(urlPasserelle))
                    return urlPasserelle;

                // 3. Même origine que la page. Une page en HTTPS impose wss://,
                //    sinon le navigateur bloque la connexion.
                string schema = uri.Scheme == "https" ? "wss" : "ws";
                return $"{schema}://{uri.Host}:{uri.Port}";
            }
            catch (Exception e)
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
            Debug.Log("[PLC] Connecté à la passerelle : " + url, this);
        };

        _ws.OnMessage += (octets) =>
        {
            try
            {
                _trame = JsonUtility.FromJson<TramePlc>(
                    System.Text.Encoding.UTF8.GetString(octets));
                _nouvelleTrame = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PLC] Trame illisible : " + e.Message, this);
            }
        };

        _ws.OnError += (msg) =>
        {
            derniereErreur = msg;
            Debug.LogError("[PLC] Erreur WebSocket : " + msg, this);
        };

        _ws.OnClose += (code) =>
        {
            connecte = false;
            Debug.LogWarning("[PLC] Passerelle déconnectée (" + code + ")", this);

            poste.ReplierEnSecurite("liaison passerelle perdue");

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
            Debug.Log("[PLC] Tentative de reconnexion...", this);
            _ = Connecter();
        }
    }


    // ======================================================================
    // CHIEN DE GARDE DU BATTEMENT
    // ======================================================================

    /// <summary>
    /// Le heartbeat vient du programme ST et traverse toute la chaîne. S'il se
    /// fige alors que la liaison est ouverte, c'est que l'automate lui-même
    /// s'est arrêté : la passerelle continuerait à émettre sans que rien ne
    /// change.
    ///
    /// PosteDeCommande a son propre chien de garde sur l'arrivée des trames.
    /// Celui-ci couvre le cas différent d'une trame qui arrive mais qui est
    /// périmée.
    /// </summary>
    private void SurveillerBattement()
    {
        if (!connecte)
            return;

        bool perdu = Time.unscaledTime - _hbDernierChangement > delaiChienDeGarde;

        if (perdu && !_battementPerdu)
            poste.ReplierEnSecurite("automate figé : plus de battement");

        _battementPerdu = perdu;
    }


    // ======================================================================
    // TRADUCTION ET REMISE AU POSTE
    // ======================================================================

    private void AppliquerTrame(TramePlc t)
    {
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
            poste.ReplierEnSecurite("automate injoignable : " + t.err);
            return;
        }

        if (_battementPerdu)
            return;

        poste.SignalerVie();

        poste.AppliquerTrain(0, new CommandeTrain
        {
            tractionPourMille = t.t1_traction,
            freinService = t.t1_fs,
            freinUrgence = t.t1_fu,
            sensAvant = t.t1_av,
            sensArriere = t.t1_ar,
            vitesseLimiteKmh = t.t1_vlim
        });

        poste.AppliquerTrain(1, new CommandeTrain
        {
            tractionPourMille = t.t2_traction,
            freinService = t.t2_fs,
            freinUrgence = t.t2_fu,
            sensAvant = t.t2_av,
            sensArriere = t.t2_ar,
            vitesseLimiteKmh = t.t2_vlim
        });

        poste.CommanderAiguille(0, t.aig1);
        poste.CommanderAiguille(1, t.aig2);

        poste.CommanderSignal(0, SignalController.DepuisEntier(t.sig1));
        poste.CommanderSignal(1, SignalController.DepuisEntier(t.sig2));

        poste.TerminerTrame();
    }
}
