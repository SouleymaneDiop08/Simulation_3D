using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Détecte le rapprochement de deux convois et déclenche le choc.
///
/// Les détecteurs se recensent eux-mêmes dans une liste statique. L'ancienne
/// version appelait FindObjectsByType() à chaque image et pour chaque
/// détecteur : coût en O(n²) et allocation d'un tableau à chaque appel.
/// </summary>
public class TrainCollisionDetector : MonoBehaviour
{
    private static readonly List<TrainCollisionDetector> Detecteurs =
        new List<TrainCollisionDetector>();


    [Header("Train associé")]
    public TrainController train;


    [Header("Détection")]
    [Tooltip("Marge ajoutée au volume de chaque caisse, en mètres. Le choc se " +
             "déclenche quand les volumes ainsi élargis se recouvrent, donc " +
             "juste avant que les modèles ne s'interpénètrent visuellement.\n\n" +
             "PLAFOND : les deux voies ne sont écartées que de 5,6 m. Marge + " +
             "largeur d'une caisse doit rester sous cette valeur, sinon deux " +
             "convois se croisant sur des voies parallèles déclencheraient un " +
             "faux choc. Les caisses ayant été mises à l'échelle 2, la marge a " +
             "été abaissée en conséquence.")]
    public float margeContact = 0.5f;

    [Tooltip("Secondes ignorées après le démarrage. Le temps que chaque convoi " +
             "soit posé sur sa voie, les wagons occupent encore leur position " +
             "d'édition — d'où de faux chocs à la première image.")]
    public float delaiArmement = 2f;


    [Header("Impact")]
    public float forceImpact = 35f;
    public float dureeImpact = 1f;


    [Header("Explosion")]
    public GameObject explosionPrefab;

    [Tooltip("Durée de vie de l'effet d'explosion, en secondes.")]
    public float dureeExplosion = 12f;

    [Tooltip("Facteur d'échelle appliqué à l'effet.")]
    public float echelleExplosion = 25f;

    [Tooltip("Nombre d'effets créés, répartis le long du point d'impact. " +
             "Un seul effet se perd à l'échelle d'un convoi.")]
    [Min(1)]
    public int nombreEclats = 5;

    [Tooltip("Dispersion des éclats autour du point d'impact, en mètres.")]
    public float dispersionEclats = 12f;


    private bool collisionEffectuee = false;


    private void OnEnable()
    {
        Detecteurs.Add(this);
    }


    private void OnDisable()
    {
        Detecteurs.Remove(this);
    }


    private void Update()
    {
        if (train == null || collisionEffectuee)
            return;

        // Le temps que TrainController pose ses wagons sur la voie, ceux-ci
        // occupent encore leur position d'édition. Sans ce délai, un choc
        // fantôme se déclenchait dès la première image.
        if (Time.timeSinceLevelLoad < delaiArmement)
            return;

        for (int i = 0; i < Detecteurs.Count; i++)
        {
            TrainCollisionDetector autre = Detecteurs[i];

            if (autre == null || autre == this)
                continue;

            if (autre.collisionEffectuee || autre.train == null)
                continue;

            // Deux détecteurs du même convoi ne se percutent pas
            if (autre.train == train)
                continue;

            // Le critère est le recouvrement RÉEL des caisses, sans
            // considération de voie. Une simple distance entre pivots ne
            // saurait pas trancher : 5,6 m séparent deux convois qui se
            // croisent sur des voies parallèles — aucun contact — et
            // séparent aussi deux caisses déjà imbriquées nez à nez sur le
            // même rail. Des volumes le distinguent, un scalaire non.
            if (ConvoisEnContact(train, autre.train, margeContact, out Vector3 point))
            {
                CollisionTrain(autre, point);
                return;
            }
        }
    }


    /// <summary>
    /// Vrai si une caisse de l'un recouvre une caisse de l'autre, marge
    /// comprise. Le point de contact renvoyé est le milieu des deux caisses
    /// fautives — l'endroit exact du choc, et non le milieu des deux convois.
    /// </summary>
    private static bool ConvoisEnContact(TrainController a, TrainController b,
                                         float marge, out Vector3 point)
    {
        point = Vector3.zero;

        if (a == null || b == null || a.wagons == null || b.wagons == null)
            return false;

        foreach (WagonController wa in a.wagons)
        {
            if (wa == null)
                continue;

            Bounds ba = wa.BornesMonde;
            ba.Expand(marge);

            foreach (WagonController wb in b.wagons)
            {
                if (wb == null)
                    continue;

                Bounds bb = wb.BornesMonde;

                if (!ba.Intersects(bb))
                    continue;

                point = (ba.center + bb.center) * 0.5f;
                return true;
            }
        }

        return false;
    }


    private void CollisionTrain(TrainCollisionDetector autre, Vector3 pointContact)
    {
        collisionEffectuee = true;
        autre.collisionEffectuee = true;

        Vector3 pointImpact = pointContact + Vector3.up * 2f;

        Debug.Log($"[Choc] {train.name} et {autre.train.name} au point {pointImpact}", this);

        // Les dégâts étaient auparavant imbriqués dans le test sur le prefab
        // d'explosion : sans prefab assigné, aucun dégât n'était appliqué.
        AppliquerDegats(train, pointImpact);
        AppliquerDegats(autre.train, pointImpact);

        DeclencherExplosion(pointImpact);

        train.AppliquerImpact(forceImpact, dureeImpact);
        autre.train.AppliquerImpact(forceImpact, dureeImpact);
    }


    /// <summary>
    /// Crée l'effet visuel du choc. Plusieurs éclats dispersés plutôt qu'un
    /// seul : à l'échelle d'un convoi de 63 m, un unique effet passe inaperçu.
    /// </summary>
    private void DeclencherExplosion(Vector3 pointImpact)
    {
        if (explosionPrefab == null)
            return;

        for (int i = 0; i < nombreEclats; i++)
        {
            // Le premier éclat est centré sur l'impact, les suivants sont
            // dispersés autour et décalés dans le temps.
            Vector3 position = pointImpact;

            if (i > 0)
            {
                Vector2 disque = Random.insideUnitCircle * dispersionEclats;
                position += new Vector3(disque.x, Random.Range(0f, dispersionEclats * 0.5f), disque.y);
            }

            GameObject eclat = Instantiate(explosionPrefab, position, Random.rotation);

            float echelle = echelleExplosion * Random.Range(0.6f, 1.4f);
            eclat.transform.localScale = Vector3.one * echelle;

            // Sans destruction programmée, chaque choc laisserait ses effets
            // dans la scène jusqu'à la fin de la partie.
            Destroy(eclat, dureeExplosion);
        }

        Debug.LogWarning($"[Choc] {nombreEclats} éclats créés en {pointImpact}", this);
    }


    private static void AppliquerDegats(TrainController train, Vector3 pointImpact)
    {
        if (train == null)
            return;

        TrainDamageController degats = train.GetComponent<TrainDamageController>();

        if (degats != null)
            degats.AppliquerDegats(pointImpact);
    }


    /// <summary>Réarme le détecteur après réinitialisation du convoi.</summary>
    public void Reinitialiser()
    {
        collisionEffectuee = false;
    }
}
