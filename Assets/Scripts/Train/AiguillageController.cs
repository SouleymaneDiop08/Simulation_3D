using UnityEngine;


/// <summary>
/// Aiguillage à deux positions, avec délai de manœuvre et contrôle.
///
/// La distinction commande / contrôle est le fondement d'un enclenchement :
/// un poste réel n'autorise jamais un mouvement sur la seule commande, il
/// attend le CONTRÔLE que l'aiguille a physiquement bougé et s'est verrouillée.
///
/// C'est aussi ce qui rend le banc intéressant : l'automate peut commander une
/// manœuvre, la simulation peut la refuser — et un attaquant qui contourne ce
/// refus produit l'accident.
/// </summary>
public class AiguillageController : MonoBehaviour
{
    public enum PositionAiguille
    {
        Principale = 0,
        Deviation = 1,
        EnManoeuvre = 2
    }


    [Header("Voies")]
    public TrackSystem voiePrincipale;
    public TrackSystem voieDeviation;


    [Header("Manœuvre")]
    [Tooltip("Durée de déplacement des lames, en secondes. Pendant ce temps " +
             "l'aiguille n'est ni d'un côté ni de l'autre.")]
    public float dureeManoeuvre = 4f;

    [Tooltip("Refuser la manœuvre tant qu'un convoi occupe l'aiguille. " +
             "Décocher pour reproduire une défaillance d'enclenchement.")]
    public bool enclenchementActif = true;


    [Header("Diagnostic (lecture seule)")]
    [Tooltip("Ce que l'automate a demandé.")]
    public bool deviationCommandee = false;

    [Tooltip("Ce que l'aiguille fait réellement.")]
    public PositionAiguille controle = PositionAiguille.Principale;

    [Tooltip("Vrai lorsqu'un convoi occupe l'aiguille.")]
    public bool occupee = false;


    private float _tempsManoeuvre;
    private bool _cibleDeviation;


    /// <summary>Voie effectivement en service. Nulle pendant une manœuvre.</summary>
    public TrackSystem GetVoieActive()
    {
        switch (controle)
        {
            case PositionAiguille.Deviation: return voieDeviation;
            case PositionAiguille.Principale: return voiePrincipale;
            default: return null;
        }
    }


    private void Update()
    {
        if (controle != PositionAiguille.EnManoeuvre)
            return;

        _tempsManoeuvre += Time.deltaTime;

        if (_tempsManoeuvre < dureeManoeuvre)
            return;

        controle = _cibleDeviation
            ? PositionAiguille.Deviation
            : PositionAiguille.Principale;

        Debug.Log($"[Aiguillage] {name} : contrôle établi — {controle}", this);
    }


    /// <summary>
    /// Commande de position, transmise par l'automate. La manœuvre n'est
    /// engagée que si l'aiguille est libre.
    /// </summary>
    public void CommanderDeviation(bool deviation)
    {
        deviationCommandee = deviation;

        bool dejaEnPlace =
            (deviation && controle == PositionAiguille.Deviation) ||
            (!deviation && controle == PositionAiguille.Principale);

        if (dejaEnPlace || controle == PositionAiguille.EnManoeuvre)
            return;

        if (enclenchementActif && occupee)
        {
            Debug.LogWarning(
                $"[Aiguillage] {name} : manœuvre refusée, aiguille occupée.", this);
            return;
        }

        _cibleDeviation = deviation;
        _tempsManoeuvre = 0f;
        controle = PositionAiguille.EnManoeuvre;

        Debug.Log($"[Aiguillage] {name} : manœuvre vers {(deviation ? "DÉVIATION" : "PRINCIPALE")}", this);
    }


    // Conservées pour compatibilité avec les déclencheurs existants
    public void ActiverDeviation() => CommanderDeviation(true);
    public void ActiverPrincipale() => CommanderDeviation(false);


    // ==========================================================
    // ESSAI DEPUIS L'ÉDITEUR
    //
    // « controle » est un compte rendu, pas une commande : l'écrire à la main
    // dans l'inspecteur place l'aiguille sans manœuvre, sans délai et sans
    // enclenchement — donc sans rien de ce qui fait l'intérêt du banc. Ces
    // deux entrées, accessibles par un clic droit sur le composant en mode
    // Play, passent par la vraie chaîne de commande.
    // ==========================================================

    [ContextMenu("Commander la déviation")]
    private void EssaiDeviation() => CommanderDeviation(true);

    [ContextMenu("Commander la voie directe")]
    private void EssaiPrincipale() => CommanderDeviation(false);
}
