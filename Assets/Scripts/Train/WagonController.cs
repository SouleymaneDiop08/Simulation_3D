using UnityEngine;


/// <summary>
/// Un wagon posé sur une voie. Ne décide de rien : il est positionné par son
/// TrainController à partir d'une distance curviligne.
/// </summary>
public class WagonController : MonoBehaviour
{
    [Header("Voie")]
    public TrackSystem trackSystem;

    [Tooltip("Longueur du wagon, en mètres.")]
    public float longueurWagon = 10f;

    [Tooltip("Entraxe des bogies, en mètres.\n\n" +
             "Une caisse ne suit pas la voie comme un point : elle repose sur " +
             "deux pivots posés sur le rail, et c'est la CORDE entre eux qui " +
             "donne son orientation, pas la tangente en son milieu. Dans une " +
             "traversée, une caisse rigide de quinze mètres orientée sur la " +
             "tangente voit ses extrémités sortir du rail et le convoi se " +
             "tortiller ; sur la corde, il s'inscrit dans la courbe comme un " +
             "vrai train.\n\n" +
             "Mettre à zéro pour revenir au placement ponctuel.")]
    public float empattementBogies = 10f;

    [Tooltip("Poser la CAISSE VISIBLE sur la voie, et non le pivot.\n\n" +
             "Le pivot de ces wagons n'est pas au milieu de leur caisse : il " +
             "en est écarté de plusieurs mètres, et jusqu'à une trentaine pour " +
             "les caisses de tête et de queue. Or c'est le pivot qu'on place " +
             "sur la voie. La caisse pend donc au bout d'un bras de levier : " +
             "dès que le pivot s'incline dans une courbe, elle balaie de " +
             "côté et sort du rail — quatre mètres et demi dans la traversée.\n\n" +
             "L'écart est MESURÉ au démarrage sur les rendus de la caisse : " +
             "rien à saisir, et aucun effet là où le pivot est déjà centré. " +
             "La caisse conserve sa place le long du convoi ; seule sa " +
             "position transversale est corrigée.")]
    public bool poserLaCaisseSurLaVoie = true;

    [Header("Train")]
    public TrainController train;

    [Header("Dégâts")]
    public ParticleSystem suieChoc;


    // ==========================================================
    // DÉRAILLEMENT
    // Écart et rotation appliqués par-dessus la position de voie.
    // Exprimés dans le repère de la voie, pas en coordonnées monde.
    // ==========================================================

    [HideInInspector]
    public Vector3 derailOffset = Vector3.zero;

    [HideInInspector]
    public Quaternion derailRotation = Quaternion.identity;


    private Renderer[] _rendus;
    private bool _rendusCherches;

    // Écart du centre de la caisse au pivot, exprimé dans le repère de
    // rotation du wagon, en mètres monde. Mesuré une fois, avant tout
    // déplacement.
    private Vector3 _ecartCaisse;


    private void Awake()
    {
        MesurerEcartCaisse();
    }


    /// <summary>
    /// Relève où se trouve la caisse par rapport à son pivot.
    ///
    /// Les systèmes de particules sont écartés : la fumée de choc est placée
    /// en avant du wagon et fausserait le centre.
    /// </summary>
    private void MesurerEcartCaisse()
    {
        MeshRenderer[] rendus = GetComponentsInChildren<MeshRenderer>();

        if (rendus == null || rendus.Length == 0)
        {
            _ecartCaisse = Vector3.zero;
            return;
        }

        Bounds bornes = rendus[0].bounds;

        for (int i = 1; i < rendus.Length; i++)
            bornes.Encapsulate(rendus[i].bounds);

        _ecartCaisse = Quaternion.Inverse(transform.rotation) *
                       (bornes.center - transform.position);
    }


    /// <summary>
    /// Volume englobant la caisse, en coordonnées monde, réévalué à chaque
    /// appel puisque le wagon bouge. Les Renderer sont mémorisés une fois :
    /// les rechercher à chaque image coûterait plus cher que le calcul.
    /// </summary>
    public Bounds BornesMonde
    {
        get
        {
            if (!_rendusCherches)
            {
                _rendus = GetComponentsInChildren<Renderer>();
                _rendusCherches = true;
            }

            if (_rendus == null || _rendus.Length == 0)
                return new Bounds(transform.position, Vector3.one * 2f);

            Bounds bornes = _rendus[0].bounds;

            for (int i = 1; i < _rendus.Length; i++)
                bornes.Encapsulate(_rendus[i].bounds);

            return bornes;
        }
    }


    public void SetTrack(TrackSystem track)
    {
        trackSystem = track;
    }


    /// <summary>
    /// Place le wagon à la distance donnée sur sa voie.
    ///
    /// L'orientation vaut +1 quand la caisse regarde dans le sens croissant du
    /// tracé, -1 dans l'autre. Elle est nécessaire parce qu'une traversée
    /// unique est parcourue à l'envers par le convoi d'en face : sans elle,
    /// tout le train pivotait d'un demi-tour à l'instant du franchissement.
    ///
    /// Elle est distincte du sens de marche : un convoi qui rebrousse chemin
    /// en gare recule, il ne se retourne pas.
    /// </summary>
    public void Move(float distanceSurVoie, int orientation = 1)
    {
        if (trackSystem == null || !trackSystem.Pret)
            return;

        float longueur = trackSystem.Longueur;

        distanceSurVoie = Mathf.Clamp(distanceSurVoie, 0f, longueur);

        Vector3 ecart = poserLaCaisseSurLaVoie ? _ecartCaisse : Vector3.zero;

        // Là où la caisse se trouve DÉJÀ le long de la voie. On la laisse à sa
        // place dans le convoi — on ne corrige que la manière dont elle y est
        // posée. Le sens compte : sur un tracé parcouru à l'envers, l'avant du
        // wagon regarde vers les distances décroissantes.
        float dCaisse = Mathf.Clamp(
            distanceSurVoie + ecart.z * (orientation < 0 ? -1f : 1f), 0f, longueur);

        // Les deux bogies, posés sur le rail de part et d'autre de la caisse.
        float demi = Mathf.Max(0f, empattementBogies * 0.5f);

        float dAvant = Mathf.Clamp(dCaisse + demi, 0f, longueur);
        float dArriere = Mathf.Clamp(dCaisse - demi, 0f, longueur);

        Vector3 bogieAvant = trackSystem.GetPosition(dAvant);
        Vector3 bogieArriere = trackSystem.GetPosition(dArriere);

        // La caisse est portée par ses pivots : son centre est au milieu de la
        // corde, légèrement à l'intérieur de la courbe — exactement comme un
        // véhicule réel — et son axe est celui de la corde.
        Vector3 position = (bogieAvant + bogieArriere) * 0.5f;
        Vector3 direction = bogieAvant - bogieArriere;

        // Empattement nul, ou caisse acculée contre une extrémité de voie :
        // les deux pivots se confondent, la corde ne dit plus rien.
        if (direction.sqrMagnitude < 1e-6f)
            direction = trackSystem.GetDirection(dCaisse);

        if (orientation < 0)
            direction = -direction;

        Quaternion rotationVoie = direction.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(direction, Vector3.up)
            : transform.rotation;

        // On a calculé où doit être la CAISSE ; on en déduit le pivot en
        // retranchant l'écart mesuré. Sans cela la caisse resterait au bout de
        // son bras de levier.
        //
        // L'écart de déraillement est tourné avec la voie : sinon un wagon
        // déraillé serait toujours poussé vers -X global, quelle que soit son
        // orientation dans la courbe.
        transform.SetPositionAndRotation(
            position + rotationVoie * (derailOffset - ecart),
            rotationVoie * derailRotation
        );
    }


    /// <summary>Remet le wagon dans son état nominal (sortie de déraillement).</summary>
    public void Reinitialiser()
    {
        derailOffset = Vector3.zero;
        derailRotation = Quaternion.identity;
    }


    public void AppliquerDegatVisuel()
    {
        if (suieChoc == null)
            return;

        suieChoc.transform.SetPositionAndRotation(
            transform.position,
            transform.rotation
        );

        suieChoc.Play();
    }
}
