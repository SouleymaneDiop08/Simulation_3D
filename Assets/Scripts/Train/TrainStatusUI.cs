using UnityEngine;
using TMPro;

public class TrainStatusUI : MonoBehaviour
{
    public TrainPhysicsController train;

    public TMP_Text vitesseTexte;
    public TMP_Text etatTexte;


    void Update()
    {
        if(train == null)
            return;


        vitesseTexte.text =
            "Vitesse : "
            + train.vitesseActuelle.ToString("0");


        if(train.vitesseActuelle > 0)
            etatTexte.text = "Etat : En marche";
        else
            etatTexte.text = "Etat : Arrêt";
    }
}