# Chaîne OpenPLC → WebGL : mise en place

## Pourquoi une passerelle est obligatoire

Un build Unity WebGL tourne dans le bac à sable du navigateur : **il ne peut pas
ouvrir de socket TCP**. `System.Net.Sockets` n'existe pas sur cette plateforme, donc
aucun client Modbus ne peut fonctionner dans le build — quelle que soit la bibliothèque.

Le navigateur n'autorise que HTTP, WebSocket et WebRTC. D'où la chaîne :

```
OpenPLC Runtime          Passerelle Node.js            Unity WebGL
simulation_ferroviaire.st   PLC/bridge/bridge.js       PlcLink.cs
      :502  ──── Modbus TCP ────►  :8081  ──── WebSocket ────►  navigateur
```

La passerelle sert **aussi** les fichiers du build : un seul processus, un seul port,
pas de problème d'origine croisée.

---

## Prérequis

**Node.js n'est pas installé sur cette machine.** Il est nécessaire pour la passerelle :

```bash
sudo apt install nodejs npm     # Debian / Ubuntu
node --version                  # vérifier : v18 ou plus récent
```

---

## Démarches

### 1. Nettoyer l'ancienne liaison

Ces deux éléments sont incompatibles WebGL et doivent partir :

```
Assets/Scripts/PLCConnection.cs      (websocket-sharp, pointait sur le port 8080)
Assets/Plugins/websocket-sharp.dll   (s'appuie sur System.Net.Sockets)
```

Supprimez aussi les `.meta` associés.

### 2. Installer NativeWebSocket

C'est la seule brique WebSocket qui fonctionne réellement en WebGL (elle passe par
un plugin `.jslib` qui appelle l'API WebSocket du navigateur).

Unity → **Window ▸ Package Manager** → **+** → *Add package from git URL* :

```
https://github.com/endel/NativeWebSocket.git#upm
```

### 3. Poser les composants dans la scène

Le projet sépare **le poste de commande** (ce qui applique les ordres) du
**transport** (ce qui les reçoit). Deux composants, donc.

**a. `PosteDeCommande`** — sur un GameObject persistant, celui qui porte
`SimulationManager` convient. C'est le point d'entrée unique des ordres
extérieurs :

| Champ | Valeur |
|---|---|
| Trains | vos `TrainController`, **dans l'ordre** — indice 0 = train 1 du ST |
| Aiguillages | vos `AiguillageController` — indice 0 = `AIG1` |
| Signaux | vos `SignalController` — indice 0 = `SIG1` (facultatif) |
| Délai Sans Ordre | `2` secondes |

Il fonctionne **sans automate** : au démarrage il serre les freins et ferme les
signaux, et les y laisse tant qu'aucune source ne s'est manifestée. Un train
immobile au lancement n'est donc pas un défaut, c'est l'absence de commande.

Pour vérifier le câblage sans rien d'autre : clic droit sur le composant →
*Essai — traction 50 % sur tous les convois*.

**b. `PlcLink`** — sur le même GameObject. Il ne demande presque rien :

| Champ | Valeur |
|---|---|
| Url Passerelle | **laisser vide** (voir « Un seul build » plus bas) |
| Poste | laisser vide, il prend `PosteDeCommande.Instance` |

Ce découpage a une conséquence utile : changer de transport — HTTP au lieu de
WebSocket, port série, autre protocole — ne demande que de réécrire `PlcLink`.
Le reste de la simulation ne connaît pas l'automate.

### 4. Démarrer OpenPLC

1. **Programs** → *Upload* `PLC/simulation_ferroviaire.st`
2. **Settings** → cocher **Enable Modbus**, port 502 → *Save changes*
   *(sans cette case, aucun registre n'est exposé — l'oubli le plus fréquent)*
3. **Dashboard** → *Start PLC*
4. **Monitoring** → vérifier que `Etape` avance et que `Heartbeat` clignote

### 5. Lancer la passerelle

```bash
cd PLC/bridge
npm install
npm start
```

Vous devez voir `[modbus] connecte a OpenPLC`. Si l'automate tourne sur une autre
machine :

```bash
PLC_HOST=192.168.1.42 npm start
```

### 6. Valider dans l'éditeur AVANT de builder

Appuyez sur Play dans Unity. Le composant `PlcLink` expose en lecture seule
`Connecte`, `Automate Vivant` et `Etape Courante` — ils doivent refléter ce que
montre la page Monitoring d'OpenPLC.

Ne passez au WebGL que quand ça fonctionne ici : déboguer un build WebGL est
nettement plus pénible (pas de point d'arrêt, console navigateur uniquement).

### 7. Configurer le build WebGL

**File ▸ Build Profiles** (ou *Build Settings*) :

- **Web / WebGL** → *Switch Platform*
- **Ajoutez votre scène à la liste des scènes du build.**
  Elle est actuellement **vide** (`m_Scenes: []` dans `EditorBuildSettings.asset`) :
  en l'état, le build produirait une application vierge.

**Player Settings ▸ Publishing Settings** :

- *Compression Format* : **Disabled** pour les essais locaux.
  Brotli et Gzip fonctionnent aussi (la passerelle envoie les bons en-têtes
  `Content-Encoding`), mais désactiver la compression élimine une source d'erreur
  pendant la mise au point.

### 8. Builder dans le dossier servi par la passerelle

Cible attendue par défaut :

```
<racine du projet>/Build/WebGL
```

Sinon, indiquez le chemin à la passerelle :

```bash
WEBGL_DIR=/chemin/vers/le/build npm start
```

### 9. Ouvrir

```
http://localhost:8081
```

---

## Où se fait le couplage : projet ou build ?

**Il s'écrit dans le projet Unity, il s'exécute dans le build.** Les deux, donc,
mais pas au même moment.

`PlcLink` est un composant MonoBehaviour : on l'ajoute à un GameObject et on
câble les références (`train1`, `aiguillage1`…) dans l'inspecteur. Cela se fait
**obligatoirement dans le projet, avant de builder**. Un build WebGL est du code
compilé : on ne peut pas y greffer la liaison après coup.

Le même composant tourne aussi en **Play mode dans l'éditeur** — c'est le même
code, la même passerelle, le même programme ST. D'où la règle : validez la
chaîne dans l'éditeur, buildez ensuite.

### Le navigateur ne joint jamais OpenPLC

```
Navigateur  ──WebSocket──>  Passerelle  ──Modbus TCP──>  OpenPLC
 (build)                     :8081                        :502
```

Le navigateur n'a besoin d'atteindre **que la passerelle**. La passerelle est la
seule à parler Modbus. OpenPLC peut donc tourner sur une autre machine, ou sur un
autre réseau, du moment que la passerelle l'atteint (`PLC_HOST`).

### Un seul build pour tous les déploiements

Laissez `Url Passerelle` **vide** dans l'inspecteur. `PlcLink` résout alors
l'adresse au démarrage, dans cet ordre :

| Priorité | Source | Cas d'usage |
|---|---|---|
| 1 | `?plc=ws://hote:port` dans l'URL | rediriger un build déjà compilé |
| 2 | champ de l'inspecteur | forcer une adresse fixe |
| 3 | origine de la page (`Application.absoluteURL`) | **le cas normal** |
| 4 | `ws://localhost:8081` | éditeur |

Le point 3 est ce qui compte : la passerelle servant le build, la page et le
WebSocket partagent hôte et port. Un build ouvert depuis `192.168.1.50:8081` se
connecte à `ws://192.168.1.50:8081`, et non à `localhost` — qui, sur un poste
distant, désignerait ce poste lui-même. Le schéma bascule aussi en `wss://` si la
page est servie en HTTPS, ce qui évite le blocage pour contenu mixte.

Pour rediriger un build sans repasser par Unity :

```
http://localhost:8081/?plc=ws://192.168.1.42:8081
```

---

## Pièges à connaître

**Contenu mixte.** Si un jour vous servez la page en **HTTPS**, le navigateur
**bloquera** une connexion `ws://`. Il faudra passer la passerelle en `wss://`
(certificat TLS). En HTTP simple — localhost ou réseau local — `ws://` convient.

**Port 8080.** C'est celui de l'interface web d'OpenPLC. La passerelle utilise 8081
pour cette raison ; ne les intervertissez pas.

**WebGL est mono-thread.** Aucun `Thread`, aucun `lock` : NativeWebSocket délivre ses
rappels sur le thread principal. Les conseils de threading valables pour un build
desktop ne s'appliquent pas ici — et c'est plus simple.

**Chien de garde.** Le bit `Heartbeat` du programme `.st` traverse toute la chaîne.
S'il se fige plus de 2 s, `PlcLink` serre le frein d'urgence de lui-même. C'est ce qui
évite qu'un train reste lancé après une coupure de l'automate ou de la passerelle.
Testez-le : arrêtez le PLC depuis le Dashboard OpenPLC, le train doit s'arrêter.

**Unités — convention arrêtée.** La simulation travaille en **m/s** en interne ;
les **km/h** ne servent qu'aux échanges. `PlcLink` fait la conversion à la
réception (`vitesseLimite / TrainController.MS_VERS_KMH`), il n'y a donc rien à
convertir côté automate : le programme ST raisonne en km/h de bout en bout.

Les échelles restent **métier**, pas normalisées sur 0–65535 : traction en pour
mille, vitesses en km/h, aspects de signal en énumération. C'est plus lisible
dans le programme ST et conforme à l'usage industriel.

---

## Diagnostic rapide

| Symptôme | Cause probable |
|---|---|
| `[modbus] erreur : Timed out` | *Enable Modbus* décoché, ou PLC arrêté |
| `ECONNREFUSED` sur :502 | runtime OpenPLC non démarré |
| Passerelle OK, Unity ne reçoit rien | mauvaise URL dans `PlcLink`, ou port 8080 au lieu de 8081 |
| Console navigateur : *insecure WebSocket* | page en HTTPS, passerelle en `ws://` |
| Page blanche, 404 sur `.wasm` | build absent de `Build/WebGL`, ou `WEBGL_DIR` erroné |
| Le train ne bouge pas, `Etape` avance | scène absente des Build Settings, ou références non câblées dans `PlcLink` |
