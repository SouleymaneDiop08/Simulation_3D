using UnityEngine;


/// <summary>
/// Déraillement progressif : les wagons quittent la voie les uns après les
/// autres et basculent, l'intensité décroissant vers la queue du convoi.
/// </summary>
public class TrainDerailmentController : MonoBehaviour
{
    [Header("Train associé")]
    public TrainController train;

    [Header("Diagnostic (lecture seule)")]
    public bool deraille = false;


    [Header("Déraillement")]
    [Tooltip("Décélération imposée au convoi déraillé, en m/s².")]
    public float ralentissement = 20f;

    [Tooltip("Délai de propagation d'un wagon au suivant, en secondes.")]
    public float tempsEntreWagons = 0.4f;


    [Header("Écart latéral")]
    [Tooltip("Déport latéral du wagon déraillé, en mètres, dans le repère de la voie.")]
    public float ecartLateral = 2f;


    [Header("Torsion des wagons")]
    public float rotationMaxAvant = 90f;
    public float rotationArriere = 50f;

    [Tooltip("Vitesse de bascule, en degrés par seconde.")]
    public float vitesseBascule = 30f;


    private bool[] wagonsActifs;
    private float tempsDeraillement = 0f;
    private int dernierWagonDeraille = 0;


    public void Derail()
    {
        if (deraille)
            return;

        if (train == null || train.wagons == null || train.wagons.Length == 0)
        {
            Debug.LogWarning("[Déraillement] Aucun wagon à faire dérailler.", this);
            return;
        }

        deraille = true;

        wagonsActifs = new bool[train.wagons.Length];
        tempsDeraillement = 0f;
        dernierWagonDeraille = 0;

        Debug.LogWarning($"[Déraillement] {train.name}", this);
    }


    private void Update()
    {
        // wagonsActifs n'existe qu'après Derail() : sans ce garde, une
        // activation de "deraille" depuis l'inspecteur provoquait un
        // NullReferenceException à chaque image.
        if (!deraille || wagonsActifs == null || train == null)
            return;

        Ralentir();
        PropagerDeraillement();
        BasculerWagons();
    }


    private void Ralentir()
    {
        if (train.physics == null)
            return;

        train.physics.vitesseActuelle = Mathf.MoveTowards(
            train.physics.vitesseActuelle,
            0f,
            ralentissement * Time.deltaTime
        );
    }


    private void PropagerDeraillement()
    {
        if (dernierWagonDeraille >= train.wagons.Length)
            return;

        tempsDeraillement += Time.deltaTime;

        if (tempsDeraillement < tempsEntreWagons)
            return;

        tempsDeraillement = 0f;

        WagonController wagon = train.wagons[dernierWagonDeraille];

        if (wagon != null)
        {
            // Écart exprimé dans le repère de la voie : WagonController le
            // fait tourner avec le tracé. Un Vector3 en coordonnées monde
            // poussait tous les wagons vers -X, quelle que soit la courbe.
            wagon.derailOffset = new Vector3(-ecartLateral, 0f, 0f);

            wagonsActifs[dernierWagonDeraille] = true;

            Debug.Log($"[Déraillement] wagon déraillé : {wagon.name}", this);
        }

        dernierWagonDeraille++;
    }


    private void BasculerWagons()
    {
        for (int i = 0; i < train.wagons.Length; i++)
        {
            if (!wagonsActifs[i])
                continue;

            WagonController wagon = train.wagons[i];

            if (wagon == null)
                continue;

            float intensite = Mathf.Lerp(
                rotationMaxAvant,
                rotationArriere,
                train.wagons.Length > 1 ? (float)i / (train.wagons.Length - 1) : 0f
            );

            Quaternion rotationCible = Quaternion.Euler(0f, 0f, -intensite);

            wagon.derailRotation = Quaternion.RotateTowards(
                wagon.derailRotation,
                rotationCible,
                vitesseBascule * Time.deltaTime
            );
        }
    }


    public void Reinitialiser()
    {
        deraille = false;
        wagonsActifs = null;
        tempsDeraillement = 0f;
        dernierWagonDeraille = 0;
    }
}
