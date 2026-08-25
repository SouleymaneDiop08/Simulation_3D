# Liaison OpenPLC → Simulation Unity

L'automate est la **source** : il produit les ordres, la simulation les applique.
Unity est **client (maître) Modbus TCP**, OpenPLC est **serveur (esclave)** sur le port **502**.

Programme : `simulation_ferroviaire.st`

---

## Mise en service côté OpenPLC

1. Interface web OpenPLC → **Programs** → *Upload Program* → `simulation_ferroviaire.st`
   Le runtime le compile avec MatIEC ; les erreurs éventuelles s'affichent dans le log de compilation.
2. **Settings** → cocher **Enable Modbus**, port **502** → *Save changes*.
   Sans cette case, aucun registre n'est exposé et Unity ne verra rien. C'est l'oubli le plus courant.
3. **Dashboard** → *Start PLC*.
4. **Monitoring** : les variables doivent défiler. `Etape` avance par paliers, `Heartbeat` clignote.

> Sur Linux, le port 502 est privilégié : le service OpenPLC doit tourner en root
> (c'est le cas par défaut avec l'installation fournie).

---

## Table d'adresses

Adresse Coil = `numéro_octet × 8 + numéro_bit`.

### Bits — lus par `ReadCoils` (fonction 01)

| Coil | Variable IEC | Nom | Signification |
|---:|---|---|---|
| 0 | `%QX0.0` | `T1_FreinService` | frein de service train 1 |
| 1 | `%QX0.1` | `T1_FreinUrgence` | frein d'urgence train 1 |
| 2 | `%QX0.2` | `T1_SensAvant` | sens avant train 1 |
| 3 | `%QX0.3` | `T1_SensArriere` | sens arrière train 1 |
| 4 | `%QX0.4` | `T2_FreinService` | frein de service train 2 |
| 5 | `%QX0.5` | `T2_FreinUrgence` | frein d'urgence train 2 |
| 6 | `%QX0.6` | `T2_SensAvant` | sens avant train 2 |
| 7 | `%QX0.7` | `T2_SensArriere` | sens arrière train 2 |
| 8 | `%QX1.0` | `AIG1_Deviation` | aiguille 1 : `TRUE` = déviation |
| 9 | `%QX1.1` | `AIG2_Deviation` | aiguille 2 : `TRUE` = déviation |
| 16 | `%QX2.0` | `Heartbeat` | bascule à 1 Hz — chien de garde |
| 17 | `%QX2.1` | `ScenarioActif` | scénario en cours |

Une seule requête suffit : `ReadCoils(0, 18)`.

### Mots — lus par `ReadHoldingRegisters` (fonction 03)

| Registre | Variable IEC | Nom | Échelle |
|---:|---|---|---|
| 0 | `%QW0` | `T1_Traction` | 0–1000 ‰ → `ChangerTraction(v / 1000f)` |
| 1 | `%QW1` | `T2_Traction` | idem |
| 2 | `%QW2` | `SIG1_Aspect` | 0 = carré, 1 = avertissement, 2 = voie libre |
| 3 | `%QW3` | `SIG2_Aspect` | idem |
| 4 | `%QW4` | `T1_VitesseLimite` | km/h |
| 5 | `%QW5` | `T2_VitesseLimite` | km/h |
| 10 | `%QW10` | `Etape` | étape du séquenceur (diagnostic) |

Une seule requête : `ReadHoldingRegisters(0, 11)`.

### Entrée — arrêt d'urgence

| Discrete Input | Variable IEC | Nom |
|---:|---|---|
| 0 | `%IX0.0` | `AU_Externe` |

Forçable à la main depuis la page **Monitoring** d'OpenPLC : c'est le moyen le plus
simple de tester la réaction de la simulation sans écrire une ligne de code.

---

## Correspondance avec les scripts existants

| Donnée PLC | Appel Unity |
|---|---|
| `T1_Traction` | `train1.physics.ChangerTraction(v / 1000f)` |
| `T1_FreinService` (front montant) | `train1.physics.FreinService()` |
| `T1_FreinUrgence` (front montant) | `train1.physics.FreinUrgence()` |
| les deux à `FALSE` (front) | `train1.physics.RelacherFrein()` |
| `T1_SensAvant` / `T1_SensArriere` | `train1.sens = SensTrain.Avant / .Arriere / .Neutre` |
| `AIG1_Deviation` | `aiguillage1.ActiverDeviation()` / `ActiverPrincipale()` |
| `T1_VitesseLimite` | `train1.vitesseAutorisee` |
| `SIG1_Aspect` | à créer — pas encore de signalisation dans la simulation |

**Appliquez les freins sur front, pas sur niveau.** Appeler `FreinService()` à chaque
cycle recharge l'état à chaque image ; ce n'est pas faux aujourd'hui, mais ça le
deviendra dès que le freinage aura une dynamique propre.

---

## Points de vigilance

**Cadence.** La `TASK` est à 50 ms, votre `Fixed Timestep` Unity est à 0,02 s. Scrutez à
20 ms depuis un thread de fond : les deux boucles s'alignent naturellement.

**Threading.** Ne faites jamais l'I/O Modbus dans `Update()`, et n'appelez aucune API
Unity depuis le thread de scrutation — ça fait planter l'éditeur. Passez par un
instantané protégé par `lock`, consommé dans `FixedUpdate()`.

**Chien de garde.** Surveillez `Heartbeat` (coil 16). S'il ne change plus d'état pendant
2 s, considérez la liaison perdue et appliquez le frein d'urgence côté Unity. Sans ça,
une coupure réseau laisse le train lancé sur la dernière consigne reçue.

**Unités.** `T1_VitesseLimite` est en **km/h**, alors que `TrainController` calcule
`distanceTrain += vitesse * Time.deltaTime`, c'est-à-dire des **m/s**. Tant que cette
ambiguïté n'est pas tranchée dans la simulation, la limitation transmise par l'automate
sera comparée à une grandeur qui n'a pas la même unité.

**Registres 16 bits non signés.** Toutes les valeurs sont en 0–65535. Pour transmettre
une grandeur négative plus tard, prévoyez un offset ou une réinterprétation en `int16`.

---

## Modifier le scénario

Tout est dans le `CASE Etape OF` du fichier `.st`. Chaque étape suit le même schéma :

```
30:
    ConsigneT1 := 600;              (* ce que fait l'étape *)

    IF TempsEtape >= 15000 THEN     (* durée en ms *)
        Etape := 40;                (* étape suivante *)
    END_IF;
```

Le séquenceur ne fixe que des **consignes** ; la rampe et les sécurités finales
(section 6 du programme) sont appliquées après le `CASE` et ne peuvent pas être
contournées par une étape.

Pour ajouter une étape : insérez un numéro entre deux existants (les paliers de 10
laissent de la place) et branchez la transition. Un numéro non déclaré tombe dans le
`ELSE` et déclenche l'arrêt d'urgence — c'est volontaire.
