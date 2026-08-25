using UnityEngine;


/// <summary>
/// Caméra de poursuite : reste derrière et au-dessus d'un convoi, en douceur.
///
/// Travaille en LateUpdate : TrainController place ses wagons dans Update, la
/// caméra doit donc se recaler après, sinon elle suit la position de l'image
/// précédente et le convoi paraît trembler.
/// </summary>
public class CameraSuiviTrain : MonoBehaviour
{
    [Header("Convoi suivi")]
    public TrainController train;

    [Tooltip("Indice du wagon suivi. 0 = tête du convoi.")]
    public int wagonSuivi = 0;


    [Header("Placement")]
    [Tooltip("Décalage dans le repère du wagon : X latéral, Y hauteur, Z avant/arrière.")]
    public Vector3 decalage = new Vector3(0f, 12f, -25f);

    [Tooltip("Hauteur du point visé au-dessus du wagon, en mètres.")]
    public float hauteurVisee = 3f;


    [Header("Douceur")]
    [Tooltip("Plus la valeur est grande, plus la caméra colle au convoi.")]
    [Min(0.1f)]
    public float amortissementPosition = 4f;

    [Min(0.1f)]
    public float amortissementRotation = 6f;


    private Transform _cible;


    private void LateUpdate()
    {
        Transform cible = TrouverCible();

        if (cible == null)
            return;

        // Position voulue, exprimée dans le repère du wagon pour que la caméra
        // reste derrière lui quelle que soit son orientation dans la courbe.
        Vector3 positionVoulue = cible.position + cible.rotation * decalage;
        Vector3 pointVise = cible.position + Vector3.up * hauteurVisee;

        transform.position = Vector3.Lerp(
            transform.position,
            positionVoulue,
            1f - Mathf.Exp(-amortissementPosition * Time.deltaTime)
        );

        Vector3 direction = pointVise - transform.position;

        if (direction.sqrMagnitude < 1e-4f)
            return;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(direction, Vector3.up),
            1f - Mathf.Exp(-amortissementRotation * Time.deltaTime)
        );
    }


    /// <summary>
    /// Wagon suivi, ou à défaut le convoi lui-même. Le résultat est mémorisé
    /// tant qu'il reste valide : inutile de le rechercher à chaque image.
    /// </summary>
    private Transform TrouverCible()
    {
        if (_cible != null)
            return _cible;

        if (train == null)
            return null;

        if (train.wagons != null && train.wagons.Length > 0)
        {
            int i = Mathf.Clamp(wagonSuivi, 0, train.wagons.Length - 1);

            if (train.wagons[i] != null)
            {
                _cible = train.wagons[i].transform;
                return _cible;
            }
        }

        _cible = train.transform;
        return _cible;
    }


    /// <summary>Place la caméra immédiatement, sans transition.</summary>
    public void Recaler()
    {
        Transform cible = TrouverCible();

        if (cible == null)
            return;

        transform.position = cible.position + cible.rotation * decalage;
        transform.LookAt(cible.position + Vector3.up * hauteurVisee, Vector3.up);
    }
}
