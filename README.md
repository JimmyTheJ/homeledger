# HomeLedger

Personal bookkeeping and budgeting app to replace monthly spreadsheet tracking. Built with **ASP.NET Core 8**, **Razor views** (server-side templating), and **HTMX** for partial-page updates without a heavy JavaScript framework.

## Why SQLite instead of files-per-entry?

A year/month/day/file tree is appealing for manual inspection and git-friendly diffs, but it fights you once you need:

- **Aggregations** — monthly summaries, % of income, budget vs actual
- **Dedup on import** — matching bank `ExternalId` across imports
- **Flexible queries** — filter by entity, category, date range
- **Concurrent writes** — imports + manual edits

SQLite gives you a real database in a single file (`data/homeledger.db`) that you can still back up or copy anywhere. If you want archive snapshots, we can add JSON export per month later without using files as the primary store.

SQLite is the default for its tiny footprint, but if you prefer a server-grade database (e.g. for larger datasets, concurrent access, or existing backup infrastructure) you can switch to **PostgreSQL** — see [Database providers](#database-providers).

## Stack

| Layer | Choice |
|-------|--------|
| Backend | .NET 10, ASP.NET Core MVC |
| Templating | Razor (`.cshtml`) — partials for HTMX fragments |
| Interactivity | HTMX 2 |
| Styling | [Pico CSS](https://picocss.com/) |
| Database | SQLite (default) or PostgreSQL, via Entity Framework Core 10 |
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
cd homeledger
dotnet restore
dotnet run --project HomeLedger.Web
```

Migrations are applied automatically on startup, so no manual step is needed. To apply them by hand (SQLite default), point at the SQLite migrations project:

```bash
dotnet ef database update --project HomeLedger.Migrations.Sqlite --startup-project HomeLedger.Web
```

Open http://localhost:5000 (or the port shown in the console).

### Docker

Ensure the shared network exists:

```bash
docker network create nginx_network
```

Then:

```bash
cp .env.example .env
# Edit .env for data path, Ollama host, models, and network
docker compose up -d --build
```

App listens on **http://localhost:5080** (mapped to container port 8080).

SQLite data is stored on the host at `./data/` by default (`HOMELEDGER_DATA_DIR` in `.env`). To reset the database, stop the container and delete that folder's `.db` files.

Docker reads `.env` automatically for `${HOMELEDGER_DATA_DIR}` and `${LLM_*}` substitution in `docker-compose.yml`. The image stays environment-agnostic; per-server config lives in `.env` (gitignored). See `.env.example` for all variables.

### Database providers

HomeLedger supports two database providers, selected with the `Database:Provider` setting (`Database__Provider` env var):

| Provider | When to use | Connection string format |
|----------|-------------|--------------------------|
| `Sqlite` *(default)* | Single file, smallest footprint, zero setup | `Data Source=data/homeledger.db` |
| `Postgres` | Server-grade DB, larger datasets, existing PG infra | `Host=...;Port=5432;Database=homeledger;Username=...;Password=...` |

Migrations are applied automatically on startup for whichever provider is configured. Each provider keeps its own migration set in a dedicated project: SQLite migrations in `HomeLedger.Migrations.Sqlite`, PostgreSQL migrations in `HomeLedger.Migrations.PostgreSql`. Switching providers starts from an empty database — use the CSV/JSON export-import to move existing data between them rather than copying the database file.

**Local development with PostgreSQL** — set the provider and connection string (e.g. in `appsettings.Development.json` or via environment variables):

```json
{
  "Database": {
    "Provider": "Postgres",
    "ConnectionString": "Host=localhost;Port=5432;Database=homeledger;Username=postgres;Password=postgres"
  }
}
```

**Docker with PostgreSQL** — set `DB_PROVIDER=Postgres` and `DB_CONNECTION_STRING=...` in `.env` (see `.env.example`).

To create or update PostgreSQL migrations after model changes, target the Postgres migrations project (the `Database__Provider=Postgres` env var makes the design-time factory use Npgsql):

```bash
Database__Provider=Postgres dotnet ef migrations add <Name> \
  --project HomeLedger.Migrations.PostgreSql --startup-project HomeLedger.Web
```

### LLM integration

**Docker:** copy `.env.example` to `.env` and set `LLM_*` variables (see above).

**Local `dotnet run`:** use `appsettings.Development.json` or environment variables:

```json
{
  "Llm": {
    "Enabled": true,
    "BaseUrl": "http://localhost:11434/v1",
    "DefaultModel": "llama3.2",
    "VisionModel": "llava",
    "UseForCategorization": true,
    "UseForImportClassification": true,
    "UseForStatementImport": true,
    "UseForReceiptImport": true
  }
}
```

Categorization order: keyword rules → similar past transactions → LLM (if enabled) → income/expense fallback.

### Receipt image import

On the Import page, upload one or more receipt photos (JPEG, PNG, WebP, etc.). Each image is analyzed with the configured **vision model** (local Ollama such as `qwen2.5vl`, or cloud GPT-4o / Claude / Gemini). Extracted transactions go through the same review, categorization, and deduplication pipeline as CSV and PDF imports. Configure via `Llm:UseForReceiptImport` and `Llm:MaxReceiptImages` (default 20).

### CSV import

Supported column names (case-insensitive, flexible): `date`, `amount`, `description`, `debit`/`credit`, `id`. Bank-specific presets can be added in `CsvImportService`.

## Project layout

```
homeledger/
├── HomeLedger.Core/           # Entities, configuration
├── HomeLedger.Infrastructure/ # EF Core, import, budgets, LLM client
├── HomeLedger.Web/            # MVC controllers, Razor views, HTMX
├── HomeLedger.slnx
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
