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

    [Tooltip("Marge autour du sujet lors du cadrage initial. Augmenter pour " +
             "embrasser aussi les voies alentour.")]
    public float margeCadrage = 2.5f;


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

        _libre.margeCadrage = margeCadrage;

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
        // En Start, et non en Awake : les convois doivent avoir placé leurs
        // wagons sur la voie, sinon on cadrerait leur position d'édition.
        if (cadrerAuDemarrage && CalculerBornesConvois(out Bounds bornes))
            _libre.Cadrer(bornes);
        else if (CalculerBornesVoies(out Bounds voies))
            _libre.Cadrer(voies);
        else
            Debug.LogWarning("[Caméras] Rien à cadrer : ni convoi ni voie trouvés.", this);

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


    /// <summary>Repli : volume englobant les voies, si aucun convoi n'est visible.</summary>
    private bool CalculerBornesVoies(out Bounds bornes)
    {
        bornes = new Bounds();
        bool commence = false;

        // Surcharge sans FindObjectsSortMode : celle qui prend le mode de tri
        // est dépréciée depuis Unity 6.
        foreach (TrackSystem voie in FindObjectsByType<TrackSystem>())
        {
            if (voie == null || !voie.Pret)
                continue;

            // Trois points suffisent à cerner grossièrement le tracé
            for (int i = 0; i <= 2; i++)
            {
                Vector3 p = voie.GetPosition(voie.Longueur * i / 2f);

                if (!commence)
                {
                    bornes = new Bounds(p, Vector3.one);
                    commence = true;
                }
                else
                {
                    bornes.Encapsulate(p);
                }
            }
        }

        return commence;
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
