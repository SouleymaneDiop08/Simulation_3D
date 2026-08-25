using UnityEngine;


/// <summary>
/// Aiguillage à deux positions.
/// </summary>
public class AiguillageController : MonoBehaviour
{
    [Header("Voies")]
    public TrackSystem voiePrincipale;
    public TrackSystem voieDeviation;


    [Header("Diagnostic (lecture seule)")]
    public bool deviationActive = false;


    public TrackSystem GetVoieActive()
    {
        return deviationActive ? voieDeviation : voiePrincipale;
    }


    public void ActiverDeviation()
    {
        if (deviationActive)
            return;

        if (voieDeviation == null)
        {
            Debug.LogWarning($"[Aiguillage] {name} : voie de déviation non assignée.", this);
            return;
        }

        deviationActive = true;
        Debug.Log($"[Aiguillage] {name} : DÉVIATION", this);
    }


    public void ActiverPrincipale()
    {
        if (!deviationActive)
            return;

        if (voiePrincipale == null)
        {
            Debug.LogWarning($"[Aiguillage] {name} : voie principale non assignée.", this);
            return;
        }

        deviationActive = false;
        Debug.Log($"[Aiguillage] {name} : PRINCIPALE", this);
    }
}
