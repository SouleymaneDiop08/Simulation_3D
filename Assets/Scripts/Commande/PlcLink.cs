using System;
using System.Collections;
using UnityEngine;
using NativeWebSocket;


/// <summary>
/// Transport bidirectionnel entre la simulation et l'automate, via la
/// passerelle WebSocket.
///
/// Descend les ordres, remonte les mesures — et rien d'autre. Toute la logique
/// métier vit dans PosteDeCommande : ce script traduit du JSON, dans les deux
/// sens, et délègue. Changer de protocole ne demande que de réécrire ce fichier.
///
/// La perte de liaison n'arrête PAS le procédé. Les navettes reprennent leurs
/// valeurs nominales et continuent de rouler : c'est PosteDeCommande qui en
/// décide, sur son propre compteur, indépendamment de ce transport.
///
/// Compatible WebGL : un build navigateur ne peut pas ouvrir de socket TCP,
/// d'où la passerelle Node.js qui parle Modbus à sa place.
/// </summary>
public class PlcLink : MonoBehaviour
{
    // ======================================================================
    // TRAME REÇUE  (automate -> simulation)
    // Champs à plat : JsonUtility ne gère ni dictionnaires ni polymorphisme.
    // Les noms doivent correspondre exactement à ceux émis par bridge.js.
    // ======================================================================

    [Serializable]
    private class TramePlc
    {
        public int seq;
        public bool plc;
        public string err;

        public bool hb;

        public int t1_vitesse;      // consigne km/h, -1 si non imposee
        public bool t1_au;          // arret d'urgence
        public bool t1_autorise;

        public int t2_vitesse;
        public bool t2_au;
        public bool t2_autorise;

        public bool aig1;           // true = deviation
        public bool aig2;

        public int sig1;            // 0 carre, 1 avertissement, 2 voie libre
        public int sig2;
    }


    // ======================================================================
    // TRAME ÉMISE  (simulation -> automate)
    // ======================================================================

    [Serializable]
    private class TrameSimulation
    {
        public int t1_pos;          // decimetres
        public int t1_vit;          // km/h x10
        public int t1_etat;
        public int t1_canton;

        public int t2_pos;
        public int t2_vit;
        public int t2_etat;
        public int t2_canton;

        public int aig1_ctrl;       // 0 principale, 1 deviation, 2 en manoeuvre
        public int aig2_ctrl;

        public int occupation;      // un bit par canton
        public int alarmes;         // 1 accident, 2 deraillement, 4 automate absent
    }


    // ======================================================================
    // CONFIGURATION
    // ======================================================================

    [Header("Passerelle")]
    [Tooltip("Laisser VIDE en production : l'adresse est déduite de la page qui " +
             "sert le build, donc un seul build fonctionne partout.")]
    public string urlPasserelle = "";

    [Tooltip("Chemin du WebSocket. nginx relaie /ws vers la passerelle.")]
    public string cheminWebSocket = "/ws";

    public float delaiReconnexion = 3f;

    [Tooltip("Période d'émission de l'état vers l'automate, en secondes.")]
    [Min(0.02f)]
    public float periodeEmission = 0.1f;


    [Header("Poste de commande")]
    [Tooltip("Laisser vide pour utiliser PosteDeCommande.Instance.")]
    public PosteDeCommande poste;


    [Header("Diagnostic (lecture seule)")]
    public bool connecte;
    public string derniereErreur = "";


    private WebSocket _ws;
    private TramePlc _trame;
    private bool _nouvelleTrame;
    private float _prochaineEmission;


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
        if (_nouvelleTrame && _trame != null)
        {
            _nouvelleTrame = false;
            AppliquerTrame(_trame);
        }

        if (Time.unscaledTime >= _prochaineEmission)
        {
            _prochaineEmission = Time.unscaledTime + periodeEmission;
            EmettreEtat();
        }
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

    private string ResoudreUrl()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string page = Application.absoluteURL;

        if (!string.IsNullOrEmpty(page))
        {
            try
            {
                Uri uri = new Uri(page);

                foreach (string couple in uri.Query.TrimStart('?').Split('&'))
                {
                    if (couple.StartsWith("plc="))
                        return Uri.UnescapeDataString(couple.Substring(4));
                }

                if (!string.IsNullOrEmpty(urlPasserelle))
                    return urlPasserelle;

                // Une page en HTTPS impose wss://, sinon le navigateur bloque.
                string schema = uri.Scheme == "https" ? "wss" : "ws";
                return $"{schema}://{uri.Host}:{uri.Port}{cheminWebSocket}";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PLC] URL de page illisible : " + e.Message);
            }
        }
#endif

        if (!string.IsNullOrEmpty(urlPasserelle))
            return urlPasserelle;

        return "ws://localhost:8081" + cheminWebSocket;
    }


    private async System.Threading.Tasks.Task Connecter()
    {
        string url = ResoudreUrl();

        _ws = new WebSocket(url);

        _ws.OnOpen += () =>
        {
            connecte = true;
            derniereErreur = "";
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

            // Aucun freinage ici : PosteDeCommande décide seul, sur son propre
            // compteur, de repasser aux valeurs nominales.
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
    // ORDRES REÇUS
    // ======================================================================

    private void AppliquerTrame(TramePlc t)
    {
        // La passerelle signale que l'automate est injoignable : on ne
        // transmet rien, PosteDeCommande retombera de lui-même au nominal.
        if (!t.plc)
        {
            derniereErreur = t.err;
            return;
        }

        poste.SignalerVie();

        poste.AppliquerTrain(0, new CommandeTrain
        {
            consigneVitesseKmh = t.t1_vitesse,
            arretUrgence = t.t1_au,
            autorisee = t.t1_autorise
        });

        poste.AppliquerTrain(1, new CommandeTrain
        {
            consigneVitesseKmh = t.t2_vitesse,
            arretUrgence = t.t2_au,
            autorisee = t.t2_autorise
        });

        poste.CommanderAiguille(0, t.aig1);
        poste.CommanderAiguille(1, t.aig2);

        poste.CommanderSignal(0, SignalController.DepuisEntier(t.sig1));
        poste.CommanderSignal(1, SignalController.DepuisEntier(t.sig2));
    }


    // ======================================================================
    // ÉTAT ÉMIS
    // ======================================================================

    private void EmettreEtat()
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
            return;

        EtatTrainMesure m1 = poste.MesurerTrain(0);
        EtatTrainMesure m2 = poste.MesurerTrain(1);

        TrameSimulation trame = new TrameSimulation
        {
            t1_pos = m1.positionDecimetres,
            t1_vit = m1.vitesseKmhDix,
            t1_etat = m1.etat,
            t1_canton = m1.canton,

            t2_pos = m2.positionDecimetres,
            t2_vit = m2.vitesseKmhDix,
            t2_etat = m2.etat,
            t2_canton = m2.canton,

            aig1_ctrl = poste.ControleAiguille(0),
            aig2_ctrl = poste.ControleAiguille(1),

            occupation = poste.OccupationCantons(),
            alarmes = poste.Alarmes()
        };

        _ws.SendText(JsonUtility.ToJson(trame));
    }
}
