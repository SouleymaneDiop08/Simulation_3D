using UnityEngine;


/// <summary>
/// Navette autonome : le convoi fait ses allers-retours entre les deux
/// extrémités de sa voie, sans intervention extérieure.
///
/// C'est le PROCÉDÉ. Il tourne seul, comme un réacteur chimique bouillonne
/// sans qu'on le lui demande. L'automate ne le pilote pas : il l'observe et
/// dispose de quelques leviers — consigne de vitesse, arrêt d'urgence,
/// autorisation de circuler.
///
/// Sans automate, la navette continue sur ses valeurs nominales. La
/// simulation reste donc démontrable seule, et couper la liaison n'arrête
/// pas le procédé : c'est ce qui fait la différence entre perturber une
/// installation et l'éteindre.
///
/// Les transitions se font sur la POSITION, jamais sur un chronomètre : un
/// écart de cinématique ne peut pas faire manquer une gare.
/// </summary>
[RequireComponent(typeof(TrainController))]
public class NavetteController : MonoBehaviour
{
    public enum Phase
    {
        Attente,
        AQuai,
        Depart,
        Marche,
        Approche,
        Freinage,
        Stationnement
    }


    [Header("Convoi")]
    public TrainController train;


    [Header("Marche nominale")]
    [Tooltip("Vitesse visée en ligne, en km/h, tant que l'automate n'en impose pas d'autre.")]
    public float vitesseNominaleKmh = 90f;

    [Tooltip("Vitesse visée pendant l'approche d'une gare, en km/h.")]
    public float vitesseApprocheKmh = 30f;


    [Header("Profil d'arrêt")]
    [Tooltip("Distance avant la butée à laquelle l'approche commence, en mètres.")]
    public float distanceApproche = 70f;

    [Tooltip("Distance avant la butée à laquelle le freinage commence, en mètres.")]
    public float distanceArret = 15f;


    [Header("Temporisations")]
    public float retardDepart = 2f;
    public float tempsPreparation = 4f;
    public float tempsStationnement = 15f;


    // ==================================================================
    // LEVIERS DE L'AUTOMATE
    // Écrits par PosteDeCommande. Valeurs nominales tant qu'aucun ordre
    // n'arrive — la navette n'attend pas la permission pour rouler.
    // ==================================================================

    [Header("Commandes extérieures (lecture seule)")]
    public float consigneVitesseKmh = -1f;
    public bool arretUrgence = false;
    public bool autorisee = true;


    [Header("Diagnostic (lecture seule)")]
    public Phase phase = Phase.Attente;
    public bool versButeeHaute = true;


    /// <summary>Vitesse effectivement visée, en km/h.</summary>
    public float VitesseVisee =>
        consigneVitesseKmh >= 0f ? consigneVitesseKmh : vitesseNominaleKmh;

    /// <summary>Vrai lorsque le convoi est immobilisé à quai.</summary>
    public bool AQuaiMaintenant => phase == Phase.AQuai || phase == Phase.Stationnement;

    /// <summary>Vrai pendant l'approche : le signal passe à l'avertissement.</summary>
    public bool EnApproche => phase == Phase.Approche || phase == Phase.Freinage;


    private float _tempsPhase;


    private void Awake()
    {
        if (train == null)
            train = GetComponent<TrainController>();
    }


    private void Update()
    {
        if (train == null || train.physics == null)
            return;

        // Un convoi accidenté ne repart pas : la navette lui rend la main.
        if (train.etat == TrainController.EtatTrain.Bloque ||
            train.etat == TrainController.EtatTrain.Impact)
        {
            phase = Phase.Attente;
            return;
        }

        _tempsPhase += Time.deltaTime;

        if (arretUrgence || !autorisee)
        {
            Immobiliser();
            return;
        }

        switch (phase)
        {
            case Phase.Attente: Attente(); break;
            case Phase.AQuai: AQuai(); break;
            case Phase.Depart: Depart(); break;
            case Phase.Marche: Marche(); break;
            case Phase.Approche: Approche(); break;
            case Phase.Freinage: Freinage(); break;
            case Phase.Stationnement: Stationnement(); break;
        }
    }


    // ==================================================================
    // PHASES
    // ==================================================================

    private void Attente()
    {
        train.physics.FreinUrgence();

        if (_tempsPhase >= retardDepart)
            ChangerPhase(Phase.AQuai);
    }


    private void AQuai()
    {
        train.physics.FreinUrgence();
        train.sens = TrainController.SensTrain.Neutre;

        if (_tempsPhase >= tempsPreparation)
            ChangerPhase(Phase.Depart);
    }


    private void Depart()
    {
        train.physics.RelacherFrein();
        train.sens = versButeeHaute
            ? TrainController.SensTrain.Avant
            : TrainController.SensTrain.Arriere;

        train.physics.DefinirVitesseCible(VitesseVisee / TrainController.MS_VERS_KMH);

        if (_tempsPhase >= 1.5f)
            ChangerPhase(Phase.Marche);
    }


    private void Marche()
    {
        train.physics.RelacherFrein();
        train.physics.DefinirVitesseCible(VitesseVisee / TrainController.MS_VERS_KMH);

        if (DistanceAvantButee() <= distanceApproche)
            ChangerPhase(Phase.Approche);
    }


    private void Approche()
    {
        train.physics.RelacherFrein();

        // L'approche se fait à la plus basse des deux : si l'automate impose
        // une consigne inférieure à la vitesse d'approche, elle prime.
        float visee = Mathf.Min(vitesseApprocheKmh, VitesseVisee);
        train.physics.DefinirVitesseCible(visee / TrainController.MS_VERS_KMH);

        if (DistanceAvantButee() <= distanceArret)
            ChangerPhase(Phase.Freinage);
    }


    private void Freinage()
    {
        train.physics.FreinService();

        // On attend l'arrêt réel, pas une durée : le convoi s'immobilise à
        // quai même si le freinage a été plus long que prévu.
        if (train.vitesse <= 0.05f || _tempsPhase > 30f)
            ChangerPhase(Phase.Stationnement);
    }


    private void Stationnement()
    {
        train.physics.FreinUrgence();
        train.sens = TrainController.SensTrain.Neutre;

        if (_tempsPhase < tempsStationnement)
            return;

        versButeeHaute = !versButeeHaute;
        ChangerPhase(Phase.AQuai);
    }


    private void Immobiliser()
    {
        train.physics.FreinUrgence();

        // La phase est conservée : dès l'arrêt d'urgence levé, la navette
        // reprend où elle en était plutôt que de recommencer son cycle.
        if (phase == Phase.Marche || phase == Phase.Approche || phase == Phase.Depart)
            phase = Phase.Freinage;
    }


    // ==================================================================
    // OUTILS
    // ==================================================================

    private void ChangerPhase(Phase nouvelle)
    {
        phase = nouvelle;
        _tempsPhase = 0f;
    }


    /// <summary>
    /// Bascule l'extrémité visée. Appelé quand le convoi passe sur un tracé
    /// parcouru en sens inverse : sa distance progresse alors à l'envers, si
    /// bien que la butée vers laquelle il roule change de bout. Sans cela la
    /// navette guetterait l'extrémité qu'elle vient de quitter et n'engagerait
    /// jamais son freinage.
    /// </summary>
    public void InverserObjectif()
    {
        versButeeHaute = !versButeeHaute;
    }


    /// <summary>Distance restante avant la butée visée, en mètres.</summary>
    public float DistanceAvantButee()
    {
        if (train == null)
            return float.MaxValue;

        return versButeeHaute
            ? train.LimiteHaute - train.distanceTrain
            : train.distanceTrain - train.LimiteBasse;
    }
}
