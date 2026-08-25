using UnityEngine;

public class SouffletController : MonoBehaviour
{
    public Transform attacheArriere;
    public Transform attacheAvant;

    public Transform boneDebut;
    public Transform boneMilieu;
    public Transform boneFin;


    private float longueurInitiale;


    void Start()
    {
        longueurInitiale =
            Vector3.Distance(
                attacheArriere.position,
                attacheAvant.position
            );
    }


    void Update()
    {
        if (attacheArriere == null ||
            attacheAvant == null)
            return;


        float longueurActuelle =
            Vector3.Distance(
                attacheArriere.position,
                attacheAvant.position
            );


        float ratio =
            longueurActuelle / longueurInitiale;


        // Compression / extension du milieu
        Vector3 scale =
            boneMilieu.localScale;

        scale.z = ratio;

        boneMilieu.localScale = scale;


        // Rotation du soufflet dans le virage
        Vector3 direction =
            attacheAvant.position -
            attacheArriere.position;


        Quaternion rotation =
            Quaternion.LookRotation(
                direction,
                Vector3.up
            );


        boneMilieu.rotation = rotation;
    }
}