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
