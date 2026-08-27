using UnityEngine;


/// <summary>
/// Modèle de vitesse du convoi.
///
/// S'exécute avant TrainController (voir DefaultExecutionOrder) : il écrit
/// train.vitesse, que TrainController lit ensuite dans la même image. Sans cet
/// ordre explicite, l'ordre d'appel des Update était indéterminé et la vitesse
/// utilisée pouvait dater de l'image précédente.
///
/// Unités : mètres par seconde, mètres par seconde carrée.
/// </summary>
[DefaultExecutionOrder(-10)]
public class TrainPhysicsController : MonoBehaviour
{
    public enum EtatFrein
    {
        Relache,
        Service,
        Urgence
    }


    [Header("Train associé")]
    public TrainController train;


    [Header("Vitesse (m/s)")]
    public float vitesseMax = 70f;
    public float vitesseDemandee = 0f;
    public float vitesseActuelle = 0f;


    [Header("Physique (m/s²)")]
    public float acceleration = 5f;
    public float freinServicePuissance = 10f;
    public float freinUrgencePuissance = 25f;


    /// <summary>État courant du freinage.</summary>
    public EtatFrein Frein => etatFrein;


    private EtatFrein etatFrein = EtatFrein.Relache;


    private void Start()
    {
        Reinitialiser();
    }


    private void Update()
    {
        if (train == null)
            return;

        float cible;
        float taux;

        switch (etatFrein)
        {
            case EtatFrein.Service:
                cible = 0f;
                taux = freinServicePuissance;
                break;

            case EtatFrein.Urgence:
                cible = 0f;
                taux = freinUrgencePuissance;
                break;

            default:
                cible = Mathf.Clamp(vitesseDemandee, 0f, vitesseMax);
                taux = acceleration;
                break;
        }

        vitesseActuelle = Mathf.MoveTowards(
            vitesseActuelle,
            cible,
            taux * Time.deltaTime
        );

        train.vitesse = vitesseActuelle;
    }


    // ==========================================================
    // COMMANDES
    // ==========================================================

    /// <summary>
    /// Consigne de traction, de 0 à 1.
    ///
    /// Ne desserre PAS les freins : c'était le cas auparavant, si bien qu'un
    /// simple changement de consigne annulait un freinage d'urgence. Le
    /// desserrage doit être demandé explicitement via RelacherFrein().
    /// </summary>
    public void ChangerTraction(float valeur)
    {
        vitesseDemandee = Mathf.Clamp01(valeur) * vitesseMax;
    }


    public void FreinService()
    {
        etatFrein = EtatFrein.Service;
    }


    public void FreinUrgence()
    {
        vitesseDemandee = 0f;
        etatFrein = EtatFrein.Urgence;
    }


    public void RelacherFrein()
    {
        etatFrein = EtatFrein.Relache;
    }


    /// <summary>
    /// Annule la vitesse sur-le-champ, sans changer l'état de frein.
    /// Utilisé par la butée de quai : le convoi doit pouvoir repartir dès que
    /// l'automate commande le sens opposé.
    /// </summary>
    public void ArreterNet()
    {
        vitesseActuelle = 0f;

        if (train != null)
            train.vitesse = 0f;
    }


    public void Reinitialiser()
    {
        etatFrein = EtatFrein.Relache;
        vitesseDemandee = 0f;
        vitesseActuelle = 0f;

        if (train != null)
            train.vitesse = 0f;
    }
}
