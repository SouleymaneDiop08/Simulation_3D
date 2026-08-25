using UnityEngine;


/// <summary>
/// Point d'entrée de la simulation. Persiste entre les scènes.
/// </summary>
public class SimulationManager : MonoBehaviour
{
    public static SimulationManager Instance { get; private set; }


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }


    private void OnDestroy()
    {
        // Sans cela, l'instance statique gardait une référence sur un objet
        // détruit après un rechargement de scène.
        if (Instance == this)
            Instance = null;
    }


    private void Start()
    {
        Debug.Log("Simulation ferroviaire démarrée");
    }
}
