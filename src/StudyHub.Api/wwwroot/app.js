// StudyHub front end. Plain JavaScript, no build step.
// Each "page" is a function that draws into <main id="app">.
// The address after the # decides which page is shown, e.g. #/note/123.

"use strict";

const API = "/api";
const PAGE_SIZE = 10;
const app = document.getElementById("app");
const nav = document.getElementById("nav");

let flash = null; // a one-time message shown at the top of the next page
let renderId = 0; // lets us ignore a slow response from a page the user already left

// ---------- who is logged in ----------

const session = (() => {
  const KEY = "studyhub.session";
  let current = null;
  try {
    current = JSON.parse(localStorage.getItem(KEY));
  } catch {
    current = null;
  }
  return {
    get: () => current,
    set(value) {
      current = value;
      try {
        localStorage.setItem(KEY, JSON.stringify(value));
      } catch {
        /* storage unavailable: stay logged in until the tab closes */
      }
    },
    clear() {
      current = null;
      try {
        localStorage.removeItem(KEY);
      } catch {
        /* nothing to clear */
      }
    },
  };
})();

// ---------- talking to the API ----------

async function api(path, { method = "GET", body } = {}) {
  const headers = {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const user = session.get();
  if (user) headers.Authorization = `Bearer ${user.token}`;

  let response;
  try {
    response = await fetch(API + path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new Error("Can't reach the server. Check your connection and try again.");
  }

  // The token expires after an hour. Send the user back to log in.
  if (response.status === 401 && user && !path.startsWith("/auth/")) {
    session.clear();
    flash = "Your session ended. Log in again to continue.";
    go(`/login?next=${encodeURIComponent(location.hash.slice(1) || "/")}`);
    throw new Error("Your session ended. Log in again to continue.");
  }

  const text = await response.text();
  let data = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = null;
  }

  if (!response.ok) throw new Error(problemMessage(data, response.status));
  return data;
}

// Turns the API's error format into one readable sentence.
function problemMessage(data, status) {
  if (data && data.errors) {
    const messages = Object.values(data.errors).flat();
    if (messages.length) return messages.join(" ");
  }
  if (data && data.title) return data.title;
  return `Something went wrong (error ${status}). Try again.`;
}

// ---------- small helpers ----------

// Always escape text that came from users before putting it into HTML.
function esc(value) {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;",
  }[c]));
}

function formatDate(iso) {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return date.toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
}

function go(path) {
  location.hash = `#${path}`;
}

function readAddress() {
  const raw = location.hash.slice(1) || "/";
  const [path, query = ""] = raw.split("?");
  return { parts: path.split("/").filter(Boolean), params: new URLSearchParams(query) };
}

function takeFlash() {
  if (!flash) return "";
  const html = `<p class="notice" role="status">${esc(flash)}</p>`;
  flash = null;
  return html;
}

function showFormError(form, message) {
  const box = form.querySelector(".error");
  box.textContent = message;
  box.hidden = false;
}

// Runs a form's action with the button disabled, and shows any error in the form.
async function submitForm(form, action) {
  const button = form.querySelector("button[type=submit]");
  const errorBox = form.querySelector(".error");
  errorBox.hidden = true;
  button.disabled = true;
  try {
    await action(new FormData(form));
  } catch (error) {
    showFormError(form, error.message);
  } finally {
    button.disabled = false;
  }
}

function parseTags(text) {
  return String(text || "")
    .split(",")
    .map((tag) => tag.trim().replace(/^#/, ""))
    .filter(Boolean);
}

// ---------- header links ----------

function renderNav() {
  const user = session.get();
  nav.innerHTML = user
    ? `<a href="#/">Browse notes</a>
       <a class="btn btn-primary" href="#/new">Write a note</a>
       <span class="who">${esc(user.displayName)}</span>
       <button type="button" class="link-button" id="logout">Log out</button>`
    : `<a href="#/">Browse notes</a>
       <a href="#/login">Log in</a>
       <a class="btn btn-primary" href="#/signup">Sign up</a>`;

  const logout = document.getElementById("logout");
  if (logout) {
    logout.addEventListener("click", () => {
      session.clear();
      flash = "Logged out.";
      if (location.hash === "#/" || location.hash === "") route();
      else go("/");
    });
  }
}

// ---------- page: browse and search ----------

async function pageHome(params, id) {
  const filters = {
    search: params.get("search") || "",
    course: params.get("course") || "",
    tag: params.get("tag") || "",
    sort: params.get("sort") === "helpful" ? "helpful" : "",
    page: Math.max(1, parseInt(params.get("page"), 10) || 1),
  };

  app.innerHTML = `
    ${takeFlash()}
    <section>
      <h1>Find notes for your course</h1>
      <p class="lede">Written by students, for students. Search by topic, course code or tag.</p>
      <form class="search" id="search-form" role="search">
        <label class="field field-wide">
          <span>Topic or keyword</span>
          <input name="search" type="search" value="${esc(filters.search)}" placeholder="deadlock, CSS grid, recursion">
        </label>
        <label class="field">
          <span>Course code</span>
          <input name="course" value="${esc(filters.course)}" placeholder="DCIT 301" maxlength="20">
        </label>
        <label class="field">
          <span>Tag</span>
          <input name="tag" value="${esc(filters.tag)}" placeholder="os" maxlength="50">
        </label>
        <button type="submit" class="btn btn-primary">Search notes</button>
      </form>
    </section>
    <section class="results" id="results" aria-live="polite">
      <p class="muted">Loading notes…</p>
    </section>`;

  document.getElementById("search-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const data = new FormData(event.target);
    go(homeAddress({
      search: data.get("search").trim(),
      course: data.get("course").trim(),
      tag: data.get("tag").trim().replace(/^#/, ""),
      sort: filters.sort,
      page: 1,
    }));
  });

  const query = new URLSearchParams({ page: filters.page, pageSize: PAGE_SIZE });
  if (filters.search) query.set("search", filters.search);
  if (filters.course) query.set("courseCode", filters.course);
  if (filters.tag) query.set("tag", filters.tag);
  if (filters.sort) query.set("sort", filters.sort);

  const results = document.getElementById("results");
  let data;
  try {
    data = await api(`/materials?${query}`);
  } catch (error) {
    if (id === renderId) results.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
    return;
  }
  if (id !== renderId) return;

  const filtered = Boolean(filters.search || filters.course || filters.tag);
  if (data.totalCount === 0) {
    results.innerHTML = filtered
      ? `<div class="empty">
           <h2>No notes match that search</h2>
           <p>Try a different word, or remove a filter. <a href="#/">Show all notes</a></p>
         </div>`
      : `<div class="empty">
           <h2>No notes yet</h2>
           <p>Be the first to share one. <a href="#/new">Write a note</a></p>
         </div>`;
    return;
  }

  const totalPages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
  const count = `${data.totalCount} ${data.totalCount === 1 ? "note" : "notes"}`;

  results.innerHTML = `
    <div class="results-head">
      <h2>${filtered ? `${count} found` : `All notes (${data.totalCount})`}</h2>
      <p class="sort">
        <span class="muted">Show first:</span>
        <a href="#${homeAddress({ ...filters, sort: "", page: 1 })}" ${filters.sort ? "" : 'aria-current="true"'}>Newest</a>
        <a href="#${homeAddress({ ...filters, sort: "helpful", page: 1 })}" ${filters.sort ? 'aria-current="true"' : ""}>Most helpful</a>
        ${filtered ? `<a class="clear" href="#${homeAddress({ sort: filters.sort })}">Clear search</a>` : ""}
      </p>
    </div>
    <ul class="note-list">
      ${data.items.map(noteRow).join("")}
    </ul>
    ${totalPages > 1 ? `
      <nav class="pager" aria-label="Pages">
        ${data.page > 1 ? `<a class="btn" href="#${homeAddress({ ...filters, page: data.page - 1 })}">Previous</a>` : ""}
        <span class="muted">Page ${data.page} of ${totalPages}</span>
        ${data.page < totalPages ? `<a class="btn" href="#${homeAddress({ ...filters, page: data.page + 1 })}">Next</a>` : ""}
      </nav>` : ""}`;
}

function homeAddress({ search, course, tag, sort, page }) {
  const query = new URLSearchParams();
  if (search) query.set("search", search);
  if (course) query.set("course", course);
  if (tag) query.set("tag", tag);
  if (sort) query.set("sort", sort);
  if (page && page > 1) query.set("page", page);
  const text = query.toString();
  return text ? `/?${text}` : "/";
}

function noteRow(note) {
  const tags = note.tags.map((tag) => `#${esc(tag)}`).join("  ");
  return `
    <li class="note-row">
      <a href="#/note/${esc(note.id)}">
        ${note.courseCode ? `<span class="course">${esc(note.courseCode)}</span>` : `<span></span>`}
        <span class="note-title">${esc(note.title)}</span>
        <span class="note-meta">${note.helpfulCount > 0 ? `<strong>${note.helpfulCount} helpful</strong>, ` : ""}${esc(note.authorName)}, ${esc(formatDate(note.createdAtUtc))}</span>
        ${tags ? `<span class="note-tags">${tags}</span>` : ""}
      </a>
    </li>`;
}

// ---------- page: read one note ----------

async function pageNote(noteId, id) {
  app.innerHTML = `<p class="muted">Loading note…</p>`;

  let note;
  try {
    note = await api(`/materials/${encodeURIComponent(noteId)}`);
  } catch (error) {
    if (id === renderId) {
      app.innerHTML = `
        <a class="back" href="#/">Back to all notes</a>
        <p class="error" role="alert">${esc(error.message)}</p>`;
    }
    return;
  }
  if (id !== renderId) return;

  const user = session.get();
  const isAuthor = Boolean(user && user.userId === note.authorId);
  const edited = new Date(note.updatedAtUtc) - new Date(note.createdAtUtc) > 60000;

  let actions;
  if (isAuthor) {
    actions = `
      <a class="btn" href="#/edit/${esc(note.id)}">Edit note</a>
      <button type="button" class="btn btn-danger" id="delete">Delete note</button>`;
  } else if (user) {
    actions = `<button type="button" class="link-button" id="report-toggle">Report this note</button>`;
  } else {
    actions = `<a href="#/login?next=${encodeURIComponent(`/note/${note.id}`)}">Log in to report this note</a>`;
  }

  document.title = `${note.title} | StudyHub`;
  app.innerHTML = `
    ${takeFlash()}
    <a class="back" href="#/">Back to all notes</a>
    <article>
      <header class="note-head">
        ${note.courseCode ? `<a class="course" href="#${homeAddress({ course: note.courseCode })}">${esc(note.courseCode)}</a>` : ""}
        <h1>${esc(note.title)}</h1>
        <p class="byline">By ${esc(note.authorName)}, ${esc(formatDate(note.createdAtUtc))}${edited ? ` (edited ${esc(formatDate(note.updatedAtUtc))})` : ""}</p>
        ${note.tags.length ? `
          <p class="tag-links">
            ${note.tags.map((tag) => `<a href="#${homeAddress({ tag })}">#${esc(tag)}</a>`).join("")}
          </p>` : ""}
      </header>
      <div class="ruled">${esc(note.body)}</div>
    </article>
    <div class="helpful" id="helpful"></div>
    <div class="note-actions" id="note-actions">${actions}</div>
    <div id="report-area"></div>`;

  renderHelpful(note, isAuthor);

  const deleteButton = document.getElementById("delete");
  if (deleteButton) deleteButton.addEventListener("click", () => confirmDelete(note));

  const reportToggle = document.getElementById("report-toggle");
  if (reportToggle) reportToggle.addEventListener("click", () => showReportForm(note));
}

// The "helpful" line under a note: a count for everyone, and a button for
// logged-in readers who didn't write the note.
function renderHelpful(note, isAuthor) {
  const area = document.getElementById("helpful");
  const user = session.get();
  const count = note.helpfulCount;
  const summary = count === 0
    ? "No one has marked this helpful yet."
    : `${count} ${count === 1 ? "student" : "students"} found this helpful.`;

  let control = "";
  if (!user) {
    control = `<a href="#/login?next=${encodeURIComponent(`/note/${note.id}`)}">Log in to mark it helpful</a>`;
  } else if (!isAuthor) {
    control = `<button type="button" class="btn ${note.markedHelpfulByMe ? "btn-marked" : ""}" id="helpful-button"
      aria-pressed="${note.markedHelpfulByMe}">${note.markedHelpfulByMe ? "Marked helpful" : "Mark as helpful"}</button>`;
  }

  area.innerHTML = `${control}<span class="muted" role="status">${summary}</span>`;

  const button = document.getElementById("helpful-button");
  if (!button) return;
  button.addEventListener("click", async () => {
    button.disabled = true;
    try {
      const result = await api(`/materials/${encodeURIComponent(note.id)}/helpful`, {
        method: note.markedHelpfulByMe ? "DELETE" : "PUT",
      });
      note.helpfulCount = result.helpfulCount;
      note.markedHelpfulByMe = result.markedHelpfulByMe;
      renderHelpful(note, isAuthor);
      document.getElementById("helpful-button").focus();
    } catch (error) {
      area.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
    }
  });
}

// Deleting asks a second time, in the page, before anything is removed.
function confirmDelete(note) {
  const area = document.getElementById("note-actions");
  area.innerHTML = `
    <span>Delete this note for good?</span>
    <button type="button" class="btn btn-danger" id="delete-yes">Yes, delete it</button>
    <button type="button" class="btn" id="delete-no">Keep it</button>`;
  document.getElementById("delete-yes").focus();

  document.getElementById("delete-no").addEventListener("click", () => route());
  document.getElementById("delete-yes").addEventListener("click", async (event) => {
    event.target.disabled = true;
    try {
      await api(`/materials/${encodeURIComponent(note.id)}`, { method: "DELETE" });
      flash = "Note deleted.";
      go("/");
    } catch (error) {
      area.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
    }
  });
}

function showReportForm(note) {
  const area = document.getElementById("report-area");
  area.innerHTML = `
    <form class="report-form" id="report-form">
      <label class="field">
        <span>What's wrong with this note?</span>
        <textarea class="short" name="reason" required minlength="5" maxlength="500"
          placeholder="Copied from a textbook, offensive, not study material"></textarea>
      </label>
      <p class="error" role="alert" hidden></p>
      <div class="form-actions">
        <button type="submit" class="btn btn-primary">Send report</button>
        <button type="button" class="link-button" id="report-cancel">Cancel</button>
      </div>
    </form>`;
  area.querySelector("textarea").focus();

  document.getElementById("report-cancel").addEventListener("click", () => {
    area.innerHTML = "";
  });

  document.getElementById("report-form").addEventListener("submit", (event) => {
    event.preventDefault();
    submitForm(event.target, async (data) => {
      await api(`/materials/${encodeURIComponent(note.id)}/reports`, {
        method: "POST",
        body: { reason: data.get("reason").trim() },
      });
      area.innerHTML = `<p class="notice" role="status">Report sent. Thanks for flagging it.</p>`;
      document.getElementById("note-actions").innerHTML = "";
    });
  });
}

// ---------- page: write or edit a note ----------

async function pageEditor(noteId, id) {
  const user = session.get();
  if (!user) {
    flash = "Log in to write a note.";
    go(`/login?next=${encodeURIComponent(noteId ? `/edit/${noteId}` : "/new")}`);
    return;
  }

  let note = { title: "", body: "", courseCode: "", tags: [] };
  if (noteId) {
    app.innerHTML = `<p class="muted">Loading note…</p>`;
    try {
      note = await api(`/materials/${encodeURIComponent(noteId)}`);
    } catch (error) {
      if (id === renderId) app.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
      return;
    }
    if (id !== renderId) return;
    if (note.authorId !== user.userId) {
      flash = "You can only edit your own notes.";
      go(`/note/${noteId}`);
      return;
    }
  }

  const saveLabel = noteId ? "Save changes" : "Publish note";
  app.innerHTML = `
    <h1>${noteId ? "Edit your note" : "Write a note"}</h1>
    <p class="lede">Share notes you wrote yourself. Don't paste in textbooks or exam papers.</p>
    <form class="form" id="note-form">
      <label class="field">
        <span>Title</span>
        <input name="title" required minlength="3" maxlength="200" value="${esc(note.title)}"
          placeholder="Scheduling algorithms cheat sheet">
      </label>
      <div class="form-row">
        <label class="field">
          <span>Course code</span>
          <input name="courseCode" maxlength="20" value="${esc(note.courseCode || "")}" placeholder="DCIT 301">
          <small>Optional</small>
        </label>
        <label class="field">
          <span>Tags</span>
          <input name="tags" value="${esc(note.tags.join(", "))}" placeholder="os, scheduling">
          <small>Up to 5, separated by commas</small>
        </label>
      </div>
      <label class="field">
        <span>Your notes</span>
        <textarea name="body" required minlength="10" maxlength="20000">${esc(note.body)}</textarea>
      </label>
      <p class="error" role="alert" hidden></p>
      <div class="form-actions">
        <button type="submit" class="btn btn-primary">${saveLabel}</button>
        <a href="#${noteId ? `/note/${esc(noteId)}` : "/"}">Cancel</a>
      </div>
    </form>`;

  document.getElementById("note-form").addEventListener("submit", (event) => {
    event.preventDefault();
    submitForm(event.target, async (data) => {
      const tags = parseTags(data.get("tags"));
      if (tags.length > 5) throw new Error("Use 5 tags or fewer.");

      const body = {
        title: data.get("title").trim(),
        body: data.get("body").trim(),
        courseCode: data.get("courseCode").trim() || null,
        tags,
      };

      const saved = noteId
        ? await api(`/materials/${encodeURIComponent(noteId)}`, { method: "PUT", body })
        : await api("/materials", { method: "POST", body });

      flash = noteId ? "Changes saved." : "Note published.";
      go(`/note/${saved.id}`);
    });
  });
}

// ---------- pages: sign up and log in ----------

function pageAuth(mode, params) {
  const isSignup = mode === "signup";
  const next = params.get("next") || "/";
  const nextQuery = params.get("next") ? `?next=${encodeURIComponent(next)}` : "";

  app.innerHTML = `
    ${takeFlash()}
    <h1>${isSignup ? "Create your account" : "Log in"}</h1>
    <form class="form form-narrow" id="auth-form">
      ${isSignup ? `
        <label class="field">
          <span>Your name</span>
          <input name="displayName" required minlength="2" maxlength="100" autocomplete="name">
          <small>Shown next to the notes you write</small>
        </label>` : ""}
      <label class="field">
        <span>Email</span>
        <input name="email" type="email" required maxlength="256" autocomplete="email">
      </label>
      <label class="field">
        <span>Password</span>
        <input name="password" type="password" required minlength="${isSignup ? 8 : 1}" maxlength="100"
          autocomplete="${isSignup ? "new-password" : "current-password"}">
        ${isSignup ? "<small>At least 8 characters</small>" : ""}
      </label>
      <p class="error" role="alert" hidden></p>
      <div class="form-actions">
        <button type="submit" class="btn btn-primary">${isSignup ? "Sign up" : "Log in"}</button>
        ${isSignup
          ? `<a href="#/login${nextQuery}">I already have an account</a>`
          : `<a href="#/signup${nextQuery}">Create an account</a>`}
      </div>
    </form>`;

  document.getElementById("auth-form").addEventListener("submit", (event) => {
    event.preventDefault();
    submitForm(event.target, async (data) => {
      const body = { email: data.get("email").trim(), password: data.get("password") };
      if (isSignup) body.displayName = data.get("displayName").trim();

      const result = await api(isSignup ? "/auth/register" : "/auth/login", { method: "POST", body });
      session.set({ token: result.token, userId: result.userId, displayName: result.displayName });
      go(next.startsWith("/") ? next : "/");
    });
  });
}

// ---------- deciding which page to show ----------

function route() {
  const id = ++renderId;
  const { parts, params } = readAddress();
  document.title = "StudyHub";
  renderNav();
  window.scrollTo(0, 0);

  switch (parts[0]) {
    case undefined:
      return pageHome(params, id);
    case "note":
      return parts[1] ? pageNote(parts[1], id) : go("/");
    case "new":
      return pageEditor(null, id);
    case "edit":
      return parts[1] ? pageEditor(parts[1], id) : go("/");
    case "login":
      return pageAuth("login", params);
    case "signup":
      return pageAuth("signup", params);
    default:
      app.innerHTML = `
        <h1>Page not found</h1>
        <p class="lede">That address doesn't lead anywhere. <a href="#/">Browse notes</a></p>`;
      return undefined;
  }
}

window.addEventListener("hashchange", route);
route();
