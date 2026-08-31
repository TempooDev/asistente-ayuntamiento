FROM node:22-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm install

COPY . .

# Argumentos de build para ngx-env
ARG Auth0__Domain
ARG Auth0__ClientId
ENV Auth0__Domain=$Auth0__Domain
ENV Auth0__ClientId=$Auth0__ClientId

RUN npm run build -- --configuration production

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/AsistenteAyuntamiento.Angular/browser /usr/share/nginx/html
EXPOSE 80
