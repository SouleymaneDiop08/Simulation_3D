#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;


/// <summary>
/// Build WebGL reproductible, utilisable en ligne de commande.
///
///   Unity.exe -quit -batchmode -nographics ^
///             -projectPath "C:\chemin\SimulationFerroviaire" ^
///             -executeMethod BuildWebGL.Construire ^
///             -logFile build.log
///
/// La sortie va dans Build/WebGL, dossier que la passerelle sert par défaut.
/// </summary>
public static class BuildWebGL
{
    private const string DossierSortie = "Build/WebGL";


    [MenuItem("Simulation/Builder en WebGL")]
    public static void Construire()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Echouer("Aucune scène active dans les Build Settings.");
            return;
        }

        Debug.Log($"[Build] {scenes.Length} scène(s) : {string.Join(", ", scenes)}");

        // Compression désactivée : la passerelle sait servir .br et .gz, mais
        // sans compression on élimine une source d'erreur à la mise au point.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

        // Le build tient dans un onglet : pas de plein écran imposé.
        PlayerSettings.runInBackground = true;

        Directory.CreateDirectory(DossierSortie);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = DossierSortie,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None
        };

        BuildReport rapport = BuildPipeline.BuildPlayer(options);
        BuildSummary bilan = rapport.summary;

        if (bilan.result == BuildResult.Succeeded)
        {
            Debug.Log(
                $"[Build] Réussi — {bilan.totalSize / (1024 * 1024)} Mo " +
                $"en {bilan.totalTime.TotalSeconds:0} s\n" +
                $"[Build] Sortie : {Path.GetFullPath(DossierSortie)}\n" +
                $"[Build] Servir avec : cd PLC/bridge && npm start");

            if (Application.isBatchMode)
                EditorApplication.Exit(0);

            return;
        }

        Echouer($"Build {bilan.result} — {bilan.totalErrors} erreur(s).");
    }


    private static void Echouer(string message)
    {
        Debug.LogError("[Build] " + message);

        // En mode batch, sortir avec un code non nul pour qu'une chaîne
        // d'intégration détecte l'échec.
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}
#endif
