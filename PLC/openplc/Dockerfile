# =============================================================================
#  OpenPLC Runtime v3, construit depuis les sources officielles.
#
#  Aucune image OpenPLC n'est publiée sur Docker Hub : GRFICS construit la
#  sienne de la même façon, à partir d'un fork du dépôt amont.
#
#  La compilation prend plusieurs minutes au premier build, puis Docker met
#  la couche en cache.
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

# La cible "docker" d'install.sh compile le runtime sans toucher à systemd
RUN ./install.sh docker

# Programmes ST montés par docker-compose
RUN mkdir -p /programs

EXPOSE 8080 502

CMD ["./start_openplc.sh"]
