using UnityEngine;


/// <summary>
/// Soufflet d'intercirculation : s'étire, se comprime et s'oriente selon
/// l'écartement des deux attaches.
/// </summary>
public class SouffletController : MonoBehaviour
{
    [Header("Attaches")]
    public Transform attacheArriere;
    public Transform attacheAvant;


    [Header("Ossature")]
    public Transform boneMilieu;


    private float longueurInitiale;
    private bool valide;


    private void Start()
    {
        // Start() déréférençait les attaches sans vérification, alors même
        // qu'Update() en faisait une : un champ non assigné provoquait un
        // NullReferenceException au démarrage.
        valide = attacheArriere != null
                 && attacheAvant != null
                 && boneMilieu != null;

        if (!valide)
        {
            Debug.LogWarning($"[Soufflet] {name} : références incomplètes, composant inactif.", this);
            enabled = false;
            return;
        }

        longueurInitiale = Vector3.Distance(
            attacheArriere.position,
            attacheAvant.position
        );

        // Une longueur initiale nulle donnerait une division par zéro
        if (longueurInitiale < 0.001f)
        {
            Debug.LogWarning($"[Soufflet] {name} : attaches confondues, composant inactif.", this);
            enabled = false;
            valide = false;
        }
    }


    private void LateUpdate()
    {
        if (!valide)
            return;

        Vector3 ecart = attacheAvant.position - attacheArriere.position;

        // Compression / extension
        float ratio = ecart.magnitude / longueurInitiale;

        Vector3 echelle = boneMilieu.localScale;
        echelle.z = ratio;
        boneMilieu.localScale = echelle;

        // Orientation dans la courbe
        if (ecart.sqrMagnitude > 1e-6f)
            boneMilieu.rotation = Quaternion.LookRotation(ecart.normalized, Vector3.up);
    }
}
