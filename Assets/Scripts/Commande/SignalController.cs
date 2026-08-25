using UnityEngine;


/// <summary>Aspects présentés par un signal. Valeurs alignées sur le programme ST.</summary>
public enum AspectSignal
{
    Carre = 0,          // arrêt absolu
    Avertissement = 1,  // prochain signal fermé
    VoieLibre = 2
}


/// <summary>
/// Signal de voie. Reçoit son aspect de l'automate et l'affiche.
///
/// Volontairement passif : il n'agit pas sur les convois. Dans cette
/// architecture, c'est l'automate qui décide du freinage et l'envoie
/// explicitement. Un signal qui freinerait aussi de son côté créerait deux
/// autorités concurrentes sur le même train.
/// </summary>
public class SignalController : MonoBehaviour
{
    [Header("Identification")]
    public string nomSignal = "SIG1";


    [Header("Feux (facultatifs)")]
    [Tooltip("Objets activés selon l'aspect. Laisser vide ceux dont vous ne disposez pas.")]
    public GameObject feuRouge;
    public GameObject feuJaune;
    public GameObject feuVert;


    [Header("Lampe (facultative)")]
    [Tooltip("Éclairage coloré selon l'aspect, si le signal en porte un.")]
    public Light lampe;

    public Color couleurCarre = Color.red;
    public Color couleurAvertissement = new Color(1f, 0.75f, 0f);
    public Color couleurVoieLibre = Color.green;


    [Header("Diagnostic (lecture seule)")]
    public AspectSignal aspect = AspectSignal.Carre;


    private bool _initialise;


    private void Start()
    {
        // Au démarrage, un signal se présente fermé : c'est l'état sûr tant
        // qu'aucune commande n'est arrivée.
        _initialise = false;
        DefinirAspect(AspectSignal.Carre);
    }


    public void DefinirAspect(AspectSignal nouvelAspect)
    {
        if (_initialise && nouvelAspect == aspect)
            return;

        aspect = nouvelAspect;
        _initialise = true;

        if (feuRouge != null) feuRouge.SetActive(aspect == AspectSignal.Carre);
        if (feuJaune != null) feuJaune.SetActive(aspect == AspectSignal.Avertissement);
        if (feuVert != null) feuVert.SetActive(aspect == AspectSignal.VoieLibre);

        if (lampe != null)
        {
            switch (aspect)
            {
                case AspectSignal.Carre:
                    lampe.color = couleurCarre;
                    break;

                case AspectSignal.Avertissement:
                    lampe.color = couleurAvertissement;
                    break;

                default:
                    lampe.color = couleurVoieLibre;
                    break;
            }
        }
    }


    /// <summary>
    /// Convertit une valeur brute reçue de l'automate. Toute valeur inconnue
    /// est ramenée au carré : dans le doute, on ferme.
    /// </summary>
    public static AspectSignal DepuisEntier(int valeur)
    {
        switch (valeur)
        {
            case 1: return AspectSignal.Avertissement;
            case 2: return AspectSignal.VoieLibre;
            default: return AspectSignal.Carre;
        }
    }
}
