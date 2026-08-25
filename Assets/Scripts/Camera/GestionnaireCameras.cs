using UnityEngine;
using UnityEngine.InputSystem;


/// <summary>
/// Bascule entre les points de vue avec la touche Tab.
///
///   vue 0 : vision générale (caméra fixe existante)
///   vue 1 : poursuite du train 1
///   vue 2 : poursuite du train 2
///
/// Les caméras de poursuite sont créées au démarrage, une par convoi, si elles
/// ne sont pas fournies. Il suffit donc de poser ce composant et de renseigner
/// la caméra générale et les convois.
/// </summary>
public class GestionnaireCameras : MonoBehaviour
{
    [Header("Vision générale")]
    [Tooltip("Laisser vide pour prendre la caméra taguée MainCamera.")]
    public Camera cameraGenerale;


    [Header("Convois à suivre")]
    public TrainController[] trains;


    [Header("Caméras de poursuite")]
    [Tooltip("Laisser vide : une caméra sera créée par convoi au démarrage.")]
    public CameraSuiviTrain[] camerasSuivi;

    [Tooltip("Décalage appliqué aux caméras créées automatiquement.")]
    public Vector3 decalageSuivi = new Vector3(0f, 12f, -25f);


    [Header("Commande")]
    public Key toucheBascule = Key.Tab;


    [Header("Diagnostic (lecture seule)")]
    public int vueActuelle;
    public string nomVueActuelle = "";


    private Camera[] _cameras;


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

        ConstruireCamerasSuivi();

        // Indice 0 = générale, puis une vue par convoi
        _cameras = new Camera[1 + camerasSuivi.Length];
        _cameras[0] = cameraGenerale;

        for (int i = 0; i < camerasSuivi.Length; i++)
            _cameras[i + 1] = camerasSuivi[i] != null
                ? camerasSuivi[i].GetComponent<Camera>()
                : null;

        ActiverVue(0);
    }


    /// <summary>
    /// Crée une caméra de poursuite par convoi, en recopiant les réglages
    /// optiques de la caméra générale pour que la bascule ne change pas
    /// l'aspect de l'image.
    /// </summary>
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
            camera.farClipPlane = cameraGenerale.farClipPlane;

            // Pas d'AudioListener ici : Unity n'en accepte qu'un par scène,
            // et la caméra générale porte déjà le sien.

            CameraSuiviTrain suivi = objet.AddComponent<CameraSuiviTrain>();
            suivi.train = trains[i];
            suivi.decalage = decalageSuivi;
            suivi.Recaler();

            camerasSuivi[i] = suivi;
        }
    }


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

        // Saute les vues dont la caméra manque, sans jamais boucler
        // indéfiniment si toutes sont absentes.
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

        // Les caméras de poursuite restent actives même masquées : elles
        // continuent de suivre leur convoi, ce qui évite un saut d'image
        // au retour sur cette vue.

        nomVueActuelle = indice == 0
            ? "Vision générale"
            : $"Suivi — {(trains != null && indice - 1 < trains.Length && trains[indice - 1] != null ? trains[indice - 1].name : "?")}";

        Debug.Log($"[Caméras] {nomVueActuelle}  (Tab pour changer)", this);
    }
}
