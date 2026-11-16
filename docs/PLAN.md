# Phase 1 plan: notes library (3 weeks)

**Goal:** a deployed API that real classmates use, with a clean repo you can talk about in interviews.

**Rule:** commit small and often, and write the commit message for someone reading it in a year ("Add tag filter to material search", not "update").

## Week 1: run it, understand it, make it yours

- [x] Install .NET 10 SDK, Docker, and the `dotnet-ef` tool
- [x] Create a **new, empty GitHub repo** and push this code (nothing reused from other projects)
- [x] `dotnet build` and `dotnet test`. If anything fails, fix it first. The code was written without being compiled, so expect a typo or two
- [x] Start Postgres, create the first migration, run the API, and click through every request in `requests.http`
- [ ] Read the code in this order: `Domain` -> `Data/AppDbContext` -> `Services/MaterialService` -> `Controllers`. For each file, explain it in your own words in a comment or notes file
- [ ] Add **one test of your own** (suggestion: searching with no results returns an empty list)
- [ ] Change one small thing without help (suggestion: add a `Description` field to materials, then a migration)

**Done when:** all tests pass, you can register, log in, create and search a note locally, and you can explain what a controller, a service and a migration are.

## Week 2: make it solid

- [ ] Add an `IsAdmin` flag (or role) and an endpoint for admins to list and resolve reports
- [ ] Handle the "two people register the same email at the same moment" case (catch the unique-index error and return `409`)
- [ ] Add input validation tests (title too short, too many tags)
- [ ] Get **GitHub Actions green** on every push
- [ ] Confirm `docker compose up --build` works from a clean clone
- [ ] Write the "Design decisions" section of the README in your own words

**Done when:** CI is green, reports can be reviewed by an admin, and a stranger can run the project from the README alone.

## Week 3: ship it

- [ ] Deploy to a free host (Render, Railway, Fly.io or similar) with a managed Postgres
- [ ] Set `Jwt__Key` as a real secret on the host
- [ ] Replace the plain-text search with PostgreSQL full-text search, and keep the tests passing
- [ ] Add a seed script with 10-20 sample notes so the live site is not empty
- [ ] Ask 5 classmates to sign up and add a note each. Write down what confuses them
- [ ] Put the live URL, a short demo GIF and a "what I learned" section in the README

**Done when:** there is a public URL, at least a few real users, and a README that tells the story.

## Interview talking points you will have earned

- Why services throw typed exceptions instead of controllers returning status codes everywhere
- How password hashing works and why the salt is random per password
- What a migration is and why the database schema lives in source control
- How you tested business rules without starting a web server
- One real bug you found and fixed

## After Phase 1

Phase 2 (live quiz with SignalR) only starts once Phase 1 is deployed. A finished small project beats an unfinished big one.
