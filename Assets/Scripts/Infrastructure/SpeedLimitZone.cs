using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Zone de limitation de vitesse.
///
/// Compte les wagons présents dans la zone plutôt que de se contenter d'un
/// booléen : avec un convoi de plusieurs wagons, le premier wagon qui sortait
/// levait la limitation alors que le reste du train était encore à l'intérieur.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class SpeedLimitZone : MonoBehaviour
{
    [Header("Limitation")]
    [Tooltip("Vitesse maximale autorisée dans la zone, en km/h.")]
    public float vitesseMax = 40f;

    [Header("Informations")]
    public string nomZone = "Courbe";


    /// <summary>
    /// Limitation exprimée en m/s, unité interne de la simulation.
    /// La valeur affichée dans l'inspecteur reste en km/h, plus parlante.
    /// </summary>
    public float VitesseMaxMs => vitesseMax / TrainController.MS_VERS_KMH;


    private readonly Dictionary<TrainController, int> _occupants =
        new Dictionary<TrainController, int>();


    private void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }


    private void OnTriggerEnter(Collider other)
    {
        TrainController train = TrouverTrain(other);

        if (train == null)
            return;

        _occupants.TryGetValue(train, out int nombre);
        _occupants[train] = nombre + 1;

        // Première entrée du convoi : on applique la limitation
        if (nombre == 0)
        {
            train.EntrerZoneVitesse(this);

            Debug.Log($"[Zone] {train.name} entre dans {nomZone} ({vitesseMax} km/h)", this);
        }
    }


    private void OnTriggerExit(Collider other)
    {
        TrainController train = TrouverTrain(other);

        if (train == null)
            return;

        if (!_occupants.TryGetValue(train, out int nombre))
            return;

        nombre--;

        if (nombre > 0)
        {
            _occupants[train] = nombre;
            return;
        }

        // Dernier wagon sorti : la limitation est levée
        _occupants.Remove(train);
        train.SortirZoneVitesse(this);

        Debug.Log($"[Zone] {train.name} sort de {nomZone}", this);
    }


    private void OnDisable()
    {
        // Sans cela, un train resterait limité à vie si la zone était
        // désactivée alors qu'il se trouvait dedans.
        foreach (TrainController train in _occupants.Keys)
        {
            if (train != null)
                train.SortirZoneVitesse(this);
        }

        _occupants.Clear();
    }


    /// <summary>
    /// Remonte du collider au convoi.
    ///
    /// L'entrée et la sortie utilisaient auparavant deux méthodes de recherche
    /// différentes (GetComponent d'un côté, GetComponentInParent de l'autre) :
    /// elles ne réagissaient donc pas aux mêmes colliders, et une zone pouvait
    /// être appliquée sans jamais être levée.
    /// </summary>
    internal static TrainController TrouverTrain(Collider collider)
    {
        WagonController wagon = collider.GetComponentInParent<WagonController>();

        if (wagon != null && wagon.train != null)
            return wagon.train;

        return collider.GetComponentInParent<TrainController>();
    }
}
