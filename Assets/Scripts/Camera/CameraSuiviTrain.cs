using UnityEngine;
using UnityEngine.InputSystem;


/// <summary>
/// Caméra de poursuite : reste derrière et au-dessus d'un convoi, en douceur,
/// et pivote autour de lui à la demande.
///
/// L'orbite n'est pas un agrément : sur une navette, le convoi repart en sens
/// inverse et la caméra se retrouve devant lui. Pouvoir tourner de 180° évite
/// de subir une vue de face pendant tout le trajet retour.
///
///   Flèches gauche/droite   pivoter autour du convoi
///   Flèches haut/bas        monter, descendre
///   Bouton droit maintenu   idem à la souris
///   Molette                 rapprocher, éloigner
///   R                       revenir au cadrage d'origine
///
/// Travaille en LateUpdate : TrainController place ses wagons dans Update, la
/// caméra doit donc se recaler après, sinon elle suit la position de l'image
/// précédente et le convoi paraît trembler.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraSuiviTrain : MonoBehaviour
{
    [Header("Convoi suivi")]
    public TrainController train;

    [Tooltip("Indice du wagon suivi. 0 = tête du convoi.")]
    public int wagonSuivi = 0;


    [Header("Placement")]
    [Tooltip("Décalage dans le repère du wagon : X latéral, Y hauteur, Z avant/arrière.")]
    public Vector3 decalage = new Vector3(6f, 7f, -22f);

    [Tooltip("Hauteur du point visé au-dessus du wagon, en mètres.")]
    public float hauteurVisee = 3f;


    [Header("Douceur")]
    [Tooltip("Plus la valeur est grande, plus la caméra colle au convoi.")]
    [Min(0.1f)]
    public float amortissementPosition = 4f;

    [Min(0.1f)]
    public float amortissementRotation = 6f;


    [Header("Orbite")]
    [Tooltip("Vitesse de rotation au clavier, en degrés par seconde.")]
    public float vitesseOrbiteClavier = 90f;

    public float sensibiliteSouris = 0.25f;

    [Tooltip("Facteur d'éloignement minimal et maximal appliqué au décalage.")]
    public float zoomMin = 0.4f;
    public float zoomMax = 4f;


    private Transform _cible;
    private Camera _camera;

    private float _lacet;      // rotation autour du convoi
    private float _tangage;    // élévation
    private float _zoom = 1f;


    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }


    private void Update()
    {
        // Ne répondre aux commandes que si cette caméra est la vue active :
        // sans ce garde, les flèches feraient pivoter les trois caméras à la
        // fois, y compris celles qu'on ne regarde pas.
        if (_camera == null || !_camera.enabled)
            return;

        LireCommandes();
    }


    private void LireCommandes()
    {
        Keyboard clavier = Keyboard.current;
        Mouse souris = Mouse.current;

        if (clavier != null)
        {
            float pas = vitesseOrbiteClavier * Time.unscaledDeltaTime;

            if (clavier.leftArrowKey.isPressed) _lacet -= pas;
            if (clavier.rightArrowKey.isPressed) _lacet += pas;
            if (clavier.upArrowKey.isPressed) _tangage += pas;
            if (clavier.downArrowKey.isPressed) _tangage -= pas;

            if (clavier.rKey.wasPressedThisFrame)
                Recentrer();
        }

        if (souris == null)
            return;

        if (souris.rightButton.isPressed)
        {
            Vector2 delta = souris.delta.ReadValue();
            _lacet += delta.x * sensibiliteSouris;
            _tangage -= delta.y * sensibiliteSouris;
        }

        float molette = souris.scroll.ReadValue().y;

        if (Mathf.Abs(molette) > 0.01f)
            _zoom = Mathf.Clamp(_zoom * (molette > 0f ? 1f / 1.15f : 1.15f), zoomMin, zoomMax);

        _tangage = Mathf.Clamp(_tangage, -60f, 80f);
    }


    private void LateUpdate()
    {
        Transform cible = TrouverCible();

        if (cible == null)
            return;

        // L'orbite s'ajoute au décalage, dans le repère du wagon : la caméra
        // reste solidaire du convoi quelle que soit son orientation en courbe.
        Quaternion orbite = Quaternion.Euler(_tangage, _lacet, 0f);

        Vector3 positionVoulue = cible.position + cible.rotation * (orbite * decalage * _zoom);
        Vector3 pointVise = cible.position + Vector3.up * hauteurVisee;

        transform.position = Vector3.Lerp(
            transform.position,
            positionVoulue,
            1f - Mathf.Exp(-amortissementPosition * Time.deltaTime)
        );

        Vector3 direction = pointVise - transform.position;

        if (direction.sqrMagnitude < 1e-4f)
            return;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(direction, Vector3.up),
            1f - Mathf.Exp(-amortissementRotation * Time.deltaTime)
        );
    }


    /// <summary>Annule orbite et zoom.</summary>
    public void Recentrer()
    {
        _lacet = 0f;
        _tangage = 0f;
        _zoom = 1f;
    }


    /// <summary>
    /// Wagon suivi, ou à défaut le convoi lui-même. Le résultat est mémorisé
    /// tant qu'il reste valide : inutile de le rechercher à chaque image.
    /// </summary>
    private Transform TrouverCible()
    {
        if (_cible != null)
            return _cible;

        if (train == null)
            return null;

        if (train.wagons != null && train.wagons.Length > 0)
        {
            int i = Mathf.Clamp(wagonSuivi, 0, train.wagons.Length - 1);

            if (train.wagons[i] != null)
            {
                _cible = train.wagons[i].transform;
                return _cible;
            }
        }

        _cible = train.transform;
        return _cible;
    }


    /// <summary>Place la caméra immédiatement, sans transition.</summary>
    public void Recaler()
    {
        Transform cible = TrouverCible();

        if (cible == null)
            return;

        transform.position = cible.position + cible.rotation * decalage;
        transform.LookAt(cible.position + Vector3.up * hauteurVisee, Vector3.up);
    }
}
