# Running JDWriter locally

Two things about this machine differ from the template's README, and both will stop you cold.

## 1. .NET lives in `~/.dotnet`, not on the system path

`brew install --cask dotnet-sdk` needs an admin password a managed machine will not give you. The
SDK was installed with Microsoft's no-admin script instead:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet"
```

So `dotnet` is on your PATH only because `~/.zshrc` puts it there:

```bash
export PATH="$HOME/.dotnet:$PATH"
```

**`.zshrc` is read by INTERACTIVE shells only.** A terminal you already had open does not have it —
open a new one, or `source ~/.zshrc`. If `npm start` prints `sh: dotnet: command not found`, that is
why: npm spawns scripts through `sh`, which inherits PATH from the terminal that launched it.

To undo: delete those lines from `~/.zshrc` and `rm -rf ~/.dotnet`.

## 2. SQL Server cannot run on Apple Silicon — use the arm64 override

`mcr.microsoft.com/mssql/server:2022-latest`, pinned by both of the template's compose files, is
amd64-only. Under QEMU on arm64 it does not run slowly, it fails to start:

```
/opt/mssql/bin/sqlservr: Invalid mapping of address 0x... in reserved address space
```

Docker Desktop's Rosetta option papers over this; a plain aarch64 Linux VM (Colima, Lima, Rancher)
has no such option. Use the additive override, which swaps in `azure-sql-edge`:

```bash
npm run db:up:arm64      # NOT npm run db:up
npm run db:down:arm64
```

Two things that image imposes:

- **No `sqlcmd` inside it** — use an external client for ad-hoc queries.
- **Its TCP port opens several seconds before it accepts logins**, so anything waiting on the socket
  alone connects and then fails to authenticate. Wait for `ready for client connections` in
  `docker logs jdwriter_devcontainer-sql-1`.

`port is already allocated` means another SQL container holds 14333; `docker ps --filter name=sql`
shows which.

## Start to finish

```bash
npm run db:up:arm64                                         # database
dotnet ef database update -p server.core -s server          # schema, first time only
dotnet run --project tools/jdw-cli -- migrate-poc --write   # load the corpus, ~15s
npm start                                                   # backend :5165 + client :5173
```

Sign in at <http://localhost:5173>. Local sign-in offers two fictional personas:

| Persona | Roles | Use it to |
|---|---|---|
| **Sample User** | Author, Admin | reach every surface, including the back end |
| **Basic User** | Author | check that the back end is hidden and the admin gates hold |

In development "sample" is whitelisted as an admin at startup. In a real environment admins come
from `Admin__BootstrapLoginIds` (configuration) plus whoever is added on `/backend/settings`.

### Signing in as yourself

With `Auth__ClientSecret` set in `server/.env`, `/login` also offers **Sign in to UC Davis** (see
the README's Auth Configuration). A campus sign-in is an **Author** unless your login ID is
whitelisted. To be an admin locally, add your campus login ID (the part before `@ucdavis.edu`):

```
Admin__BootstrapLoginIds=yourloginid
```

### Local data accumulates

Building JDs, classifying descriptions and uploading files all add to your local database. Once any
JD has been saved, `migrate-poc --write` refuses to replace the class profiles — deliberately, so
saved work cannot be wiped by a reload.

`migrate-poc` is idempotent; without `--write` it is a dry run reporting what it would load.

### The admin panels need two directories

The ingest and standards panels on `/backend` read files from disk, so they need to be told where.
Add to `server/.env` (gitignored):

```
Corpus__Directory=/Users/<you>/Desktop/Projects/JDWriter/Sample JDs
Standards__Directory=/Users/<you>/Desktop/Projects/JDWriter/Job Standards
```

Without them the panels show a `400` naming the missing setting. That is deliberate rather than an
empty list: a deployed environment may simply not have the HRTMS exports, and "nothing to ingest"
would be a lie.

## Working on the UI without a backend

The client was built against `docs/API-CONTRACT.md` with MSW mocks before the API existed, and
those mocks are still maintained — their fixtures come from real class profiles. They are **off by
default**: with no env file present the client talks to ASP.NET Core through Vite's proxy.

To switch them on, create `client/.env.development` (gitignored):

```
VITE_USE_MSW=true
```

That gives a fully clickable app with no backend and no database. Mocks are doubly gated — DEV
build AND this flag — so they cannot reach production, and unhandled requests pass through, so a
partially-built backend can be used as it lands.

## Tests

```bash
npm run test:server          # default: excludes the two opt-in categories
npm run test:server:corpus   # needs the 1,367-file HRTMS corpus (gitignored, 150MB)
npm run test:server:live     # makes REAL Anthropic API calls; needs ANTHROPIC_API_KEY exported
cd client && npm test -- --run
```

Corpus tests **fail rather than skip** when the corpus is absent. That is deliberate: a parity suite
that quietly skips goes green while nobody learns the parser was never checked.

## Choosing a model provider

The provider is configuration, never an in-app choice. Anthropic is the default. To run on a local
model instead — nothing leaves the machine — add to `server/.env`:

```bash
Llm__Provider=openai-compatible
Llm__Endpoint=http://localhost:11434/v1   # Ollama; LM Studio is http://localhost:1234/v1
Llm__Model=qwen3.8:latest                 # whatever `ollama list` shows
```

Azure OpenAI uses `Llm__Provider=azure-openai`, `Llm__Endpoint=https://NAME.openai.azure.com`,
`Llm__Model=<deployment name>` and `AZURE_OPENAI_API_KEY`. Settings shows which provider is active.

The prompts were written for Claude and are sent unchanged to every provider. Expect a local model
to be slower (a tailored assembly took about 2.5 minutes on Qwen locally, against seconds on the
API) and its judgment to differ; check results before relying on one.

## The API key

`ANTHROPIC_API_KEY` lives in `server/.env`, which is gitignored. The app loads it; the test host does
not, which is why `test:server:live` needs it exported.
