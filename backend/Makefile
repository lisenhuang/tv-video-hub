# Makefile for the MediaHub backend — a thin wrapper around docker compose.
#
# Quick start (from the backend/ directory):
#     make up         # build + start in the background
#     make logs       # watch the logs
#     open http://localhost:8080/admin
#
# Overrides:
#     make up COMPOSE="docker-compose"   # if you have the v1 compose binary
#     make health PORT=8090              # if you changed the published port
#
# NOTE: the published host port is set in docker-compose.yml (default 8080:8080).
# The PORT variable below only affects the convenience URLs printed by make.

COMPOSE ?= docker compose
SERVICE ?= mediahub
PORT    ?= 8080

.DEFAULT_GOAL := help

.PHONY: help build up down restart rebuild logs ps sh health open clean reset

help: ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
		awk 'BEGIN{FS=":.*?## "}{printf "  \033[36m%-9s\033[0m %s\n", $$1, $$2}'

build: ## Build the Docker image
	$(COMPOSE) build

up: ## Build (if needed) and start in the background
	$(COMPOSE) up -d --build
	@echo "→ dashboard: http://localhost:$(PORT)/admin"

down: ## Stop & remove containers (keeps the App_Data volume / your DB config)
	$(COMPOSE) down

restart: ## Restart the service
	$(COMPOSE) restart $(SERVICE)

rebuild: ## Rebuild the image and recreate the container from scratch
	$(COMPOSE) up -d --build --force-recreate
	@echo "→ dashboard: http://localhost:$(PORT)/admin"

logs: ## Tail the last 100 log lines and follow (Ctrl-C to stop)
	$(COMPOSE) logs -f --tail=100 $(SERVICE)

ps: ## Show container status and port mapping
	$(COMPOSE) ps

sh: ## Open a shell inside the running container
	$(COMPOSE) exec $(SERVICE) /bin/sh

health: ## Curl the health endpoint (expects the service to be up)
	@curl -fsS http://localhost:$(PORT)/api/health && echo

open: ## Print the admin dashboard URL
	@echo "http://localhost:$(PORT)/admin"

clean: ## Stop containers and remove the locally-built image (keeps data)
	$(COMPOSE) down --rmi local

reset: ## DANGER: stop and DELETE the App_Data volume (wipes DB connection + any SQLite DB)
	@printf "This deletes the App_Data volume (db.json + SQLite data). Continue? [y/N] " && \
		read ans && [ "$$ans" = "y" ] || (echo "aborted"; exit 1)
	$(COMPOSE) down -v
