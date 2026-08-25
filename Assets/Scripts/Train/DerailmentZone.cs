using UnityEngine;


/// <summary>
/// Zone où un convoi déraille s'il la franchit trop vite.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DerailmentZone : MonoBehaviour
{
    [Header("Seuil")]
    [Tooltip("Vitesse au-delà de laquelle le convoi déraille, en km/h.")]
    public float vitesseDeraillement = 100f;


    /// <summary>Seuil converti en m/s, unité interne de la simulation.</summary>
    public float VitesseDeraillementMs =>
        vitesseDeraillement / TrainController.MS_VERS_KMH;


    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }


    private void OnTriggerEnter(Collider other)
    {
        // Même méthode de recherche que SpeedLimitZone : les deux zones
        // réagissent ainsi exactement aux mêmes colliders.
        TrainController train = SpeedLimitZone.TrouverTrain(other);

        if (train == null)
            return;

        if (train.vitesse <= VitesseDeraillementMs)
            return;

        TrainDerailmentController derail =
            train.GetComponent<TrainDerailmentController>();

        if (derail == null)
        {
            Debug.LogWarning(
                $"[Déraillement] {train.name} : survitesse détectée mais aucun " +
                "TrainDerailmentController sur le convoi.", this);
            return;
        }

        Debug.LogWarning(
            $"[Déraillement] {train.name} : survitesse " +
            $"{train.VitesseKmh:0} > {vitesseDeraillement:0} km/h", this);

        derail.Derail();
    }
}
