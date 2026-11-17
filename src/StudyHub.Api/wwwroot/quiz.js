// StudyHub live quizzes: writing a quiz, hosting a game, and playing one.
// This file only defines functions. app.js decides when to call them, and
// provides the shared helpers used here (api, esc, toast, go, session, app...).

"use strict";

const SIGNALR_URL = "https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/7.0.12/signalr.min.js";
const OPTION_LETTERS = ["A", "B", "C", "D"];
const SECONDS_CHOICES = [10, 20, 30, 45, 60];
const PAUSE_CHOICES = [3, 5, 8, 15]; // seconds to wait after the answer is shown, when "move on automatically" is on
const DEFAULT_PAUSE = 8;
const PRACTICE_COUNTS = [5, 10, 15];
const DEFAULT_PRACTICE_COUNT = 10;

let quizConnection = null; // the live connection to the server while hosting or playing
let quizTimer = null; // the countdown that is running, if any
const loadedScripts = new Map();

// ---------- shared: connection, countdown, small helpers ----------

// Called by app.js whenever the page changes, so a game never keeps running in the background.
function leaveQuizPage() {
  stopCountdown();
  if (quizConnection) {
    const connection = quizConnection;
    quizConnection = null;
    connection.stop().catch(() => {});
  }
}

function loadScript(url) {
  if (!loadedScripts.has(url)) {
    loadedScripts.set(url, new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src = url;
      script.onload = resolve;
      script.onerror = () => {
        loadedScripts.delete(url);
        reject(new Error("Couldn't load the live-quiz library."));
      };
      document.head.append(script);
    }));
  }
  return loadedScripts.get(url);
}

// Opens the live connection. Unlike normal requests, it stays open, so the
// server can send messages (a new question, the scores) the moment they happen.
async function openQuizConnection() {
  let connection;
  if (window.__quizConnectionFactory) {
    connection = window.__quizConnectionFactory(); // used by automated tests
  } else {
    await loadScript(SIGNALR_URL);
    connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/quiz", { accessTokenFactory: () => (session.get() ? session.get().token : "") })
      .build();
  }
  await connection.start();
  return connection;
}

// Turns a server or connection error into a sentence worth showing.
function hubMessage(error) {
  const text = String(error && error.message ? error.message : error);
  if (/unauthorized/i.test(text)) return "Log in again to host a quiz.";
  if (/429|too many requests/i.test(text)) return "Too many people are connecting from your network right now. Wait a minute and try again.";
  const match = text.match(/HubException:\s*([\s\S]*)$/);
  if (match) return match[1].trim();
  return "Couldn't reach the quiz server. Check your connection and try again.";
}

function startCountdown(seconds, onTick, onDone) {
  stopCountdown();
  const endsAt = Date.now() + seconds * 1000;
  const tick = () => {
    const left = Math.max(0, Math.ceil((endsAt - Date.now()) / 1000));
    onTick(left);
    if (left <= 0) {
      stopCountdown();
      onDone();
    }
  };
  quizTimer = setInterval(tick, 250);
  tick();
}

function stopCountdown() {
  if (quizTimer) {
    clearInterval(quizTimer);
    quizTimer = null;
  }
}

function ordinal(n) {
  const lastTwo = n % 100;
  if (lastTwo >= 11 && lastTwo <= 13) return `${n}th`;
  return `${n}${["th", "st", "nd", "rd"][n % 10] || "th"}`;
}

function plural(count, word, many = `${word}s`) {
  return `${count} ${count === 1 ? word : many}`;
}

function leaderboardHtml(rows) {
  if (!rows.length) return `<p class="muted">No scores yet.</p>`;
  return `
    <ol class="leaderboard">
      ${rows.map((row) => `
        <li><span class="leaderboard-name">${esc(row.name)}</span><span class="leaderboard-score">${row.score}</span></li>`).join("")}
    </ol>`;
}

// ---------- page: my quizzes ----------

async function pageQuizzes(id) {
  if (!session.get()) {
    toast("Log in to write and host quizzes.", "info");
    go(`/login?next=${encodeURIComponent("/quizzes")}`);
    return;
  }

  app.innerHTML = `
    <h1>Live quizzes</h1>
    <p class="lede">Write a quiz, put the game code on a screen, and let your classmates answer on their phones.</p>
    <div class="quiz-top">
      <a class="btn btn-primary" href="#/quiz/new">New quiz</a>
      <a class="btn" href="#/join">Join a game</a>
      <a class="btn" href="#/practice">Practise on your own</a>
      <a class="btn" href="#/history">Your quiz history</a>
    </div>
    <section class="quiz-list" id="quiz-list"><p class="muted">Loading your quizzes…</p></section>`;

  const box = document.getElementById("quiz-list");
  let quizzes;
  try {
    quizzes = await api("/quizzes/mine");
  } catch (error) {
    if (id === renderId) box.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
    return;
  }
  if (id !== renderId) return;

  if (quizzes.length === 0) {
    box.innerHTML = `
      <div class="empty">
        <h2>You haven't written a quiz yet</h2>
        <p class="muted">A quiz is a set of multiple-choice questions you can run live.</p>
        <a class="btn btn-primary" href="#/quiz/new">Write your first quiz</a>
      </div>`;
    return;
  }

  box.innerHTML = quizzes.map((quiz) => `
    <article class="panel quiz-item">
      <div>
        <h2>${esc(quiz.title)}</h2>
        <p class="muted">${plural(quiz.questionCount, "question")}, written ${esc(formatDate(quiz.createdAtUtc))}, ${quiz.timesPlayed === 0 ? "not played live yet" : `played live ${plural(quiz.timesPlayed, "time")}`}</p>
        <p class="quiz-flags">
          <span class="quiz-flag ${quiz.isPublic ? "is-shared" : ""}">${quiz.isPublic ? "Shared for practice" : "Private"}</span>
          ${quiz.topic ? `<span class="quiz-flag">${esc(quiz.topic)}</span>` : ""}
        </p>
      </div>
      <div class="form-actions">
        <a class="btn btn-primary" href="#/host/${esc(quiz.id)}">Host live</a>
        <a class="btn" href="#/practice/quiz/${esc(quiz.id)}">Practise</a>
        <a class="btn" href="#/quiz/stats/${esc(quiz.id)}">Stats</a>
        <a class="btn" href="#/quiz/edit/${esc(quiz.id)}">Edit</a>
        <button type="button" class="btn btn-danger" data-delete-quiz="${esc(quiz.id)}">Delete</button>
      </div>
    </article>`).join("");

  box.querySelectorAll("[data-delete-quiz]").forEach((button) => {
    confirmButton(button, "Yes, delete", async () => {
      await api(`/quizzes/${encodeURIComponent(button.dataset.deleteQuiz)}`, { method: "DELETE" });
      toast("Quiz deleted.");
      route();
    });
  });
}

// ---------- page: how one of my quizzes has been played ----------

async function pageQuizStats(quizId, id) {
  if (!session.get()) {
    toast("Log in to see your quiz stats.", "info");
    go(`/login?next=${encodeURIComponent(`/quiz/stats/${quizId}`)}`);
    return;
  }

  app.innerHTML = `
    <a class="back" href="#/quizzes">Back to your quizzes</a>
    <div id="stats-page"><p class="muted">Loading stats…</p></div>`;
  const box = document.getElementById("stats-page");

  let stats;
  try {
    stats = await api(`/quizzes/${encodeURIComponent(quizId)}/stats`);
  } catch (error) {
    if (id === renderId) box.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
    return;
  }
  if (id !== renderId) return;

  document.title = `${stats.title}: stats | StudyHub`;

  if (stats.timesPlayed === 0 && stats.timesPractised === 0) {
    box.innerHTML = `
      <h1>${esc(stats.title)}</h1>
      <div class="empty stats-empty">
        <h2>Nobody has played this quiz yet</h2>
        <p class="muted">Host it live, or share it for practice. Results show up here after a game's final scores, or when a logged-in student finishes practising it.</p>
        <a class="btn btn-primary" href="#/host/${esc(stats.quizId)}">Host live</a>
      </div>`;
    return;
  }

  box.innerHTML = `
    <h1>${esc(stats.title)}</h1>
    <p class="lede">How this quiz has gone so far.</p>

    <section class="stat-cards" aria-label="Summary">
      ${statCard("Live games", stats.timesPlayed)}
      ${statCard("Players in live games", stats.totalPlayers, stats.signedInPlayers > 0 ? `${plural(stats.signedInPlayers, "student")} with an account` : "")}
      ${statCard("Practice runs", stats.timesPractised, "By logged-in students")}
      ${statCard("Average right answers", `${stats.averageCorrectPercent}%`, "Live games and practice")}
      ${statCard("Last played", formatDate(stats.lastPlayedAtUtc))}
    </section>

    <section class="panel stats-section">
      <h2>Questions, hardest first</h2>
      <p class="muted">The share of answers that were right for each question, across live games and practice.</p>
      <ul class="question-stats">
        ${stats.questions.map((q) => `
          <li>
            <div class="question-stat-head">
              <span class="question-stat-text">${esc(q.text)}</span>
              <span class="question-stat-value">${q.correctPercent}% right</span>
            </div>
            <div class="bar" aria-hidden="true"><span style="width:${Math.max(0, Math.min(100, q.correctPercent))}%"></span></div>
            <p class="muted question-stat-detail">${q.correct} right out of ${plural(q.asked, "try", "tries")}${q.asked - q.answered > 0 ? `, ${q.asked - q.answered} not answered` : ""}</p>
          </li>`).join("")}
      </ul>
    </section>

    ${stats.sessions.length === 0 ? "" : `<section class="panel stats-section">
      <h2>Recent live games</h2>
      <div class="table-scroll">
        <table class="games-table">
          <thead>
            <tr><th scope="col">Date</th><th scope="col">Players</th><th scope="col">Winner</th><th scope="col">Scoring</th></tr>
          </thead>
          <tbody>
            ${stats.sessions.map((s) => `
              <tr>
                <td>${esc(formatDate(s.playedAtUtc))}</td>
                <td>${s.players}</td>
                <td>${s.winner ? `${esc(s.winner)} (${s.winnerScore})` : "None"}</td>
                <td>${s.correctOnly ? "Correct answers only" : "Speed and accuracy"}</td>
              </tr>`).join("")}
          </tbody>
        </table>
      </div>
    </section>`}`;
}

function statCard(label, value, detail = "") {
  return `
    <div class="stat-card">
      <p class="stat-card-label">${esc(label)}</p>
      <p class="stat-card-value">${esc(value)}</p>
      ${detail ? `<p class="stat-card-detail">${esc(detail)}</p>` : ""}
    </div>`;
}

// ---------- page: the quizzes I've played ----------

async function pageHistory(id) {
  if (!session.get()) {
    toast("Log in to see your quiz history.", "info");
    go(`/login?next=${encodeURIComponent("/history")}`);
    return;
  }

  app.innerHTML = `
    <a class="back" href="#/quizzes">Back to quizzes</a>
    <h1>Your quiz history</h1>
    <p class="lede">Every live quiz and practice run you've done while logged in. Only you can see this.</p>
    <section class="history" id="history"><p class="muted">Loading your results…</p></section>`;
  const box = document.getElementById("history");

  let items;
  try {
    items = await api("/history/mine");
  } catch (error) {
    if (id === renderId) box.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
    return;
  }
  if (id !== renderId) return;

  if (items.length === 0) {
    box.innerHTML = `
      <div class="empty">
        <h2>Nothing here yet</h2>
        <p class="muted">Join a live quiz or practise on your own while you're logged in, and your results will be saved here.</p>
        <div class="form-actions">
          <a class="btn btn-primary" href="#/join">Join a quiz</a>
          <a class="btn" href="#/practice">Practise on your own</a>
        </div>
      </div>`;
    return;
  }

  box.innerHTML = `
    <ul class="history-list">
      ${items.map((item) => item.isPractice ? `
        <li class="panel history-item is-practice">
          <div class="history-main">
            <h2>${esc(item.quizTitle)}</h2>
            <p class="muted">${esc(formatDate(item.playedAtUtc))}, practice on your own</p>
          </div>
          <dl class="history-figures">
            <div><dt>Right answers</dt><dd>${item.correctCount} of ${item.questionsPlayed}</dd></div>
          </dl>
        </li>` : `
        <li class="panel history-item">
          <div class="history-main">
            <h2>${esc(item.quizTitle)}</h2>
            <p class="muted">${esc(formatDate(item.playedAtUtc))}, live game, ${item.correctOnly ? "correct answers only" : "speed and accuracy"}</p>
          </div>
          <dl class="history-figures">
            <div><dt>Place</dt><dd>${ordinal(item.rank)} of ${item.players}</dd></div>
            <div><dt>Right answers</dt><dd>${item.correctCount} of ${item.questionsPlayed}</dd></div>
            <div><dt>Score</dt><dd>${item.score}</dd></div>
          </dl>
        </li>`).join("")}
    </ul>`;
}

// ---------- page: write or edit a quiz ----------

function blankQuestion() {
  return { text: "", options: ["", "", "", ""], correctIndex: 0, seconds: 20 };
}

async function pageQuizEditor(quizId, id) {
  if (!session.get()) {
    toast("Log in to write a quiz.", "info");
    go(`/login?next=${encodeURIComponent(quizId ? `/quiz/edit/${quizId}` : "/quiz/new")}`);
    return;
  }

  let title = "";
  let topic = "";
  let isPublic = false;
  let questions = [blankQuestion()];

  if (quizId) {
    app.innerHTML = `<p class="muted">Loading quiz…</p>`;
    try {
      const quiz = await api(`/quizzes/${encodeURIComponent(quizId)}`);
      if (id !== renderId) return;
      title = quiz.title;
      topic = quiz.topic || "";
      isPublic = quiz.isPublic;
      questions = quiz.questions.map((q) => ({
        text: q.text,
        options: [0, 1, 2, 3].map((i) => q.options[i] || ""),
        correctIndex: q.correctIndex,
        seconds: q.seconds,
      }));
    } catch (error) {
      if (id === renderId) app.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
      return;
    }
  }

  // Copies what is typed in the form back into the "questions" list.
  const collect = () => {
    const form = document.getElementById("quiz-form");
    if (!form) return;
    title = form.elements.title.value;
    topic = form.elements.topic.value;
    isPublic = form.elements.isPublic.checked;
    questions = [...form.querySelectorAll(".question-edit")].map((fieldset, index) => {
      const checked = fieldset.querySelector(`input[name="correct-${index}"]:checked`);
      return {
        text: fieldset.querySelector("[name=q-text]").value,
        options: [...fieldset.querySelectorAll("[name=q-option]")].map((input) => input.value),
        correctIndex: checked ? Number(checked.value) : 0,
        seconds: Number(fieldset.querySelector("[name=q-seconds]").value),
      };
    });
  };

  const draw = (errorMessage = "") => {
    app.innerHTML = `
      <section class="panel panel-form">
        <h1>${quizId ? "Edit quiz" : "New quiz"}</h1>
        <p class="lede">Each question needs at least two answers. Mark the correct one.</p>
        <form class="form" id="quiz-form" novalidate>
          <label class="field">
            <span>Quiz title</span>
            <input name="title" maxlength="200" value="${esc(title)}" placeholder="Operating systems: week 4 recap">
          </label>

          <div class="share-row">
            <label class="field">
              <span>Topic (optional)</span>
              <input name="topic" maxlength="50" value="${esc(topic)}" placeholder="operating systems" autocomplete="off">
              <small>Used to group questions for random practice</small>
            </label>
            <label class="check-field">
              <input type="checkbox" name="isPublic" ${isPublic ? "checked" : ""}>
              <span>
                <strong>Share for practice</strong>
                <small>Other students can practise this quiz on their own. Only you can edit it or host it.</small>
              </span>
            </label>
          </div>

          ${questions.map((question, index) => `
            <fieldset class="question-edit">
              <legend>Question ${index + 1}</legend>
              <label class="field">
                <span>Question</span>
                <input name="q-text" maxlength="500" value="${esc(question.text)}" placeholder="Which scheduler can starve long jobs?">
              </label>
              <p class="field-label">Answers</p>
              ${OPTION_LETTERS.map((letter, i) => `
                <div class="answer-row">
                  <label class="answer-pick">
                    <input type="radio" name="correct-${index}" value="${i}" ${question.correctIndex === i ? "checked" : ""}>
                    <span class="sr-only">Answer ${letter} is correct</span>
                  </label>
                  <span class="letter letter-${i}" aria-hidden="true">${letter}</span>
                  <input name="q-option" maxlength="200" value="${esc(question.options[i])}"
                    aria-label="Answer ${letter}" placeholder="${i < 2 ? `Answer ${letter}` : `Answer ${letter} (optional)`}">
                </div>`).join("")}
              <div class="question-foot">
                <label class="field field-inline">
                  <span>Time to answer</span>
                  <select name="q-seconds">
                    ${SECONDS_CHOICES.map((s) => `<option value="${s}" ${question.seconds === s ? "selected" : ""}>${s} seconds</option>`).join("")}
                  </select>
                </label>
                ${questions.length > 1 ? `<button type="button" class="link-button remove-question" data-remove="${index}">Remove question</button>` : ""}
              </div>
            </fieldset>`).join("")}

          <button type="button" class="btn" id="add-question">Add question</button>
          <p class="error" role="alert" ${errorMessage ? "" : "hidden"}>${esc(errorMessage)}</p>
          <div class="form-actions">
            <button type="submit" class="btn btn-primary">Save quiz</button>
            <a href="#/quizzes">Cancel</a>
          </div>
        </form>
      </section>`;

    const form = document.getElementById("quiz-form");

    document.getElementById("add-question").addEventListener("click", () => {
      collect();
      questions.push(blankQuestion());
      draw();
      const all = document.querySelectorAll(".question-edit [name=q-text]");
      all[all.length - 1].focus();
    });

    form.querySelectorAll("[data-remove]").forEach((button) => {
      button.addEventListener("click", () => {
        collect();
        questions.splice(Number(button.dataset.remove), 1);
        draw();
      });
    });

    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      collect();

      let request;
      try {
        request = buildQuizRequest(title, questions, { topic, isPublic });
      } catch (error) {
        draw(error.message);
        return;
      }

      const button = form.querySelector("button[type=submit]");
      button.disabled = true;
      try {
        if (quizId) await api(`/quizzes/${encodeURIComponent(quizId)}`, { method: "PUT", body: request });
        else await api("/quizzes", { method: "POST", body: request });
        toast("Quiz saved.");
        go("/quizzes");
      } catch (error) {
        draw(error.message);
      }
    });
  };

  draw();
}

// Checks the quiz and shapes it the way the API expects. Blank answers are dropped.
function buildQuizRequest(title, questions, { topic = "", isPublic = false } = {}) {
  if (title.trim().length < 3) throw new Error("Give the quiz a title of at least 3 characters.");

  return {
    title: title.trim(),
    topic: topic.trim() || null,
    isPublic,
    questions: questions.map((question, index) => {
      const label = `Question ${index + 1}`;
      if (question.text.trim().length < 3) throw new Error(`${label}: write the question.`);

      const filled = question.options
        .map((text, i) => ({ text: text.trim(), i }))
        .filter((option) => option.text.length > 0);
      if (filled.length < 2) throw new Error(`${label}: give at least two answers.`);

      const correctIndex = filled.findIndex((option) => option.i === question.correctIndex);
      if (correctIndex < 0) throw new Error(`${label}: the answer marked as correct is empty.`);

      return {
        text: question.text.trim(),
        options: filled.map((option) => option.text),
        correctIndex,
        seconds: question.seconds,
      };
    }),
  };
}

// ---------- page: host a live game ----------

async function pageHost(quizId, id) {
  if (!session.get()) {
    toast("Log in to host a quiz.", "info");
    go(`/login?next=${encodeURIComponent(`/host/${quizId}`)}`);
    return;
  }

  app.innerHTML = `<section class="stage" id="stage"><p class="stage-label">Starting the game…</p></section>`;
  const stage = document.getElementById("stage");

  const fail = (message) => {
    stage.innerHTML = `
      <p class="error" role="alert">${esc(message)}</p>
      <a class="btn" href="#/quizzes">Back to your quizzes</a>`;
  };

  let connection;
  let game;
  try {
    connection = await openQuizConnection();
    if (id !== renderId) {
      connection.stop().catch(() => {});
      return;
    }
    quizConnection = connection;
    game = await connection.invoke("CreateGame", quizId);
  } catch (error) {
    console.error(error);
    if (id === renderId) fail(hubMessage(error));
    return;
  }
  if (id !== renderId) return;

  let phase = "lobby"; // lobby, question, revealing, reveal, finished
  let players = [];
  let question = null;
  let answered = 0;
  let correctOnly = false; // scoring: false = speed and accuracy, true = correct answers only
  let autoAdvance = false; // move to the next question by itself after the answer is shown
  let pauseSeconds = DEFAULT_PAUSE; // how long to show the answer before moving on

  const joinAddress = `${location.host}/#/join`;

  const drawLobby = () => {
    stage.innerHTML = `
      <p class="stage-label">Go to <strong>${esc(joinAddress)}</strong> and enter this code</p>
      <p class="game-code" id="game-code" aria-label="Game code ${esc(game.code.split("").join(" "))}">${esc(game.code)}</p>
      <h1 class="stage-title">${esc(game.title)}</h1>
      <p class="stage-label" id="player-count">${players.length === 0 ? "Waiting for players…" : `${plural(players.length, "player")} joined`}</p>
      <ul class="player-chips" id="player-chips">
        ${players.map((name) => `<li>${esc(name)}</li>`).join("")}
      </ul>
      <fieldset class="game-settings">
        <legend>Game settings</legend>
        <div class="setting">
          <p class="setting-label" id="scoring-label">Scoring</p>
          <div class="setting-choices" role="radiogroup" aria-labelledby="scoring-label">
            <label><input type="radio" name="scoring" value="speed" ${correctOnly ? "" : "checked"}> Speed and accuracy</label>
            <label><input type="radio" name="scoring" value="correct" ${correctOnly ? "checked" : ""}> Correct answers only</label>
          </div>
          <p class="setting-help">${correctOnly
            ? "One point for each right answer. Speed doesn't matter."
            : "500 points for a right answer, plus up to 500 more for answering quickly."}</p>
        </div>
        <div class="setting">
          <label class="setting-toggle">
            <input type="checkbox" id="auto-advance" ${autoAdvance ? "checked" : ""}>
            Move on automatically
          </label>
          <label class="setting-inline">
            <span>Show each answer for</span>
            <select id="pause-seconds" ${autoAdvance ? "" : "disabled"}>
              ${PAUSE_CHOICES.map((s) => `<option value="${s}" ${s === pauseSeconds ? "selected" : ""}>${s} seconds</option>`).join("")}
            </select>
          </label>
          <p class="setting-help" id="pause-help">${autoAdvance
            ? `The next question starts ${pauseSeconds} seconds after the answer is shown. You can still pause or move on yourself.`
            : "Off: you move to each question yourself."}</p>
        </div>
      </fieldset>
      <button type="button" class="btn btn-accent btn-big" id="start-quiz" ${players.length === 0 ? "disabled" : ""}>Start quiz</button>`;

    stage.querySelectorAll("input[name=scoring]").forEach((input) => {
      input.addEventListener("change", () => {
        correctOnly = input.value === "correct";
        drawLobby();
        stage.querySelector("input[name=scoring]:checked").focus();
      });
    });
    const pauseSelect = document.getElementById("pause-seconds");
    const pauseHelp = document.getElementById("pause-help");
    const describePause = () => {
      pauseSelect.disabled = !autoAdvance;
      pauseHelp.textContent = autoAdvance
        ? `The next question starts ${pauseSeconds} seconds after the answer is shown. You can still pause or move on yourself.`
        : "Off: you move to each question yourself.";
    };
    document.getElementById("auto-advance").addEventListener("change", (event) => {
      autoAdvance = event.target.checked;
      describePause();
    });
    pauseSelect.addEventListener("change", () => {
      pauseSeconds = Number(pauseSelect.value);
      describePause();
    });
    document.getElementById("start-quiz").addEventListener("click", startQuiz);
  };

  // Sends the chosen scoring to the server, then shows the first question.
  async function startQuiz() {
    if (phase !== "lobby") return;
    const button = document.getElementById("start-quiz");
    button.disabled = true;
    try {
      await connection.invoke("SetScoring", game.code, correctOnly);
    } catch (error) {
      button.disabled = false;
      toast(hubMessage(error), "error");
      return;
    }
    nextQuestion();
  }

  const optionsHtml = (reveal) => `
    <ul class="stage-options">
      ${question.options.map((text, i) => `
        <li class="option option-${i} ${reveal ? (i === reveal.correctIndex ? "is-correct" : "is-wrong") : ""}">
          <span class="letter" aria-hidden="true">${OPTION_LETTERS[i]}</span>
          <span class="option-text">${esc(text)}</span>
          ${reveal ? `<span class="option-count">${i === reveal.correctIndex ? "Correct, " : ""}${plural(reveal.counts[i], "answer")}</span>` : ""}
        </li>`).join("")}
    </ul>`;

  const drawQuestion = () => {
    stage.innerHTML = `
      <p class="stage-label">Question ${question.number} of ${question.total}</p>
      <h1 class="stage-question">${esc(question.text)}</h1>
      ${optionsHtml(null)}
      <div class="stage-status">
        <span class="countdown" id="countdown" aria-label="Seconds left">${question.seconds}</span>
        <span id="answered-count">${answered} of ${players.length} answered</span>
        <button type="button" class="btn" id="reveal-answer">Show answer</button>
      </div>`;
    document.getElementById("reveal-answer").addEventListener("click", revealAnswer);
  };

  const drawReveal = (reveal) => {
    stage.innerHTML = `
      <p class="stage-label">Question ${question.number} of ${question.total}</p>
      <h1 class="stage-question">${esc(question.text)}</h1>
      ${optionsHtml(reveal)}
      <div class="stage-board">
        <h2>${reveal.isLast ? "Top scores" : "Scores so far"}</h2>
        ${leaderboardHtml(reveal.leaderboard)}
      </div>
      <button type="button" class="btn btn-accent btn-big" id="next-step">${reveal.isLast ? "Show final results" : "Next question"}</button>
      ${autoAdvance ? `
        <p class="auto-line" id="auto-line">
          <span id="auto-text"></span>
          <button type="button" class="btn" id="auto-pause">Pause</button>
        </p>` : ""}`;

    const next = reveal.isLast ? finishGame : nextQuestion;
    document.getElementById("next-step").addEventListener("click", () => {
      stopCountdown();
      next();
    });

    if (autoAdvance) {
      const what = reveal.isLast ? "Final results" : "Next question";
      startCountdown(
        pauseSeconds,
        (left) => {
          const text = document.getElementById("auto-text");
          if (text) text.textContent = `${what} in ${plural(left, "second")}`;
        },
        next);

      // Pausing turns auto-advance off for the rest of the game; the host moves on by hand.
      document.getElementById("auto-pause").addEventListener("click", () => {
        stopCountdown();
        autoAdvance = false;
        document.getElementById("auto-line").remove();
        document.getElementById("next-step").focus();
      });
    }
  };

  async function nextQuestion() {
    if (phase !== "lobby" && phase !== "reveal") return;
    const before = phase;
    phase = "starting";
    try {
      question = await connection.invoke("StartQuestion", game.code);
    } catch (error) {
      phase = before;
      toast(hubMessage(error), "error");
      return;
    }
    phase = "question";
    answered = 0;
    drawQuestion();
    startCountdown(
      question.seconds,
      (left) => {
        const el = document.getElementById("countdown");
        if (el) el.textContent = left;
      },
      revealAnswer);
  }

  async function revealAnswer() {
    if (phase !== "question") return;
    phase = "revealing";
    stopCountdown();
    try {
      const reveal = await connection.invoke("RevealAnswer", game.code);
      phase = "reveal";
      drawReveal(reveal);
    } catch (error) {
      phase = "question";
      toast(hubMessage(error), "error");
    }
  }

  async function finishGame() {
    if (phase !== "reveal") return;
    phase = "finishing";
    let final;
    try {
      final = await connection.invoke("FinishGame", game.code);
    } catch (error) {
      phase = "reveal";
      toast(hubMessage(error), "error");
      return;
    }
    phase = "finished";
    leaveQuizPage();
    const scoringNote = correctOnly ? "Scored on correct answers only" : "Scored on speed and accuracy";
    stage.innerHTML = `
      <p class="stage-label">${esc(game.title)}</p>
      <h1 class="stage-title">Final results</h1>
      <p class="stage-label">${scoringNote}</p>
      <div class="stage-board stage-board-final">${leaderboardHtml(final)}</div>
      <div class="form-actions stage-actions">
        <a class="btn btn-accent" href="#/quizzes">Back to your quizzes</a>
      </div>`;
  }

  connection.on("PlayersChanged", (names) => {
    players = names;
    if (phase === "lobby") drawLobby();
    const count = document.getElementById("answered-count");
    if (count) count.textContent = `${answered} of ${players.length} answered`;
  });

  connection.on("AnswerProgress", (progress) => {
    answered = progress.answered;
    const count = document.getElementById("answered-count");
    if (count) count.textContent = `${progress.answered} of ${progress.players} answered`;
    if (phase === "question" && progress.answered >= progress.players) revealAnswer(); // everyone has answered
  });

  connection.onclose(() => {
    if (quizConnection !== connection) return; // we closed it ourselves
    quizConnection = null;
    stopCountdown();
    fail("The connection was lost, so the game has ended.");
  });

  drawLobby();
}

// ---------- page: join and play ----------

function pageJoin(params) {
  const user = session.get();
  const suggestedName = user ? user.displayName.trim().split(/\s+/)[0].slice(0, 20) : "";

  app.innerHTML = `
    <section class="panel panel-narrow" id="play">
      <h1>Join a quiz</h1>
      <p class="lede">Enter the code on the host's screen.</p>
      <p class="join-note">${user
        ? `Logged in as ${esc(user.displayName)}. Your result will be saved to your <a href="#/history">quiz history</a>.`
        : `Playing as a guest. <a href="#/login?next=${encodeURIComponent("/join")}">Log in</a> first if you want to keep your results.`}</p>
      <form class="form" id="join-form">
        <label class="field">
          <span>Game code</span>
          <input class="code-input" name="code" required maxlength="6" autocomplete="off" autocapitalize="characters"
            spellcheck="false" value="${esc((params.get("code") || "").toUpperCase())}" placeholder="ABC234">
        </label>
        <label class="field">
          <span>Your name</span>
          <input name="name" required maxlength="20" autocomplete="nickname" value="${esc(suggestedName)}">
          <small>Shown on the scoreboard</small>
        </label>
        <p class="error" role="alert" hidden></p>
        <div class="form-actions">
          <button type="submit" class="btn btn-primary">Join game</button>
        </div>
      </form>
    </section>`;

  const id = renderId;
  const play = document.getElementById("play");

  document.getElementById("join-form").addEventListener("submit", (event) => {
    event.preventDefault();
    submitForm(event.target, async (data) => {
      const code = data.get("code").trim().toUpperCase();
      const name = data.get("name").trim();

      let connection;
      let joined;
      try {
        connection = await openQuizConnection();
        if (id !== renderId) {
          connection.stop().catch(() => {});
          return;
        }
        joined = await connection.invoke("JoinGame", code, name);
      } catch (error) {
        console.error(error);
        if (connection) connection.stop().catch(() => {});
        throw new Error(hubMessage(error));
      }
      if (id !== renderId) {
        connection.stop().catch(() => {});
        return;
      }

      quizConnection = connection;
      playGame(play, connection, joined, name);
    });
  });
}

function playGame(play, connection, joined, name) {
  play.className = "play";
  let question = null;
  let locked = false;

  const show = (html) => {
    play.innerHTML = html;
  };

  const drawWaiting = () => show(`
    <p class="stage-label">${esc(joined.title)}</p>
    <h1>You're in, ${esc(name)}</h1>
    <p class="lede">Watch the host's screen. The first question will appear here when the quiz starts.</p>`);

  const setStatus = (text) => {
    const el = document.getElementById("play-status");
    if (el) el.textContent = text;
  };

  const lockButtons = (chosen) => {
    locked = true;
    play.querySelectorAll(".answer").forEach((button, i) => {
      button.disabled = true;
      if (i === chosen) button.classList.add("is-chosen");
    });
  };

  connection.on("QuestionStarted", (view) => {
    question = view;
    locked = false;
    show(`
      <p class="stage-label">Question ${view.number} of ${view.total}</p>
      <h1 class="play-question">${esc(view.text)}</h1>
      <div class="answer-buttons">
        ${view.options.map((text, i) => `
          <button type="button" class="answer option-${i}" data-answer="${i}">
            <span class="letter" aria-hidden="true">${OPTION_LETTERS[i]}</span>
            <span>${esc(text)}</span>
          </button>`).join("")}
      </div>
      <p class="play-status" id="play-status" role="status"></p>
      <p class="muted play-hint">${view.speedCounts ? "Faster right answers score more." : "Only right answers count. Take your time."}</p>`);

    play.querySelectorAll(".answer").forEach((button) => {
      button.addEventListener("click", async () => {
        if (locked) return;
        const choice = Number(button.dataset.answer);
        lockButtons(choice);
        stopCountdown();
        setStatus("Sending your answer…");
        try {
          await connection.invoke("SubmitAnswer", joined.code, choice);
          setStatus("Answer locked in. Waiting for the others.");
        } catch (error) {
          setStatus(hubMessage(error));
        }
      });
    });

    startCountdown(
      view.seconds,
      (left) => {
        if (!locked) setStatus(`${plural(left, "second")} left`);
      },
      () => {
        if (!locked) {
          lockButtons(-1);
          setStatus("Time's up. Waiting for the answer.");
        }
      });
  });

  connection.on("YourResult", (result) => {
    stopCountdown();
    const heading = !result.answered ? "No answer" : result.correct ? "Correct!" : "Not quite";
    const answerText = question && result.correctIndex >= 0
      ? `${OPTION_LETTERS[result.correctIndex]}: ${question.options[result.correctIndex]}`
      : "";
    show(`
      <p class="stage-label">${question ? `Question ${question.number} of ${question.total}` : ""}</p>
      <h1 class="result-heading ${result.correct ? "is-right" : "is-wrong"}">${heading}</h1>
      <p class="result-points">${result.correct ? `+${plural(result.points, "point")}` : "0 points"}</p>
      ${answerText ? `<p class="lede">The answer was ${esc(answerText)}</p>` : ""}
      <p class="score-line"><strong>${result.score}</strong> ${result.score === 1 ? "point" : "points"}, ${ordinal(result.rank)} of ${result.players}</p>
      <p class="muted">Watch the host's screen for the next question.</p>`);
  });

  connection.on("GameFinished", (final) => {
    stopCountdown();
    quizConnection = null;
    connection.stop().catch(() => {});
    show(`
      <p class="stage-label">${esc(joined.title)}</p>
      <h1>${ordinal(final.rank)} place</h1>
      <p class="score-line"><strong>${final.score}</strong> ${final.score === 1 ? "point" : "points"}, out of ${plural(final.players, "player")}</p>
      <div class="stage-board">
        <h2>Top scores</h2>
        ${leaderboardHtml(final.leaderboard)}
      </div>
      <p class="muted">${joined.resultsSaved
        ? `Saved to your <a href="#/history">quiz history</a>.`
        : "You played as a guest, so this result wasn't saved. Log in before joining next time to keep your results."}</p>
      <div class="form-actions">
        <a class="btn btn-primary" href="#/join">Join another game</a>
        <a class="btn" href="#/">Browse notes</a>
      </div>`);
  });

  const ended = (message) => {
    stopCountdown();
    show(`
      <h1>The game has ended</h1>
      <p class="lede">${esc(message)}</p>
      <div class="form-actions">
        <button type="button" class="btn btn-primary" id="join-again">Join another game</button>
      </div>`);
    document.getElementById("join-again").addEventListener("click", () => route());
  };

  connection.on("GameEnded", (reason) => {
    quizConnection = null;
    connection.stop().catch(() => {});
    ended(reason);
  });

  connection.onclose(() => {
    if (quizConnection !== connection) return; // we closed it ourselves
    quizConnection = null;
    ended("The connection was lost.");
  });

  drawWaiting();
}

// ---------- page: practise on your own ----------

async function pagePractice(id) {
  const user = session.get();
  app.innerHTML = `
    <section id="practice">
      <h1>Practise on your own</h1>
      <p class="lede">No host, no timer. Answer at your own pace and see the right answer straight away.</p>
      <p class="join-note">${user
        ? `Logged in as ${esc(user.displayName)}. Your practice runs are saved to your <a href="#/history">quiz history</a>.`
        : `Practising as a guest. <a href="#/login?next=${encodeURIComponent("/practice")}">Log in</a> first if you want to keep your results.`}</p>

      <div class="practice-grid">
        <section class="panel practice-mix">
          <h2>Random mix</h2>
          <p class="muted">Questions picked at random from every quiz students have shared.</p>
          <form class="form" id="mix-form">
            <label class="field">
              <span>Topic</span>
              <select name="topic" id="mix-topic">
                <option value="">All topics</option>
              </select>
            </label>
            <label class="field">
              <span>How many questions</span>
              <select name="count">
                ${PRACTICE_COUNTS.map((n) => `<option value="${n}" ${n === DEFAULT_PRACTICE_COUNT ? "selected" : ""}>${n} questions</option>`).join("")}
              </select>
            </label>
            <p class="error" role="alert" hidden></p>
            <div class="form-actions">
              <button type="submit" class="btn btn-primary">Start practising</button>
            </div>
          </form>
        </section>

        <section class="practice-shared">
          <h2>Shared quizzes</h2>
          <div id="shared-list"><p class="muted">Loading shared quizzes…</p></div>
        </section>
      </div>
    </section>`;

  document.getElementById("mix-form").addEventListener("submit", (event) => {
    event.preventDefault();
    submitForm(event.target, async (data) => {
      const body = { topic: data.get("topic") || null, count: Number(data.get("count")) };
      const begin = () => api("/practice/start", { method: "POST", body });
      const start = await begin();
      if (id === renderId) runPractice(start, begin);
    });
  });

  const list = document.getElementById("shared-list");
  let topics;
  let quizzes;
  try {
    [topics, quizzes] = await Promise.all([api("/practice/topics"), api("/practice/quizzes")]);
  } catch (error) {
    if (id === renderId) list.innerHTML = `<p class="error" role="alert">${esc(error.message)}</p>`;
    return;
  }
  if (id !== renderId) return;

  document.getElementById("mix-topic").insertAdjacentHTML(
    "beforeend",
    topics.map((t) => `<option value="${esc(t)}">${esc(t)}</option>`).join(""));

  if (quizzes.length === 0) {
    list.innerHTML = `
      <div class="empty">
        <h2>No shared quizzes yet</h2>
        <p class="muted">When you write a quiz, tick "Share for practice" so other students can use it.</p>
        ${user ? `<a class="btn btn-primary" href="#/quiz/new">Write a quiz</a>` : ""}
      </div>`;
    return;
  }

  list.innerHTML = `
    <ul class="shared-quizzes">
      ${quizzes.map((quiz) => `
        <li class="panel shared-quiz">
          <div>
            <h3>${esc(quiz.title)}</h3>
            <p class="muted">${plural(quiz.questionCount, "question")}, by ${esc(quiz.authorName)}</p>
            ${quiz.topic ? `<p class="quiz-flags"><span class="quiz-flag">${esc(quiz.topic)}</span></p>` : ""}
          </div>
          <a class="btn" href="#/practice/quiz/${esc(quiz.id)}">Practise</a>
        </li>`).join("")}
    </ul>`;
}

// Opening #/practice/quiz/{id} starts that quiz straight away.
async function pagePracticeQuiz(quizId, id) {
  const begin = () => api("/practice/start", { method: "POST", body: { quizId } });
  app.innerHTML = `<section class="play" id="practice-run"><p class="stage-label">Getting your questions…</p></section>`;
  let start;
  try {
    start = await begin();
  } catch (error) {
    if (id !== renderId) return;
    document.getElementById("practice-run").innerHTML = `
      <h1>Can't start this practice</h1>
      <p class="lede">${esc(error.message)}</p>
      <div class="form-actions"><a class="btn btn-primary" href="#/practice">See shared quizzes</a></div>`;
    return;
  }
  if (id === renderId) runPractice(start, begin);
}

// One question at a time. The server checks each answer and says which one was right.
// "Practise again" calls begin() for a fresh run with the same settings (a new random mix, or the same quiz).
function runPractice(start, begin) {
  app.innerHTML = `<section class="play" id="practice-run"></section>`;
  const box = document.getElementById("practice-run");
  const id = renderId;
  const total = start.questions.length;
  let index = 0;
  let score = 0;

  const drawQuestion = () => {
    const q = start.questions[index];
    box.innerHTML = `
      <p class="stage-label">${esc(start.title)}</p>
      <p class="practice-progress">Question ${index + 1} of ${total}<span aria-hidden="true">, </span><span>${score} right so far</span></p>
      <h1 class="play-question">${esc(q.text)}</h1>
      <div class="answer-buttons">
        ${q.options.map((text, i) => `
          <button type="button" class="answer option-${i}" data-answer="${i}">
            <span class="letter" aria-hidden="true">${OPTION_LETTERS[i]}</span>
            <span>${esc(text)}</span>
          </button>`).join("")}
      </div>
      <p class="play-status" id="play-status" role="status"></p>
      <div class="practice-next" id="practice-next"></div>`;

    const status = document.getElementById("play-status");
    const buttons = [...box.querySelectorAll(".answer")];
    buttons[0].focus();

    buttons.forEach((button) => {
      button.addEventListener("click", async () => {
        const choice = Number(button.dataset.answer);
        buttons.forEach((b) => { b.disabled = true; });
        status.textContent = "Checking…";

        let result;
        try {
          result = await api(`/practice/${encodeURIComponent(start.practiceId)}/answer`, {
            method: "POST",
            body: { question: index, option: choice },
          });
        } catch (error) {
          if (id !== renderId) return;
          status.textContent = error.message;
          buttons.forEach((b) => { b.disabled = false; });
          return;
        }
        if (id !== renderId) return;

        score = result.score;
        buttons.forEach((b, i) => {
          if (i === result.correctIndex) b.classList.add("is-answer");
          else if (i === choice) b.classList.add("is-miss");
        });
        status.innerHTML = result.correct
          ? `<span class="is-right">Correct!</span>`
          : `<span class="is-wrong">Not quite.</span> The answer was ${esc(OPTION_LETTERS[result.correctIndex])}: ${esc(q.options[result.correctIndex])}`;

        const last = index === total - 1;
        const next = document.getElementById("practice-next");
        next.innerHTML = `<button type="button" class="btn btn-primary" id="next-button">${last ? "See results" : "Next question"}</button>`;
        const nextButton = document.getElementById("next-button");
        nextButton.focus();
        nextButton.addEventListener("click", () => {
          if (last) finish();
          else {
            index += 1;
            drawQuestion();
          }
        });
      });
    });
  };

  const finish = async () => {
    box.innerHTML = `<p class="stage-label">Adding up your score…</p>`;
    let final;
    try {
      final = await api(`/practice/${encodeURIComponent(start.practiceId)}/finish`, { method: "POST" });
    } catch (error) {
      if (id !== renderId) return;
      box.innerHTML = `
        <h1>Couldn't finish this practice</h1>
        <p class="lede">${esc(error.message)}</p>
        <div class="form-actions"><a class="btn btn-primary" href="#/practice">Back to practice</a></div>`;
      return;
    }
    if (id !== renderId) return;

    const percent = final.total === 0 ? 0 : Math.round((100 * final.score) / final.total);
    box.innerHTML = `
      <p class="stage-label">${esc(start.title)}</p>
      <h1>${final.score} of ${final.total} right</h1>
      <div class="practice-meter" role="img" aria-label="${percent}% right"><span style="width:${percent}%"></span></div>
      <p class="lede">${percent === 100 ? "Every one right. Nicely done." : percent >= 70 ? "Good going. Have another go at the ones you missed." : "Keep at it. Each run makes the answers stick."}</p>
      <p class="muted">${final.saved
        ? `Saved to your <a href="#/history">quiz history</a>.`
        : session.get() ? "Nothing was answered, so this run wasn't saved." : "You practised as a guest, so this run wasn't saved."}</p>
      <div class="form-actions">
        <button type="button" class="btn btn-primary" id="practice-again">Practise again</button>
        <a class="btn" href="#/practice">Choose something else</a>
      </div>`;
    document.getElementById("practice-again").addEventListener("click", async (event) => {
      event.currentTarget.disabled = true;
      try {
        const fresh = await begin();
        if (id === renderId) runPractice(fresh, begin);
      } catch (error) {
        if (id === renderId) toast(error.message, "error");
      }
    });
  };

  drawQuestion();
}
