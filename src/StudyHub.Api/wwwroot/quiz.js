// StudyHub live quizzes: writing a quiz, hosting a game, and playing one.
// This file only defines functions. app.js decides when to call them, and
// provides the shared helpers used here (api, esc, toast, go, session, app...).

"use strict";

const SIGNALR_URL = "https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/7.0.12/signalr.min.js";
const OPTION_LETTERS = ["A", "B", "C", "D"];
const SECONDS_CHOICES = [10, 20, 30, 45, 60];

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

function plural(count, word) {
  return `${count} ${word}${count === 1 ? "" : "s"}`;
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
        <p class="muted">${plural(quiz.questionCount, "question")}, written ${esc(formatDate(quiz.createdAtUtc))}</p>
      </div>
      <div class="form-actions">
        <a class="btn btn-primary" href="#/host/${esc(quiz.id)}">Host live</a>
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
  let questions = [blankQuestion()];

  if (quizId) {
    app.innerHTML = `<p class="muted">Loading quiz…</p>`;
    try {
      const quiz = await api(`/quizzes/${encodeURIComponent(quizId)}`);
      if (id !== renderId) return;
      title = quiz.title;
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
        request = buildQuizRequest(title, questions);
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
function buildQuizRequest(title, questions) {
  if (title.trim().length < 3) throw new Error("Give the quiz a title of at least 3 characters.");

  return {
    title: title.trim(),
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
      <button type="button" class="btn btn-accent btn-big" id="start-quiz" ${players.length === 0 ? "disabled" : ""}>Start quiz</button>`;
    document.getElementById("start-quiz").addEventListener("click", nextQuestion);
  };

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
      <button type="button" class="btn btn-accent btn-big" id="next-step">${reveal.isLast ? "Show final results" : "Next question"}</button>`;
    document.getElementById("next-step").addEventListener("click", reveal.isLast ? finishGame : nextQuestion);
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
    stage.innerHTML = `
      <p class="stage-label">${esc(game.title)}</p>
      <h1 class="stage-title">Final results</h1>
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
      <p class="play-status" id="play-status" role="status"></p>`);

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
      <p class="result-points">${result.correct ? `+${result.points} points` : "0 points"}</p>
      ${answerText ? `<p class="lede">The answer was ${esc(answerText)}</p>` : ""}
      <p class="score-line"><strong>${result.score}</strong> points, ${ordinal(result.rank)} of ${result.players}</p>
      <p class="muted">Watch the host's screen for the next question.</p>`);
  });

  connection.on("GameFinished", (final) => {
    stopCountdown();
    quizConnection = null;
    connection.stop().catch(() => {});
    show(`
      <p class="stage-label">${esc(joined.title)}</p>
      <h1>${ordinal(final.rank)} place</h1>
      <p class="score-line"><strong>${final.score}</strong> points, out of ${plural(final.players, "player")}</p>
      <div class="stage-board">
        <h2>Top scores</h2>
        ${leaderboardHtml(final.leaderboard)}
      </div>
      <div class="form-actions">
        <a class="btn btn-primary" href="#/">Browse notes</a>
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
