# Ledger

Personal bookkeeping and budgeting app to replace monthly spreadsheet tracking. Built with **ASP.NET Core 8**, **Razor views** (server-side templating), and **HTMX** for partial-page updates without a heavy JavaScript framework.

## Why SQLite instead of files-per-entry?

A year/month/day/file tree is appealing for manual inspection and git-friendly diffs, but it fights you once you need:

- **Aggregations** — monthly summaries, % of income, budget vs actual
- **Dedup on import** — matching bank `ExternalId` across imports
- **Flexible queries** — filter by entity, category, date range
- **Concurrent writes** — imports + manual edits

SQLite gives you a real database in a single file (`data/ledger.db`) that you can still back up or copy anywhere. If you want archive snapshots, we can add JSON export per month later without using files as the primary store.

## Stack

| Layer | Choice |
|-------|--------|
| Backend | .NET 10, ASP.NET Core MVC |
| Templating | Razor (`.cshtml`) — partials for HTMX fragments |
| Interactivity | HTMX 2 |
| Styling | [Pico CSS](https://picocss.com/) |
| Database | SQLite + Entity Framework Core 10 |
| CSV import | CsvHelper with flexible column detection |
| LLM (optional) | OpenAI-compatible API (Ollama, etc.) |

## Features (initial scaffold)

- **Dashboard** — monthly income, expenses, net, category breakdown with % of income
- **Transactions** — list, filter, create, edit, delete (HTMX delete)
- **Import** — CSV upload with auto-accept or step-through review workflow
- **Entities & accounts** — split finances by person/household without login
- **Budgets** — per-category limits with weekly/monthly/quarterly/yearly/custom periods and warning thresholds
- **Settings** — LLM configuration overview
- **Docker** — `docker-compose` on external `nginx_network`

## Getting started

### Local development

```bash
cd D:\workspace\ledger
dotnet restore
dotnet ef database update --project Ledger.Infrastructure --startup-project Ledger.Web
dotnet run --project Ledger.Web
```

Open http://localhost:5000 (or the port shown in the console).

### Docker

Ensure the shared network exists:

```bash
docker network create nginx_network
```

Then:

```bash
docker compose up -d --build
```

App listens on **http://localhost:5080** (mapped to container port 8080).

### LLM integration

Edit `appsettings.json` or set environment variables:

```json
{
  "Llm": {
    "Enabled": true,
    "BaseUrl": "http://localhost:11434/v1",
    "DefaultModel": "llama3.2",
    "UseForCategorization": true
  }
}
```

Categorization order: keyword rules → similar past transactions → LLM (if enabled) → income/expense fallback.

### CSV import

Supported column names (case-insensitive, flexible): `date`, `amount`, `description`, `debit`/`credit`, `id`. Bank-specific presets can be added in `CsvImportService`.

## Project layout

```
ledger/
├── Ledger.Core/           # Entities, configuration
├── Ledger.Infrastructure/ # EF Core, import, budgets, LLM client
├── Ledger.Web/            # MVC controllers, Razor views, HTMX
├── Dockerfile
└── docker-compose.yml
```

## HTMX + Razor pattern

Full pages use `Views/{Controller}/{Action}.cshtml`. HTMX requests return **partials** from `Views/{Controller}/_{Partial}.cshtml` via `HtmxExtensions.IsHtmxRequest()`. This keeps HTML in readable template files instead of string-building in C#.

## Roadmap ideas

- OFX/QFX bank import
- Split transactions (one receipt → multiple categories)
- Yearly summary page like your spreadsheet
- Category/group management UI
- JSON export to year/month folders for archival
- Recurring transaction detection
