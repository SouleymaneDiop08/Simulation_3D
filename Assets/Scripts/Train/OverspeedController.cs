using UnityEngine;


/// <summary>
/// Contrôle de vitesse. Surveille le dépassement de la vitesse autorisée et
/// réagit par paliers, après temporisation.
///
/// Le nom du fichier doit rester identique au nom de la classe : sinon Unity
/// refuse d'attacher le composant. Le fichier s'appelait auparavant
/// OverSpeedController.cs pour une classe OverspeedController.
/// </summary>
public class OverspeedController : MonoBehaviour
{
    public enum NiveauSurvitesse
    {
        Aucun = 0,
        Alerte = 1,
        FreinageAutomatique = 2,
        Critique = 3
    }


    [Header("Train associé")]
    public TrainController train;


    [Header("Temps avant réaction (s)")]
    public float tempsAlerte = 3f;
    public float tempsFreinage = 8f;
    public float tempsCritique = 12f;


    [Header("Diagnostic (lecture seule)")]
    public NiveauSurvitesse niveauSurvitesse = NiveauSurvitesse.Aucun;


    private float tempsSurvitesse = 0f;
    private NiveauSurvitesse niveauPrecedent = NiveauSurvitesse.Aucun;


    private void Update()
    {
        if (train == null)
            return;

        // Les deux grandeurs sont en m/s
        float depassement = train.vitesse - train.vitesseAutorisee;

        if (depassement <= 0f)
        {
            tempsSurvitesse = 0f;
            DefinirNiveau(NiveauSurvitesse.Aucun);
            return;
        }

        tempsSurvitesse += Time.deltaTime;

        // Chaînage en else if : les paliers s'excluent. Auparavant les trois
        // conditions étaient évaluées séparément et déclenchaient en cascade
        // dans la même image, en journalisant à chaque appel.
        if (tempsSurvitesse >= tempsCritique)
            DefinirNiveau(NiveauSurvitesse.Critique);

        else if (tempsSurvitesse >= tempsFreinage)
            DefinirNiveau(NiveauSurvitesse.FreinageAutomatique);

        else if (tempsSurvitesse >= tempsAlerte)
            DefinirNiveau(NiveauSurvitesse.Alerte);
    }


    private void DefinirNiveau(NiveauSurvitesse niveau)
    {
        niveauSurvitesse = niveau;

        // Ne réagir qu'au changement de palier : l'ancienne version rappelait
        // FreinService() et journalisait à chaque image de survitesse.
        if (niveau == niveauPrecedent)
            return;

        niveauPrecedent = niveau;

        switch (niveau)
        {
            case NiveauSurvitesse.Alerte:
                Debug.LogWarning(
                    $"[Survitesse] {train.name} : ALERTE " +
                    $"({train.VitesseKmh:0} / {train.vitesseAutorisee * TrainController.MS_VERS_KMH:0} km/h)",
                    this);
                break;

            case NiveauSurvitesse.FreinageAutomatique:
                Debug.LogWarning($"[Survitesse] {train.name} : FREINAGE AUTOMATIQUE", this);

                if (train.physics != null)
                    train.physics.FreinService();
                break;

            case NiveauSurvitesse.Critique:
                Debug.LogError($"[Survitesse] {train.name} : CRITIQUE — frein d'urgence", this);

                if (train.physics != null)
                    train.physics.FreinUrgence();
                break;
        }
    }
}
