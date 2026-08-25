using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class SpeedLimitZone : MonoBehaviour
{
    [Header("Limitation")]
    public float vitesseMax = 40f;

    [Header("Informations")]
    public string nomZone = "Courbe";

    private void Reset()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {Debug.Log("Entrée dans la zone : " + other.name);
        WagonController wagon =
    other.GetComponent<WagonController>();

if (wagon == null)
    return;

TrainController train = wagon.train;

if (train == null)
    return;

        if (train == null)
            return;

        train.EntrerZoneVitesse(this);

        Debug.Log(
            train.name +
            " entre dans " +
            nomZone +
            " (" +
            vitesseMax +
            " km/h)"
        );
    }

    private void OnTriggerExit(Collider other)
    {
        TrainController train = other.GetComponentInParent<TrainController>();

        if (train == null)
            return;

        train.SortirZoneVitesse(this);

        Debug.Log(
            train.name +
            " sort de " +
            nomZone
        );
    }
}