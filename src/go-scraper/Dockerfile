# syntax=docker/dockerfile:1
FROM golang:1.25-alpine AS builder
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 GOOS=linux go build -trimpath -ldflags="-s -w" -o /out/scraper .

# distroless/static ships CA certificates and tzdata, and runs as an unprivileged user
FROM gcr.io/distroless/static-debian12:nonroot
COPY --from=builder /out/scraper /scraper
USER nonroot:nonroot
ENTRYPOINT ["/scraper"]
