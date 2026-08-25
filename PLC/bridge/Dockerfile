FROM node:20-alpine

WORKDIR /app

# Couche de dépendances séparée : un changement de bridge.js ne réinstalle pas
COPY package.json ./
RUN npm install --omit=dev

COPY bridge.js ./

EXPOSE 8081

CMD ["node", "bridge.js"]
