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

    [Tooltip("Refuser la déviation tant qu'un convoi occupe l'itinéraire visé.\n\n" +
             "DÉCOCHÉ PAR DÉFAUT : les deux navettes parcourent chacune toute " +
             "sa voie, si bien qu'un itinéraire est presque toujours occupé — " +
             "cocher cette case interdirait donc la déviation la plupart du " +
             "temps. Le choc n'immobilise plus rien : mieux vaut le montrer " +
             "que d'interdire la manœuvre. À cocher pour démontrer ce que " +
             "l'enclenchement empêche, et à décocher pour montrer ce qu'il en " +
             "coûte de s'en passer.")]
    public bool controleItineraire = false;

    [Tooltip("Distance en deçà de laquelle une caisse est considérée comme " +
             "posée sur une voie, en mètres.")]
    public float gabaritVoie = 4f;


    [Header("Diagnostic (lecture seule)")]
    [Tooltip("Ce que l'automate a demandé.")]
    public bool deviationCommandee = false;

    [Tooltip("Ce que l'aiguille fait réellement.")]
    public PositionAiguille controle = PositionAiguille.Principale;

    [Tooltip("Vrai lorsqu'un convoi occupe l'aiguille.")]
    public bool occupee = false;


    private float _tempsManoeuvre;
    private bool _cibleDeviation;
    private string _refusSignale;


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
            Refuser("aiguille occupée");
            return;
        }

        if (deviation && controleItineraire && ItineraireOccupe(out string qui))
        {
            Refuser($"itinéraire occupé par {qui}");
            return;
        }

        _refusSignale = null;

        _cibleDeviation = deviation;
        _tempsManoeuvre = 0f;
        controle = PositionAiguille.EnManoeuvre;

        Debug.Log($"[Aiguillage] {name} : manœuvre vers {(deviation ? "DÉVIATION" : "PRINCIPALE")}", this);
    }


    // ==========================================================
    // ENCLENCHEMENT D'ITINÉRAIRE
    // ==========================================================

    /// <summary>
    /// Vrai si un convoi se trouve sur la partie de la déviation qui lui est
    /// propre.
    ///
    /// Une déviation longe d'abord la voie directe avant de s'en écarter : le
    /// convoi qui se présente à l'aiguille est donc lui-même « sur » la
    /// déviation, au sens géométrique. On ne retient donc que les caisses qui
    /// sont sur la déviation SANS être sur la voie directe — c'est-à-dire
    /// au-delà de la traversée, là où l'itinéraire est réellement engagé.
    /// </summary>
    private bool ItineraireOccupe(out string qui)
    {
        qui = null;

        if (voieDeviation == null || !voieDeviation.Pret || PosteDeCommande.Instance == null)
            return false;

        foreach (TrainController train in PosteDeCommande.Instance.trains)
        {
            if (train == null || train.wagons == null)
                continue;

            foreach (WagonController wagon in train.wagons)
            {
                if (wagon == null)
                    continue;

                Vector3 p = wagon.transform.position;

                if (!SurVoie(p, voieDeviation))
                    continue;

                if (SurVoie(p, voiePrincipale))
                    continue;

                qui = train.name;
                return true;
            }
        }

        return false;
    }


    private bool SurVoie(Vector3 point, TrackSystem voie)
    {
        if (voie == null || !voie.Pret)
            return false;

        Vector3 surLaVoie = voie.GetPosition(voie.ProjeterDistance(point));

        return Vector3.Distance(surLaVoie, point) <= gabaritVoie;
    }


    /// <summary>Refuse une manœuvre sans inonder la console : un motif, un message.</summary>
    private void Refuser(string motif)
    {
        if (_refusSignale == motif)
            return;

        _refusSignale = motif;

        Debug.LogWarning($"[Aiguillage] {name} : manœuvre refusée — {motif}.", this);
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
