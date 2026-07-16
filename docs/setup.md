# Setup & Run Guide

Follow these steps to run the DemoTfg project locally.

## Prerequisites

Make sure you have the following installed on your machine:

1. **.NET 8.0 SDK** (or newer)
2. **Docker Desktop** (required by .NET Aspire to spin up PostgreSQL, RabbitMQ, Azurite, and Ollama)
3. **Ollama** installed locally (if running LLMs locally, though Aspire's AppHost can pull and run the Ollama container automatically)
4. **VS Code** with the **.NET Aspire workload** (or Visual Studio 2022 / JetBrains Rider)

## Running the Application

### 1. Start Docker Desktop
Ensure Docker Desktop is running before launching the solution, as .NET Aspire relies on it for container resources.

### 2. Run with .NET CLI
Run the AppHost project directly from the repository root:

```bash
dotnet run --project src/DemoTfg.AppHost/DemoTfg.AppHost.csproj
```

Alternatively, you can open the root folder in VS Code, and start the app using the pre-configured debug launcher (`F5`).

### 3. Accessing the Dashboards
Once running, check the terminal output for the **.NET Aspire Dashboard** link (usually `http://localhost:17191` or similar).

From the Aspire Dashboard you can:
- View live console logs for the Web, API, and Worker projects.
- Access the Web Frontend endpoint.
- Monitor metrics and trace telemetry details.
