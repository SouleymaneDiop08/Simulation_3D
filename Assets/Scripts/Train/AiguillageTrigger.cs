using UnityEngine;


/// <summary>
/// Déclencheur placé en amont d'un aiguillage : oriente le convoi qui le
/// franchit vers la voie actuellement sélectionnée.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AiguillageTrigger : MonoBehaviour
{
    [Header("Aiguillage commandé")]
    public AiguillageController aiguillage;

    [Tooltip("Laisser vide pour orienter le convoi détecté, quel qu'il soit.")]
    public TrainController train;


    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }


    private void OnTriggerEnter(Collider other)
    {
        if (aiguillage == null)
        {
            Debug.LogWarning($"[Aiguillage] {name} : aucun AiguillageController assigné.", this);
            return;
        }

        // Recherche identique à celle des autres zones : GetComponent seul
        // ratait les colliders portés par les enfants du wagon.
        TrainController detecte = SpeedLimitZone.TrouverTrain(other);

        if (detecte == null)
            return;

        // Un déclencheur peut être réservé à un convoi précis
        if (train != null && detecte != train)
            return;

        TrackSystem voie = aiguillage.GetVoieActive();

        if (voie == null)
        {
            Debug.LogWarning($"[Aiguillage] {aiguillage.name} : voie active non assignée.", this);
            return;
        }

        detecte.DemanderChangementVoie(voie);
    }
}
