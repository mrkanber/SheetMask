(function () {
  "use strict";

  // ------------------------------------------------------------ language
  // A choice made with the TR/EN button wins; otherwise the host reports the Windows language
  // (Turkish display language or Turkish regional format → Turkish). Turkish until it answers.
  var chosen = "";
  try { chosen = localStorage.getItem("lang") || ""; } catch (e) { chosen = ""; }
  var lang = chosen === "en" || chosen === "tr" ? chosen : "tr";

  function T() { return window.I18N[lang]; }
  function t(key, vars) {
    var s = key.split(".").reduce(function (o, k) { return o && o[k]; }, T());
    if (s == null) s = key.split(".").reduce(function (o, k) { return o && o[k]; }, window.I18N.en) || key;
    if (vars) Object.keys(vars).forEach(function (k) { s = s.split("{" + k + "}").join(vars[k]); });
    return s;
  }
  function applyTexts() {
    document.documentElement.lang = lang;
    document.querySelectorAll("[data-t]").forEach(function (el) { el.textContent = t(el.getAttribute("data-t")); });
    $("aiText").placeholder = t("pastePh");
    $("lang").textContent = lang === "tr" ? "EN" : "TR";
  }

  // ------------------------------------------------------------ helpers
  function $(id) { return document.getElementById(id); }
  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }
  function show(el, on) { el.classList.toggle("hidden", !on); }
  function api(path, body) {
    return fetch("/api/" + path, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body || {}) })
      .then(function (r) {
        return r.json().then(function (j) {
          if (!r.ok) { var e = new Error(j.error || "Error"); e.code = j.code; throw e; }
          return j;
        });
      });
  }

  var KIND_GROUPS = [
    ["keep", ["Keep"]],
    ["numbers", ["Scale", "RandomNumber", "DateShift"]],
    ["people", ["FullName", "FirstName", "LastName", "NationalId", "Phone", "Email", "Iban", "Address", "Identifier"]],
    ["places", ["City", "Country", "Category"]],
    ["other", ["FreeText", "Redact", "Clear"]]
  ];
  var IRREVERSIBLE = { RandomNumber: 1, Redact: 1, Clear: 1 };
  var WHAT = { first: "FirstName", last: "LastName", city: "City", country: "Country", cat: "Category", id: "Identifier",
               tckn: "NationalId", phone: "Phone", email: "Email", iban: "Iban", address: "Address" };

  // ------------------------------------------------------------ state
  var S = { plan: null, file: null, sheet: null, pane: "columns", preview: null, previewSeq: 0, timer: 0, busy: false, keyPath: null };

  function sheetPlan() { return S.plan ? S.plan.sheets.filter(function (s) { return s.name === S.sheet; })[0] : null; }
  function previewSheet() { return S.preview ? S.preview.filter(function (s) { return s.name === S.sheet; })[0] : null; }

  // ------------------------------------------------------------ open / analyse
  function openFile(path) {
    show($("empty"), true); show($("work"), false);
    show($("loading"), true); show($("openErr"), false);
    $("openBtn").disabled = true;
    api("open", { path: path || null }).then(function (r) {
      if (r.cancelled) return;
      loadPlan(r, false);
    }).catch(function (e) {
      $("openErr").textContent = e.message; show($("openErr"), true);
    }).then(function () {
      show($("loading"), false); $("openBtn").disabled = false;
      if (S.plan) { show($("empty"), false); show($("work"), true); }
    });
  }

  function loadPlan(r, keepChoices) {
    var old = S.plan;
    S.plan = r.plan;
    S.file = r.file;
    if (keepChoices && old) {
      S.plan.options = old.options;
      S.plan.sheets.forEach(function (sp) {
        if (sp.name === S.sheet) return;   // re-analysed sheet keeps the new detection
        var prev = old.sheets.filter(function (o) { return o.name === sp.name; })[0];
        if (!prev) return;
        sp.columns.forEach(function (c) {
          var p = prev.columns.filter(function (x) { return x.column === c.column; })[0];
          if (p) c.kind = p.kind;
        });
      });
    }
    if (!S.sheet || !S.plan.sheets.some(function (s) { return s.name === S.sheet; }))
      S.sheet = S.plan.sheets.length ? S.plan.sheets[0].name : null;
    S.preview = null;
    $("fileChip").innerHTML = "<b>" + esc(S.file.name) + "</b>";
    $("fileChip").title = S.file.folder;
    $("result").innerHTML = ""; show($("result"), false);
    bindOptions();
    renderAll();
    schedulePreview(0);
  }

  // ------------------------------------------------------------ rendering
  function renderAll() { renderSheets(); renderHeader(); renderColumns(); if (S.pane === "preview") renderPreview(); }

  function renderSheets() {
    $("sheetList").innerHTML = S.plan.sheets.map(function (s) {
      var n = s.columns.filter(function (c) { return c.kind !== "Keep"; }).length;
      return '<div class="sheet' + (s.name === S.sheet ? " on" : "") + '" data-sheet="' + esc(s.name) + '">' + esc(s.name) +
        "<small>" + esc(t("changing", { n: n, m: s.columns.length })) + "</small></div>";
    }).join("");
  }

  function renderHeader() {
    var sp = sheetPlan();
    if (!sp) return;
    $("sheetTitle").textContent = sp.name;
    $("sheetRows").textContent = sp.headerRow > 0
      ? t("rows", { h: sp.headerRow, a: sp.firstDataRow, b: sp.lastRow })
      : t("noHeader", { a: sp.firstDataRow, b: sp.lastRow });
    $("hdrRow").value = sp.headerRow;
  }

  function kindSelect(c) {
    return '<select data-col="' + c.column + '">' + KIND_GROUPS.map(function (g) {
      return '<optgroup label="' + esc(t("groups." + g[0])) + '">' + g[1].map(function (k) {
        return '<option value="' + k + '"' + (c.kind === k ? " selected" : "") + ">" + esc(t("kinds." + k)) + "</option>";
      }).join("") + "</optgroup>";
    }).join("") + "</select>";
  }

  function example(c, i) {
    var pv = previewSheet();
    if (!pv) return '<span class="muted">…</span>';
    for (var r = 0; r < pv.before.length; r++) {
      var b = pv.before[r][i], a = pv.after[r][i];
      if (b === "" || b == null) continue;
      if (c.kind === "Keep" || a === b) return '<span>' + esc(b) + "</span>";
      return '<span class="real">' + esc(b) + '</span><span class="fake">' + esc(a === "" ? "∅" : a) + "</span>" +
        (IRREVERSIBLE[c.kind] ? '<span class="irr">' + esc(t("irreversible")) + "</span>" : "");
    }
    return '<span class="muted">' + esc(t("types.empty")) + "</span>";
  }

  function reasonText(r) { var m = T().reasons[r]; return m || r; }

  function renderColumns() {
    var sp = sheetPlan();
    if (!sp) { $("colBody").innerHTML = ""; return; }
    $("colBody").innerHTML = sp.columns.map(function (c, i) {
      var cls = c.kind === "Keep" ? "keep" : (c.kind !== c.detected ? "changed" : "");
      return '<tr class="' + cls + '"><td class="col">' + esc(c.letter + " · " + (c.header || "")) +
        "<small>" + esc(t("types." + c.valueType)) + "</small></td><td>" + kindSelect(c) +
        '</td><td class="ex">' + example(c, i) + '</td><td class="why">' + esc(reasonText(c.reason)) + "</td></tr>";
    }).join("");
  }

  function gridHtml(sp, rows, compare) {
    var head = "<tr>" + sp.columns.map(function (c) { return "<th>" + esc(c.letter + " " + (c.header || "")) + "</th>"; }).join("") + "</tr>";
    var body = rows.map(function (row, r) {
      return "<tr>" + row.map(function (v, i) {
        var ch = compare && compare[r] && compare[r][i] !== v;
        return "<td" + (ch ? ' class="ch"' : "") + ' title="' + esc(v) + '">' + esc(v) + "</td>";
      }).join("") + "</tr>";
    }).join("");
    return "<table>" + head + body + "</table>";
  }

  function renderPreview() {
    var sp = sheetPlan(), pv = previewSheet();
    if (!sp || !pv) { $("gBefore").innerHTML = $("gAfter").innerHTML = '<p class="note" style="padding:10px">…</p>'; return; }
    $("prevNote").textContent = t("previewNote", { n: pv.before.length });
    $("gBefore").innerHTML = gridHtml(sp, pv.before, null);
    $("gAfter").innerHTML = gridHtml(sp, pv.after, pv.before);
  }

  // ------------------------------------------------------------ preview
  function schedulePreview(delay) {
    clearTimeout(S.timer);
    S.timer = setTimeout(runPreview, delay == null ? 250 : delay);
  }
  function runPreview() {
    if (!S.plan) return;
    var seq = ++S.previewSeq;
    api("preview", { plan: S.plan }).then(function (r) {
      if (seq !== S.previewSeq) return;
      S.preview = r.sheets;
      renderColumns();
      if (S.pane === "preview") renderPreview();
    }).catch(function (e) { console.warn(e); });
  }

  // ------------------------------------------------------------ options
  var OPT = [["oShared", "sharedFactor"], ["oRound", "keepRoundness"], ["oWeekday", "keepWeekday"],
             ["oScan", "scanAllText"], ["oComments", "removeComments"], ["oProps", "clearProperties"]];
  function bindOptions() {
    var o = S.plan.options;
    OPT.forEach(function (p) { $(p[0]).checked = !!o[p[1]]; });
    $("oNoise").value = o.noisePercent || 0;
  }
  OPT.forEach(function (p) {
    $(p[0]).addEventListener("change", function () { if (!S.plan) return; S.plan.options[p[1]] = this.checked; schedulePreview(); });
  });
  $("oNoise").addEventListener("input", function () {
    if (!S.plan) return;
    var v = parseFloat(this.value);
    S.plan.options.noisePercent = isFinite(v) ? Math.max(0, Math.min(20, v)) : 0;
    schedulePreview();
  });

  // ------------------------------------------------------------ mask run
  function stageText(st) { return t("stage." + st); }

  window.chrome && chrome.webview && chrome.webview.addEventListener("message", function (ev) {
    var m = ev.data;
    if (m && m.type === "progress") $("stage").textContent = stageText(m.stage);
  });

  function runMask() {
    var pw = $("pw1").value, pw2 = $("pw2").value;
    var err = $("runErr");
    show(err, false);
    var changes = S.plan.sheets.some(function (s) { return s.columns.some(function (c) { return c.kind !== "Keep"; }); });
    if (!changes) { err.textContent = t("nothing"); show(err, true); return; }
    if (pw.length < 6) { err.textContent = t("pwShort"); show(err, true); return; }
    if (pw !== pw2) { err.textContent = t("pwMismatch"); show(err, true); return; }

    S.busy = true;
    $("runBtn").disabled = true; $("runBtn").textContent = t("running");
    show($("progress"), true); $("stage").textContent = stageText("open");
    show($("result"), false);
    api("mask", { plan: S.plan, password: pw }).then(function (r) {
      renderResult(r);
      $("pw1").value = $("pw2").value = "";
    }).catch(function (e) {
      err.textContent = e.message; show(err, true);
    }).then(function () {
      S.busy = false;
      $("runBtn").disabled = false; $("runBtn").textContent = t("run");
      show($("progress"), false);
    });
  }

  function fileName(p) { return String(p || "").split(/[\\/]/).pop(); }

  function renderResult(r) {
    var leaks = r.leaks || [];
    var html = leaks.length === 0
      ? '<div class="state clean">' + esc(t("leakClean")) + "</div>"
      : '<div class="state leak">' + esc(t("leakFound", { n: leaks.length })) + "</div><ul>" +
        leaks.slice(0, 8).map(function (l) {
          var what = WHAT[l.what] ? t("kinds." + WHAT[l.what]) : l.what;
          return "<li>" + esc(what) + ": " + esc(l.hint) + " (" + esc(l.part) + ")</li>";
        }).join("") + (leaks.length > 8 ? "<li>…</li>" : "") + "</ul>";
    html += '<div class="muted">' + esc(t("cells", { c: r.cellsChanged, t: r.textReplacements })) + "</div>";
    html += '<div class="path"><b>' + esc(t("maskedFile")) + "</b><span title=\"" + esc(r.outputPath) + "\">" + esc(fileName(r.outputPath)) + '</span> <button class="linkbtn" data-reveal="' + esc(r.outputPath) + '">' + esc(t("showFile")) + "</button></div>";
    html += '<div class="path"><b>' + esc(t("keyFile")) + "</b><span title=\"" + esc(r.keyPath) + "\">" + esc(fileName(r.keyPath)) + "</span></div>";
    (r.notes || []).forEach(function (n) { html += '<div class="warnbox">' + esc(n) + "</div>"; });
    html += '<div class="warnbox">' + esc(t("keepKey")) + "</div>";
    $("result").innerHTML = html;
    show($("result"), true);
    S.lastKey = r.keyPath;
  }

  // ------------------------------------------------------------ reveal
  function setKey(path, name) {
    S.keyPath = path;
    $("keyName").textContent = name;
    show($("keyOk"), false);
    $("revealBtn").disabled = $("xlsxBtn").disabled = true;
  }

  function unlock() {
    show($("keyErr"), false);
    if (!S.keyPath) { $("keyErr").textContent = t("locked"); show($("keyErr"), true); return; }
    $("keyUnlock").disabled = true;
    api("key/unlock", { path: S.keyPath, password: $("keyPw").value }).then(function (k) {
      $("keyPw").value = "";
      $("keyOk").innerHTML = esc(t("unlocked", { f: k.sourceFile })) + "<br>" + esc(t("keyInfo", { v: k.values, c: k.columns })) +
        (k.noise > 0 ? "<br>" + esc(t("noiseNote")) : "");
      show($("keyOk"), true);
      $("rDates").disabled = !k.hasDates; if (!k.hasDates) $("rDates").checked = false;
      $("rNumbers").disabled = !k.hasAmounts; if (!k.hasAmounts) $("rNumbers").checked = false;
      $("revealBtn").disabled = $("xlsxBtn").disabled = false;
    }).catch(function (e) {
      $("keyErr").textContent = e.code === "password" ? t("wrongPw") : e.message; show($("keyErr"), true);
    }).then(function () { $("keyUnlock").disabled = false; });
  }

  function revealText() {
    api("reveal/text", { text: $("aiText").value, numbers: $("rNumbers").checked, dates: $("rDates").checked }).then(function (r) {
      $("realText").value = r.text;
      $("revCount").textContent = t("replaced", { n: r.count });
    }).catch(function (e) { $("revCount").textContent = e.message; });
  }

  function revealXlsx() {
    $("xlsxBtn").disabled = true;
    $("xlsxOut").innerHTML = '<div class="progress"><span class="spin"></span>' + esc(t("running")) + "</div>";
    api("reveal/xlsx").then(function (r) {
      if (r.cancelled) { $("xlsxOut").innerHTML = ""; return; }
      $("xlsxOut").innerHTML = '<div class="ok">' + esc(t("cells", { c: r.changed, t: 0 }).split(" · ")[0]) + '<br><span title="' + esc(r.outputPath) + '">' +
        esc(fileName(r.outputPath)) + '</span><br><button class="linkbtn" data-reveal="' + esc(r.outputPath) + '">' + esc(t("showFile")) + "</button></div>" +
        (r.notes || []).map(function (n) { return '<div class="warnbox">' + esc(n) + "</div>"; }).join("");
    }).catch(function (e) {
      $("xlsxOut").innerHTML = '<div class="err">' + esc(e.message) + "</div>";
    }).then(function () { $("xlsxBtn").disabled = false; });
  }

  // ------------------------------------------------------------ events
  document.querySelectorAll(".tab").forEach(function (b) {
    b.addEventListener("click", function () {
      document.querySelectorAll(".tab").forEach(function (x) { x.classList.toggle("on", x === b); });
      var v = b.getAttribute("data-view");
      show($("maskView"), v === "mask"); show($("revealView"), v === "reveal");
      if (v === "reveal" && !S.keyPath && S.lastKey) setKey(S.lastKey, S.lastKey.split("\\").pop());
    });
  });
  document.querySelectorAll(".seg button").forEach(function (b) {
    b.addEventListener("click", function () {
      document.querySelectorAll(".seg button").forEach(function (x) { x.classList.toggle("on", x === b); });
      S.pane = b.getAttribute("data-pane");
      show($("colPane"), S.pane === "columns"); show($("prevPane"), S.pane === "preview");
      if (S.pane === "preview") renderPreview();
    });
  });
  $("openBtn").addEventListener("click", function () { openFile(null); });
  $("openOther").addEventListener("click", function () { openFile(null); });
  $("sheetList").addEventListener("click", function (e) {
    var el = e.target.closest(".sheet");
    if (!el) return;
    S.sheet = el.getAttribute("data-sheet");
    renderAll();
  });
  $("colBody").addEventListener("change", function (e) {
    var sel = e.target.closest("select");
    if (!sel) return;
    var col = +sel.getAttribute("data-col");
    var c = sheetPlan().columns.filter(function (x) { return x.column === col; })[0];
    c.kind = sel.value;
    renderSheets();
    renderColumns();
    schedulePreview();
  });
  $("hdrApply").addEventListener("click", function () {
    var n = parseInt($("hdrRow").value, 10);
    if (!(n >= 0)) return;
    var rows = {};
    S.plan.sheets.forEach(function (s) { rows[s.name] = s.headerRow; });
    rows[S.sheet] = n;
    $("hdrApply").disabled = true;
    api("reanalyze", { headerRows: rows }).then(function (r) { loadPlan(r, true); })
      .catch(function (e) { alert(e.message); })
      .then(function () { $("hdrApply").disabled = false; });
  });
  $("runBtn").addEventListener("click", runMask);
  document.addEventListener("click", function (e) {
    var b = e.target.closest("[data-reveal]");
    if (b) api("reveal-folder", { path: b.getAttribute("data-reveal") });
  });
  // keep the two preview grids scrolled together
  var syncing = false;
  ["gBefore", "gAfter"].forEach(function (id, i, all) {
    $(id).addEventListener("scroll", function () {
      if (syncing) return;
      syncing = true;
      var other = $(all[1 - i]);
      other.scrollTop = this.scrollTop; other.scrollLeft = this.scrollLeft;
      requestAnimationFrame(function () { syncing = false; });
    });
  });

  $("keyChoose").addEventListener("click", function () {
    api("key/choose").then(function (r) { if (!r.cancelled) setKey(r.path, r.name); });
  });
  $("keyUnlock").addEventListener("click", unlock);
  $("keyPw").addEventListener("keydown", function (e) { if (e.key === "Enter") unlock(); });
  $("revealBtn").addEventListener("click", revealText);
  $("xlsxBtn").addEventListener("click", revealXlsx);
  $("copyBtn").addEventListener("click", function () {
    var txt = $("realText").value;
    if (!txt) return;
    navigator.clipboard.writeText(txt).then(function () {
      $("copyBtn").textContent = t("copied");
      setTimeout(function () { $("copyBtn").textContent = t("copy"); }, 1200);
    });
  });
  $("lang").addEventListener("click", function () {
    lang = lang === "tr" ? "en" : "tr";
    try { localStorage.setItem("lang", lang); } catch (e) { /* private mode */ }
    applyTexts();
    if (S.plan) renderAll();
  });

  // ------------------------------------------------------------ start
  applyTexts();
  api("state").then(function (s) {
    if (!chosen && s.lang && s.lang !== lang) { lang = s.lang; applyTexts(); }
    if (s.file) openFile(s.file);
  }).catch(function () {});
})();
