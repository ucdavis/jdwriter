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

## 2. On Apple Silicon, emulate amd64 with Rosetta

`mcr.microsoft.com/mssql/server:2022-latest` is amd64-only, so an Apple Silicon Mac runs it under
emulation. **Which emulator matters.** Under QEMU it does not run slowly, it fails to start:

```
/opt/mssql/bin/sqlservr: Invalid mapping of address 0x... in reserved address space
```

Under Rosetta it is ready in seconds and runs the migrations and the full corpus load normally
(verified 2026-10-06). Turn Rosetta on once:

- **Docker Desktop:** Settings → General → *Use Rosetta for x86_64/amd64 emulation on Apple Silicon*.
- **Colima:** `colima start --vm-type vz --vz-rosetta`, or set `vmType: vz` and `rosetta: true` in
  `~/.colima/default/colima.yaml` and `colima restart`. (Restarting stops running containers;
  `npm run db:up` brings the database back.)

Check which emulator is active — this should print `rosetta`:

```bash
docker run --rm --platform linux/amd64 alpine sh -c 'grep -m1 -o rosetta /proc/self/maps || echo qemu'
```

Then use the same commands as everyone else:

```bash
npm run db:up
npm run db:down
```

**Fallback** where Rosetta is unavailable: `npm run db:up:arm64` overlays
`.devcontainer/docker-compose.arm64.yml`, which swaps in `azure-sql-edge` (native arm64). Microsoft
has retired that image, it has no `sqlcmd`, and its port opens several seconds before it accepts
logins (wait for `ready for client connections` in `docker logs jdwriter_devcontainer-sql-1`).

`port is already allocated` means another SQL container holds 14333; `docker ps --filter name=sql`
shows which.

## Start to finish

```bash
npm run db:up                                               # database (Rosetta on Apple Silicon)
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
