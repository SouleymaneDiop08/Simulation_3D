# NativeWebSocket — copie intégrée

Source : https://github.com/endel/NativeWebSocket — branche `upm`, version **1.1.6**
Licence : Apache 2.0 (voir `LICENSE`)

Intégré dans `Assets/Plugins/` plutôt que déclaré comme dépendance git dans
`Packages/manifest.json` : Unity exige alors que **git soit installé** sur la
machine qui ouvre le projet, ce qui n'est pas garanti sous Windows.

C'est la seule brique WebSocket qui fonctionne en WebGL : elle passe par
`WebSocket.jslib`, qui appelle l'API WebSocket du navigateur. Toute
bibliothèque s'appuyant sur `System.Net.Sockets` est inutilisable là.

## Correctif local appliqué

`WebSocket.jslib` appelait `Module.dynCall_vi`, `dynCall_vii` et `dynCall_viii`.
Ces fonctions ne sont plus exportées par l'Emscripten d'Unity 6 : le build se
chargeait, le WebSocket se connectait, puis le tout premier rappel faisait
échouer le contenu Unity avec

    TypeError: Module.dynCall_vi is not a function

Les cinq appels sont passés par la macro Emscripten `makeDynCall`, qui résout
le pointeur de fonction à la construction :

    {{{ makeDynCall('vi', 'webSocketState.onOpen') }}}(instanceId);

Signatures : `vi` pour onOpen, `viii` pour onMessage, `vii` pour onError et
onClose.

## Mise à jour

Retélécharger les fichiers de `WebSocket/` depuis la branche `upm`, **puis
réappliquer le correctif ci-dessus** tant qu'il n'est pas intégré en amont.
