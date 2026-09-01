FROM node:22-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm install

COPY . .

RUN npm run build -- --configuration production

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY 40-envsubst-config.sh /docker-entrypoint.d/40-envsubst-config.sh
RUN chmod +x /docker-entrypoint.d/40-envsubst-config.sh
COPY --from=build /app/dist/AsistenteAyuntamiento.Angular/browser /usr/share/nginx/html
EXPOSE 80
