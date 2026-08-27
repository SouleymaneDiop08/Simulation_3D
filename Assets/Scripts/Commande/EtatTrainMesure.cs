using System;


/// <summary>
/// État du procédé, remonté vers l'automate.
///
/// C'est le pendant de CommandeTrain : là où celle-ci descend des ordres,
/// celui-ci fait remonter des mesures. Les deux ensemble ferment la boucle.
///
/// Unités choisies pour tenir dans des registres 16 bits non signés, sans
/// perdre en lisibilité côté programme ST.
/// </summary>
[Serializable]
public struct EtatTrainMesure
{
    /// <summary>Position sur la voie, en décimètres.</summary>
    public int positionDecimetres;

    /// <summary>Vitesse réelle, en km/h multipliés par dix.</summary>
    public int vitesseKmhDix;

    /// <summary>0 en ligne, 1 à quai, 2 accidenté, 3 déraillé.</summary>
    public int etat;

    /// <summary>Canton occupé, numéroté à partir de 1. Zéro si hors ligne.</summary>
    public int canton;


    public const int ETAT_EN_LIGNE = 0;
    public const int ETAT_A_QUAI = 1;
    public const int ETAT_ACCIDENTE = 2;
    public const int ETAT_DERAILLE = 3;
}
