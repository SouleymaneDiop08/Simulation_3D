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

// Index dans le tableau de coils
const C = {
    T1_FreinService: 0,     // %QX0.0
    T1_FreinUrgence: 1,     // %QX0.1
    T1_SensAvant: 2,        // %QX0.2
    T1_SensArriere: 3,      // %QX0.3
    T2_FreinService: 4,     // %QX0.4
    T2_FreinUrgence: 5,     // %QX0.5
    T2_SensAvant: 6,        // %QX0.6
    T2_SensArriere: 7,      // %QX0.7
    AIG1_Deviation: 8,      // %QX1.0
    AIG2_Deviation: 9,      // %QX1.1
    Heartbeat: 16,          // %QX2.0
    ScenarioActif: 17,      // %QX2.1
};

// Index dans le tableau de holding registers
const R = {
    T1_Traction: 0,         // %QW0
    T2_Traction: 1,         // %QW1
    SIG1_Aspect: 2,         // %QW2
    SIG2_Aspect: 3,         // %QW3
    T1_VitesseLimite: 4,    // %QW4
    T2_VitesseLimite: 5,    // %QW5
    Etape: 10,              // %QW10
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

// Trame de repli diffusee quand l'automate est injoignable.
// plc:false doit declencher le frein d'urgence cote Unity.
function trameDegradee() {
    return {
        seq: seq++,
        plc: false,
        err: derniereErreur,
        hb: false,
        etape: 0,
        scenario: false,
        t1_traction: 0, t1_fs: false, t1_fu: true,
        t1_av: false, t1_ar: false, t1_vlim: 0,
        t2_traction: 0, t2_fs: false, t2_fu: true,
        t2_av: false, t2_ar: false, t2_vlim: 0,
        aig1: false, aig2: false,
        sig1: 0, sig2: 0,
    };
}

function construireTrame(coils, regs) {
    return {
        seq: seq++,
        plc: true,
        err: "",
        hb: !!coils[C.Heartbeat],
        etape: regs[R.Etape],
        scenario: !!coils[C.ScenarioActif],

        t1_traction: regs[R.T1_Traction],
        t1_fs: !!coils[C.T1_FreinService],
        t1_fu: !!coils[C.T1_FreinUrgence],
        t1_av: !!coils[C.T1_SensAvant],
        t1_ar: !!coils[C.T1_SensArriere],
        t1_vlim: regs[R.T1_VitesseLimite],

        t2_traction: regs[R.T2_Traction],
        t2_fs: !!coils[C.T2_FreinService],
        t2_fu: !!coils[C.T2_FreinUrgence],
        t2_av: !!coils[C.T2_SensAvant],
        t2_ar: !!coils[C.T2_SensArriere],
        t2_vlim: regs[R.T2_VitesseLimite],

        aig1: !!coils[C.AIG1_Deviation],
        aig2: !!coils[C.AIG2_Deviation],

        sig1: regs[R.SIG1_Aspect],
        sig2: regs[R.SIG2_Aspect],
    };
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

    fs.readFile(fichier, (err, data) => {
        if (err) {
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
        res.writeHead(200, entetes);
        res.end(data);
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
