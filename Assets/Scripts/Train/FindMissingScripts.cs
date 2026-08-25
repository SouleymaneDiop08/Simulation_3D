using UnityEngine;


public class FindMissingScripts : MonoBehaviour
{

    void Start()
    {

        GameObject[] objets =
            Object.FindObjectsByType<GameObject>();



        foreach(GameObject obj in objets)
        {

            Component[] composants =
                obj.GetComponents<Component>();


            foreach(Component c in composants)
            {

                if(c == null)
                {
                    Debug.Log(
                        "SCRIPT MANQUANT SUR : "
                        + obj.name
                    );
                }

            }

        }

    }

}