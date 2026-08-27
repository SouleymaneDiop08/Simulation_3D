(*
    ==================================================================
    SIMULATION FERROVIAIRE - PROGRAMME DE COMMANDE
    ------------------------------------------------------------------
    Cible      : OpenPLC Runtime v3  (compilateur MatIEC)
    Langage    : IEC 61131-3 Structured Text
    Periode    : 50 ms (voir bloc CONFIGURATION en fin de fichier)

    Role : l'automate est la SOURCE. Il produit les ordres de conduite,
    la position des aiguilles et l'aspect des signaux. Unity se contente
    de lire ces valeurs et de les appliquer.

    EXPLOITATION SIMULEE
    Deux navettes font l'aller-retour entre deux gares, sur leurs voies
    respectives. Chaque convoi enchaine : depart, marche en ligne,
    approche ralentie, arret en gare, stationnement, puis repart en sens
    inverse. Les deux navettes sont decalees dans le temps pour qu'elles
    ne soient jamais en phase.

    Le mouvement est commande EN AVEUGLE : l'automate ne recoit aucun
    retour de la simulation. Les durees de marche sont donc calibrees
    pour rester en deca de la longueur de voie (~1860 m). Allonger
    DUREE_MARCHE ferait buter les convois en bout de voie.

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



(*
    ------------------------------------------------------------------
    BLOC FONCTIONNEL : navette entre deux gares
    ------------------------------------------------------------------
    Un cycle complet : gare A -> gare B -> gare A, indefiniment.

    Le meme bloc pilote les deux convois : modifier le cycle ici le
    modifie pour les deux. Le parametre Retard suffit a les desynchroniser.

    Le sequenceur ne produit que des CONSIGNES. Les rampes et les
    securites finales sont appliquees par le programme appelant, apres
    coup, et aucune etape ne peut les contourner.
*)
FUNCTION_BLOCK NAVETTE
VAR_INPUT
    (* Autorisation de circuler. A FALSE, la navette retombe au repos. *)
    Autorisee       : BOOL;

    (* Periode de scan, en ms : sert de base de temps au sequenceur. *)
    PeriodeMs       : UDINT;

    (* Decalage avant le tout premier depart, en ms. *)
    Retard          : UDINT;

    (* Consigne de traction en ligne, en pour mille. *)
    TractionMarche  : UINT;

    (* Vitesses affichees en cabine, en km/h. *)
    VitesseLigne    : UINT;
    VitesseApproche : UINT;

    (* Duree de la marche entre les deux gares, en ms. *)
    DureeMarche     : UDINT;
END_VAR

VAR_OUTPUT
    Traction        : UINT;
    FreinService    : BOOL;
    FreinUrgence    : BOOL;
    SensAvant       : BOOL;
    SensArriere     : BOOL;
    VitesseLimite   : UINT;

    Etape           : UINT;   (* etape courante, pour la supervision *)
    EnGare          : BOOL;   (* convoi a quai, signal ferme *)
    EnApproche      : BOOL;   (* convoi ralenti, signal a l'avertissement *)
    VersGareB       : BOOL;   (* sens courant du trajet *)
END_VAR

VAR
    Temps     : UDINT;
    EtapePrec : UINT;
END_VAR


(* Le poste peut retirer l'autorisation a tout moment *)
IF NOT Autorisee THEN
    Etape := 0;
END_IF;


(* Base de temps : remise a zero a chaque changement d'etape *)
IF Etape <> EtapePrec THEN
    Temps := 0;
    EtapePrec := Etape;
ELSE
    Temps := Temps + PeriodeMs;
END_IF;


CASE Etape OF

    (* ---------- 0 : REPOS ---------- *)
    0:
        Traction      := 0;
        FreinService  := FALSE;
        FreinUrgence  := TRUE;
        SensAvant     := FALSE;
        SensArriere   := FALSE;
        VitesseLimite := 0;
        EnGare        := TRUE;
        EnApproche    := FALSE;
        VersGareB     := TRUE;

        IF Autorisee AND Temps >= Retard THEN
            Etape := 10;
        END_IF;


    (* ---------- 10 : GARE A, preparation du depart ---------- *)
    10:
        Traction      := 0;
        FreinUrgence  := TRUE;
        FreinService  := FALSE;
        VitesseLimite := 0;
        EnGare        := TRUE;
        EnApproche    := FALSE;
        VersGareB     := TRUE;

        IF Temps >= 4000 THEN
            Etape := 20;
        END_IF;


    (* ---------- 20 : DEPART VERS B ---------- *)
    (* Desserrage et engagement du sens avant le premier effort *)
    20:
        FreinUrgence  := FALSE;
        FreinService  := FALSE;
        SensAvant     := TRUE;
        SensArriere   := FALSE;
        VitesseLimite := VitesseLigne;
        EnGare        := FALSE;

        IF Temps >= 1500 THEN
            Etape := 30;
        END_IF;


    (* ---------- 30 : MARCHE EN LIGNE VERS B ---------- *)
    30:
        Traction := TractionMarche;

        IF Temps >= DureeMarche THEN
            Etape := 40;
        END_IF;


    (* ---------- 40 : APPROCHE DE B ---------- *)
    40:
        Traction      := TractionMarche / 3;
        VitesseLimite := VitesseApproche;
        EnApproche    := TRUE;

        IF Temps >= 12000 THEN
            Etape := 50;
        END_IF;


    (* ---------- 50 : FREINAGE D'ARRET EN B ---------- *)
    50:
        Traction      := 0;
        FreinService  := TRUE;
        VitesseLimite := 0;

        IF Temps >= 12000 THEN
            Etape := 60;
        END_IF;


    (* ---------- 60 : STATIONNEMENT EN B ---------- *)
    60:
        FreinUrgence  := TRUE;
        FreinService  := FALSE;
        SensAvant     := FALSE;
        SensArriere   := FALSE;
        EnGare        := TRUE;
        EnApproche    := FALSE;

        IF Temps >= 15000 THEN
            Etape := 70;
        END_IF;


    (* ---------- 70 : DEPART VERS A, sens inverse ---------- *)
    70:
        VersGareB     := FALSE;
        FreinUrgence  := FALSE;
        SensAvant     := FALSE;
        SensArriere   := TRUE;
        VitesseLimite := VitesseLigne;
        EnGare        := FALSE;

        IF Temps >= 1500 THEN
            Etape := 80;
        END_IF;


    (* ---------- 80 : MARCHE EN LIGNE VERS A ---------- *)
    80:
        Traction := TractionMarche;

        IF Temps >= DureeMarche THEN
            Etape := 90;
        END_IF;


    (* ---------- 90 : APPROCHE DE A ---------- *)
    90:
        Traction      := TractionMarche / 3;
        VitesseLimite := VitesseApproche;
        EnApproche    := TRUE;

        IF Temps >= 12000 THEN
            Etape := 100;
        END_IF;


    (* ---------- 100 : FREINAGE D'ARRET EN A ---------- *)
    100:
        Traction      := 0;
        FreinService  := TRUE;
        VitesseLimite := 0;

        IF Temps >= 12000 THEN
            Etape := 110;
        END_IF;


    (* ---------- 110 : STATIONNEMENT EN A ---------- *)
    110:
        FreinUrgence  := TRUE;
        FreinService  := FALSE;
        SensAvant     := FALSE;
        SensArriere   := FALSE;
        EnGare        := TRUE;
        EnApproche    := FALSE;

        IF Temps >= 15000 THEN
            Etape := 10;
        END_IF;

ELSE
    (* Etape inconnue : repli au repos *)
    Etape := 0;

END_CASE;

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

    (* ---- Calibrage de l'exploitation ----
       A 600 pour mille et vitesseMax = 25 m/s cote simulation, la marche
       se fait a 15 m/s : 90 s donnent environ 1350 m, largement en deca
       des 1860 m de voie. Augmenter DUREE_MARCHE ferait buter les convois
       en bout de voie, l'automate n'ayant aucun retour de position. *)
    TRACTION_MARCHE  : UINT  := 600;
    VITESSE_LIGNE    : UINT  := 90;    (* km/h affiches en cabine *)
    VITESSE_APPROCHE : UINT  := 30;
    DUREE_MARCHE     : UDINT := 90000; (* ms *)

    (* Decalage entre les deux navettes, pour qu'elles ne soient jamais
       en phase et que la ligne paraisse vivante. *)
    RETARD_T1        : UDINT := 2000;
    RETARD_T2        : UDINT := 60000;
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
    Etape             AT %QW10 : UINT;   (* etape de la navette 1 *)
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
    NavetteT1 : NAVETTE;
    NavetteT2 : NAVETTE;

    RampeT1   : RAMPE_UINT;
    RampeT2   : RAMPE_UINT;

    Circulation : BOOL;       (* autorisation generale de circuler *)

    TmrCoeur  : TON;          (* oscillateur du chien de garde *)
END_VAR


(*
    ------------------------------------------------------------------
    1. CHIEN DE GARDE
    ------------------------------------------------------------------
    Heartbeat bascule toutes les 500 ms. Cote Unity, PlcLink surveille ce
    bit : s'il cesse de changer d'etat pendant plus de 2 s, la liaison est
    consideree perdue et le frein d'urgence est applique.
    Sans ce mecanisme, une coupure laisse le train lance a la derniere
    consigne recue.
*)
TmrCoeur(IN := NOT TmrCoeur.Q, PT := T#500ms);

IF TmrCoeur.Q THEN
    Heartbeat := NOT Heartbeat;
END_IF;


(*
    ------------------------------------------------------------------
    2. AUTORISATION GENERALE
    ------------------------------------------------------------------
    L'arret d'urgence est traite en amont de tout : il coupe la
    circulation des deux navettes en meme temps.
*)
Circulation   := NOT AU_Externe;
ScenarioActif := Circulation;


(*
    ------------------------------------------------------------------
    3. SEQUENCEURS
    ------------------------------------------------------------------
    Le meme bloc fonctionnel pilote les deux convois. Seul le retard au
    premier depart les distingue, ce qui suffit a les desynchroniser
    definitivement.
*)
NavetteT1(
    Autorisee       := Circulation,
    PeriodeMs       := PERIODE_MS,
    Retard          := RETARD_T1,
    TractionMarche  := TRACTION_MARCHE,
    VitesseLigne    := VITESSE_LIGNE,
    VitesseApproche := VITESSE_APPROCHE,
    DureeMarche     := DUREE_MARCHE
);

NavetteT2(
    Autorisee       := Circulation,
    PeriodeMs       := PERIODE_MS,
    Retard          := RETARD_T2,
    TractionMarche  := TRACTION_MARCHE,
    VitesseLigne    := VITESSE_LIGNE,
    VitesseApproche := VITESSE_APPROCHE,
    DureeMarche     := DUREE_MARCHE
);


(*
    ------------------------------------------------------------------
    4. REPORT DES SORTIES
    ------------------------------------------------------------------
*)
T1_FreinService  := NavetteT1.FreinService;
T1_FreinUrgence  := NavetteT1.FreinUrgence;
T1_SensAvant     := NavetteT1.SensAvant;
T1_SensArriere   := NavetteT1.SensArriere;
T1_VitesseLimite := NavetteT1.VitesseLimite;

T2_FreinService  := NavetteT2.FreinService;
T2_FreinUrgence  := NavetteT2.FreinUrgence;
T2_SensAvant     := NavetteT2.SensAvant;
T2_SensArriere   := NavetteT2.SensArriere;
T2_VitesseLimite := NavetteT2.VitesseLimite;

Etape := NavetteT1.Etape;


(*
    ------------------------------------------------------------------
    5. SIGNALISATION
    ------------------------------------------------------------------
    Un signal par convoi. Il presente le carre tant que le convoi est a
    quai, l'avertissement pendant l'approche, la voie libre en ligne.
*)
IF NavetteT1.EnGare THEN
    SIG1_Aspect := CARRE;
ELSIF NavetteT1.EnApproche THEN
    SIG1_Aspect := AVERTISSEMENT;
ELSE
    SIG1_Aspect := VOIE_LIBRE;
END_IF;

IF NavetteT2.EnGare THEN
    SIG2_Aspect := CARRE;
ELSIF NavetteT2.EnApproche THEN
    SIG2_Aspect := AVERTISSEMENT;
ELSE
    SIG2_Aspect := VOIE_LIBRE;
END_IF;


(*
    ------------------------------------------------------------------
    6. AIGUILLAGES
    ------------------------------------------------------------------
    Les aiguilles suivent le sens de marche : voie deviee au retour.
    Elles ne bougent que convoi a quai — un enclenchement reel refuse de
    manoeuvrer sous circulation, et l'imiter ici evite de faire sauter un
    train d'une voie a l'autre en pleine marche.
*)
IF NavetteT1.EnGare THEN
    AIG1_Deviation := NOT NavetteT1.VersGareB;
END_IF;

IF NavetteT2.EnGare THEN
    AIG2_Deviation := NOT NavetteT2.VersGareB;
END_IF;


(*
    ------------------------------------------------------------------
    7. RAMPES DE TRACTION
    ------------------------------------------------------------------
    Les sequenceurs ne manipulent que des consignes. La sortie envoyee a
    la simulation passe toujours par une rampe : pas de saut d'effort.
*)
RampeT1(Consigne := NavetteT1.Traction, Pas := PAS_RAMPE);
T1_Traction := RampeT1.Sortie;

RampeT2(Consigne := NavetteT2.Traction, Pas := PAS_RAMPE);
T2_Traction := RampeT2.Sortie;


(*
    ------------------------------------------------------------------
    8. SECURITES FINALES
    ------------------------------------------------------------------
    Appliquees en dernier pour qu'aucune etape ne puisse les contredire.
*)

(* L'arret d'urgence prime sur tout *)
IF AU_Externe THEN
    T1_FreinUrgence := TRUE;
    T2_FreinUrgence := TRUE;
    T1_FreinService := FALSE;
    T2_FreinService := FALSE;
    SIG1_Aspect     := CARRE;
    SIG2_Aspect     := CARRE;
    T1_VitesseLimite := 0;
    T2_VitesseLimite := 0;
END_IF;

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
