/*
 * ============================================================================
 * PASSERELLE  OpenPLC (Modbus TCP)  <->  Unity WebGL (WebSocket)
 * ============================================================================
 *
 * Un build Unity WebGL ne peut pas ouvrir de socket TCP : le navigateur ne le
 * permet pas. Cette passerelle est donc obligatoire. Elle :
 *
 *   1. interroge le runtime OpenPLC en Modbus TCP (client/maitre) ;
 *   2. diffuse l'etat lu a tous les clients WebSocket connectes, en JSON ;
 *   3. sert optionnellement les fichiers du build WebGL, avec les bons
 *      en-tetes Content-Encoding (Unity compresse en .br / .gz).
 *
 * Un seul processus a lancer, donc, pour toute la chaine cote navigateur.
 *
 * Installation :   npm install
 * Lancement    :   npm start
 *
 * ============================================================================
 */

const http = require("http");
const fs = require("fs");
const path = require("path");
const ModbusRTU = require("modbus-serial");
const { WebSocketServer } = require("ws");

// ============================================================================
// CONFIGURATION
// ============================================================================

const CFG = {
    // Runtime OpenPLC
    plcHost: process.env.PLC_HOST || "127.0.0.1",
    plcPort: parseInt(process.env.PLC_PORT || "502", 10),
    plcUnitId: 1,

    // Periode de scrutation. La TASK du programme .st est a 50 ms ;
    // on scrute un peu plus vite pour ne pas rater de transition.
    pollMs: 20,

    // Serveur HTTP + WebSocket.
    // 8080 est deja pris par l'interface web d'OpenPLC : ne pas le reutiliser.
    httpPort: parseInt(process.env.PORT || "8081", 10),

    // Dossier du build WebGL a servir. Laisser tel quel si vous servez
    // le build autrement (Unity "Build and Run", nginx, etc.).
    webglDir: process.env.WEBGL_DIR || path.join(__dirname, "..", "..", "Build", "WebGL"),
};

// ============================================================================
// TABLE D'ADRESSES  (doit rester alignee sur simulation_ferroviaire.st)
// ============================================================================

const COIL_BASE = 0;
const COIL_COUNT = 18;      // %QX0.0 .. %QX2.1

const REG_BASE = 0;
const REG_COUNT = 11;       // %QW0 .. %QW10

// Voie de retour : les %MW commencent au registre de maintien 1024 dans le
// mappage Modbus d'OpenPLC. Le programme ST ne doit QUE les lire — rien
// n'empeche techniquement d'y ecrire, mais il ecraserait alors ces mesures.
const MW_BASE = 1024;       // %MW0
const MW_COUNT = 12;        // %MW0 .. %MW11

// Ordres de l'automate : index dans le tableau de coils
const C = {
    T1_ArretUrgence: 0,     // %QX0.0
    T2_ArretUrgence: 1,     // %QX0.1
    T1_Autorisee: 2,        // %QX0.2
    T2_Autorisee: 3,        // %QX0.3
    AIG1_Deviation: 8,      // %QX1.0
    AIG2_Deviation: 9,      // %QX1.1
    Heartbeat: 16,          // %QX2.0
    SupervisionActive: 17,  // %QX2.1
};

// Ordres de l'automate : index dans le tableau de holding registers
const R = {
    T1_ConsigneVitesse: 0,  // %QW0, km/h
    T2_ConsigneVitesse: 1,  // %QW1, km/h
    SIG1_Aspect: 2,         // %QW2
    SIG2_Aspect: 3,         // %QW3
};

// Mesures remontees : index dans le bloc %MW
const M = {
    T1_Position: 0,         // %MW0, decimetres
    T1_Vitesse: 1,          // %MW1, km/h x10
    T1_Etat: 2,             // %MW2
    T1_Canton: 3,           // %MW3
    T2_Position: 4,         // %MW4
    T2_Vitesse: 5,          // %MW5
    T2_Etat: 6,             // %MW6
    T2_Canton: 7,           // %MW7
    AIG1_Controle: 8,       // %MW8
    AIG2_Controle: 9,       // %MW9
    Occupation: 10,         // %MW10
    Alarmes: 11,            // %MW11
};

// ============================================================================
// ETAT
// ============================================================================

const client = new ModbusRTU();
let plcConnecte = false;
let seq = 0;
let derniereErreur = "";

// Derniere trame lue, servie telle quelle sur GET /data.
// C'est le mode de liaison retenu par GRFICS : la vue 3D interroge un
// endpoint HTTP de meme origine, sans jamais parler Modbus elle-meme.
let derniereTrame = null;

// Dernieres mesures recues de la simulation, en attente d'ecriture dans les
// %MW. Null tant qu'aucun navigateur n'a rien envoye : on n'ecrit alors rien,
// plutot que d'ecraser les registres avec des zeros.
let mesures = null;

// Trame de repli diffusee quand l'automate est injoignable.
// plc:false doit declencher le frein d'urgence cote Unity.
function trameDegradee() {
    return {
        seq: seq++,
        plc: false,
        err: derniereErreur,
        hb: false,
        // Consigne negative et autorisation acquise : sans automate, les
        // navettes restent sur leurs valeurs nominales et continuent de
        // rouler. Couper la liaison ne doit pas eteindre le procede.
        t1_vitesse: -1, t1_au: false, t1_autorise: true,
        t2_vitesse: -1, t2_au: false, t2_autorise: true,
        aig1: false, aig2: false,
        sig1: 2, sig2: 2,
    };
}

function construireTrame(coils, regs) {
    return {
        seq: seq++,
        plc: true,
        err: "",
        hb: !!coils[C.Heartbeat],
        supervision: !!coils[C.SupervisionActive],

        t1_vitesse: regs[R.T1_ConsigneVitesse],
        t1_au: !!coils[C.T1_ArretUrgence],
        t1_autorise: !!coils[C.T1_Autorisee],

        t2_vitesse: regs[R.T2_ConsigneVitesse],
        t2_au: !!coils[C.T2_ArretUrgence],
        t2_autorise: !!coils[C.T2_Autorisee],

        aig1: !!coils[C.AIG1_Deviation],
        aig2: !!coils[C.AIG2_Deviation],

        sig1: regs[R.SIG1_Aspect],
        sig2: regs[R.SIG2_Aspect],
    };
}

/**
 * Convertit les mesures recues du navigateur en bloc de registres %MW.
 *
 * Les registres Modbus sont des entiers 16 bits non signes : toute valeur est
 * ramenee dans 0..65535, faute de quoi la bibliotheque leve une erreur et la
 * boucle de scrutation tombe.
 */
function construireMesures(m) {
    const bloc = new Array(MW_COUNT).fill(0);

    const borne = (v) => {
        const n = Math.round(Number(v) || 0);
        return n < 0 ? 0 : (n > 65535 ? 65535 : n);
    };

    bloc[M.T1_Position] = borne(m.t1_pos);
    bloc[M.T1_Vitesse] = borne(m.t1_vit);
    bloc[M.T1_Etat] = borne(m.t1_etat);
    bloc[M.T1_Canton] = borne(m.t1_canton);

    bloc[M.T2_Position] = borne(m.t2_pos);
    bloc[M.T2_Vitesse] = borne(m.t2_vit);
    bloc[M.T2_Etat] = borne(m.t2_etat);
    bloc[M.T2_Canton] = borne(m.t2_canton);

    bloc[M.AIG1_Controle] = borne(m.aig1_ctrl);
    bloc[M.AIG2_Controle] = borne(m.aig2_ctrl);

    bloc[M.Occupation] = borne(m.occupation);
    bloc[M.Alarmes] = borne(m.alarmes);

    return bloc;
}


// ============================================================================
// SERVEUR HTTP  (build WebGL)
// ============================================================================

const MIME = {
    ".html": "text/html", ".js": "application/javascript",
    ".css": "text/css", ".json": "application/json",
    ".wasm": "application/wasm", ".data": "application/octet-stream",
    ".png": "image/png", ".jpg": "image/jpeg", ".svg": "image/svg+xml",
    ".ico": "image/x-icon", ".symbols": "application/octet-stream",
};

const serveur = http.createServer((req, res) => {
    let rel = decodeURIComponent(req.url.split("?")[0]);

    // ---- Endpoint de donnees (equivalent du data/index.php de GRFICS) ----
    // Meme origine que le build : ni CORS, ni contenu mixte a gerer.
    if (rel === "/data") {
        res.writeHead(200, {
            "Content-Type": "application/json",
            "Cache-Control": "no-store",
            "Access-Control-Allow-Origin": "*",
        });
        res.end(JSON.stringify(derniereTrame || trameDegradee()));
        return;
    }

    if (rel === "/") rel = "/index.html";

    // Empeche toute remontee hors du dossier servi
    const fichier = path.join(CFG.webglDir, path.normalize(rel).replace(/^(\.\.[/\\])+/, ""));

    // Diffusion en flux, et non en memoire. readFile chargeait le fichier
    // entier avant de repondre : 37 Mo de RAM par requete sur le .wasm
    // d'Unity, et autant de fois qu'il y a de visiteurs.
    fs.stat(fichier, (err, infos) => {
        if (err || !infos.isFile()) {
            res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
            res.end(
                "404 - build WebGL introuvable.\n\n" +
                "Dossier attendu : " + CFG.webglDir + "\n" +
                "Buildez le projet dans ce dossier, ou definissez WEBGL_DIR.\n"
            );
            return;
        }

        let ext = path.extname(fichier);
        const entetes = {};

        // Unity compresse en .br ou .gz. Sans le bon Content-Encoding,
        // le navigateur refuse de charger le build.
        if (ext === ".br") {
            entetes["Content-Encoding"] = "br";
            ext = path.extname(fichier.slice(0, -3));
        } else if (ext === ".gz") {
            entetes["Content-Encoding"] = "gzip";
            ext = path.extname(fichier.slice(0, -3));
        }

        entetes["Content-Type"] = MIME[ext] || "application/octet-stream";
        entetes["Content-Length"] = infos.size;

        // Requetes partielles : le chargeur Unity y recourt sur les gros
        // fichiers, et un navigateur qui reprend un telechargement aussi.
        const plage = req.headers.range;

        if (plage) {
            const m = /bytes=(\d*)-(\d*)/.exec(plage);

            if (m) {
                const debut = m[1] ? parseInt(m[1], 10) : 0;
                const fin = m[2] ? parseInt(m[2], 10) : infos.size - 1;

                if (debut <= fin && fin < infos.size) {
                    entetes["Content-Length"] = fin - debut + 1;
                    entetes["Content-Range"] = `bytes ${debut}-${fin}/${infos.size}`;
                    entetes["Accept-Ranges"] = "bytes";

                    res.writeHead(206, entetes);
                    fs.createReadStream(fichier, { start: debut, end: fin }).pipe(res);
                    return;
                }
            }
        }

        entetes["Accept-Ranges"] = "bytes";
        res.writeHead(200, entetes);
        fs.createReadStream(fichier).pipe(res);
    });
});

// ============================================================================
// SERVEUR WEBSOCKET
// ============================================================================

const wss = new WebSocketServer({ server: serveur });
let clients = 0;

wss.on("connection", (ws, req) => {
    clients++;
    console.log(`[ws] client connecte (${clients}) depuis ${req.socket.remoteAddress}`);

    ws.on("message", (donnees) => {
        try {
            const m = JSON.parse(donnees.toString());

            // Un seul navigateur fait autorite sur les mesures : le dernier a
            // avoir parle. Plusieurs onglets ouverts se disputeraient sinon
            // les registres, chacun ecrivant sa propre position.
            mesures = m;
        } catch (e) {
            console.error("[ws] trame illisible :", e.message);
        }
    });

    ws.on("close", () => {
        clients--;
        console.log(`[ws] client deconnecte (${clients})`);
    });

    ws.on("error", (e) => console.error("[ws] erreur :", e.message));
});

function diffuser(trame) {
    // Conservee meme sans client WebSocket : GET /data doit rester a jour
    derniereTrame = trame;

    if (clients === 0) return;
    const msg = JSON.stringify(trame);
    for (const ws of wss.clients) {
        if (ws.readyState === 1) ws.send(msg);
    }
}

// ============================================================================
// BOUCLE DE SCRUTATION MODBUS
// ============================================================================

function pause(ms) {
    return new Promise((r) => setTimeout(r, ms));
}

async function connecterPlc() {
    console.log(`[modbus] connexion a ${CFG.plcHost}:${CFG.plcPort} ...`);
    await client.connectTCP(CFG.plcHost, { port: CFG.plcPort });
    client.setID(CFG.plcUnitId);
    client.setTimeout(1000);
    plcConnecte = true;
    derniereErreur = "";
    console.log("[modbus] connecte a OpenPLC");
}

async function boucle() {
    for (;;) {
        try {
            if (!plcConnecte) {
                await connecterPlc();
            }

            const coils = await client.readCoils(COIL_BASE, COIL_COUNT);
            const regs = await client.readHoldingRegisters(REG_BASE, REG_COUNT);

            diffuser(construireTrame(coils.data, regs.data));

            // Voie de retour : les mesures de la simulation partent dans les
            // %MW. Ecriture apres lecture, pour que la trame diffusee reflete
            // l'etat de l'automate au moment ou il a ete interroge.
            if (mesures) {
                await client.writeRegisters(MW_BASE, construireMesures(mesures));
            }

        } catch (e) {
            if (plcConnecte || derniereErreur !== e.message) {
                console.error("[modbus] erreur :", e.message);
            }
            derniereErreur = e.message;
            plcConnecte = false;

            try { client.close(() => {}); } catch (_) {}

            diffuser(trameDegradee());

            // On ne martele pas le runtime en cas de panne
            await pause(1000);
        }

        await pause(CFG.pollMs);
    }
}

// ============================================================================
// DEMARRAGE
// ============================================================================

serveur.listen(CFG.httpPort, () => {
    console.log("========================================================");
    console.log("  Passerelle OpenPLC <-> Unity WebGL");
    console.log("========================================================");
    console.log(`  OpenPLC   : modbus tcp ${CFG.plcHost}:${CFG.plcPort}`);
    console.log(`  WebSocket : ws://localhost:${CFG.httpPort}`);
    console.log(`  Donnees   : http://localhost:${CFG.httpPort}/data`);
    console.log(`  Retour    : %MW0..%MW11 (registres ${MW_BASE}..${MW_BASE + MW_COUNT - 1})`);
    console.log(`  Build     : http://localhost:${CFG.httpPort}  (${CFG.webglDir})`);
    console.log(`  Scrutation: ${CFG.pollMs} ms`);
    console.log("========================================================");
    boucle();
});

process.on("SIGINT", () => {
    console.log("\n[bridge] arret");
    try { client.close(() => {}); } catch (_) {}
    process.exit(0);
});
