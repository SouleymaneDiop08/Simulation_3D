using System;


/// <summary>
/// Jeu d'ordres destiné à un convoi, tel que l'automate l'émet.
///
/// Les unités sont celles du programme ST — échelles métier, non normalisées :
/// la traction en pour mille, la vitesse limite en km/h. La conversion vers les
/// unités internes de la simulation (m/s) est faite par PosteDeCommande, en un
/// seul endroit.
/// </summary>
[Serializable]
public struct CommandeTrain
{
    /// <summary>Consigne de traction, de 0 à 1000 pour mille.</summary>
    public int tractionPourMille;

    public bool freinService;
    public bool freinUrgence;

    public bool sensAvant;
    public bool sensArriere;

    /// <summary>Vitesse maximale autorisée, en km/h.</summary>
    public int vitesseLimiteKmh;

    /// <summary>
    /// Position du convoi sur la voie, en décimètres, telle que l'automate
    /// la calcule. Négative si l'automate ne la fournit pas.
    /// </summary>
    public int positionDecimetres;


    /// <summary>
    /// Ordre de repli : traction nulle, frein d'urgence serré, aucun sens
    /// engagé. C'est l'état appliqué tant qu'aucune source de commande ne
    /// s'est manifestée, et celui vers lequel on retombe si la liaison tombe.
    /// </summary>
    public static CommandeTrain Securite => new CommandeTrain
    {
        tractionPourMille = 0,
        freinService = false,
        freinUrgence = true,
        sensAvant = false,
        sensArriere = false,
        vitesseLimiteKmh = 0,
        positionDecimetres = -1
    };


    /// <summary>Position en mètres, ou -1 si l'automate ne la fournit pas.</summary>
    public float PositionMetres =>
        positionDecimetres < 0 ? -1f : positionDecimetres / 10f;


    /// <summary>Traction effective, de 0 à 1, une fois les freins pris en compte.</summary>
    public float TractionEffective
    {
        get
        {
            if (freinUrgence || freinService)
                return 0f;

            // Sans sens engagé, la traction n'a pas de sens physique
            if (sensAvant == sensArriere)
                return 0f;

            return UnityEngine.Mathf.Clamp01(tractionPourMille / 1000f);
        }
    }
}
