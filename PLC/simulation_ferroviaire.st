(*
    ==================================================================
    SIMULATION FERROVIAIRE - PROGRAMME DE SUPERVISION
    ------------------------------------------------------------------
    Cible      : OpenPLC Runtime v3  (compilateur MatIEC)
    Langage    : IEC 61131-3 Structured Text
    Periode    : 50 ms (voir bloc CONFIGURATION en fin de fichier)

    ------------------------------------------------------------------
    ROLE

    L'automate NE CONDUIT PAS les trains. Les navettes roulent seules
    dans la simulation, qui detient toute la physique. L'automate observe
    et dispose de trois leviers seulement :

      - la consigne de vitesse de chaque convoi ;
      - l'arret d'urgence de chaque convoi ;
      - la position de chaque aiguille.

    C'est un procede qui tourne, qu'on vient perturber. Couper cet
    automate n'arrete pas l'installation : la simulation retombe sur ses
    valeurs nominales et poursuit.

    ------------------------------------------------------------------
    LA BOUCLE EST FERMEE

    La simulation remonte ses mesures dans les %MW : position, vitesse
    reelle, etat, canton occupe, et surtout le CONTROLE des aiguilles —
    leur position reelle, pas celle commandee.

    Le programme s'en sert pour un debut d'enclenchement : il refuse de
    manoeuvrer une aiguille dont le canton est occupe. Un attaquant qui
    ecrase ce refus produit l'accident : c'est tout l'interet du banc.

    REGLE ABSOLUE : ce programme ne fait que LIRE les %MW. Y ecrire
    ecraserait les mesures que la passerelle vient d'y deposer. Rien ne
    l'empeche techniquement, c'est une convention a tenir.

    ------------------------------------------------------------------
    CONVENTIONS D'ECRITURE

    INT et BOOL exclusivement. Un seul espace entre les elements d'une
    declaration, aucun commentaire sur ces lignes : le parseur de la page
    Monitoring d'OpenPLC se desaligne sinon.

    Durees comptees en cycles de scrutation : 1 cycle = 50 ms.

    Voir PLC/README-modbus.md pour la table d'adresses complete.
    ==================================================================
*)


PROGRAM main

(*
    ==================================================================
    ORDRES VERS LA SIMULATION
    ==================================================================
*)
VAR
    T1_ArretUrgence AT %QX0.0 : BOOL;
    T2_ArretUrgence AT %QX0.1 : BOOL;
    T1_Autorisee AT %QX0.2 : BOOL;
    T2_Autorisee AT %QX0.3 : BOOL;
    AIG1_Deviation AT %QX1.0 : BOOL;
    AIG2_Deviation AT %QX1.1 : BOOL;
    Heartbeat AT %QX2.0 : BOOL;
    SupervisionActive AT %QX2.1 : BOOL;
    T1_ConsigneVitesse AT %QW0 : INT;
    T2_ConsigneVitesse AT %QW1 : INT;
    SIG1_Aspect AT %QW2 : INT;
    SIG2_Aspect AT %QW3 : INT;
END_VAR


(*
    ==================================================================
    MESURES REMONTEES PAR LA SIMULATION
    ------------------------------------------------------------------
    LECTURE SEULE. Ne jamais affecter ces variables.
    ==================================================================
*)
VAR
    T1_Position AT %MW0 : INT;
    T1_Vitesse AT %MW1 : INT;
    T1_Etat AT %MW2 : INT;
    T1_Canton AT %MW3 : INT;
    T2_Position AT %MW4 : INT;
    T2_Vitesse AT %MW5 : INT;
    T2_Etat AT %MW6 : INT;
    T2_Canton AT %MW7 : INT;
    AIG1_Controle AT %MW8 : INT;
    AIG2_Controle AT %MW9 : INT;
    Occupation AT %MW10 : INT;
    Alarmes AT %MW11 : INT;
END_VAR


(*
    ==================================================================
    ENTREES DE L'OPERATEUR
    ------------------------------------------------------------------
    Forcables depuis la page Monitoring d'OpenPLC. C'est par la qu'on
    perturbe l'installation sans toucher au code.
    ==================================================================
*)
VAR
    AU_General AT %IX0.0 : BOOL;
    Cmd_AIG1 AT %IX0.1 : BOOL;
    Cmd_AIG2 AT %IX0.2 : BOOL;
    Inhiber_Enclenchement AT %IX0.3 : BOOL;
END_VAR


VAR CONSTANT
    CARRE : INT := 0;
    AVERTISSEMENT : INT := 1;
    VOIE_LIBRE : INT := 2;

    VITESSE_NOMINALE : INT := 90;
    VITESSE_RESTRICTION : INT := 40;

    ETAT_EN_LIGNE : INT := 0;
    ETAT_A_QUAI : INT := 1;
    ETAT_ACCIDENTE : INT := 2;
    ETAT_DERAILLE : INT := 3;

    AIG_PRINCIPALE : INT := 0;
    AIG_DEVIATION : INT := 1;
    AIG_EN_MANOEUVRE : INT := 2;

    DEMI_PERIODE_COEUR : INT := 10;
    SEUIL_SURVITESSE : INT := 1100;
END_VAR


VAR
    CompteurCoeur : INT;
    T1_Accidente : BOOL;
    T2_Accidente : BOOL;
    T1_Survitesse : BOOL;
    T2_Survitesse : BOOL;
    Aig1_Libre : BOOL;
    Aig2_Libre : BOOL;
END_VAR


(*
    ------------------------------------------------------------------
    1. CHIEN DE GARDE
    ------------------------------------------------------------------
    Heartbeat bascule toutes les 500 ms. La simulation s'en sert pour
    savoir si l'automate repond encore. S'il se tait, elle ne s'arrete
    pas : elle reprend ses valeurs nominales.
*)
IF CompteurCoeur >= DEMI_PERIODE_COEUR THEN
    CompteurCoeur := 0;
    Heartbeat := NOT Heartbeat;
ELSE
    CompteurCoeur := CompteurCoeur + 1;
END_IF;

SupervisionActive := TRUE;


(*
    ------------------------------------------------------------------
    2. LECTURE DE L'ETAT DU PROCEDE
    ------------------------------------------------------------------
*)
T1_Accidente := (T1_Etat = ETAT_ACCIDENTE) OR (T1_Etat = ETAT_DERAILLE);
T2_Accidente := (T2_Etat = ETAT_ACCIDENTE) OR (T2_Etat = ETAT_DERAILLE);

(* Vitesse remontee en km/h x10 : 1100 vaut 110 km/h *)
T1_Survitesse := T1_Vitesse > SEUIL_SURVITESSE;
T2_Survitesse := T2_Vitesse > SEUIL_SURVITESSE;


(*
    ------------------------------------------------------------------
    3. CONSIGNES DE VITESSE
    ------------------------------------------------------------------
    Nominale en marche normale, restreinte des qu'un convoi approche
    d'une aiguille en manoeuvre — une aiguille dont les lames bougent ne
    doit pas etre abordee a pleine vitesse.
*)
T1_ConsigneVitesse := VITESSE_NOMINALE;
T2_ConsigneVitesse := VITESSE_NOMINALE;

IF AIG1_Controle = AIG_EN_MANOEUVRE THEN
    T1_ConsigneVitesse := VITESSE_RESTRICTION;
END_IF;

IF AIG2_Controle = AIG_EN_MANOEUVRE THEN
    T2_ConsigneVitesse := VITESSE_RESTRICTION;
END_IF;


(*
    ------------------------------------------------------------------
    4. ENCLENCHEMENT DES AIGUILLES
    ------------------------------------------------------------------
    Une aiguille ne se manoeuvre pas sous circulation. Le programme
    verifie que le canton correspondant est libre avant de transmettre la
    commande de l'operateur.

    Inhiber_Enclenchement court-circuite ce controle : c'est le levier
    qui permet de montrer, sur ce banc, ce que produit la desactivation
    d'une securite.
*)
Aig1_Libre := (T1_Etat = ETAT_A_QUAI) OR Inhiber_Enclenchement;
Aig2_Libre := (T2_Etat = ETAT_A_QUAI) OR Inhiber_Enclenchement;

IF Aig1_Libre THEN
    AIG1_Deviation := Cmd_AIG1;
END_IF;

IF Aig2_Libre THEN
    AIG2_Deviation := Cmd_AIG2;
END_IF;


(*
    ------------------------------------------------------------------
    5. SIGNALISATION
    ------------------------------------------------------------------
    L'aspect decoule de l'etat REEL du convoi et de son aiguille, non
    d'un scenario. C'est la difference entre un signal qui informe et un
    signal qui recite.
*)
IF T1_Accidente OR T1_ArretUrgence THEN
    SIG1_Aspect := CARRE;
ELSIF T1_Etat = ETAT_A_QUAI THEN
    SIG1_Aspect := CARRE;
ELSIF AIG1_Controle = AIG_EN_MANOEUVRE THEN
    SIG1_Aspect := AVERTISSEMENT;
ELSE
    SIG1_Aspect := VOIE_LIBRE;
END_IF;

IF T2_Accidente OR T2_ArretUrgence THEN
    SIG2_Aspect := CARRE;
ELSIF T2_Etat = ETAT_A_QUAI THEN
    SIG2_Aspect := CARRE;
ELSIF AIG2_Controle = AIG_EN_MANOEUVRE THEN
    SIG2_Aspect := AVERTISSEMENT;
ELSE
    SIG2_Aspect := VOIE_LIBRE;
END_IF;


(*
    ------------------------------------------------------------------
    6. ARRETS D'URGENCE
    ------------------------------------------------------------------
    Trois causes : l'operateur, un accident constate, une survitesse.
    Un convoi accidente reste freine, l'automate n'ayant aucune raison de
    le relancer.
*)
T1_ArretUrgence := AU_General OR T1_Accidente OR T1_Survitesse;
T2_ArretUrgence := AU_General OR T2_Accidente OR T2_Survitesse;


(*
    ------------------------------------------------------------------
    7. AUTORISATIONS DE CIRCULER
    ------------------------------------------------------------------
    Retirees pendant une manoeuvre d'aiguille : le convoi attend a quai
    plutot que d'aborder des lames en mouvement.
*)
T1_Autorisee := NOT AU_General AND NOT T1_Accidente;
T2_Autorisee := NOT AU_General AND NOT T2_Accidente;

IF AIG1_Controle = AIG_EN_MANOEUVRE AND T1_Etat = ETAT_A_QUAI THEN
    T1_Autorisee := FALSE;
END_IF;

IF AIG2_Controle = AIG_EN_MANOEUVRE AND T2_Etat = ETAT_A_QUAI THEN
    T2_Autorisee := FALSE;
END_IF;

END_PROGRAM



CONFIGURATION Config0

    RESOURCE Res0 ON PLC
        TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
        PROGRAM instance0 WITH task0 : main;
    END_RESOURCE

END_CONFIGURATION
