using UnityEngine;
using UnityEngine.InputSystem;

public class AiguillageTest : MonoBehaviour
{
    private AiguillageController aiguillage;


    void Start()
    {
        aiguillage = GetComponent<AiguillageController>();

        if (aiguillage == null)
        {
            Debug.LogError("Aucun AiguillageController trouvé !");
        }
        else
        {
            Debug.Log("AiguillageTest prêt");
        }
    }


    void Update()
    {
        if (Keyboard.current == null)
            return;


        if (Keyboard.current.dKey.wasPressedThisFrame)
        {
            Debug.Log("Touche D détectée");
            aiguillage.ActiverDeviation();
        }


        if (Keyboard.current.pKey.wasPressedThisFrame)
        {
            Debug.Log("Touche P détectée");
            aiguillage.ActiverPrincipale();
        }
    }
}