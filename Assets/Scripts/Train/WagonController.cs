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
    /// </summary>
    public void Move(float distanceSurVoie)
    {
        if (trackSystem == null || !trackSystem.Pret)
            return;

        distanceSurVoie = Mathf.Clamp(distanceSurVoie, 0f, trackSystem.Longueur);

        Vector3 position = trackSystem.GetPosition(distanceSurVoie);
        Vector3 direction = trackSystem.GetDirection(distanceSurVoie);

        Quaternion rotationVoie = direction.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(direction, Vector3.up)
            : transform.rotation;

        // L'écart de déraillement est tourné avec la voie : sinon un wagon
        // déraillé serait toujours poussé vers -X global, quelle que soit son
        // orientation dans la courbe.
        transform.SetPositionAndRotation(
            position + rotationVoie * derailOffset,
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
