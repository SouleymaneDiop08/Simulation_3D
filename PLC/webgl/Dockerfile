# =============================================================================
#  Le build Unity WebGL, embarqué dans une image nginx.
#
#  Le build lui-meme est produit par Unity sur la machine de developpement :
#  compiler Unity dans Docker demanderait une licence et plusieurs dizaines de
#  gigaoctets. On empaquette donc l'artefact deja produit, ce qui rend l'image
#  reproductible et versionnable.
#
#  Contexte de build = racine du projet (voir docker-compose.yml).
# =============================================================================
FROM nginx:1.27-alpine

# Configuration : service statique + relais vers la passerelle
COPY PLC/webgl/nginx.conf /etc/nginx/conf.d/default.conf

# L'artefact Unity. Absent -> le build echoue ici, avec un message clair,
# plutot que de produire une image qui sert une page blanche.
COPY Build/WebGL/ /usr/share/nginx/html/

# Verification a la construction : on ne publie pas une image vide
RUN test -f /usr/share/nginx/html/index.html \
    || (echo "ERREUR : Build/WebGL/index.html introuvable. Buildez depuis Unity avant." && exit 1)

EXPOSE 80
