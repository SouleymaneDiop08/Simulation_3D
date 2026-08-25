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

### 3. Poser le composant dans la scène

Ajoutez `PlcLink` sur un GameObject persistant (celui qui porte `SimulationManager`
convient) et renseignez dans l'inspecteur :

| Champ | Valeur |
|---|---|
| Url Passerelle | `ws://localhost:8081` |
| Train 1 / Train 2 | vos deux `TrainController` |
| Aiguillage 1 / 2 | vos `AiguillageController` |

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

**Unités.** L'automate envoie `vitesseLimite` en **km/h**, alors que `TrainController`
calcule en **m/s** (`distanceTrain += vitesse * Time.deltaTime`). Tant que ce point
n'est pas tranché dans la simulation, `vitesseAutorisee` sera comparée à une grandeur
d'unité différente.

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
