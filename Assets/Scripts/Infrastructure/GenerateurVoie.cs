using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Construit les rails, les traverses et le ballast le long d'une voie, à
/// partir de sa spline.
///
/// C'est la seule façon d'avoir des rails qui suivent VRAIMENT le tracé. Les
/// rails d'origine vivaient dans un maillage figé : ils ne pouvaient pas
/// s'écarter avec les voies, ni suivre un appareil de voie — d'où une
/// déviation que rien ne matérialisait au sol.
///
/// Le maillage est balayé le long de la spline : à chaque échantillon on
/// calcule la tangente et la normale horizontale, puis on place les sections.
/// Les rails restent donc parallèles au tracé y compris en courbe, là où un
/// simple décalage latéral constant les ferait déborder.
/// </summary>
[RequireComponent(typeof(TrackSystem))]
[DisallowMultipleComponent]
public class GenerateurVoie : MonoBehaviour
{
    [Header("Rails")]
    [Tooltip("Écartement entre les deux files, en mètres, d'axe à axe.")]
    public float ecartement = 3f;

    [Tooltip("Largeur du champignon du rail, en mètres.")]
    public float largeurRail = 0.35f;

    [Tooltip("Hauteur du rail au-dessus du ballast, en mètres.")]
    public float hauteurRail = 0.35f;


    [Header("Traverses")]
    public bool genererTraverses = true;

    [Tooltip("Intervalle entre deux traverses, en mètres.")]
    [Min(0.5f)]
    public float espacementTraverses = 3f;

    public float largeurTraverse = 1.2f;
    public float epaisseurTraverse = 0.25f;

    [Tooltip("Débord de la traverse au-delà des rails, de chaque côté.")]
    public float debordTraverse = 0.9f;


    [Header("Ballast")]
    public bool genererBallast = true;

    [Tooltip("Demi-largeur de la plateforme, en mètres.")]
    public float largeurBallast = 4f;

    public float epaisseurBallast = 0.3f;


    [Header("Portion générée")]
    [Tooltip("Distance de début le long de la voie, en mètres.")]
    public float distanceDebut = 0f;

    [Tooltip("Distance de fin, en mètres. Négatif = jusqu'au bout de la voie.\n\n" +
             "Sert aux itinéraires déviés : une déviation partage l'essentiel " +
             "de son tracé avec la voie directe, et n'a de rails propres que " +
             "sur sa portion divergente. Sans cette plage, on poserait une " +
             "seconde voie par-dessus la première sur toute la ligne.")]
    public float distanceFin = -1f;


    [Header("Échantillonnage")]
    [Tooltip("Pas de balayage le long de la voie, en mètres. Plus fin = plus " +
             "lisse en courbe, plus lourd en géométrie.")]
    [Min(0.5f)]
    public float pasEchantillon = 4f;


    [Header("Apparence")]
    public Material materiauRail;
    public Material materiauTraverse;
    public Material materiauBallast;

    public Color couleurRail = new Color(0.42f, 0.42f, 0.45f);
    public Color couleurTraverse = new Color(0.30f, 0.26f, 0.22f);
    public Color couleurBallast = new Color(0.38f, 0.36f, 0.34f);


    private TrackSystem _voie;
    private Transform _racine;


    private void Start()
    {
        Construire();
    }


    [ContextMenu("Reconstruire la voie")]
    public void Construire()
    {
        _voie = GetComponent<TrackSystem>();

        if (_voie == null || !_voie.Pret)
        {
            Debug.LogWarning($"[Voie] {name} : tracé indisponible, rien à construire.", this);
            return;
        }

        Nettoyer();

        _racine = new GameObject("Voie générée").transform;
        _racine.SetParent(transform, false);

        // Échantillonnage commun : la géométrie des trois éléments s'appuie
        // sur les mêmes repères, donc tout reste solidaire.
        List<Repere> reperes = Echantillonner();

        if (reperes.Count < 2)
            return;

        if (genererBallast)
            Poser("Ballast", Ruban(reperes, largeurBallast, -epaisseurBallast, 0f),
                  materiauBallast, couleurBallast);

        if (genererTraverses)
            Poser("Traverses", Traverses(reperes), materiauTraverse, couleurTraverse);

        Poser("Rails", Rails(reperes), materiauRail, couleurRail);

        float fin = distanceFin < 0f ? _voie.Longueur : distanceFin;

        Debug.Log($"[Voie] {name} : voie générée de {distanceDebut:0} à {fin:0} m " +
                  $"({reperes.Count} sections).", this);
    }


    private void Nettoyer()
    {
        Transform ancienne = transform.Find("Voie générée");

        if (ancienne == null)
            return;

        if (Application.isPlaying)
            Destroy(ancienne.gameObject);
        else
            DestroyImmediate(ancienne.gameObject);
    }


    // ==================================================================
    // ÉCHANTILLONNAGE
    // ==================================================================

    private struct Repere
    {
        public Vector3 position;
        public Vector3 lateral;   // normale horizontale, unitaire
    }


    private List<Repere> Echantillonner()
    {
        List<Repere> reperes = new List<Repere>();

        float debut = Mathf.Clamp(distanceDebut, 0f, _voie.Longueur);
        float fin = distanceFin < 0f ? _voie.Longueur : Mathf.Clamp(distanceFin, debut, _voie.Longueur);
        float portee = fin - debut;

        if (portee <= 0.01f)
            return reperes;

        int n = Mathf.Max(2, Mathf.CeilToInt(portee / pasEchantillon));

        for (int i = 0; i <= n; i++)
        {
            float d = debut + portee * i / n;

            Vector3 avant = _voie.GetDirection(d);
            Vector3 lateral = Vector3.Cross(Vector3.up, avant);

            // En ligne droite verticale la normale dégénère : on garde la
            // précédente plutôt que de produire un vecteur nul.
            if (lateral.sqrMagnitude < 1e-6f)
                lateral = reperes.Count > 0 ? reperes[reperes.Count - 1].lateral : Vector3.right;

            reperes.Add(new Repere
            {
                position = _voie.GetPosition(d),
                lateral = lateral.normalized
            });
        }

        return reperes;
    }


    // ==================================================================
    // GÉOMÉTRIE
    // ==================================================================

    /// <summary>
    /// Ajoute un ruban horizontal au maillage en cours : une bande suivant le
    /// tracé, décalée latéralement et d'épaisseur donnée. Sert au ballast comme
    /// à chaque file de rail.
    /// </summary>
    private void AjouterRuban(List<Vector3> sommets, List<int> triangles,
                              List<Repere> reperes, float demiLargeur,
                              float bas, float haut, float decalage)
    {
        int depart = sommets.Count;

        foreach (Repere r in reperes)
        {
            Vector3 centre = r.position + r.lateral * decalage;
            Vector3 g = centre - r.lateral * demiLargeur;
            Vector3 d = centre + r.lateral * demiLargeur;

            sommets.Add(transform.InverseTransformPoint(g + Vector3.up * bas));
            sommets.Add(transform.InverseTransformPoint(d + Vector3.up * bas));
            sommets.Add(transform.InverseTransformPoint(g + Vector3.up * haut));
            sommets.Add(transform.InverseTransformPoint(d + Vector3.up * haut));
        }

        for (int i = 0; i < reperes.Count - 1; i++)
        {
            int a = depart + i * 4;
            int b = depart + (i + 1) * 4;

            Quad(triangles, a + 2, b + 2, b + 3, a + 3);   // dessus
            Quad(triangles, a, b, b + 2, a + 2);           // flanc gauche
            Quad(triangles, a + 3, b + 3, b + 1, a + 1);   // flanc droit
        }
    }


    private Mesh Ruban(List<Repere> reperes, float demiLargeur, float bas, float haut,
                       float decalage = 0f)
    {
        List<Vector3> sommets = new List<Vector3>();
        List<int> triangles = new List<int>();

        AjouterRuban(sommets, triangles, reperes, demiLargeur, bas, haut, decalage);

        return Assembler(sommets, triangles);
    }


    /// <summary>
    /// Les deux files dans un seul maillage. Les assembler directement évite
    /// un CombineMeshes, et garde un seul Renderer pour toute la voie.
    /// </summary>
    private Mesh Rails(List<Repere> reperes)
    {
        List<Vector3> sommets = new List<Vector3>();
        List<int> triangles = new List<int>();

        float demi = largeurRail * 0.5f;

        AjouterRuban(sommets, triangles, reperes, demi, 0f, hauteurRail, -ecartement * 0.5f);
        AjouterRuban(sommets, triangles, reperes, demi, 0f, hauteurRail, ecartement * 0.5f);

        return Assembler(sommets, triangles);
    }


    private Mesh Traverses(List<Repere> reperes)
    {
        List<Vector3> sommets = new List<Vector3>();
        List<int> triangles = new List<int>();

        float demiLongueur = ecartement * 0.5f + debordTraverse;
        float demiLargeur = largeurTraverse * 0.5f;

        int pas = Mathf.Max(1, Mathf.RoundToInt(espacementTraverses / pasEchantillon));

        for (int i = 0; i < reperes.Count; i += pas)
        {
            Repere r = reperes[i];

            // L'axe longitudinal se déduit du voisin : une traverse est un
            // pavé posé en travers, elle suit donc l'orientation locale.
            Vector3 suivant = reperes[Mathf.Min(i + 1, reperes.Count - 1)].position;
            Vector3 avant = (suivant - r.position);

            if (avant.sqrMagnitude < 1e-6f)
                avant = Vector3.Cross(r.lateral, Vector3.up);

            avant = avant.normalized;

            int b = sommets.Count;

            for (int cote = 0; cote < 2; cote++)
            {
                float y = cote == 0 ? -epaisseurTraverse : 0f;

                foreach (int s in new[] { -1, 1 })
                foreach (int l in new[] { -1, 1 })
                {
                    Vector3 p = r.position
                              + r.lateral * (demiLongueur * s)
                              + avant * (demiLargeur * l)
                              + Vector3.up * y;

                    sommets.Add(transform.InverseTransformPoint(p));
                }
            }

            // dessus, puis les quatre flancs
            Quad(triangles, b + 4, b + 5, b + 7, b + 6);
            Quad(triangles, b, b + 1, b + 5, b + 4);
            Quad(triangles, b + 2, b + 6, b + 7, b + 3);
            Quad(triangles, b, b + 4, b + 6, b + 2);
            Quad(triangles, b + 1, b + 3, b + 7, b + 5);
        }

        return Assembler(sommets, triangles);
    }


    private static void Quad(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(a); triangles.Add(c); triangles.Add(d);
    }


    private static Mesh Assembler(List<Vector3> sommets, List<int> triangles)
    {
        Mesh maillage = new Mesh();

        // Au-delà de 65 535 sommets il faut des indices 32 bits : une voie de
        // 1860 m échantillonnée finement les dépasse aisément.
        maillage.indexFormat = sommets.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        maillage.SetVertices(sommets);
        maillage.SetTriangles(triangles, 0);
        maillage.RecalculateNormals();
        maillage.RecalculateBounds();

        return maillage;
    }


    private void Poser(string nom, Mesh maillage, Material materiau, Color couleur)
    {
        GameObject objet = new GameObject(nom);
        objet.transform.SetParent(_racine, false);

        objet.AddComponent<MeshFilter>().sharedMesh = maillage;

        MeshRenderer rendu = objet.AddComponent<MeshRenderer>();
        rendu.sharedMaterial = materiau != null ? materiau : MateriauParDefaut(nom, couleur);
    }


    /// <summary>
    /// Matériau de secours, créé à la volée. Le projet est en pipeline
    /// intégré : le shader Standard est donc toujours disponible.
    /// </summary>
    private static Material MateriauParDefaut(string nom, Color couleur)
    {
        Shader shader = Shader.Find("Standard");

        if (shader == null)
            shader = Shader.Find("Diffuse");

        Material materiau = new Material(shader) { name = "Voie — " + nom };
        materiau.color = couleur;

        if (materiau.HasProperty("_Glossiness"))
            materiau.SetFloat("_Glossiness", 0.25f);

        return materiau;
    }
}
