// StudyHub front end. Plain JavaScript, no build step.
// Each "page" is a function that draws into <main id="app">.
// The address after the # decides which page is shown, e.g. #/note/123.

"use strict";

const API = "/api";
const PAGE_SIZE = 10;
const app = document.getElementById("app");
const nav = document.getElementById("nav");

const toasts = document.getElementById("toasts");
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
  // Every tab of the site shares one login. When another tab logs in or out,
  // this tab is told, and redraws so it never shows the wrong person's page.
  window.addEventListener("storage", (event) => {
    if (event.key !== KEY) return;
    const before = current ? current.userId : null;
    try {
      current = JSON.parse(event.newValue);
    } catch {
      current = null;
    }
    const after = current ? current.userId : null;
    if (before !== after) route();
    else renderNav();
  });

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
    toast("Your session ended. Log in again to continue.", "error");
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
  if (status === 403) return "You don't have permission to do that.";
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

// A coloured circle with the first letter of a name. The colour is picked
// from the name, so the same person always gets the same colour.
function avatar(name) {
  const text = String(name || "?").trim() || "?";
  let hash = 0;
  for (const ch of text) hash = (hash * 31 + ch.codePointAt(0)) % 997;
  return `<span class="avatar avatar-${hash % 5}" aria-hidden="true">${esc(text[0].toUpperCase())}</span>`;
}

// The drawing in the home page band: two sheets of ruled notes.
const HERO_ART = `
  <svg class="hero-art" viewBox="0 0 340 250" aria-hidden="true">
    <rect x="66" y="30" width="214" height="194" rx="10" fill="#33456f" transform="rotate(-8 173 127)"/>
    <g transform="rotate(4 170 125)">
      <rect x="58" y="22" width="224" height="204" rx="10" fill="#ffffff"/>
      <rect x="90" y="22" width="2" height="204" fill="#d2414f"/>
      <rect x="104" y="46" width="88" height="22" rx="3" fill="#ffe35a"/>
      <rect x="104" y="80" width="150" height="12" rx="6" fill="#17233f"/>
      <g stroke="#c5d6ec" stroke-width="1.5">
        <path d="M58 114h224M58 138h224M58 162h224M58 186h224M58 210h224"/>
      </g>
      <g fill="#93a1bd">
        <rect x="104" y="103" width="142" height="6" rx="3"/>
        <rect x="104" y="127" width="118" height="6" rx="3"/>
        <rect x="104" y="151" width="154" height="6" rx="3"/>
        <rect x="104" y="175" width="96" height="6" rx="3"/>
      </g>
      <circle cx="262" cy="48" r="17" fill="#ffe35a"/>
      <path d="M254 48l6 6 11-12" fill="none" stroke="#17233f" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"/>
    </g>
  </svg>`;

function go(path) {
  location.hash = `#${path}`;
}

function readAddress() {
  const raw = location.hash.slice(1) || "/";
  const [path, query = ""] = raw.split("?");
  return { parts: path.split("/").filter(Boolean), params: new URLSearchParams(query) };
}

// A small message that pops up in the corner and disappears by itself.
function toast(message, type = "success") {
  const el = document.createElement("div");
  el.className = `toast toast-${type}`;
  el.setAttribute("role", "status");
  el.textContent = message;
  toasts.append(el);
  while (toasts.children.length > 3) toasts.firstElementChild.remove(); // never more than 3 at once
  setTimeout(() => el.remove(), 4500);
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

// Tidies the start of a note into one short line of text for a card.
function makePreview(text) {
  const clean = String(text || "").replace(/\s+/g, " ").trim();
  return clean.length > 150 ? `${clean.slice(0, 150).trimEnd()}…` : clean;
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
       <a href="#/quizzes">Quizzes</a>
       ${user.isAdmin ? `<a href="#/admin">Reports</a>` : ""}
       <a class="btn btn-primary" href="#/new">Write a note</a>
       <span class="who">${avatar(user.displayName)}<span>${esc(user.displayName)}</span></span>
       <button type="button" class="link-button" id="logout">Log out</button>`
    : `<a href="#/">Browse notes</a>
       <a href="#/join">Join a quiz</a>
       <a href="#/login">Log in</a>
       <a class="btn btn-primary" href="#/signup">Sign up</a>`;

  const logout = document.getElementById("logout");
  if (logout) {
    logout.addEventListener("click", () => {
      session.clear();
      toast("Logged out.");
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
    <section class="hero">
      <div>
        <h1>Find notes for your course</h1>
        <p class="lede">Written by students, for students. Search by keyword, topic or course code.</p>
      </div>
      ${HERO_ART}
    </section>
    <form class="search panel" id="search-form" role="search">
        <label class="field field-wide">
          <span>Topic or keyword</span>
          <input name="search" type="search" value="${esc(filters.search)}" placeholder="deadlock, CSS grid, recursion">
        </label>
        <label class="field">
          <span>Course code</span>
          <input name="course" value="${esc(filters.course)}" placeholder="CSC 301" maxlength="20">
        </label>
        <label class="field">
          <span>Topic</span>
          <input name="tag" value="${esc(filters.tag)}" placeholder="os" maxlength="50">
        </label>
        <button type="submit" class="btn btn-primary">Search notes</button>
    </form>
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
           <p class="muted">Try a different word, or remove a filter.</p>
           <a class="btn" href="#/">Show all notes</a>
         </div>`
      : `<div class="empty">
           <h2>No notes yet</h2>
           <p class="muted">Be the first to share one.</p>
           <a class="btn btn-primary" href="#/new">Write a note</a>
         </div>`;
    return;
  }

  const totalPages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
  const count = `${data.totalCount} ${data.totalCount === 1 ? "note" : "notes"}`;

  results.innerHTML = `
    <div class="results-head">
      <h2>${filtered ? `${count} found` : `All notes (${data.totalCount})`}</h2>
      <p class="sort">
        <span class="muted">Show first</span>
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
        <span class="card-top">
          ${note.courseCode
            ? `<span class="course">${esc(note.courseCode)}</span>`
            : `<span class="note-tags">${tags}</span>`}
          <span class="card-counts">
            ${note.helpfulCount > 0 ? `<span class="helpful-count">${note.helpfulCount} helpful</span>` : ""}
            ${note.commentCount > 0 ? `<span class="comment-count">${note.commentCount} ${note.commentCount === 1 ? "comment" : "comments"}</span>` : ""}
          </span>
        </span>
        <span class="note-title">${esc(note.title)}</span>
        ${note.preview ? `<span class="note-preview">${esc(makePreview(note.preview))}</span>` : ""}
        ${note.courseCode && tags ? `<span class="note-tags">${tags}</span>` : ""}
        <span class="note-meta">${avatar(note.authorName)}<span>${esc(note.authorName)}, ${esc(formatDate(note.createdAtUtc))}</span></span>
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
    actions = `
      <button type="button" class="link-button" id="report-toggle">Report this note</button>
      ${user.isAdmin ? `<button type="button" class="btn btn-danger" id="admin-remove">Remove note</button>` : ""}`;
  } else {
    actions = `<a href="#/login?next=${encodeURIComponent(`/note/${note.id}`)}">Log in to report this note</a>`;
  }

  document.title = `${note.title} | StudyHub`;
  app.innerHTML = `
    <a class="back" href="#/">Back to all notes</a>
    <article class="sheet">
      <header class="note-head">
        ${note.courseCode ? `<a class="course" href="#${homeAddress({ course: note.courseCode })}">${esc(note.courseCode)}</a>` : ""}
        <h1>${esc(note.title)}</h1>
        <p class="byline">${avatar(note.authorName)}<span>${esc(note.authorName)}, ${esc(formatDate(note.createdAtUtc))}${edited ? ` (edited ${esc(formatDate(note.updatedAtUtc))})` : ""}</span></p>
        ${note.tags.length ? `
          <p class="tag-links">
            ${note.tags.map((tag) => `<a href="#${homeAddress({ tag })}">#${esc(tag)}</a>`).join("")}
          </p>` : ""}
      </header>
      <div class="ruled">${esc(note.body)}</div>
      <footer class="sheet-foot">
        <div class="helpful" id="helpful"></div>
        <div class="note-actions" id="note-actions">${actions}</div>
        <div id="report-area"></div>
      </footer>
    </article>
    <section class="comments panel" id="comments" aria-label="Comments">
      <p class="muted">Loading comments…</p>
    </section>`;

  renderHelpful(note, isAuthor);
  loadComments(note, id);

  const deleteButton = document.getElementById("delete");
  if (deleteButton) deleteButton.addEventListener("click", () => confirmDelete(note));

  const reportToggle = document.getElementById("report-toggle");
  if (reportToggle) reportToggle.addEventListener("click", () => showReportForm(note));

  // Admins can remove any note.
  const adminRemove = document.getElementById("admin-remove");
  if (adminRemove) {
    confirmButton(adminRemove, "Yes, remove this note", async () => {
      await api(`/admin/materials/${encodeURIComponent(note.id)}`, { method: "DELETE" });
      toast("Note removed.");
      go("/");
    });
  }
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

// ---------- comments under a note ----------

async function loadComments(note, id) {
  const box = document.getElementById("comments");
  let comments;
  try {
    comments = await api(`/materials/${encodeURIComponent(note.id)}/comments`);
  } catch (error) {
    if (id === renderId) box.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
    return;
  }
  if (id !== renderId) return;
  renderComments(note, comments);
}

function renderComments(note, comments) {
  const box = document.getElementById("comments");
  const user = session.get();

  const list = comments.length
    ? `<ul class="comment-list">
         ${comments.map((comment) => `
           <li class="comment">
             ${avatar(comment.authorName)}
             <div class="comment-main">
               <p class="comment-head">
                 <strong>${esc(comment.authorName)}</strong>
                 <span class="muted">${esc(formatDate(comment.createdAtUtc))}</span>
                 ${user && (user.userId === comment.authorId || user.isAdmin)
                   ? `<button type="button" class="link-button comment-delete" data-delete="${esc(comment.id)}"
                        ${user.userId === comment.authorId ? "" : 'data-admin="yes"'}>Delete</button>`
                   : ""}
               </p>
               <p class="comment-body">${esc(comment.body)}</p>
             </div>
           </li>`).join("")}
       </ul>`
    : `<p class="muted">No comments yet. Ask a question or share a correction.</p>`;

  const form = user
    ? `<form class="comment-form" id="comment-form">
         <label class="field">
           <span>Add a comment</span>
           <textarea class="short" name="body" required minlength="2" maxlength="1000"
             placeholder="Ask a question, or point out something to fix"></textarea>
         </label>
         <p class="error" role="alert" hidden></p>
         <div class="form-actions">
           <button type="submit" class="btn btn-primary">Post comment</button>
         </div>
       </form>`
    : `<p><a href="#/login?next=${encodeURIComponent(`/note/${note.id}`)}">Log in to comment</a></p>`;

  box.innerHTML = `
    <h2>${comments.length === 0 ? "Comments" : `Comments (${comments.length})`}</h2>
    ${list}
    ${form}`;

  box.querySelectorAll("[data-delete]").forEach((button) => {
    button.addEventListener("click", async () => {
      button.disabled = true;
      try {
        // Your own comment goes through the normal route; an admin removing someone else's uses the admin route.
        const commentId = encodeURIComponent(button.dataset.delete);
        const path = button.dataset.admin
          ? `/admin/comments/${commentId}`
          : `/materials/${encodeURIComponent(note.id)}/comments/${commentId}`;
        await api(path, { method: "DELETE" });
        toast("Comment deleted.");
        renderComments(note, comments.filter((comment) => comment.id !== button.dataset.delete));
      } catch (error) {
        button.disabled = false;
        toast(error.message, "error");
      }
    });
  });

  const commentForm = document.getElementById("comment-form");
  if (commentForm) {
    commentForm.addEventListener("submit", (event) => {
      event.preventDefault();
      submitForm(commentForm, async (data) => {
        const added = await api(`/materials/${encodeURIComponent(note.id)}/comments`, {
          method: "POST",
          body: { body: data.get("body").trim() },
        });
        toast("Comment posted.");
        renderComments(note, [...comments, added]);
      });
    });
  }
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
      toast("Note deleted.");
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
      area.innerHTML = "";
      document.getElementById("note-actions").innerHTML = "";
      toast("Report sent. Thanks for flagging it.");
    });
  });
}

// ---------- page: write or edit a note ----------

async function pageEditor(noteId, id) {
  const user = session.get();
  if (!user) {
    toast("Log in to write a note.", "info");
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
      toast("You can only edit your own notes.", "error");
      go(`/note/${noteId}`);
      return;
    }
  }

  const saveLabel = noteId ? "Save changes" : "Publish note";
  app.innerHTML = `
    <section class="panel panel-form">
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
          <input name="courseCode" maxlength="20" value="${esc(note.courseCode || "")}" placeholder="CSC 301">
          <small>Optional</small>
        </label>
        <label class="field">
          <span>Topics</span>
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
    </form>
    </section>`;

  document.getElementById("note-form").addEventListener("submit", (event) => {
    event.preventDefault();
    submitForm(event.target, async (data) => {
      const tags = parseTags(data.get("tags"));
      if (tags.length > 5) throw new Error("Use 5 topics or fewer.");

      const body = {
        title: data.get("title").trim(),
        body: data.get("body").trim(),
        courseCode: data.get("courseCode").trim() || null,
        tags,
      };

      const saved = noteId
        ? await api(`/materials/${encodeURIComponent(noteId)}`, { method: "PUT", body })
        : await api("/materials", { method: "POST", body });

      toast(noteId ? "Changes saved." : "Note published.");
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
    <section class="panel panel-narrow">
    <h1>${isSignup ? "Create your account" : "Log in"}</h1>
    <form class="form" id="auth-form">
      ${isSignup ? `
        <label class="field">
          <span>Your name</span>
          <input name="displayName" required minlength="2" maxlength="100" autocomplete="name">
          <small>Shown next to the notes you write</small>
        </label>` : ""}
      <label class="field">
        <span>Email</span>
        <input name="email" id="email" type="email" required maxlength="256" autocomplete="username">
      </label>
      <label class="field">
        <span>Password</span>
        <input name="password" type="password" required minlength="${isSignup ? 8 : 1}" maxlength="100"
          id="password" autocomplete="${isSignup ? "new-password" : "current-password"}">
        ${isSignup ? "<small>At least 8 characters</small>" : ""}
      </label>
      ${isSignup ? `
        <label class="field">
          <span>Confirm password</span>
          <input name="confirm" type="password" required maxlength="100" autocomplete="new-password">
        </label>` : ""}
      <p class="error" role="alert" hidden></p>
      <div class="form-actions">
        <button type="submit" class="btn btn-primary">${isSignup ? "Sign up" : "Log in"}</button>
        ${isSignup
          ? `<a href="#/login${nextQuery}">I already have an account</a>`
          : `<a href="#/signup${nextQuery}">Create an account</a>`}
      </div>
    </form>
    </section>`;

  document.getElementById("auth-form").addEventListener("submit", (event) => {
    event.preventDefault();
    submitForm(event.target, async (data) => {
      if (isSignup && data.get("password") !== data.get("confirm")) {
        throw new Error("The two passwords don't match.");
      }

      const body = { email: data.get("email").trim(), password: data.get("password") };
      if (isSignup) body.displayName = data.get("displayName").trim();

      const result = await api(isSignup ? "/auth/register" : "/auth/login", { method: "POST", body });
      session.set({ token: result.token, userId: result.userId, displayName: result.displayName });
      go(next.startsWith("/") ? next : "/");
      refreshAdmin();
    });
  });
}

// ---------- admin: reported notes ----------

// Asks the server whether this user is an admin, so the "Reports" link can be shown.
// The server checks again on every admin request, so hiding the link is not the protection.
async function refreshAdmin() {
  const user = session.get();
  if (!user) return;
  try {
    const me = await api("/auth/me");
    if (Boolean(user.isAdmin) !== me.isAdmin) {
      session.set({ ...user, isAdmin: me.isAdmin });
      route();
    }
  } catch {
    /* not critical: the user just won't see the admin link */
  }
}

// Dangerous buttons need two clicks: the first asks, the second does it.
function confirmButton(button, confirmLabel, action) {
  button.addEventListener("click", async () => {
    if (button.dataset.armed !== "yes") {
      button.dataset.armed = "yes";
      button.textContent = confirmLabel;
      return;
    }
    button.disabled = true;
    try {
      await action();
    } catch (error) {
      button.disabled = false;
      toast(error.message, "error");
    }
  });
}

async function pageAdmin(id) {
  if (!session.get()) {
    toast("Log in to continue.", "info");
    go(`/login?next=${encodeURIComponent("/admin")}`);
    return;
  }

  app.innerHTML = `
    <h1>Reported notes</h1>
    <p class="lede">Notes that students have flagged. Keep the note and dismiss the report, or remove the note for everyone.</p>
    <div class="reports" id="reports"><p class="muted">Loading reports…</p></div>`;

  const box = document.getElementById("reports");
  let reports;
  try {
    reports = await api("/admin/reports");
  } catch (error) {
    if (id === renderId) box.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
    return;
  }
  if (id !== renderId) return;

  if (reports.length === 0) {
    box.innerHTML = `
      <div class="empty">
        <h2>No open reports</h2>
        <p class="muted">Nothing needs your attention right now.</p>
      </div>`;
    return;
  }

  box.innerHTML = reports.map((report) => `
    <article class="report panel">
      <h2><a href="#/note/${esc(report.materialId)}">${esc(report.materialTitle)}</a></h2>
      <p class="muted">Written by ${esc(report.materialAuthorName)}</p>
      <p class="report-preview">${esc(makePreview(report.materialPreview))}</p>
      <p class="report-reason">
        <strong>${esc(report.reporterName)} reported this on ${esc(formatDate(report.createdAtUtc))}</strong>
        <span>${esc(report.reason)}</span>
      </p>
      <div class="form-actions">
        <button type="button" class="btn" data-dismiss="${esc(report.id)}">Keep note, dismiss report</button>
        <button type="button" class="btn btn-danger" data-remove="${esc(report.materialId)}">Remove note</button>
      </div>
    </article>`).join("");

  box.querySelectorAll("[data-dismiss]").forEach((button) => {
    button.addEventListener("click", async () => {
      button.disabled = true;
      try {
        await api(`/admin/reports/${encodeURIComponent(button.dataset.dismiss)}/dismiss`, { method: "POST" });
        toast("Report dismissed.");
        route();
      } catch (error) {
        button.disabled = false;
        toast(error.message, "error");
      }
    });
  });

  box.querySelectorAll("[data-remove]").forEach((button) => {
    confirmButton(button, "Yes, remove this note", async () => {
      await api(`/admin/materials/${encodeURIComponent(button.dataset.remove)}`, { method: "DELETE" });
      toast("Note removed.");
      route();
    });
  });
}

// ---------- deciding which page to show ----------

function route() {
  const id = ++renderId;
  const { parts, params } = readAddress();
  document.title = "StudyHub";
  leaveQuizPage(); // close any live quiz connection from the page we are leaving
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
    case "admin":
      return pageAdmin(id);
    case "quizzes":
      return pageQuizzes(id);
    case "quiz":
      if (parts[1] === "new") return pageQuizEditor(null, id);
      if (parts[1] === "edit" && parts[2]) return pageQuizEditor(parts[2], id);
      return go("/quizzes");
    case "host":
      return parts[1] ? pageHost(parts[1], id) : go("/quizzes");
    case "join":
      return pageJoin(params);
    default:
      app.innerHTML = `
        <h1>Page not found</h1>
        <p class="lede">That address doesn't lead anywhere. <a href="#/">Browse notes</a></p>`;
      return undefined;
  }
}

window.addEventListener("hashchange", route);

// Clicking a link to the page you're already on doesn't change the address, so the
// browser does nothing. Redraw the page instead, e.g. "Join a quiz" after a game ends.
document.addEventListener("click", (event) => {
  const link = event.target.closest("a[href^='#']");
  if (!link || event.defaultPrevented || event.button !== 0) return;
  if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
  if (link.getAttribute("href") === location.hash) {
    event.preventDefault();
    route();
  }
});
route();
refreshAdmin();
