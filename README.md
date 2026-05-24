# SignalStack

This repository contains the SignalStack trading platform solution.

## Development prerequisites

Install these tools on your development machine before setting up the repo:

- **Git** for cloning and source control
- **.NET SDK 8.0** matching the pinned version in [`global.json`](./global.json)
- **Node.js 22 LTS or newer** for the Next.js web app
- **npm** for workspace scripts and package installs
- **Docker Desktop** or another Docker Engine with Compose support, because the local stack runs MongoDB, Redis, and PostgreSQL in containers
- **A code editor or IDE** such as Visual Studio 2022 or VS Code

Optional, but useful:

- **Azure CLI** for working with Azure-linked configuration and deployment tasks
- **PostgreSQL tools** if you prefer to inspect the local database outside the app

## Local environment notes

- The local infrastructure lives in [`infra/docker/docker-compose.yml`](./infra/docker/docker-compose.yml).
- Use [`infra/docker/.env.example`](./infra/docker/.env.example) as the starting point for your local environment file.


## Prompts to continue builing the solution
- Continue execution using RUNBOOK.md