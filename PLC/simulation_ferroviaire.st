(*
    ==================================================================
    SIMULATION FERROVIAIRE - PROGRAMME DE COMMANDE
    ------------------------------------------------------------------
    Cible      : OpenPLC Runtime v3  (compilateur MatIEC)
    Langage    : IEC 61131-3 Structured Text
    Periode    : 50 ms (voir bloc CONFIGURATION en fin de fichier)

    Role : l'automate est la SOURCE. Il produit les ordres de conduite,
    la position des aiguilles et l'aspect des signaux. Unity se contente
    de lire ces valeurs en Modbus TCP et de les appliquer.

    Toutes les sorties sont mappees sur des adresses %QX / %QW, donc
    exposees en Coils et Holding Registers : lisibles par un client
    Modbus sans aucun contournement.

    Voir PLC/README-modbus.md pour la table d'adresses cote Unity.
    ==================================================================
*)


(*
    ------------------------------------------------------------------
    BLOC FONCTIONNEL : rampe sur entier non signe
    ------------------------------------------------------------------
    Fait evoluer une sortie vers une consigne par pas fixes, sans
    a-coup. Les soustractions sont gardees pour eviter tout
    depassement par le bas sur un UINT.
*)
FUNCTION_BLOCK RAMPE_UINT
VAR_INPUT
    Consigne : UINT;
    Pas      : UINT;
END_VAR
VAR_OUTPUT
    Sortie   : UINT;
END_VAR

IF Sortie < Consigne THEN

    IF (Consigne - Sortie) > Pas THEN
        Sortie := Sortie + Pas;
    ELSE
        Sortie := Consigne;
    END_IF;

ELSIF Sortie > Consigne THEN

    IF (Sortie - Consigne) > Pas THEN
        Sortie := Sortie - Pas;
    ELSE
        Sortie := Consigne;
    END_IF;

END_IF;

END_FUNCTION_BLOCK



PROGRAM main
VAR CONSTANT
    (* Doit correspondre a l'INTERVAL de la TASK declaree plus bas *)
    PERIODE_MS   : UDINT := 50;

    (* 5 pour mille par scan a 50 ms = 0 -> 100 % en 10 s *)
    PAS_RAMPE    : UINT  := 5;

    (* Aspects de signal *)
    CARRE        : UINT  := 0;   (* arret absolu *)
    AVERTISSEMENT: UINT  := 1;   (* prochain signal ferme *)
    VOIE_LIBRE   : UINT  := 2;
END_VAR

VAR
    (* ==============================================================
       ENTREE : arret d'urgence externe
       Forcable a la main depuis la page Monitoring d'OpenPLC.
       ============================================================== *)
    AU_Externe        AT %IX0.0 : BOOL;


    (* ==============================================================
       SORTIES TRAIN 1
       ============================================================== *)
    T1_Traction       AT %QW0  : UINT;   (* consigne 0..1000 pour mille *)
    T1_FreinService   AT %QX0.0 : BOOL;
    T1_FreinUrgence   AT %QX0.1 : BOOL;
    T1_SensAvant      AT %QX0.2 : BOOL;
    T1_SensArriere    AT %QX0.3 : BOOL;

    (* ==============================================================
       SORTIES TRAIN 2
       ============================================================== *)
    T2_Traction       AT %QW1  : UINT;
    T2_FreinService   AT %QX0.4 : BOOL;
    T2_FreinUrgence   AT %QX0.5 : BOOL;
    T2_SensAvant      AT %QX0.6 : BOOL;
    T2_SensArriere    AT %QX0.7 : BOOL;

    (* ==============================================================
       AIGUILLAGES
       TRUE  = voie deviee
       FALSE = voie principale
       ============================================================== *)
    AIG1_Deviation    AT %QX1.0 : BOOL;
    AIG2_Deviation    AT %QX1.1 : BOOL;

    (* ==============================================================
       SIGNALISATION
       0 = carre, 1 = avertissement, 2 = voie libre
       ============================================================== *)
    SIG1_Aspect       AT %QW2  : UINT;
    SIG2_Aspect       AT %QW3  : UINT;

    (* Vitesse autorisee affichee en cabine, en km/h *)
    T1_VitesseLimite  AT %QW4  : UINT;
    T2_VitesseLimite  AT %QW5  : UINT;

    (* ==============================================================
       SUPERVISION
       ============================================================== *)
    Etape             AT %QW10 : UINT;   (* etape courante du scenario *)
    Heartbeat         AT %QX2.0 : BOOL;  (* bascule a 1 Hz *)
    ScenarioActif     AT %QX2.1 : BOOL;
END_VAR


(* ==================================================================
   VARIABLES INTERNES
   ------------------------------------------------------------------
   Bloc VAR distinct, et ce n'est pas cosmetique : MatIEC refuse de
   melanger des variables localisees (AT %...) et des variables
   ordinaires dans un meme bloc. Les regrouper produit une cascade
   d'erreurs « invalid located variable declaration ».
   ================================================================== *)
VAR
    ConsigneT1   : UINT;      (* cible de la rampe train 1 *)
    ConsigneT2   : UINT;
    RampeT1      : RAMPE_UINT;
    RampeT2      : RAMPE_UINT;

    EtapePrec    : UINT;
    TempsEtape   : UDINT;     (* temps passe dans l'etape, en ms *)

    TmrCoeur     : TON;       (* oscillateur du chien de garde *)
END_VAR


(*
    ------------------------------------------------------------------
    1. CHIEN DE GARDE
    ------------------------------------------------------------------
    Heartbeat bascule toutes les 500 ms. Cote Unity, surveillez ce bit :
    s'il cesse de changer d'etat pendant plus de 2 s, la liaison est
    perdue et il faut appliquer le frein d'urgence de vous-meme.
    Sans ce mecanisme, une coupure reseau laisse le train lance a la
    derniere consigne recue.
*)
TmrCoeur(IN := NOT TmrCoeur.Q, PT := T#500ms);

IF TmrCoeur.Q THEN
    Heartbeat := NOT Heartbeat;
END_IF;


(*
    ------------------------------------------------------------------
    2. BASE DE TEMPS DU SEQUENCEUR
    ------------------------------------------------------------------
    TempsEtape est remis a zero a chaque changement d'etape, puis
    incremente d'une periode de scan. Plus lisible et plus deterministe
    qu'un TON par etape.
*)
IF Etape <> EtapePrec THEN
    TempsEtape := 0;
    EtapePrec  := Etape;
ELSE
    TempsEtape := TempsEtape + PERIODE_MS;
END_IF;


(*
    ------------------------------------------------------------------
    3. ARRET D'URGENCE PRIORITAIRE
    ------------------------------------------------------------------
    Traite avant le sequenceur : il doit gagner sur toute autre logique.
*)
IF AU_Externe THEN
    Etape := 999;
END_IF;


(*
    ------------------------------------------------------------------
    4. SEQUENCEUR DE CONDUITE
    ------------------------------------------------------------------
*)
CASE Etape OF

    (* ---------- 0 : INITIALISATION ---------- *)
    0:
        ScenarioActif   := FALSE;

        ConsigneT1      := 0;
        ConsigneT2      := 0;

        T1_FreinUrgence := TRUE;
        T1_FreinService := FALSE;
        T1_SensAvant    := FALSE;
        T1_SensArriere  := FALSE;

        T2_FreinUrgence := TRUE;
        T2_FreinService := FALSE;
        T2_SensAvant    := FALSE;
        T2_SensArriere  := FALSE;

        AIG1_Deviation  := FALSE;
        AIG2_Deviation  := FALSE;

        SIG1_Aspect     := CARRE;
        SIG2_Aspect     := CARRE;

        T1_VitesseLimite := 0;
        T2_VitesseLimite := 0;

        IF TempsEtape >= 1000 THEN
            Etape := 10;
        END_IF;


    (* ---------- 10 : ETABLISSEMENT DE L'ITINERAIRE ---------- *)
    (* Les aiguilles sont commandees en position principale. On laisse
       un temps de manoeuvre avant d'ouvrir le signal : c'est ce delai
       qui rend le comportement credible. *)
    10:
        ScenarioActif   := TRUE;

        AIG1_Deviation  := FALSE;
        AIG2_Deviation  := FALSE;

        SIG1_Aspect     := CARRE;

        IF TempsEtape >= 4000 THEN
            Etape := 20;
        END_IF;


    (* ---------- 20 : OUVERTURE DU SIGNAL ET DESSERRAGE ---------- *)
    20:
        SIG1_Aspect      := VOIE_LIBRE;
        T1_VitesseLimite := 70;

        T1_FreinUrgence  := FALSE;
        T1_FreinService  := FALSE;
        T1_SensAvant     := TRUE;
        T1_SensArriere   := FALSE;

        IF TempsEtape >= 1500 THEN
            Etape := 30;
        END_IF;


    (* ---------- 30 : MISE EN VITESSE ---------- *)
    30:
        ConsigneT1 := 600;              (* 60 % de traction *)

        IF TempsEtape >= 15000 THEN
            Etape := 40;
        END_IF;


    (* ---------- 40 : APPROCHE DE LA ZONE LIMITEE ---------- *)
    (* Le signal passe a l'avertissement et la consigne retombe : le
       train doit etre ralenti AVANT d'aborder l'aiguille deviee. *)
    40:
        SIG2_Aspect      := AVERTISSEMENT;
        T1_VitesseLimite := 40;

        ConsigneT1       := 250;
        T1_FreinService  := TRUE;

        IF TempsEtape >= 8000 THEN
            Etape := 50;
        END_IF;


    (* ---------- 50 : MANOEUVRE DE L'AIGUILLE ---------- *)
    (* On ferme le signal avant de bouger l'aiguille, jamais l'inverse. *)
    50:
        SIG2_Aspect     := CARRE;
        T1_FreinService := TRUE;
        ConsigneT1      := 0;

        IF TempsEtape >= 2000 THEN
            AIG1_Deviation := TRUE;
        END_IF;

        IF TempsEtape >= 6000 THEN
            Etape := 60;
        END_IF;


    (* ---------- 60 : FRANCHISSEMENT EN DEVIATION ---------- *)
    60:
        SIG2_Aspect      := VOIE_LIBRE;
        T1_VitesseLimite := 40;

        T1_FreinService  := FALSE;
        ConsigneT1       := 300;

        IF TempsEtape >= 12000 THEN
            Etape := 70;
        END_IF;


    (* ---------- 70 : FREINAGE D'ARRIVEE ---------- *)
    70:
        SIG2_Aspect      := CARRE;
        T1_VitesseLimite := 0;

        ConsigneT1       := 0;
        T1_FreinService  := TRUE;

        IF TempsEtape >= 10000 THEN
            Etape := 80;
        END_IF;


    (* ---------- 80 : STATIONNEMENT ---------- *)
    80:
        T1_FreinUrgence := TRUE;
        T1_SensAvant    := FALSE;

        IF TempsEtape >= 5000 THEN
            Etape := 90;
        END_IF;


    (* ---------- 90 : REPRISE DU CYCLE ---------- *)
    90:
        AIG1_Deviation := FALSE;

        IF TempsEtape >= 3000 THEN
            Etape := 0;
        END_IF;


    (* ---------- 999 : ARRET D'URGENCE ---------- *)
    999:
        ScenarioActif   := FALSE;

        ConsigneT1      := 0;
        ConsigneT2      := 0;

        T1_FreinUrgence := TRUE;
        T2_FreinUrgence := TRUE;
        T1_FreinService := FALSE;
        T2_FreinService := FALSE;

        SIG1_Aspect     := CARRE;
        SIG2_Aspect     := CARRE;

        T1_VitesseLimite := 0;
        T2_VitesseLimite := 0;

        (* Reprise seulement apres retombee de l'AU, et jamais
           directement en marche : on repasse par l'initialisation. *)
        IF NOT AU_Externe AND TempsEtape >= 2000 THEN
            Etape := 0;
        END_IF;

ELSE
    (* Etape inconnue : repli sur l'arret d'urgence *)
    Etape := 999;

END_CASE;


(*
    ------------------------------------------------------------------
    5. APPLICATION DES RAMPES DE TRACTION
    ------------------------------------------------------------------
    Le sequenceur ne manipule que des consignes. La sortie envoyee a la
    simulation passe toujours par une rampe : pas de saut de traction.
*)
RampeT1(Consigne := ConsigneT1, Pas := PAS_RAMPE);
T1_Traction := RampeT1.Sortie;

RampeT2(Consigne := ConsigneT2, Pas := PAS_RAMPE);
T2_Traction := RampeT2.Sortie;


(*
    ------------------------------------------------------------------
    6. SECURITES FINALES
    ------------------------------------------------------------------
    Appliquees en dernier pour qu'aucune etape ne puisse les contredire.
*)

(* Un frein serre annule toute traction *)
IF T1_FreinUrgence OR T1_FreinService THEN
    T1_Traction := 0;
END_IF;

IF T2_FreinUrgence OR T2_FreinService THEN
    T2_Traction := 0;
END_IF;

(* Les deux sens ne peuvent jamais etre commandes ensemble *)
IF T1_SensAvant AND T1_SensArriere THEN
    T1_SensAvant   := FALSE;
    T1_SensArriere := FALSE;
END_IF;

IF T2_SensAvant AND T2_SensArriere THEN
    T2_SensAvant   := FALSE;
    T2_SensArriere := FALSE;
END_IF;

(* Sans sens engage, pas de traction *)
IF NOT T1_SensAvant AND NOT T1_SensArriere THEN
    T1_Traction := 0;
END_IF;

IF NOT T2_SensAvant AND NOT T2_SensArriere THEN
    T2_Traction := 0;
END_IF;

END_PROGRAM



CONFIGURATION Config0

    RESOURCE Res0 ON PLC
        TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
        PROGRAM instance0 WITH task0 : main;
    END_RESOURCE

END_CONFIGURATION
