using UnityEngine;


/// <summary>
/// Applique les dégâts visuels aux wagons selon leur distance au point d'impact.
/// </summary>
public class TrainDamageController : MonoBehaviour
{
    [Header("Train associé")]
    public TrainController train;


    [Header("Distance des dégâts (m)")]
    public float distanceDegatsForts = 5f;
    public float distanceDegatsLegers = 15f;


    public void AppliquerDegats(Vector3 pointImpact)
    {
        if (train == null || train.wagons == null)
            return;

        int forts = 0;
        int legers = 0;

        foreach (WagonController wagon in train.wagons)
        {
            if (wagon == null)
                continue;

            float distance = Vector3.Distance(wagon.transform.position, pointImpact);

            if (distance <= distanceDegatsForts)
            {
                wagon.AppliquerDegatVisuel();
                forts++;
            }
            else if (distance <= distanceDegatsLegers)
            {
                legers++;
            }
        }

        // Un seul message de bilan, plutôt qu'une ligne par wagon analysé
        Debug.Log($"[Dégâts] {train.name} : {forts} wagon(s) touché(s), {legers} léger(s)", this);
    }
}
