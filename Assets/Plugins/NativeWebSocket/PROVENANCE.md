# NativeWebSocket — copie intégrée

Source : https://github.com/endel/NativeWebSocket — branche `upm`, version **1.1.6**
Licence : Apache 2.0 (voir `LICENSE`)

Intégré dans `Assets/Plugins/` plutôt que déclaré comme dépendance git dans
`Packages/manifest.json` : Unity exige alors que **git soit installé** sur la
machine qui ouvre le projet, ce qui n'est pas garanti sous Windows.

C'est la seule brique WebSocket qui fonctionne en WebGL : elle passe par
`WebSocket.jslib`, qui appelle l'API WebSocket du navigateur. Toute
bibliothèque s'appuyant sur `System.Net.Sockets` est inutilisable là.

Pour mettre à jour : retélécharger les fichiers de `WebSocket/` depuis la
branche `upm`. Ne rien modifier ici — aucun correctif local n'a été appliqué.
