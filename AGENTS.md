# Agent instructions

## JDWriter: read this before the template guidance below

This application is a **port**, not a greenfield build. It is being re-platformed from a
working Next.js 16 proof of concept at `~/Desktop/Projects/JDWriter` onto this template. The
migration plan is at `~/.claude/plans/iridescent-riding-deer.md`.

**The POC is the specification.** Its TypeScript is the reference implementation and the parity
oracle for every ported module. Keep it runnable. Do not retire it until parity holds.

### Golden-fixture parity

The POC's value is accumulated, empirically-earned quirks — Excel percent cells, four
near-identical form variants, repeating page headers, a strict/loose title-key split. Hand-written
C# unit tests would cover the cases we think of, which is the set that already works.

So: `npm run fixtures` in the POC repo regenerates deterministic JSON oracles into `fixtures/`.
Each ported module gets a test asserting it reproduces its fixture **exactly**. When a port and a
fixture disagree, the fixture is right until proven otherwise.

Extraction is only partly portable, and that boundary is deliberate: `kindFor`, `tidy` and the
size thresholds are byte-parity testable; docx and pdf text extraction are **not**, because
mammoth/unpdf and OpenXml/PdfPig disagree on whitespace and reading order. Those get behavioral
tests, not fixtures. Never claim fixture coverage for them.

### Layout B of the standards parser is a blind port

No real layout-B workbook (a UC website PDF-to-Excel export) is available: every usable sheet in
the 19 local workbooks is layout A. `parseFamilyTable` — the continuation pointer that survives
repeating page headers — is therefore verified only against synthetic inputs built from the format
as documented.

That pins the C# to the reference's behavior. It proves nothing about either implementation against
a real UC export. This was a deliberate call: get it running, fix bugs when a real file surfaces
rather than let the gap block the port.

When such a file appears: drop it in the POC's `Job Standards/`, run `npm run fixtures`, and the
test `Layout_B_has_no_real_world_coverage_and_that_is_recorded` will FAIL — which is the signal to
verify the port against it and delete the COVERAGE WARNING from `StandardsWorkbookParser`.

### Corpus-dependent tests

The HRTMS corpus is 1,367 real job descriptions, 150MB, gitignored — it cannot live in this repo.
Tests that need it are tagged `Category=CorpusParity` and resolve it from `JDW_CORPUS_DIR`, or
from a sibling POC checkout.

Where the corpus is unavailable, exclude them explicitly:

    dotnet test --filter "Category!=CorpusParity"

They FAIL rather than silently pass when the corpus is missing. A parity suite that quietly skips
is worse than none: the run goes green and nobody learns the parser was never checked.

### Invariants that must survive the port

Each of these was a real bug once. Preserve them explicitly, and do not "simplify" them away.

- **The model decides, code assembles.** Return indices, never reproduced strings. Return only
  the delta, never the document plus the delta. Recompute every number in C# — never trust a
  model to preserve a `% time` summing to 100.
- **Prompts port byte-for-byte.** Use C# raw string literals. Do not reflow, reword, or improve
  prompt text during a port; a behavior change is indistinguishable from a port bug.
- **The unit's proposed job code is withheld from the ranking prompt.** This is anti-anchoring,
  not confidentiality. A model shown the answer the unit wants tends to ratify it, which makes the
  confidence number meaningless. It is argued separately by a second call and compared afterward.
- **Strict vs loose title keys.** `titleCodeKey` keeps bargaining-unit suffixes; `titleKey` drops
  them. Merging them once collapsed 221 reference keys and produced silently *wrong* job codes.
  Resolution returns null on ambiguity rather than guessing.
- **Superseded job codes are remapped at parse time,** and every path that creates a profile
  guards against them. Hiding a dead class from browse and the classifier is not sufficient.
- **Six real JDs do not sum to 100% time** (range 0-200). Do not add a constraint requiring it.
- **One model seam.** Every model call goes through `IStructuredLlm`; the provider (Anthropic,
  Azure OpenAI, OpenAI-compatible) is chosen by `Llm:Provider` configuration and never in the app.
  Prompts are the same bytes for every provider — do not fork them per model.
- **Keys are write-only and audited.** No endpoint or log ever carries a key. In-app keys are
  stored per provider, encrypted with Data Protection, and every change writes `AppSecretAudits`.

### Data handling

JD records carry department names, UCPath position numbers, and reporting lines for real
employees. **Raw JD records are never committed.** Fixtures for them are content hashes plus
non-identifying shape statistics. Note that HRTMS filenames embed position numbers, so file paths
are identifiers too.

**Envelopes are not committed either** (decided 2026-10-01). They contain no position numbers, but
they are products of the system, so they live in the database and nowhere in the repository. The
MSW fixture keeps the shape of three real profiles with every piece of envelope text synthetic.
Position Descriptions are cleared for commit; the one real PD in `pd.json` has no position numbers.

Seed the real corpus into `prod` only. `test` gets a scrubbed subset (`jdw-cli migrate-poc --scrub`).

### The corpus grows in use

The 1,367-JD CLI load is only the start. Corpus records (`JobDescription`) carry an `Origin` —
`Export` (CLI load or admin upload), `Authored` (a finished JD that changed its envelope and passed
the envelope check), `Classify` (a Classify-page submission, filed under a class), `Imported` (a JD from a unit outside
the college — JDX can't export HRTMS's format — read from a Word/PDF/text copy and filed under the
class its stated title resolves to; never guessed on ambiguity) — and an
`AddedAt` the CLI never sets. Rebuilding a class reads all of them. Uploaded and submitted
originals are kept as bytes in the **database only** (Azure SQL encrypts at rest), never written to
disk, never logged — HRTMS file names embed position numbers.

Rebuilding a class regenerates its envelope **even if an admin edited it by hand** — a product
decision (2026-10-02): edits are superseded as JDs written against them flow back in. The admin
queue warns first. Do not "fix" this back to preserving manual envelopes.

Tests that read the local database must not assume the corpus is exactly what the CLI loaded;
scope to `AddedAt == null` when that is what they mean.

### Local environment

SQL Server 2022 is amd64-only. On Apple Silicon it runs only under **Rosetta** emulation — under
QEMU it aborts at startup. With Rosetta on (Docker Desktop setting, or Colima `vz` + `rosetta: true`),
use `npm run db:up` like every other machine; `docs/RUNNING-LOCALLY.md` has the setup and a check.
`npm run db:up:arm64` (an `azure-sql-edge` overlay) remains as a fallback only: that image is retired,
has no `sqlcmd`, and opens its port several seconds before it accepts logins.

The image's `sqlcmd` is `/opt/mssql-tools18/bin/sqlcmd` and needs `-C` (self-signed certificate).

---

This is a full-stack web application template using modern React and .NET technologies. Please follow these guidelines when generating code suggestions.

## Pull requests

`main` on `ucdavis/jdwriter` is branch-protected: changes go through a pull request, and the
**Validate** check must pass with the branch up to date (no approvals are required). Work on a
branch, rebase onto `origin/main` before opening the PR — other people push here too — and merge
with `--rebase` to keep the commit messages.

Follow `.github/pull_request_template.md` when creating or updating pull requests, including through the CLI. Use a concise, descriptive title and describe the final change. Remove optional sections that do not apply, and never claim validation that was not performed.

## Docker sandbox for investigation

Use the [Docker sandbox quick start](README.md#run-the-docker-sandbox) to investigate the current checkout with local sign-in and sample data. See [the sandbox guide](docs/SANDBOX.md) for role checks, logs, alternate ports, and browser investigation.

## Architecture Overview

- **Frontend**: React 19 with TypeScript, built with Vite
- **Backend**: ASP.NET Core 10 Web API
- **Development Flow**: Vite serves the frontend on port `5173` and proxies API/auth/health requests to ASP.NET Core on port `5165`
- **Visual Studio Integration**: ASP.NET Core `SpaProxy` can launch Vite for Visual Studio users
- **Production Flow**: ASP.NET Core serves the built frontend from `server/wwwroot`

## Frontend Technology Stack

### Build Tools & Development

- **Vite** (`^7.1.5`) - Primary build tool and dev server on port `5173`
- **TypeScript** (`^5.9.2`) - Primary language for all React components
- **Node.js** - See [development setup](README.md#set-up-for-development) for runtime requirements and setup.

### React & Routing

- **React** `^19.1.1` with **React DOM** `^19.1.1`
- **TanStack Router** (`^1.132.33`) - File-based routing system
  - Routes live in `src/routes/`
  - The generated route tree is in `routeTree.gen.ts`
  - Router context includes the shared `QueryClient`
  - Default preload strategy is `'intent'`
  - Router devtools are enabled in development

### State Management & Data Fetching

- **TanStack Query** (`^5.90.2`) - Server state management
  - `QueryClient` is created in `src/main.tsx`
  - React Query devtools are enabled in development
  - Router preloading uses `defaultPreloadStaleTime: 0`

### Forms & Tables

- **TanStack React Form** (`^1.23.5`) - Form state management
- **TanStack React Table** (`^8.21.3`) - Table/data grid functionality

### Styling & UI

- **Tailwind CSS** (`^4.1.14`) - Utility-first CSS framework
- **DaisyUI** (`^5.1.27`) - Tailwind CSS component library
- **UC Davis Gunrock Tailwind** - Custom design system; dependency version is in [client/package.json](client/package.json).
- CSS imports are structured like:
  ```css
  @import "tailwindcss";
  @plugin "daisyui";
  @import "@ucdavis/gunrock-tailwind/imports.css";
  ```

### Code Quality & Linting

- **ESLint** (`^9.35.0`) with custom config (`@nkzw/eslint-config`)
- **Prettier** (`^3.6.2`) - Code formatting
- **TanStack ESLint plugins** for Query and Router
- **Vitest** (`^4.0.5`) - Testing framework
- **Testing Library** + **jsdom** - Component and route tests

### Path Aliases

- `@/` resolves to `./src/`

## Backend Technology Stack

### Framework & Runtime

- **ASP.NET Core 10.0** - Web API framework
- **.NET 10.0** - Target framework
- **C#** with nullable reference types enabled

### Authentication & Authorization

- **Microsoft Identity Web** (`3.14.1`) - Authentication integration
- Cookie-based auth flow is exposed through backend endpoints like `/login` and `/signin-oidc`

### Monitoring & Observability

- **OpenTelemetry** - Distributed tracing and metrics
  - OTLP exporter
  - ASP.NET Core instrumentation
  - HTTP instrumentation

### Development Tools

- **Swashbuckle** (`6.9.0`) - Swagger/OpenAPI documentation
- **Dotenv.Extensions.Microsoft.Configuration** (`3.1.0`) - `.env` configuration loading
- **Microsoft.AspNetCore.SpaProxy** (`10.0.1`) - Visual Studio dev-time frontend launch support

## Development Patterns

### Development Request Flow

See [Development Architecture](docs/ARCHITECTURE.md#development-request-flow) for request-flow diagrams and [Vite's responsibilities](docs/ARCHITECTURE.md#clientviteconfigts) for the backend proxy routes.

### Project Structure

```text
/
├── client/              # Vite React app
│   ├── src/
│   │   ├── routes/      # TanStack Router file-based routes
│   │   ├── queries/     # TanStack Query hooks
│   │   ├── lib/         # API helpers and utilities
│   │   ├── shared/      # Reusable UI/auth components
│   │   └── test/        # Client tests
├── server/              # ASP.NET Core host app
│   ├── Examples/Notifications/ # Optional notification samples
│   ├── Controllers/
│   ├── Helpers/
│   ├── Properties/
│   ├── Program.cs       # Backend startup and middleware pipeline
│   └── server.csproj    # SpaProxy and publish integration
├── server.core/         # Shared server domain/data code
└── tests/server.tests/  # .NET server tests
```

### Routing Conventions

- File-based routing lives in `src/routes/`
- Protected routes are grouped under `(authenticated)/`
- The authenticated layout preloads the current user via `ensureQueryData(meQueryOptions())`
- Route components should use TanStack Router hooks and integrate cleanly with React Query

### Component Guidelines

- Use TypeScript for all components
- Prefer function components with hooks
- Use Tailwind CSS classes for styling
- Leverage DaisyUI components when appropriate
- Follow UC Davis Gunrock design system patterns

### Data Fetching

- Use TanStack Query for server state
- Create custom hooks and query options in `queries/`
- Prefer the shared `fetchJson` helper in `src/lib/api.ts`
- Keep API calls relative, for example `/api/example`
- Integrate query usage with router loading when route data is required up front

### Authentication Conventions

- In development, the browser talks to Vite on `:5173`, and Vite proxies auth and API requests to ASP.NET Core on `:5165`
- The authenticated route tree fetches `/api/user/me` before rendering child routes
- `fetchJson` redirects `401` responses to `/login?returnUrl=...` unless explicitly disabled
- Avoid hardcoding backend origins; use relative URLs so the same code works in development and production

### Form Handling

- Use TanStack React Form for complex forms
- Combine form state with TanStack Query mutations for server interactions
- Follow existing validation and submission patterns where present

### API Integration

- **Development mode**: Follow the [Vite proxy configuration](docs/ARCHITECTURE.md#clientviteconfigts) for backend routes
- **Production mode**: ASP.NET Core serves static files from `wwwroot/` and handles `/api` routes directly
- Authentication modes are documented under [Auth Configuration](README.md#auth-configuration)
- Use type-safe API client patterns for request and response shapes
- Prefer relative paths like `/api/example`, never hardcoded `http://localhost:5165/api/example`

### Development Commands

- `npm start` - Start backend and frontend together from the repo root
- `npm run start:server` - Start only the ASP.NET Core backend with `dotnet watch`
- `npm run start:client` - Start only the Vite dev server
- `npm run db:up` - Start the local SQL Server container
- `npm run db:down` - Stop the local SQL Server container
- `npm run db:logs` - Tail SQL Server logs
- `cd client && npm run build` - Build the frontend for production
- `cd client && npm run lint` - Run ESLint
- Client test commands: see [Client tests](README.md#client-tests).
- `dotnet test` - Execute the .NET test project(s)

### Testing

- Client tests use Vitest, jsdom, and Testing Library
- Server tests live under `tests/server.tests/`
- Frontend route work often needs auth-aware mocking because authenticated routes preload `/api/user/me`

## Code Generation Preferences

1. **Always use TypeScript** - No plain JavaScript files for app code
2. **Prefer functional components** - Use hooks over class components
3. **Use Tailwind CSS classes** - Avoid custom CSS unless necessary
4. **Leverage the TanStack ecosystem** - Router, Query, Form, and Table are the default tools
5. **Follow file-based routing** - Add route files in the correct `src/routes/` location
6. **Use type-safe API calls** - Maintain TypeScript interfaces for API responses
7. **Use modern React patterns** - Hooks, context, and current React APIs
8. **Prefer existing shared helpers** - Reuse `fetchJson`, query options, auth context, and shared components before adding new abstractions
9. **Environment-aware code** - Keep development and production behavior aligned through relative URLs and existing proxy/static-file patterns
10. **Responsive design** - Use Tailwind responsive utilities

### Optional Notifications

See [optional email notifications](server.core/Notification/README.md) for reusable services, sample boundaries, configuration, and removal steps.

### C# Readability Preferences

Prefer simple, explicit C# conditionals that balance compactness with readability. Use familiar null-conditional operators, direct boolean checks, and short local variables when they make intent easier to scan.

Avoid advanced pattern-matching forms for routine null checks, collection checks, property extraction, or aliasing. Use pattern matching only when it clearly models the domain logic better than ordinary conditionals.

### Async Timing and Synchronization

Do not use arbitrary delays, sleeps, timers, or timeout-based retries to hide race conditions or make async code appear to work. Avoid patterns like `setTimeout`, `Task.Delay`, polling loops, or fixed wait times unless the delay itself is a real product requirement.

When coordinating async work, prefer explicit completion signals and proper async primitives: `Promise` chains, `async`/`await`, awaited task results, cancellation tokens, event callbacks, query/mutation lifecycle hooks, router loaders, or framework-provided readiness states.

If timing code seems necessary, first identify the real dependency being waited on and model that dependency directly. Use timeouts only for bounded failure handling, cancellation, user feedback, or external-system resilience, not as a substitute for correct async flow.

## Common Patterns

### Route Component Example

```tsx
import { createFileRoute } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';

export const Route = createFileRoute('/(authenticated)/example')({
  component: ExampleComponent,
});

function ExampleComponent() {
  const { data } = useQuery({
    queryKey: ['example'],
    queryFn: () => fetch('/api/example').then((res) => res.json()),
  });

  return (
    <div className="container mx-auto p-4">
      {/* DaisyUI and Tailwind styling */}
    </div>
  );
}
```

### API Controller Pattern

```csharp
[ApiController]
[Route("api/[controller]")]
public class ExampleController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetExample()
    {
        // Implementation
    }
}
```

## Important Development Notes

1. **Port Usage**:
   - Backend runs on `http://localhost:5165`
   - Vite runs on `http://localhost:5173`
   - For command-line development, use `http://localhost:5173`
   - Visual Studio may launch through the backend profile and redirect to `:5173` via `SpaProxy`

2. **API Calls**:
   - Always use relative paths like `/api/example`
   - Do not hardcode localhost ports into frontend fetch calls
   - Relative paths keep auth and proxy behavior correct in both development and production

3. **Proxy Configuration**:
   - Development proxying lives in `client/vite.config.ts`
   - `SpaProxy` settings live in `server/server.csproj` and launch settings
   - `server/Program.cs` does not proxy Vite in development

4. **Hot Reload**:
   - Vite HMR runs directly on `:5173`
   - Backend changes trigger `dotnet watch` restarts on `:5165`
   - Visual Studio users can rely on `SpaProxy` to launch the frontend automatically

5. **Production Build**:
   - `cd client && npm run build` outputs frontend assets to `client/dist/`
   - `dotnet publish` also builds the client and copies `client/dist/` into `wwwroot`
   - ASP.NET Core serves static files directly in production
   - See [the middleware responsibilities](docs/ARCHITECTURE.md#serverprogramcs) for static files and SPA fallback behavior

When generating code, ensure it follows these patterns and integrates well with the existing technology stack.
