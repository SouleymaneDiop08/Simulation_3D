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

    ------------------------------------------------------------------
    CONVENTIONS DE TYPAGE ET D'ECRITURE

    Le programme n'emploie que deux types : INT et BOOL.

    Ce n'est pas un choix de style. La page Monitoring d'OpenPLC analyse
    les declarations avec un parseur rudimentaire : les espaces
    d'alignement et les commentaires en fin de ligne le desalignent, et
    il affiche alors « AT » ou une case vide dans la colonne Type.

    Trois regles pour les blocs VAR :
      - un seul espace entre chaque element de la declaration ;
      - jamais de commentaire sur la ligne d'une declaration ;
      - INT et BOOL exclusivement.

    Corollaires : les durees sont comptees en CYCLES DE SCRUTATION et non
    en millisecondes, ce qui les maintient dans la plage d'un INT
    (-32768..32767) ; et le chien de garde utilise un compteur plutot
    qu'un bloc TON, ce qui evite le type TIME.

      1 cycle = 50 ms     1 s = 20 cycles     100 s = 2000 cycles

    ------------------------------------------------------------------
    EXPLOITATION SIMULEE

    Deux navettes font l'aller-retour entre deux gares, sur leurs voies
    respectives. Chaque convoi enchaine : depart, marche en ligne,
    approche ralentie, arret en gare, stationnement, puis repart en sens
    inverse. Les deux navettes sont decalees pour ne jamais etre en phase.

    Voir PLC/README-modbus.md pour la table d'adresses cote Unity.
    ==================================================================
*)


(*
    ------------------------------------------------------------------
    BLOC FONCTIONNEL : rampe
    ------------------------------------------------------------------
    Fait evoluer une sortie vers une consigne par pas fixes, sans a-coup.
*)
FUNCTION_BLOCK RAMPE
VAR_INPUT
    Consigne : INT;
    Pas : INT;
END_VAR
VAR_OUTPUT
    Sortie : INT;
END_VAR

IF Sortie < Consigne THEN

    Sortie := Sortie + Pas;

    IF Sortie > Consigne THEN
        Sortie := Consigne;
    END_IF;

ELSIF Sortie > Consigne THEN

    Sortie := Sortie - Pas;

    IF Sortie < Consigne THEN
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

    Toutes les durees sont exprimees en CYCLES DE SCRUTATION (50 ms).

    Le sequenceur ne produit que des CONSIGNES. Les rampes et les
    securites finales sont appliquees par le programme appelant, apres
    coup, et aucune etape ne peut les contourner.
*)
FUNCTION_BLOCK NAVETTE
VAR_INPUT
    Autorisee : BOOL;
    Retard : INT;
    TractionMarche : INT;
    VitesseLigne : INT;
    VitesseApproche : INT;
    ButeeBasse : INT;
    ButeeHaute : INT;
    DistanceApproche : INT;
    DistanceArret : INT;
END_VAR

VAR_OUTPUT
    Traction : INT;
    FreinService : BOOL;
    FreinUrgence : BOOL;
    SensAvant : BOOL;
    SensArriere : BOOL;
    VitesseLimite : INT;
    Etape : INT;
    EnGare : BOOL;
    EnApproche : BOOL;
    VersGareB : BOOL;
    Position : INT;
    Vitesse : INT;
END_VAR

VAR
    Cycles : INT;
    EtapePrec : INT;
    Accumulateur : INT;
    Avance : INT;
    VitesseVoulue : INT;
    Initialise : BOOL;
END_VAR


(* Position initiale : le convoi est pose au-dela du quai. *)
IF NOT Initialise THEN
    Initialise := TRUE;
    Position := ButeeBasse;
END_IF;


(* Le poste peut retirer l'autorisation a tout moment *)
IF NOT Autorisee THEN
    Etape := 0;
END_IF;


(* Base de temps : remise a zero a chaque changement d'etape. Le compteur
   est plafonne pour ne jamais deborder un INT si une etape s'eternise. *)
IF Etape <> EtapePrec THEN
    Cycles := 0;
    EtapePrec := Etape;
ELSIF Cycles < 30000 THEN
    Cycles := Cycles + 1;
END_IF;


CASE Etape OF

    (* ---------- 0 : REPOS ---------- *)
    0:
        Traction := 0;
        FreinService := FALSE;
        FreinUrgence := TRUE;
        SensAvant := FALSE;
        SensArriere := FALSE;
        VitesseLimite := 0;
        EnGare := TRUE;
        EnApproche := FALSE;
        VersGareB := TRUE;

        IF Autorisee AND Cycles >= Retard THEN
            Etape := 10;
        END_IF;


    (* ---------- 10 : A QUAI, preparation du depart (80 cycles = 4 s) ---------- *)
    10:
        Traction := 0;
        FreinUrgence := TRUE;
        FreinService := FALSE;
        VitesseLimite := 0;
        EnGare := TRUE;
        EnApproche := FALSE;

        IF Cycles >= 80 THEN
            Etape := 20;
        END_IF;


    (* ---------- 20 : DESSERRAGE ET ENGAGEMENT DU SENS (30 cycles) ---------- *)
    20:
        FreinUrgence := FALSE;
        FreinService := FALSE;
        SensAvant := VersGareB;
        SensArriere := NOT VersGareB;
        VitesseLimite := VitesseLigne;
        EnGare := FALSE;

        IF Cycles >= 30 THEN
            Etape := 30;
        END_IF;


    (* ---------- 30 : MARCHE EN LIGNE ---------- *)
    (* Transition sur la POSITION et non sur un chronometre : le convoi
       aborde l'approche au bon endroit, quelle que soit sa cinematique. *)
    30:
        Traction := TractionMarche;
        VitesseLimite := VitesseLigne;
        EnApproche := FALSE;

        IF VersGareB THEN
            IF Position >= (ButeeHaute - DistanceApproche) THEN
                Etape := 40;
            END_IF;
        ELSE
            IF Position <= (ButeeBasse + DistanceApproche) THEN
                Etape := 40;
            END_IF;
        END_IF;


    (* ---------- 40 : APPROCHE RALENTIE ---------- *)
    40:
        Traction := TractionMarche / 3;
        VitesseLimite := VitesseApproche;
        EnApproche := TRUE;

        IF VersGareB THEN
            IF Position >= (ButeeHaute - DistanceArret) THEN
                Etape := 50;
            END_IF;
        ELSE
            IF Position <= (ButeeBasse + DistanceArret) THEN
                Etape := 50;
            END_IF;
        END_IF;


    (* ---------- 50 : FREINAGE D'ARRET ---------- *)
    (* On attend l'arret reel, pas une duree : le convoi s'immobilise a
       quai meme si le freinage a ete plus long que prevu. *)
    50:
        Traction := 0;
        FreinService := TRUE;
        VitesseLimite := 0;

        IF Vitesse <= 0 OR Cycles >= 600 THEN
            Etape := 60;
        END_IF;


    (* ---------- 60 : STATIONNEMENT (300 cycles = 15 s) ---------- *)
    60:
        FreinUrgence := TRUE;
        FreinService := FALSE;
        SensAvant := FALSE;
        SensArriere := FALSE;
        EnGare := TRUE;
        EnApproche := FALSE;

        IF Cycles >= 300 THEN
            VersGareB := NOT VersGareB;
            Etape := 10;
        END_IF;

ELSE
    Etape := 0;

END_CASE;


(*
    ------------------------------------------------------------------
    MODELE DE MARCHE
    ------------------------------------------------------------------
    L'automate calcule lui-meme la vitesse et la position du convoi, et
    les transmet. La simulation n'integre plus rien : elle place le train
    ou l'automate le dit.

    C'est ce qui rend la vue reproductible — un rechargement de page
    replace le convoi exactement ou il se trouve, sans desynchronisation.

    Unites : vitesse en DECIMETRES PAR SECONDE, position en DECIMETRES.
    A 25 m/s la vitesse vaut 250 ; la voie de 1860 m fait 18600 dm, ce
    qui tient largement dans un INT.

      acceleration 5 m/s2   -> 50 dm/s2  -> 2,5 dm/s par cycle
      frein service 8 m/s2  -> 80 dm/s2  -> 4 dm/s par cycle
      frein urgence 20 m/s2 -> 200 dm/s2 -> 10 dm/s par cycle

    Les pas sont doubles et la periode comptee sur deux cycles pour
    rester en entiers sans perdre la moitie de l'acceleration.
*)

(* Vitesse voulue, deduite de la traction : 1000 pour mille = 250 dm/s *)
VitesseVoulue := Traction / 4;

IF FreinUrgence THEN
    VitesseVoulue := 0;
ELSIF FreinService THEN
    VitesseVoulue := 0;
END_IF;

IF SensAvant = FALSE AND SensArriere = FALSE THEN
    VitesseVoulue := 0;
END_IF;


(* Rapprochement de la vitesse voulue, avec le taux propre a l'etat *)
IF Vitesse < VitesseVoulue THEN

    Vitesse := Vitesse + 3;

    IF Vitesse > VitesseVoulue THEN
        Vitesse := VitesseVoulue;
    END_IF;

ELSIF Vitesse > VitesseVoulue THEN

    IF FreinUrgence THEN
        Vitesse := Vitesse - 10;
    ELSIF FreinService THEN
        Vitesse := Vitesse - 4;
    ELSE
        Vitesse := Vitesse - 3;
    END_IF;

    IF Vitesse < VitesseVoulue THEN
        Vitesse := VitesseVoulue;
    END_IF;

END_IF;

IF Vitesse < 0 THEN
    Vitesse := 0;
END_IF;


(* Integration exacte : l'accumulateur retient les fractions de decimetre,
   si bien qu'aucune erreur ne s'accumule sur la duree. *)
Accumulateur := Accumulateur + Vitesse;
Avance := Accumulateur / 20;
Accumulateur := Accumulateur - (Avance * 20);

IF SensAvant THEN
    Position := Position + Avance;
ELSIF SensArriere THEN
    Position := Position - Avance;
END_IF;


(* Butees de quai : l'automate les connait, il ne commande donc jamais
   au-dela. La simulation les applique aussi, en dernier recours. *)
IF Position > ButeeHaute THEN
    Position := ButeeHaute;
    Vitesse := 0;
END_IF;

IF Position < ButeeBasse THEN
    Position := ButeeBasse;
    Vitesse := 0;
END_IF;

END_FUNCTION_BLOCK



PROGRAM main

(*
    ==================================================================
    ENTREES ET SORTIES
    ------------------------------------------------------------------
    Format impose par le parseur de la page Monitoring d'OpenPLC :
    un seul espace entre chaque element, aucun commentaire sur la ligne.

      %IX0.0        arret d'urgence externe, forcable depuis Monitoring
      %QX0.0..0.7   freins et sens des deux convois
      %QX1.0..1.1   aiguillages, TRUE = voie deviee
      %QX2.0..2.1   chien de garde, scenario actif
      %QW0..%QW1    traction, 0 a 1000 pour mille
      %QW2..%QW3    aspect des signaux, 0 carre 1 avertissement 2 voie libre
      %QW4..%QW5    vitesse autorisee en cabine, km/h
      %QW6..%QW7    position du convoi sur la voie, en DECIMETRES
      %QW10         etape de la navette 1, pour la supervision
    ==================================================================
*)
VAR
    AU_Externe AT %IX0.0 : BOOL;
    T1_Traction AT %QW0 : INT;
    T1_FreinService AT %QX0.0 : BOOL;
    T1_FreinUrgence AT %QX0.1 : BOOL;
    T1_SensAvant AT %QX0.2 : BOOL;
    T1_SensArriere AT %QX0.3 : BOOL;
    T2_Traction AT %QW1 : INT;
    T2_FreinService AT %QX0.4 : BOOL;
    T2_FreinUrgence AT %QX0.5 : BOOL;
    T2_SensAvant AT %QX0.6 : BOOL;
    T2_SensArriere AT %QX0.7 : BOOL;
    AIG1_Deviation AT %QX1.0 : BOOL;
    AIG2_Deviation AT %QX1.1 : BOOL;
    SIG1_Aspect AT %QW2 : INT;
    SIG2_Aspect AT %QW3 : INT;
    T1_VitesseLimite AT %QW4 : INT;
    T2_VitesseLimite AT %QW5 : INT;
    T1_Position AT %QW6 : INT;
    T2_Position AT %QW7 : INT;
    Etape AT %QW10 : INT;
    Heartbeat AT %QX2.0 : BOOL;
    ScenarioActif AT %QX2.1 : BOOL;
END_VAR


(*
    ==================================================================
    CALIBRAGE DE L'EXPLOITATION
    ------------------------------------------------------------------
    Mesure relevee sur la scene Unity :

      longueur de voie          1860 m   (gare A z=616 -> gare B z=2476)
      longueur d'un convoi        63 m   (4 intervalles de 15,7 m)
      marge de quai               25 m   a chaque extremite

      butee basse   25 + 63    =   88 m
      butee haute   1860 - 25  = 1835 m
      course utile               1747 m

    Cinematique cote simulation :

      vitesseMax                  25 m/s
      traction 700 pour mille -> 17,5 m/s en croisiere
      acceleration                 5 m/s2
      frein de service             8 m/s2

    Les butees sont exprimees en DECIMETRES pour l'automate :

      butee basse    880 dm  =   88 m
      butee haute  18350 dm  = 1835 m

    Le sequenceur ne se fie plus a des durees. Il change d'etape sur la
    POSITION : approche a 70 m de la butee, freinage a 15 m, arret
    constate sur la vitesse. Un ecart de cinematique ne peut donc plus
    faire manquer une gare, alors qu'un chronometre mal calibre y menait
    tout droit.

    L'automate n'est plus aveugle : il calcule lui-meme la position et la
    transmet. La simulation ne fait que l'appliquer.
    ==================================================================
*)
VAR CONSTANT
    PAS_RAMPE : INT := 5;
    CARRE : INT := 0;
    AVERTISSEMENT : INT := 1;
    VOIE_LIBRE : INT := 2;
    LONGUEUR_VOIE : INT := 1860;
    TRACTION_MARCHE : INT := 700;
    VITESSE_LIGNE : INT := 90;
    VITESSE_APPROCHE : INT := 30;
    BUTEE_BASSE : INT := 880;
    BUTEE_HAUTE : INT := 18350;
    DISTANCE_APPROCHE : INT := 700;
    DISTANCE_ARRET : INT := 150;
    RETARD_T1 : INT := 40;
    RETARD_T2 : INT := 1200;
    DEMI_PERIODE_COEUR : INT := 10;
END_VAR


(*
    ------------------------------------------------------------------
    VARIABLES INTERNES
    ------------------------------------------------------------------
    Bloc VAR distinct, et ce n'est pas cosmetique : MatIEC refuse de
    melanger des variables localisees (AT %...) et des variables
    ordinaires dans un meme bloc.
    ------------------------------------------------------------------
*)
VAR
    NavetteT1 : NAVETTE;
    NavetteT2 : NAVETTE;
    RampeT1 : RAMPE;
    RampeT2 : RAMPE;
    Circulation : BOOL;
    CompteurCoeur : INT;
END_VAR


(*
    ------------------------------------------------------------------
    1. CHIEN DE GARDE
    ------------------------------------------------------------------
    Heartbeat bascule toutes les 500 ms, soit 10 cycles. Un simple
    compteur remplace le bloc TON, ce qui evite le type TIME.

    Cote Unity, PlcLink surveille ce bit : s'il cesse de changer d'etat
    pendant plus de 2 s, la liaison est consideree perdue et le frein
    d'urgence est applique. Sans ce mecanisme, une coupure laisse le
    train lance a la derniere consigne recue.
*)
IF CompteurCoeur >= DEMI_PERIODE_COEUR THEN
    CompteurCoeur := 0;
    Heartbeat := NOT Heartbeat;
ELSE
    CompteurCoeur := CompteurCoeur + 1;
END_IF;


(*
    ------------------------------------------------------------------
    2. AUTORISATION GENERALE
    ------------------------------------------------------------------
    L'arret d'urgence coupe la circulation des deux navettes ensemble.
*)
Circulation := NOT AU_Externe;
ScenarioActif := Circulation;


(*
    ------------------------------------------------------------------
    3. SEQUENCEURS
    ------------------------------------------------------------------
    Le meme bloc pilote les deux convois. Seul le retard au premier
    depart les distingue, ce qui suffit a les desynchroniser
    definitivement.
*)
NavetteT1(
    Autorisee := Circulation,
    Retard := RETARD_T1,
    TractionMarche := TRACTION_MARCHE,
    VitesseLigne := VITESSE_LIGNE,
    VitesseApproche := VITESSE_APPROCHE,
    ButeeBasse := BUTEE_BASSE,
    ButeeHaute := BUTEE_HAUTE,
    DistanceApproche := DISTANCE_APPROCHE,
    DistanceArret := DISTANCE_ARRET
);

NavetteT2(
    Autorisee := Circulation,
    Retard := RETARD_T2,
    TractionMarche := TRACTION_MARCHE,
    VitesseLigne := VITESSE_LIGNE,
    VitesseApproche := VITESSE_APPROCHE,
    ButeeBasse := BUTEE_BASSE,
    ButeeHaute := BUTEE_HAUTE,
    DistanceApproche := DISTANCE_APPROCHE,
    DistanceArret := DISTANCE_ARRET
);


(*
    ------------------------------------------------------------------
    4. REPORT DES SORTIES
    ------------------------------------------------------------------
*)
T1_FreinService := NavetteT1.FreinService;
T1_FreinUrgence := NavetteT1.FreinUrgence;
T1_SensAvant := NavetteT1.SensAvant;
T1_SensArriere := NavetteT1.SensArriere;
T1_VitesseLimite := NavetteT1.VitesseLimite;

T2_FreinService := NavetteT2.FreinService;
T2_FreinUrgence := NavetteT2.FreinUrgence;
T2_SensAvant := NavetteT2.SensAvant;
T2_SensArriere := NavetteT2.SensArriere;
T2_VitesseLimite := NavetteT2.VitesseLimite;

T1_Position := NavetteT1.Position;
T2_Position := NavetteT2.Position;

Etape := NavetteT1.Etape;


(*
    ------------------------------------------------------------------
    5. SIGNALISATION
    ------------------------------------------------------------------
    Un signal par convoi : carre a quai, avertissement en approche,
    voie libre en ligne.
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
    SIG1_Aspect := CARRE;
    SIG2_Aspect := CARRE;
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
    T1_SensAvant := FALSE;
    T1_SensArriere := FALSE;
END_IF;

IF T2_SensAvant AND T2_SensArriere THEN
    T2_SensAvant := FALSE;
    T2_SensArriere := FALSE;
END_IF;

(* Sans sens engage, pas de traction *)
IF NOT T1_SensAvant AND NOT T1_SensArriere THEN
    T1_Traction := 0;
END_IF;

IF NOT T2_SensAvant AND NOT T2_SensArriere THEN
    T2_Traction := 0;
END_IF;

(* Une traction negative n'a pas de sens : le sens de marche est porte
   par les bits SensAvant et SensArriere, jamais par le signe. *)
IF T1_Traction < 0 THEN
    T1_Traction := 0;
END_IF;

IF T2_Traction < 0 THEN
    T2_Traction := 0;
END_IF;

END_PROGRAM



CONFIGURATION Config0

    RESOURCE Res0 ON PLC
        TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
        PROGRAM instance0 WITH task0 : main;
    END_RESOURCE

END_CONFIGURATION
