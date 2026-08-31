FROM node:22-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm install

COPY . .

# Argumentos de build para ngx-env
ARG Auth0__Domain
ARG Auth0__ClientId
ARG Auth0__Audience
ENV NG_APP_AUTH0_DOMAIN=$Auth0__Domain
ENV NG_APP_AUTH0_CLIENT_ID=$Auth0__ClientId
ENV NG_APP_AUTH0_AUDIENCE=$Auth0__Audience

RUN npm run build -- --configuration production

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/AsistenteAyuntamiento.Angular/browser /usr/share/nginx/html
EXPOSE 80
