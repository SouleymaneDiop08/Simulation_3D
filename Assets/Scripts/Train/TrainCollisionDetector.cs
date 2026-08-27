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
    [Tooltip("Distance de déclenchement du choc, en mètres.")]
    public float distanceDetection = 5f;


    [Header("Impact")]
    public float forceImpact = 35f;
    public float dureeImpact = 1f;


    [Header("Explosion")]
    public GameObject explosionPrefab;

    [Tooltip("Durée de vie de l'effet d'explosion, en secondes.")]
    public float dureeExplosion = 5f;

    public float echelleExplosion = 5f;


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

        float seuilCarre = distanceDetection * distanceDetection;

        for (int i = 0; i < Detecteurs.Count; i++)
        {
            TrainCollisionDetector autre = Detecteurs[i];

            if (autre == null || autre == this)
                continue;

            if (autre.collisionEffectuee)
                continue;

            // Deux détecteurs du même convoi ne se percutent pas
            if (autre.train == null || autre.train == train)
                continue;

            // Deux convois sur des voies différentes ne peuvent pas se
            // heurter. Sans ce test, les voies parallèles de la scène —
            // distantes de 5,6 m seulement, pour un seuil de détection de
            // 5 m — provoquaient un faux choc au croisement des navettes :
            // le convoi passait en Impact puis en Bloque, état sans retour.
            if (autre.train.trackSystem != train.trackSystem)
                continue;

            float distanceCarree =
                (autre.transform.position - transform.position).sqrMagnitude;

            if (distanceCarree <= seuilCarre)
            {
                CollisionTrain(autre);
                return;
            }
        }
    }


    private void CollisionTrain(TrainCollisionDetector autre)
    {
        collisionEffectuee = true;
        autre.collisionEffectuee = true;

        Vector3 pointImpact =
            (transform.position + autre.transform.position) * 0.5f
            + Vector3.up * 2f;

        Debug.Log($"[Choc] {train.name} et {autre.train.name} au point {pointImpact}", this);

        // Les dégâts étaient auparavant imbriqués dans le test sur le prefab
        // d'explosion : sans prefab assigné, aucun dégât n'était appliqué.
        AppliquerDegats(train, pointImpact);
        AppliquerDegats(autre.train, pointImpact);

        if (explosionPrefab != null)
        {
            GameObject explosion = Instantiate(
                explosionPrefab,
                pointImpact,
                Quaternion.identity
            );

            explosion.transform.localScale = Vector3.one * echelleExplosion;

            // Sans destruction programmée, chaque choc laissait un objet
            // d'effet dans la scène jusqu'à la fin de la partie.
            Destroy(explosion, dureeExplosion);
        }

        train.AppliquerImpact(forceImpact, dureeImpact);
        autre.train.AppliquerImpact(forceImpact, dureeImpact);
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
