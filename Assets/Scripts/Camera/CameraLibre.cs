using UnityEngine;
using UnityEngine.InputSystem;


/// <summary>
/// Caméra d'observation libre : on vole où l'on veut dans la scène.
///
///   ZQSD / WASD / flèches   avancer, reculer, se déplacer latéralement
///   A / E                   descendre, monter
///   Bouton droit maintenu   regarder autour
///   Molette                 régler la vitesse de déplacement
///   Maj                     accélérer
///   F                       recadrer sur les convois
///
/// Les touches sont désignées par leur POSITION physique : Key.W correspond au
/// Z d'un clavier AZERTY. Le triplet ZQSD tombe donc juste sans configuration.
/// Les flèches restent disponibles dans tous les cas.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraLibre : MonoBehaviour
{
    [Header("Déplacement")]
    [Tooltip("Vitesse de base, en mètres par seconde. Réglable à la molette.")]
    public float vitesse = 60f;

    [Tooltip("Facteur appliqué tant que Maj est enfoncé.")]
    public float multiplicateurRapide = 5f;

    public float vitesseMin = 2f;
    public float vitesseMax = 1500f;


    [Header("Orientation")]
    public float sensibilite = 0.12f;

    [Tooltip("Si vrai, il faut maintenir le bouton droit pour tourner la vue. " +
             "Indispensable en navigateur, où la souris reste libre.")]
    public bool rotationAvecBoutonDroit = true;


    [Header("Cadrage")]
    [Tooltip("Angle de plongée utilisé lors d'un recadrage, en degrés.")]
    public float anglePlongee = 28f;

    [Tooltip("Marge autour du sujet lors d'un recadrage. 1 = au plus juste.")]
    public float margeCadrage = 1.6f;


    private Camera _camera;
    private float _lacet;
    private float _tangage;
    private Bounds _dernierSujet;
    private bool _sujetConnu;


    private void Awake()
    {
        _camera = GetComponent<Camera>();
        LireAnglesActuels();
    }


    private void OnEnable()
    {
        LireAnglesActuels();
    }


    private void LireAnglesActuels()
    {
        Vector3 angles = transform.eulerAngles;

        _lacet = angles.y;

        // eulerAngles renvoie 0..360 ; on repasse en -180..180 pour que le
        // verrouillage du tangage à ±89° se comporte correctement.
        _tangage = angles.x > 180f ? angles.x - 360f : angles.x;
    }


    private void Update()
    {
        Keyboard clavier = Keyboard.current;
        Mouse souris = Mouse.current;

        if (souris != null)
        {
            ReglerVitesse(souris);
            Tourner(souris);
        }

        if (clavier != null)
        {
            Deplacer(clavier);

            if (clavier.fKey.wasPressedThisFrame && _sujetConnu)
                Cadrer(_dernierSujet);
        }
    }


    private void ReglerVitesse(Mouse souris)
    {
        float molette = souris.scroll.ReadValue().y;

        if (Mathf.Abs(molette) < 0.01f)
            return;

        vitesse = Mathf.Clamp(
            vitesse * (molette > 0f ? 1.18f : 1f / 1.18f),
            vitesseMin,
            vitesseMax
        );
    }


    private void Tourner(Mouse souris)
    {
        if (rotationAvecBoutonDroit && !souris.rightButton.isPressed)
            return;

        Vector2 delta = souris.delta.ReadValue();

        if (delta.sqrMagnitude < 1e-6f)
            return;

        _lacet += delta.x * sensibilite;
        _tangage = Mathf.Clamp(_tangage - delta.y * sensibilite, -89f, 89f);

        // Reconstruit l'orientation depuis les deux angles : accumuler des
        // rotations relatives ferait dériver un roulis parasite.
        transform.rotation = Quaternion.Euler(_tangage, _lacet, 0f);
    }


    private void Deplacer(Keyboard clavier)
    {
        Vector3 direction = Vector3.zero;

        if (clavier.wKey.isPressed || clavier.upArrowKey.isPressed) direction += Vector3.forward;
        if (clavier.sKey.isPressed || clavier.downArrowKey.isPressed) direction += Vector3.back;
        if (clavier.aKey.isPressed || clavier.leftArrowKey.isPressed) direction += Vector3.left;
        if (clavier.dKey.isPressed || clavier.rightArrowKey.isPressed) direction += Vector3.right;

        if (clavier.eKey.isPressed || clavier.pageUpKey.isPressed) direction += Vector3.up;
        if (clavier.qKey.isPressed || clavier.pageDownKey.isPressed) direction += Vector3.down;

        if (direction.sqrMagnitude < 1e-6f)
            return;

        float allure = vitesse;

        if (clavier.leftShiftKey.isPressed || clavier.rightShiftKey.isPressed)
            allure *= multiplicateurRapide;

        // Les composantes avant/latérales suivent l'orientation de la caméra,
        // la composante verticale reste dans le repère du monde : sinon
        // « monter » ferait piquer vers le sol dès que la vue plonge.
        Vector3 deplacement =
            transform.right * direction.x +
            transform.forward * direction.z +
            Vector3.up * direction.y;

        transform.position += deplacement.normalized * allure * Time.unscaledDeltaTime;
    }


    /// <summary>
    /// Place la caméra de façon à voir entièrement le volume donné.
    /// La distance est calculée depuis l'angle de champ, elle s'adapte donc
    /// à un convoi comme à l'ensemble du réseau.
    /// </summary>
    public void Cadrer(Bounds sujet)
    {
        if (_camera == null)
            _camera = GetComponent<Camera>();

        _dernierSujet = sujet;
        _sujetConnu = true;

        float rayon = Mathf.Max(sujet.extents.magnitude, 5f);

        float demiAngle = Mathf.Deg2Rad * _camera.fieldOfView * 0.5f;
        float distance = rayon / Mathf.Max(Mathf.Tan(demiAngle), 0.01f) * margeCadrage;

        _tangage = anglePlongee;
        _lacet = transform.eulerAngles.y;

        Quaternion orientation = Quaternion.Euler(_tangage, _lacet, 0f);

        transform.rotation = orientation;
        transform.position = sujet.center - orientation * Vector3.forward * distance;

        // Le plan lointain doit englober la scène, sinon le décor disparaît
        // dès qu'on prend de la hauteur.
        _camera.farClipPlane = Mathf.Max(_camera.farClipPlane, distance * 4f);
    }


    /// <summary>
    /// Cadre une installation allongée — une ligne de chemin de fer — en se
    /// plaçant dans son axe, en retrait et en hauteur.
    ///
    /// Un cadrage sphérique classique placerait la caméra perpendiculairement
    /// au tracé : on verrait la ligne de côté, écrasée. En se plaçant dans
    /// l'axe, la perspective fait converger les voies vers l'horizon et les
    /// deux extrémités tiennent dans l'image.
    /// </summary>
    /// <param name="axe">Direction horizontale de la ligne, normalisée.</param>
    /// <param name="reculRelatif">Recul derrière l'extrémité, en fraction de la longueur.</param>
    /// <param name="hauteurRelative">Altitude de la caméra, en fraction de la longueur.</param>
    public void CadrerLigne(Bounds sujet, Vector3 axe,
                            float reculRelatif = 0.65f,
                            float hauteurRelative = 0.16f)
    {
        _dernierSujet = sujet;
        _sujetConnu = true;

        if (_camera == null)
            _camera = GetComponent<Camera>();

        axe.y = 0f;

        if (axe.sqrMagnitude < 1e-4f)
        {
            Cadrer(sujet);
            return;
        }

        axe.Normalize();

        // Longueur de la ligne mesurée le long de son propre axe
        float longueur = Mathf.Abs(Vector3.Dot(sujet.size, axe)) +
                         Mathf.Abs(Vector3.Dot(sujet.size, Vector3.Cross(axe, Vector3.up))) * 0.25f;

        longueur = Mathf.Max(longueur, 50f);

        Vector3 centre = sujet.center;

        transform.position = centre
                             - axe * (longueur * reculRelatif)
                             + Vector3.up * (longueur * hauteurRelative);

        transform.LookAt(centre, Vector3.up);
        LireAnglesActuels();

        // Sans cela, le fond de la ligne serait tronqué par le plan lointain
        _camera.farClipPlane = Mathf.Max(_camera.farClipPlane, longueur * 3f);
    }


    /// <summary>Mémorise le sujet à recadrer avec F, sans bouger la caméra.</summary>
    public void DefinirSujet(Bounds sujet)
    {
        _dernierSujet = sujet;
        _sujetConnu = true;
    }
}
