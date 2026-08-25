# Ce que fait GRFICSv3, et ce qu'on en reprend

Analyse du dépôt [Fortiphyd/GRFICSv3](https://github.com/Fortiphyd/GRFICSv3),
branche `main`, pour aligner notre liaison sur la leur.

---

## Leur architecture réelle

```
              ┌──────────────────────────────────────────────┐
              │  simulation/simulation/                      │
              │  TE_process.cc + main.cc  (C++, procédé)     │
              │  socket JSON sur le port 55555               │
              └──────┬──────────────────────────┬────────────┘
                     │                          │
        {"request":"read"}            {"request":"read"/"write"}
                     │                          │
        ┌────────────▼───────────┐   ┌──────────▼─────────────────────┐
        │ web_visualization/     │   │ simulation/remote_io/modbus/   │
        │   data/index.php       │   │   tank.py feed1.py purge.py …  │
        │   (relais HTTP)        │   │   serveurs pymodbus ESCLAVES   │
        └────────────┬───────────┘   │   192.168.95.10-15 : 502       │
                     │ HTTP          └──────────┬─────────────────────┘
        ┌────────────▼───────────┐              │ Modbus TCP
        │ Build Unity WebGL      │   ┌──────────▼─────────────────────┐
        │ index.html + Build/    │   │ OpenPLC — MAÎTRE Modbus        │
        │ servi par nginx :80    │   │ configuré en « Slave Devices » │
        └────────────────────────┘   └────────────────────────────────┘
```

Les trois faits qui comptent :

**1. Le build WebGL ne parle jamais Modbus.** Il interroge `data/index.php`,
servi par le même serveur web que le build. Le PHP ouvre un socket TCP vers le
procédé et relaie la réponse JSON :

```php
$fp = pfsockopen('127.0.0.1', 55555, $errno, $errstr);
fwrite($fp, '{"request":"read"}\n');
echo fgets($fp, 1500);
```

C'est exactement la contrainte que nous avons : le navigateur interdit les
sockets TCP, donc un relais côté serveur est obligatoire. Eux l'ont fait en PHP
sur HTTP, nous en Node.

**2. Une couche « E/S déportées » traduit procédé ↔ registres.** Un processus
Python par équipement, chacun sur sa propre IP, écrivant dans les registres
d'un serveur pymodbus. Extrait de `tank.py` :

```python
pressure = int(data["outputs"]["pressure"] / 3200.0 * 65535)
level    = int(data["outputs"]["liquid_level"] / 100.0 * 65535)
context[slave_id].setValues(4, 1, [pressure, level])   # 4 = input registers
```

À noter : ils normalisent tout sur la pleine échelle 0–65535. Nous utilisons des
échelles métier (‰ de traction, km/h × 1), plus lisibles dans le programme ST.

**3. Tout monte en une commande.** `docker-compose.yml` déclare `simulation`,
`plc`, `scadalts`, `workstation`, `router` sur un réseau ICS à adresses fixes.

---

## La différence de fond avec notre cas

**Chez GRFICS, le procédé est l'autorité.** Le réacteur chimique calcule sa
physique en continu ; l'automate lit des capteurs (registres d'entrée) et
renvoie des consignes d'actionneurs. La vue 3D observe le procédé.

**Chez nous, c'est l'automate qui commande.** Le programme ST produit les
ordres, la simulation les applique. Il n'y a pas de physique de procédé que
l'automate aurait besoin de lire.

Conséquence concrète sur le sens Modbus :

|  | GRFICS | Nous |
|---|---|---|
| Autorité | le procédé | l'automate |
| Rôle d'OpenPLC | **maître** (Slave Devices) | **serveur** |
| Rôle de la passerelle | équipement d'E/S déporté (esclave) | **client** Modbus |
| Registres lus par le PLC | `%IW` (capteurs) | — |
| Registres lus par la simulation | — | `%QX` / `%QW` (ordres) |

C'est pourquoi la passerelle reste cliente Modbus du runtime OpenPLC : dans le
sens que vous voulez, il n'y a rien à remonter vers l'automate, et le montage
« Slave Devices » ajouterait de la configuration sans rien apporter.

---

## Ce qu'on a repris

**Un endpoint HTTP de même origine**, équivalent de leur `data/index.php` :

```
GET http://localhost:8081/data   →   la dernière trame lue, en JSON
```

Servi par la passerelle elle-même, sur le port du build. Ni CORS, ni contenu
mixte à gérer. Le WebSocket reste disponible en parallèle sur la même adresse.

**L'orchestration par conteneurs** — `PLC/docker-compose.yml` :

```bash
cd PLC
docker compose up --build
```

Monte OpenPLC (`172.28.0.2`) et la passerelle (`172.28.0.3`) sur un réseau ICS
dédié, la passerelle n'étant lancée qu'une fois le runtime OpenPLC sain.

| Interface | Adresse |
|---|---|
| OpenPLC | http://localhost:8080 — identifiants par défaut `openplc` / `openplc` |
| Vue 3D | http://localhost:8081 |
| Données brutes | http://localhost:8081/data |

Le build WebGL est monté en lecture seule depuis `Build/WebGL` : un rebuild
Unity ne demande qu'un rafraîchissement du navigateur.

Aucune image OpenPLC n'étant publiée sur Docker Hub, `openplc/Dockerfile`
compile le runtime depuis le dépôt officiel — c'est aussi ce que fait GRFICS.
Comptez plusieurs minutes au premier `--build`, puis Docker met en cache.

---

## WebSocket ou HTTP : lequel choisir

Les deux sont disponibles, `PlcLink.cs` utilise aujourd'hui le WebSocket.

**WebSocket** — le serveur pousse à 50 Hz, latence minimale, une seule
connexion. C'est le bon choix pour du ferroviaire, où un ordre de freinage doit
arriver vite. Coût : gestion de la reconnexion, et blocage si la page passe un
jour en HTTPS avec un `ws://`.

**HTTP polling** — ce que fait GRFICS. Plus simple, sans état, aucune
reconnexion à écrire. Convient parce que leur procédé chimique évolue lentement.
À 50 Hz sur un train, cela ferait 50 requêtes par seconde et par client.

Recommandation : garder le WebSocket, et se servir de `/data` pour le
diagnostic — un simple `curl http://localhost:8081/data` dit immédiatement si la
chaîne Modbus fonctionne, sans lancer Unity.

---

## Ce qui n'a pas été vérifié

La syntaxe de `bridge.js` est validée (`node --check` dans un conteneur), et
`docker-compose.yml` passe `docker compose config`. En revanche **la chaîne n'a
jamais été exécutée** : l'environnement de travail n'a pas d'accès réseau, donc
ni `npm install` ni le build des images n'ont pu aboutir ici. Le premier
`docker compose up --build` sur votre machine reste le vrai test.
