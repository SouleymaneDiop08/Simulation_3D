using UnityEngine;
using UnityEngine.InputSystem;


/// <summary>
/// Bascule entre les points de vue avec la touche Tab.
///
///   vue 0 : observation libre — on vole dans la scène (voir CameraLibre)
///   vue 1 : poursuite du train 1
///   vue 2 : poursuite du train 2
///
/// Au démarrage, la caméra d'observation est placée par le calcul, de façon à
/// englober les convois. La position enregistrée dans la scène est ignorée :
/// elle plaçait la caméra à un mètre du sol, face au vide.
/// </summary>
public class GestionnaireCameras : MonoBehaviour
{
    [Header("Observation libre")]
    [Tooltip("Laisser vide pour prendre la caméra taguée MainCamera.")]
    public Camera cameraGenerale;

    [Tooltip("Recadrer sur les convois au démarrage.")]
    public bool cadrerAuDemarrage = true;

    [Tooltip("Recul de la caméra derrière l'extrémité de la ligne, en fraction " +
             "de sa longueur. Augmenter pour prendre du champ.")]
    public float reculCadrage = 0.65f;

    [Tooltip("Altitude de la caméra, en fraction de la longueur de la ligne. " +
             "Augmenter pour une vue plus plongeante.")]
    public float hauteurCadrage = 0.16f;


    [Header("Convois à suivre")]
    public TrainController[] trains;


    [Header("Caméras de poursuite")]
    [Tooltip("Laisser vide : une caméra sera créée par convoi au démarrage.")]
    public CameraSuiviTrain[] camerasSuivi;

    [Tooltip("Décalage appliqué aux caméras créées automatiquement.")]
    public Vector3 decalageSuivi = new Vector3(6f, 7f, -22f);


    [Header("Commande")]
    public Key toucheBascule = Key.Tab;


    [Header("Diagnostic (lecture seule)")]
    public int vueActuelle;
    public string nomVueActuelle = "";


    private Camera[] _cameras;
    private CameraLibre _libre;


    private void Awake()
    {
        if (cameraGenerale == null)
            cameraGenerale = Camera.main;

        if (cameraGenerale == null)
        {
            Debug.LogError("[Caméras] Aucune caméra générale : composant désactivé.", this);
            enabled = false;
            return;
        }

        if (trains == null)
            trains = new TrainController[0];

        // La navigation libre est ajoutée si elle n'a pas été posée à la main
        _libre = cameraGenerale.GetComponent<CameraLibre>();

        if (_libre == null)
            _libre = cameraGenerale.gameObject.AddComponent<CameraLibre>();


        ConstruireCamerasSuivi();

        _cameras = new Camera[1 + camerasSuivi.Length];
        _cameras[0] = cameraGenerale;

        for (int i = 0; i < camerasSuivi.Length; i++)
            _cameras[i + 1] = camerasSuivi[i] != null
                ? camerasSuivi[i].GetComponent<Camera>()
                : null;
    }


    private void Start()
    {
        // En Start, et non en Awake : les voies doivent avoir construit leur
        // table d'échantillons, et les convois posé leurs wagons.
        if (cadrerAuDemarrage)
            CadrerSurLaLigne();

        ActiverVue(0);
    }


    // ======================================================================
    // CADRAGE
    // ======================================================================

    /// <summary>Volume englobant les wagons de tous les convois.</summary>
    private bool CalculerBornesConvois(out Bounds bornes)
    {
        bornes = new Bounds();
        bool commence = false;

        foreach (TrainController train in trains)
        {
            if (train == null || train.wagons == null)
                continue;

            foreach (WagonController wagon in train.wagons)
            {
                if (wagon == null)
                    continue;

                foreach (Renderer rendu in wagon.GetComponentsInChildren<Renderer>())
                {
                    if (!commence)
                    {
                        bornes = rendu.bounds;
                        commence = true;
                    }
                    else
                    {
                        bornes.Encapsulate(rendu.bounds);
                    }
                }
            }
        }

        return commence;
    }


    /// <summary>
    /// Place la caméra dans l'axe de la ligne, de façon à embrasser tout le
    /// tracé — donc les deux gares, qui en occupent les extrémités.
    ///
    /// Les gares sont noyées dans le mesh d'environnement et ne peuvent pas
    /// être retrouvées par leur nom ; les voies, elles, sont connues, et elles
    /// vont d'une gare à l'autre. Cadrer la ligne revient donc à cadrer les
    /// deux gares.
    /// </summary>
    private void CadrerSurLaLigne()
    {
        Bounds bornes = new Bounds();
        bool commence = false;

        Vector3 axe = Vector3.zero;
        float plusLongue = 0f;

        // Surcharge sans FindObjectsSortMode : celle qui prend le mode de tri
        // est dépréciée depuis Unity 6.
        foreach (TrackSystem voie in FindObjectsByType<TrackSystem>())
        {
            if (voie == null || !voie.Pret)
                continue;

            if (!commence)
            {
                bornes = voie.Bornes;
                commence = true;
            }
            else
            {
                bornes.Encapsulate(voie.Bornes);
            }

            // L'axe est donné par la voie la plus longue : c'est elle qui
            // porte la direction générale de la ligne.
            if (voie.Longueur > plusLongue)
            {
                plusLongue = voie.Longueur;
                axe = voie.PointFin - voie.PointDebut;
            }
        }

        // Les convois font partie du sujet, même s'ils sont hors du tracé
        if (CalculerBornesConvois(out Bounds convois))
        {
            if (!commence) { bornes = convois; commence = true; }
            else bornes.Encapsulate(convois);
        }

        if (!commence)
        {
            Debug.LogWarning("[Caméras] Rien à cadrer : ni voie ni convoi trouvés.", this);
            return;
        }

        _libre.CadrerLigne(bornes, axe, reculCadrage, hauteurCadrage);

        Debug.Log($"[Caméras] Ligne cadrée : {bornes.size.magnitude:0} m de diagonale, " +
                  $"centre {bornes.center}", this);
    }


    // ======================================================================
    // CAMÉRAS DE POURSUITE
    // ======================================================================

    private void ConstruireCamerasSuivi()
    {
        if (camerasSuivi != null && camerasSuivi.Length > 0)
            return;

        camerasSuivi = new CameraSuiviTrain[trains.Length];

        for (int i = 0; i < trains.Length; i++)
        {
            if (trains[i] == null)
                continue;

            // Volontairement non parentées : ce composant vit sur la caméra
            // générale, et une caméra de poursuite fille suivrait ses
            // désactivations.
            GameObject objet = new GameObject($"Camera Suivi — {trains[i].name}");

            Camera camera = objet.AddComponent<Camera>();
            camera.clearFlags = cameraGenerale.clearFlags;
            camera.backgroundColor = cameraGenerale.backgroundColor;
            camera.cullingMask = cameraGenerale.cullingMask;
            camera.fieldOfView = cameraGenerale.fieldOfView;
            camera.nearClipPlane = cameraGenerale.nearClipPlane;
            camera.farClipPlane = Mathf.Max(cameraGenerale.farClipPlane, 3000f);

            // Pas d'AudioListener ici : Unity n'en accepte qu'un par scène,
            // et la caméra générale porte déjà le sien.

            CameraSuiviTrain suivi = objet.AddComponent<CameraSuiviTrain>();
            suivi.train = trains[i];
            suivi.decalage = decalageSuivi;
            suivi.Recaler();

            camerasSuivi[i] = suivi;
        }
    }


    // ======================================================================
    // BASCULE
    // ======================================================================

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current[toucheBascule].wasPressedThisFrame)
            VueSuivante();
    }


    public void VueSuivante()
    {
        if (_cameras == null || _cameras.Length == 0)
            return;

        for (int essai = 1; essai <= _cameras.Length; essai++)
        {
            int candidat = (vueActuelle + essai) % _cameras.Length;

            if (_cameras[candidat] != null)
            {
                ActiverVue(candidat);
                return;
            }
        }
    }


    public void ActiverVue(int indice)
    {
        if (_cameras == null || indice < 0 || indice >= _cameras.Length)
            return;

        vueActuelle = indice;

        for (int i = 0; i < _cameras.Length; i++)
        {
            if (_cameras[i] != null)
                _cameras[i].enabled = (i == indice);
        }

        // La navigation ne doit répondre au clavier que sur sa propre vue,
        // sinon ZQSD ferait dériver la caméra libre pendant une poursuite.
        if (_libre != null)
            _libre.enabled = (indice == 0);

        nomVueActuelle = indice == 0
            ? "Observation libre"
            : $"Suivi — {(indice - 1 < trains.Length && trains[indice - 1] != null ? trains[indice - 1].name : "?")}";

        if (indice == 0)
            Debug.Log("[Caméras] Observation libre — ZQSD/flèches, bouton droit pour regarder, " +
                      "molette pour la vitesse, F pour recadrer, Tab pour changer de vue", this);
        else
            Debug.Log($"[Caméras] {nomVueActuelle}  (Tab pour changer)", this);
    }
}
