using UnityEngine;

public class SimulationManager : MonoBehaviour
{
    public static SimulationManager Instance;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        Debug.Log("Simulation ferroviaire démarrée");
    }
}