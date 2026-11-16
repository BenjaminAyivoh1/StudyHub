# StudyHub

A study-notes platform for university students. Students write and share notes, tag them by course, search them, and report anything that shouldn't be there. A live quiz mode comes in Phase 2.

> **Status:** Phase 1 (notes library API) in progress. `StudyHub` is a working name; rename it whenever you like.

## What it does today

- Sign up and log in (JWT authentication, PBKDF2 password hashing)
- Create, edit and delete your own notes
- Tag notes and tie them to a course code (for example `DCIT 305`)
- Search by text, tag or course, with pagination
- Report a note (one open report per user per note)
- Health check at `/health`

## Tech stack

| Area | Choice |
|---|---|
| Language / framework | C# 14, ASP.NET Core (.NET 10), controllers |
| Database | PostgreSQL 17 with Entity Framework Core |
| Auth | JWT bearer tokens |
| Tests | xUnit with the EF Core in-memory provider |
| Tooling | Docker, docker-compose, GitHub Actions |

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Docker.

```bash
# 1. Start PostgreSQL
docker compose up -d db

# 2. Install the EF Core command-line tool (once)
dotnet tool install --global dotnet-ef

# 3. Build and run the tests
dotnet build
dotnet test

# 4. Create the first database migration (once, then commit the Migrations folder)
dotnet ef migrations add InitialCreate --project src/StudyHub.Api --output-dir Data/Migrations

# 5. Run the API (applies migrations automatically in Development)
dotnet run --project src/StudyHub.Api
```

The API listens on `http://localhost:5080`. Open `src/StudyHub.Api/requests.http` in VS Code (REST Client extension) or Rider to try it out.

Run everything in containers instead:

```bash
docker compose up --build        # API on http://localhost:8080
```

## API

| Method | Route | Auth | What it does |
|---|---|---|---|
| POST | `/api/auth/register` | no | Create an account, returns a token |
| POST | `/api/auth/login` | no | Log in, returns a token |
| GET | `/api/materials?search=&tag=&courseCode=&page=&pageSize=` | no | Search and list notes |
| GET | `/api/materials/{id}` | no | Read one note |
| POST | `/api/materials` | yes | Create a note |
| PUT | `/api/materials/{id}` | yes (author only) | Edit a note |
| DELETE | `/api/materials/{id}` | yes (author only) | Delete a note |
| POST | `/api/materials/{id}/reports` | yes | Report a note |

Errors come back as standard problem-details JSON (`401`, `403`, `404`, `409`, `400` for validation).

## How the code is organised

```
src/StudyHub.Api
  Controllers/   thin HTTP layer: read the request, call a service, return a result
  Services/      the actual rules (who may edit what, how search works)
  Domain/        entities: User, Material, Tag, Report
  Data/          EF Core DbContext and migrations
  Security/      password hashing and JWT creation
  Contracts/     request and response shapes (what the API accepts and returns)
  Common/        exceptions and the handler that turns them into HTTP responses
tests/StudyHub.Tests   unit tests for the services and the password hasher
```

## Design decisions

- **Services throw typed exceptions** (`NotFoundException`, `ForbiddenException`...) and one handler maps them to HTTP status codes, so controllers stay short and the rules are easy to test without HTTP.
- **Tags are normalised** (trimmed, lower-case) and shared between notes, so `OS`, `os` and ` Os ` are one tag.
- **The same message for unknown email and wrong password** at login, so the API doesn't reveal which emails have accounts.
- **Plain-text search for now**, upgraded to PostgreSQL full-text search once the basics are deployed.
- **Students write their own notes.** Uploading other people's past papers raises copyright questions, so it is deliberately out of scope until there is a takedown process.

## Roadmap

- [ ] Phase 1: notes library, deployed with real users (see `docs/PLAN.md`)
- [ ] Phase 2: live quiz with SignalR (teacher creates a quiz, classmates join with a code)
- [ ] Phase 3: rate limiting on the public endpoints
- [ ] Phase 4: study assistant that generates practice questions from notes

## Security notes

- `appsettings.Development.json` contains a development-only signing key. Real deployments must set `Jwt__Key` (32+ random characters) as an environment variable and never commit it.
- Passwords are never stored in plain text.
