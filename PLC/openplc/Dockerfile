# =============================================================================
#  OpenPLC Runtime v3, avec le programme du banc deja compile.
#
#  Aucune image OpenPLC n'est publiee sur Docker Hub : GRFICS construit la
#  sienne de la meme facon, a partir d'un fork du depot amont.
#
#  Le programme ST est compile A LA CONSTRUCTION, pas au premier demarrage :
#  l'image est donc un automate deja configure. Sans cela, tout `compose down`
#  effacait le programme televerse et le runtime repartait sur le programme
#  vide, en apparence fonctionnel mais sans rien executer.
#
#  Contexte de build = dossier PLC/ (voir docker-compose.yml).
# =============================================================================
FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
        git build-essential cmake automake libtool pkg-config \
        make sudo curl ca-certificates \
        python3 python3-pip python3-dev \
    && rm -rf /var/lib/apt/lists/*

RUN git clone --depth 1 https://github.com/thiagoralves/OpenPLC_v3.git /opt/OpenPLC_v3

WORKDIR /opt/OpenPLC_v3

# La cible "docker" d'install.sh compile le runtime sans toucher a systemd
RUN ./install.sh docker

# ---- Programme du banc ----
COPY simulation_ferroviaire.st /opt/OpenPLC_v3/webserver/st_files/

WORKDIR /opt/OpenPLC_v3/webserver

# compile_program.sh ecrit aussi active_program : le runtime demarrera donc
# sur ce programme, sans intervention.
RUN ./scripts/compile_program.sh simulation_ferroviaire.st \
    && test -x ./core/openplc \
    || (echo "ERREUR : la compilation MatIEC a echoue." && exit 1)

# ---- Preconfiguration ----
# Referencement dans la base pour que le programme apparaisse dans
# l'interface web, et demarrage automatique du runtime.
RUN python3 - <<'PY'
import sqlite3, time
c = sqlite3.connect('/opt/OpenPLC_v3/webserver/openplc.db')
c.execute("INSERT INTO Programs (Name, Description, File, Date_upload) VALUES (?,?,?,?)",
          ("Simulation Ferroviaire",
           "Scenario de conduite du banc. Voir PLC/simulation_ferroviaire.st",
           "simulation_ferroviaire.st", int(time.time())))
c.execute("UPDATE Settings SET Value='true'  WHERE Key='Start_run_mode'")
c.execute("UPDATE Settings SET Value='502'   WHERE Key='Modbus_port'")
c.commit(); c.close()
print("Programme enregistre, demarrage automatique active")
PY

EXPOSE 8080 502

WORKDIR /opt/OpenPLC_v3
CMD ["./start_openplc.sh"]
