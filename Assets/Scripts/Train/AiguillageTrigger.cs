using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Déclencheur placé en amont d'un aiguillage : oriente le convoi qui le
/// franchit vers la voie actuellement sélectionnée.
///
/// Il renseigne aussi l'occupation de l'appareil, dont dépend l'enclenchement :
/// on ne déplace pas les lames sous un convoi.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AiguillageTrigger : MonoBehaviour
{
    [Header("Aiguillage commandé")]
    public AiguillageController aiguillage;

    [Tooltip("Laisser vide pour orienter le convoi détecté, quel qu'il soit.")]
    public TrainController train;


    // Caisses présentes sur l'appareil. Un compteur ne suffirait pas : une
    // caisse détruite ou désactivée ne produit pas d'OnTriggerExit, et
    // l'appareil resterait occupé pour toujours.
    private readonly HashSet<Collider> _presents = new HashSet<Collider>();


    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }


    private void OnDisable()
    {
        _presents.Clear();
        Signaler();
    }


    /// <summary>Reporte l'occupation sur l'aiguillage, en purgeant les absents.</summary>
    private void Signaler()
    {
        if (aiguillage == null)
            return;

        _presents.RemoveWhere(c => c == null || !c.gameObject.activeInHierarchy);

        aiguillage.occupee = _presents.Count > 0;
    }


    private void OnTriggerExit(Collider other)
    {
        _presents.Remove(other);
        Signaler();
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

        // L'appareil est occupé tant que la caisse le franchit : c'est cette
        // information que l'enclenchement attendait pour refuser une manœuvre.
        _presents.Add(other);
        Signaler();

        TrackSystem voie = aiguillage.GetVoieActive();

        if (voie == null)
        {
            Debug.LogWarning($"[Aiguillage] {aiguillage.name} : voie active non assignée.", this);
            return;
        }

        detecte.DemanderChangementVoie(voie);
    }
}
