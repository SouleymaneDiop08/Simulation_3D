using System;


/// <summary>
/// Leviers dont l'automate dispose sur un convoi.
///
/// Volontairement pauvre : la navette conduit seule. L'automate ne pousse pas
/// le train, il agit sur trois choses seulement — jusqu'où il peut aller vite,
/// s'il a le droit de rouler, et s'il doit s'arrêter immédiatement.
///
/// C'est ce qui fait la valeur du banc : peu de leviers, mais chacun aux
/// conséquences physiques réelles.
/// </summary>
[Serializable]
public struct CommandeTrain
{
    /// <summary>
    /// Vitesse visée en ligne, en km/h. Négative si l'automate n'en impose
    /// pas — la navette retombe alors sur sa valeur nominale.
    ///
    /// Elle n'est PAS écrêtée à la vitesse nominale du matériel : une consigne
    /// aberrante doit produire une survitesse observable, pas disparaître
    /// silencieusement.
    /// </summary>
    public int consigneVitesseKmh;

    /// <summary>Freinage immédiat et immobilisation.</summary>
    public bool arretUrgence;

    /// <summary>Autorisation de circuler. À faux, le convoi reste à quai.</summary>
    public bool autorisee;


    /// <summary>
    /// Ordre de repli, appliqué tant qu'aucune source ne s'est manifestée.
    ///
    /// La consigne négative laisse la navette sur ses valeurs nominales, et
    /// l'autorisation reste acquise : sans automate, le procédé continue de
    /// tourner. Couper la liaison perturbe la supervision, pas l'installation.
    /// </summary>
    public static CommandeTrain Nominale => new CommandeTrain
    {
        consigneVitesseKmh = -1,
        arretUrgence = false,
        autorisee = true
    };
}
