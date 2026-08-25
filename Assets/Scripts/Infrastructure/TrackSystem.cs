using UnityEngine;
using UnityEngine.Splines;


/// <summary>
/// Une voie ferrée. Convertit une distance curviligne, exprimée en mètres dans
/// le repère monde, en position et direction sur la spline.
///
/// Unité de référence du projet : le MÈTRE pour les distances, le MÈTRE PAR
/// SECONDE pour les vitesses. Les km/h n'apparaissent qu'aux interfaces
/// (affichage, échange avec l'automate).
/// </summary>
[DisallowMultipleComponent]
public class TrackSystem : MonoBehaviour
{
    [Header("Tracé")]
    public SplineContainer splineContainer;


    [Header("Échantillonnage")]
    [Tooltip("Nombre de points de la table distance → paramètre de spline. " +
             "Plus élevé = plus précis. Le coût n'est payé qu'au démarrage.")]
    [Min(32)]
    public int resolution = 1024;


    /// <summary>Longueur réelle de la voie, en mètres monde.</summary>
    public float Longueur => _longueur;

    /// <summary>Faux tant que la table n'a pas pu être construite.</summary>
    public bool Pret => _pret;

    /// <summary>Volume englobant le tracé, en coordonnées monde.</summary>
    public Bounds Bornes => _bornes;

    /// <summary>Premier et dernier point du tracé, en coordonnées monde.</summary>
    public Vector3 PointDebut => _pret ? _points[0] : transform.position;
    public Vector3 PointFin => _pret ? _points[_points.Length - 1] : transform.position;


    private Vector3[] _points;   // positions monde échantillonnées
    private float[] _cumul;      // distance cumulée à chaque échantillon
    private float _longueur;
    private bool _pret;
    private Bounds _bornes;


    private void Awake()
    {
        Reconstruire();
    }


    /// <summary>
    /// Construit la table de correspondance distance → paramètre de spline.
    ///
    /// EvaluatePosition(t) n'est pas paramétré par longueur d'arc : à pas de t
    /// régulier, les points sont plus resserrés dans les courbes. Sans cette
    /// table, un train à vitesse constante accélérerait et ralentirait tout
    /// seul selon la courbure du tracé.
    ///
    /// La longueur est aussi mesurée ici, et non saisie à la main : une valeur
    /// arbitraire dans l'inspecteur rendrait les « mètres » sans rapport avec
    /// la géométrie réelle.
    /// </summary>
    public void Reconstruire()
    {
        _pret = false;

        if (splineContainer == null || splineContainer.Spline == null)
        {
            Debug.LogError($"[TrackSystem] {name} : SplineContainer manquant.", this);
            return;
        }

        if (splineContainer.Spline.Count < 2)
        {
            Debug.LogError($"[TrackSystem] {name} : la spline a moins de deux points.", this);
            return;
        }

        int n = Mathf.Max(32, resolution);

        _points = new Vector3[n + 1];
        _cumul = new float[n + 1];

        Transform repere = splineContainer.transform;

        _points[0] = repere.TransformPoint(splineContainer.Spline.EvaluatePosition(0f));
        _cumul[0] = 0f;
        _bornes = new Bounds(_points[0], Vector3.zero);

        for (int i = 1; i <= n; i++)
        {
            float t = (float)i / n;

            _points[i] = repere.TransformPoint(splineContainer.Spline.EvaluatePosition(t));
            _cumul[i] = _cumul[i - 1] + Vector3.Distance(_points[i - 1], _points[i]);
            _bornes.Encapsulate(_points[i]);
        }

        _longueur = _cumul[n];

        if (_longueur <= 0.001f)
        {
            Debug.LogError($"[TrackSystem] {name} : longueur nulle.", this);
            return;
        }

        _pret = true;
    }


    // ======================================================================
    // CONVERSIONS
    // ======================================================================

    /// <summary>Distance curviligne → paramètre normalisé de la spline.</summary>
    private float DistanceVersT(float distance)
    {
        distance = Mathf.Clamp(distance, 0f, _longueur);

        int n = _cumul.Length - 1;

        // Recherche dichotomique du segment contenant la distance
        int bas = 0;
        int haut = n;

        while (bas < haut - 1)
        {
            int milieu = (bas + haut) / 2;

            if (_cumul[milieu] <= distance)
                bas = milieu;
            else
                haut = milieu;
        }

        float longueurSegment = _cumul[haut] - _cumul[bas];

        float fraction = longueurSegment > 0.0001f
            ? (distance - _cumul[bas]) / longueurSegment
            : 0f;

        return (bas + fraction) / n;
    }


    public Vector3 GetPosition(float distance)
    {
        if (!_pret)
            return transform.position;

        float t = DistanceVersT(distance);

        return splineContainer.transform.TransformPoint(
            splineContainer.Spline.EvaluatePosition(t)
        );
    }


    /// <summary>Direction unitaire de la voie à cette distance (sens croissant).</summary>
    public Vector3 GetDirection(float distance)
    {
        if (!_pret)
            return transform.forward;

        float t = DistanceVersT(distance);

        Vector3 tangente = splineContainer.transform.TransformDirection(
            splineContainer.Spline.EvaluateTangent(t)
        );

        return tangente.sqrMagnitude > 1e-6f
            ? tangente.normalized
            : transform.forward;
    }


    /// <summary>
    /// Projette une position monde sur la voie et renvoie la distance
    /// curviligne correspondante.
    ///
    /// Indispensable au changement de voie : conserver telle quelle la
    /// distance de l'ancienne voie ferait sauter le train, puisque les deux
    /// tracés n'ont ni la même origine ni la même longueur.
    /// </summary>
    public float ProjeterDistance(Vector3 positionMonde)
    {
        if (!_pret)
            return 0f;

        // 1. Échantillon le plus proche
        int meilleur = 0;
        float distanceMin = float.MaxValue;

        for (int i = 0; i < _points.Length; i++)
        {
            float d = (_points[i] - positionMonde).sqrMagnitude;

            if (d < distanceMin)
            {
                distanceMin = d;
                meilleur = i;
            }
        }

        // 2. Affinage sur les deux segments voisins
        float resultat = _cumul[meilleur];

        int premier = Mathf.Max(0, meilleur - 1);
        int dernier = Mathf.Min(_points.Length - 2, meilleur);

        for (int i = premier; i <= dernier; i++)
        {
            Vector3 a = _points[i];
            Vector3 ab = _points[i + 1] - a;

            float longueurCarree = ab.sqrMagnitude;

            if (longueurCarree < 1e-6f)
                continue;

            float f = Mathf.Clamp01(Vector3.Dot(positionMonde - a, ab) / longueurCarree);

            Vector3 projete = a + ab * f;
            float d = (projete - positionMonde).sqrMagnitude;

            if (d < distanceMin)
            {
                distanceMin = d;
                resultat = Mathf.Lerp(_cumul[i], _cumul[i + 1], f);
            }
        }

        return resultat;
    }
}
