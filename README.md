# StudyHub

A study-notes platform for university students. Students write and share notes, organise them by topic and course, search them, and report anything that shouldn't be there. They can also run live quizzes that classmates join from their phones.

**Live site:** https://studyhub-3ek7.onrender.com (free hosting, so the first load can take a minute)

> **Status:** Phase 1 (notes library) and Phase 2 (live quizzes) are built. Next: more real users, then Phase 3.

## What it does today

- Sign up and log in (JWT authentication, PBKDF2 password hashing)
- Create, edit and delete your own notes
- Tag notes and tie them to a course code (for example `CSC 305`)
- Search by text, tag or course, with pagination
- Mark notes as helpful, and sort by most helpful
- Comment on notes
- Report a note (one open report per user per note)
- An admin page for reviewing reports and removing notes or comments
- Quiz history: finished games are saved. Logged-in players see their own results, the student who wrote a quiz sees how it has been played (times played, players, hardest questions), and admins see site-wide totals only
- Live quizzes: write multiple-choice questions, host a game, and let classmates join on their phones with a code and a nickname. Faster right answers score more, with a live leaderboard
- A website for all of the above, served by the API (plain HTML, CSS and JavaScript in `wwwroot`)
- 15 starter notes so a new site isn't empty, posted by a clearly named "StudyHub Team" account. They are organised by topic with no course code, since codes differ between universities; students add their own
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

The website and API are both at `http://localhost:5080`. To call the API directly, open `src/StudyHub.Api/requests.http` in VS Code (REST Client extension) or Rider to try it out.

Run everything in containers instead:

```bash
docker compose up --build        # API on http://localhost:8080
```

## Settings for a hosting provider

Set these as environment variables on the host. None of them belong in Git.

| Variable | Value |
|---|---|
| `DATABASE_URL` | The database address from your provider, for example `postgresql://user:password@host/dbname` |
| `Jwt__Key` | A long random secret (32 characters or more) |
| `Database__MigrateOnStartup` | `true`, so the tables are created on first start |
| `Database__SeedSampleNotes` | `true`, to add the starter notes once |
| `Admin__Emails` | The email addresses of the site's admins, separated by commas |

Sign up on the site with your admin email **before** setting `Admin__Emails`, so nobody else can register that address first.

The app reads the `PORT` variable if the host sets one.

## API

| Method | Route | Auth | What it does |
|---|---|---|---|
| POST | `/api/auth/register` | no | Create an account, returns a token |
| POST | `/api/auth/login` | no | Log in, returns a token |
| GET | `/api/materials?search=&tag=&courseCode=&sort=&page=&pageSize=` | no | Search and list notes (`sort=helpful` for most helpful first) |
| GET | `/api/materials/{id}` | no | Read one note |
| POST | `/api/materials` | yes | Create a note |
| PUT | `/api/materials/{id}` | yes (author only) | Edit a note |
| DELETE | `/api/materials/{id}` | yes (author only) | Delete a note |
| POST | `/api/materials/{id}/reports` | yes | Report a note |
| PUT | `/api/materials/{id}/helpful` | yes (not the author) | Mark a note as helpful |
| DELETE | `/api/materials/{id}/helpful` | yes | Remove your helpful mark |
| GET | `/api/materials/{id}/comments` | no | List a note's comments |
| POST | `/api/materials/{id}/comments` | yes | Add a comment |
| DELETE | `/api/materials/{id}/comments/{commentId}` | yes (comment author only) | Delete a comment |
| GET | `/api/auth/me` | yes | Who is logged in, and whether they are an admin |
| GET | `/api/quizzes/mine` | yes | List your quizzes |
| GET | `/api/quizzes/{id}` | yes (owner only) | One quiz, with its answers |
| POST | `/api/quizzes` | yes | Create a quiz |
| PUT | `/api/quizzes/{id}` | yes (owner only) | Replace a quiz |
| DELETE | `/api/quizzes/{id}` | yes (owner only) | Delete a quiz |
| GET | `/api/quizzes/{id}/stats` | yes (owner only) | How the quiz has been played |
| GET | `/api/history/mine` | yes | The quiz games you have played |
| GET | `/api/admin/stats` | admin | Site-wide totals |
| GET | `/api/admin/reports` | admin | List open reports |
| POST | `/api/admin/reports/{id}/dismiss` | admin | Keep the note and close the report |
| DELETE | `/api/admin/materials/{id}` | admin | Remove any note |
| DELETE | `/api/admin/comments/{id}` | admin | Remove any comment |

Errors come back as standard problem-details JSON (`401`, `403`, `404`, `409`, `400` for validation).

## How a live quiz works

Playing a quiz uses SignalR instead of ordinary requests. Each browser keeps a connection open to `/hubs/quiz`, so the server can push a new question or the scores to every phone at once.

- `Live/QuizGame.cs` holds the rules: who can join, how answers are scored, who is winning. It has no web or database code, so the tests call it directly.
- `Live/GameRegistry.cs` keeps the running games in memory and hands out the six-character codes.
- `Live/QuizHub.cs` passes messages between the host, the players and the game.

Running games are kept in memory, not in the database. When the host shows the final results, the game is saved (`GameSession`, `GameResult`, `GameQuestionStat`) by `Services/GameHistoryService.cs`. Players who joined while logged in have the result linked to their account; guests are saved by nickname only. A game that ends early because the host left is not saved. That is simple and fast, and it means a game ends if the server restarts or the host leaves. Quizzes themselves are saved in the database.

The host chooses two settings in the lobby:

- **Scoring.** *Speed and accuracy* (the default): a right answer earns 500 points plus up to 500 more for speed. The server measures the time, so a player cannot claim to have answered faster than they did. *Correct answers only*: one point per right answer, and speed doesn't matter.
- **Move on automatically.** When it's on, the next question starts 8 seconds after the answer is shown. The host can pause it, and can always click Next themselves.

## How the code is organised

```
src/StudyHub.Api
  Controllers/   thin HTTP layer: read the request, call a service, return a result
  Services/      the actual rules (who may edit what, how search works)
  Domain/        entities: User, Material, Tag, Report, HelpfulVote, Comment, Quiz, QuizQuestion, GameSession (with its results)
  Data/          EF Core DbContext and migrations
  Security/      password hashing and JWT creation
  Contracts/     request and response shapes (what the API accepts and returns)
  Live/          live quiz games: the rules, the registry of running games, and the SignalR hub
  Common/        exceptions and the handler that turns them into HTTP responses
  wwwroot/       the website: index.html, styles.css, app.js (notes) and quiz.js (quizzes)
tests/StudyHub.Tests   unit tests for the services and the password hasher
```

## Design decisions

- **Services throw typed exceptions** (`NotFoundException`, `ForbiddenException`...) and one handler maps them to HTTP status codes, so controllers stay short and the rules are easy to test without HTTP.
- **Tags are normalised** (trimmed, lower-case) and shared between notes, so `OS`, `os` and ` Os ` are one tag.
- **The same message for unknown email and wrong password** at login, so the API doesn't reveal which emails have accounts.
- **Plain-text search for now**, upgraded to PostgreSQL full-text search once the basics are deployed.
- **Students write their own notes.** Uploading other people's past papers raises copyright questions, so it is deliberately out of scope until there is a takedown process.

## Roadmap

- [x] Phase 1: notes library, built and deployed (still to do: grow real users)
- [x] Phase 2: live quiz with SignalR (a host writes a quiz, classmates join with a code)
- [ ] Phase 3: rate limiting on the public endpoints
- [ ] Phase 4: study assistant that generates practice questions from notes

## Security notes

- `appsettings.Development.json` contains a development-only signing key. Real deployments must set `Jwt__Key` (32+ random characters) as an environment variable and never commit it.
- Passwords are never stored in plain text.
