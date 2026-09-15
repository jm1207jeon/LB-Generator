/* batch.js — 연속(배치) 작업 큐
 *
 * 큐 한 줄 = 라벨 1종 × 매수(copies).
 * 큐를 채우는 방법 4가지
 *   1) 현재 입력값을 한 줄 추가
 *   2) 엑셀/시트에서 복사한 탭 구분 텍스트 붙여넣기   ← 현장에서 가장 많이 쓰는 방식
 *   3) CSV/XLSX 파일 불러오기 (열 자동 매핑)
 *   4) SN 연번 전개 (1~100 → 100줄)
 *
 * 각 줄은 실시간으로 검증되어 상태(대기/오류/경고)를 표시한다.
 */
window.LB = window.LB || {};

LB.batch = (() => {
  'use strict';

  const STATUS = {
    pending: { label: '대기', cls: 'pending' },
    running: { label: '처리중', cls: 'running' },
    done: { label: '완료', cls: 'done' },
    error: { label: '오류', cls: 'error' },
    skipped: { label: '건너뜀', cls: 'skipped' },
  };

  /* 붙여넣기/파일의 열 이름 → 내부 필드 */
  const COLUMN_ALIASES = {
    item: ['품목번호', '품목', 'item', 'product number', 'productnumber', 'pn', '품번', 'h'],
    lot: ['lot', '로트', '로트번호', 'lot no', 'lotno', 'batch'],
    sn: ['sn', '시리얼', '일련번호', 'serial', 'serial no'],
    mfg: ['mfg', '제조일', '제조일자', 'manufacture', 'manufacturing date', 'date of manufacture'],
    exp: ['exp', '유효일', '유효기간', 'use by', 'expiry', 'expiration'],
    months: ['개월', '유효기간개월', 'months', '유효개월'],
    copies: ['매수', '수량', 'qty', 'quantity', 'copies', 'count'],
  };

  const state = {
    rows: [],
    running: false,
    paused: false,
    cancelRequested: false,
    progress: { index: 0, total: 0 },
  };

  let seq = 1;
  const listeners = [];
  function onChange(fn) { listeners.push(fn); }
  function emit() { for (const fn of listeners) { try { fn(state); } catch (_) {} } }

  function get() { return state; }
  function rows() { return state.rows; }
  function count() { return state.rows.length; }
  function totalLabels() { return state.rows.reduce((s, r) => s + (Number(r.copies) || 1), 0); }

  function newRow(init) {
    return Object.assign({
      id: 'q' + (seq++),
      item: '', lot: '', sn: '', mfg: '', months: 36, expAuto: true, exp: '',
      copies: 1, status: 'pending', fileName: '', error: '', issues: [],
    }, init || {});
  }

  /* ---------------- 행 조작 ---------------- */

  function add(init, { select = false } = {}) {
    const r = newRow(init);
    state.rows.push(r);
    emit();
    return r;
  }
  function addMany(list) {
    const added = list.map(i => newRow(i));
    state.rows.push(...added);
    emit();
    return added;
  }
  function update(id, patch) {
    const r = state.rows.find(x => x.id === id);
    if (!r) return null;
    Object.assign(r, patch);
    if (patch.status === undefined && r.status !== 'running') r.status = 'pending';
    emit();
    return r;
  }
  function remove(ids) {
    const s = new Set(Array.isArray(ids) ? ids : [ids]);
    state.rows = state.rows.filter(r => !s.has(r.id));
    emit();
  }
  function duplicate(ids) {
    const s = new Set(Array.isArray(ids) ? ids : [ids]);
    const out = [];
    const next = [];
    for (const r of state.rows) {
      next.push(r);
      if (s.has(r.id)) {
        const c = newRow(Object.assign({}, r, { id: undefined, status: 'pending', fileName: '', error: '' }));
        c.id = 'q' + (seq++);
        next.push(c); out.push(c);
      }
    }
    state.rows = next;
    emit();
    return out;
  }
  function move(id, delta) {
    const i = state.rows.findIndex(r => r.id === id);
    const j = i + delta;
    if (i < 0 || j < 0 || j >= state.rows.length) return;
    const a = state.rows;
    [a[i], a[j]] = [a[j], a[i]];
    emit();
  }
  function clear() {
    state.rows = [];
    state.progress = { index: 0, total: 0 };
    emit();
  }
  function removeCompleted() {
    state.rows = state.rows.filter(r => r.status !== 'done');
    emit();
  }
  function resetStatus() {
    for (const r of state.rows) {
      if (r.status !== 'running') { r.status = 'pending'; r.fileName = ''; r.error = ''; }
    }
    state.progress = { index: 0, total: 0 };
    emit();
  }

  /* ---------------- SN 연번 전개 ---------------- */

  /**
   * 한 줄을 SN from~to 로 펼친다. 숫자 SN만 지원하며 자릿수(0채움)를 유지한다.
   * @returns {{ok:boolean, added?:number, error?:string}}
   */
  function expandSn(id, from, to, { pad } = {}) {
    const r = state.rows.find(x => x.id === id);
    if (!r) return { ok: false, error: '행을 찾을 수 없습니다.' };
    const a = Number(from), b = Number(to);
    if (!Number.isFinite(a) || !Number.isFinite(b)) return { ok: false, error: 'SN 시작/끝은 숫자여야 합니다.' };
    if (b < a) return { ok: false, error: 'SN 끝 값이 시작 값보다 작습니다.' };
    const n = b - a + 1;
    if (n > 2000) return { ok: false, error: `한 번에 ${n}개는 너무 많습니다. 2000개 이하로 나누어 주세요.` };
    const width = pad != null ? pad : String(from).length;
    const i = state.rows.indexOf(r);
    const made = [];
    for (let v = a; v <= b; v++) {
      const c = newRow(Object.assign({}, r, {
        id: undefined, sn: String(v).padStart(width, '0'),
        status: 'pending', fileName: '', error: '',
      }));
      c.id = 'q' + (seq++);
      made.push(c);
    }
    state.rows.splice(i, 1, ...made);
    emit();
    return { ok: true, added: made.length };
  }

  /* ---------------- 붙여넣기 / 파일 ---------------- */

  function normHeader(h) {
    return String(h || '').trim().toLowerCase().replace(/[\s_()[\].-]/g, '');
  }
  function matchColumn(header) {
    const h = normHeader(header);
    for (const field in COLUMN_ALIASES) {
      for (const a of COLUMN_ALIASES[field]) {
        if (normHeader(a) === h) return field;
      }
    }
    return null;
  }

  /** 다양한 날짜 표기를 ISO(YYYY-MM-DD)로 */
  function normDate(v) {
    const s = String(v == null ? '' : v).trim();
    if (!s) return '';
    let m = s.match(/^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})$/);
    if (m) return `${m[1]}-${String(m[2]).padStart(2, '0')}-${String(m[3]).padStart(2, '0')}`;
    m = s.match(/^(\d{4})(\d{2})(\d{2})$/);
    if (m) return `${m[1]}-${m[2]}-${m[3]}`;
    m = s.match(/^(\d{2})(\d{2})(\d{2})$/);          // YYMMDD
    if (m) return `20${m[1]}-${m[2]}-${m[3]}`;
    // 엑셀 날짜 일련번호
    if (/^\d{5}$/.test(s)) {
      const d = new Date(Date.UTC(1899, 11, 30) + Number(s) * 86400000);
      if (!isNaN(d.getTime())) {
        return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}-${String(d.getUTCDate()).padStart(2, '0')}`;
      }
    }
    return s;
  }

  /**
   * 표 형태 텍스트(탭/쉼표 구분)를 행으로. 첫 줄이 머리글이면 자동 인식한다.
   * @returns {{rows:Array, mapping:object, headerDetected:boolean, error?:string}}
   */
  function parseTable(text, defaults) {
    const raw = String(text || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n').trim();
    if (!raw) return { rows: [], mapping: {}, headerDetected: false, error: '붙여넣을 내용이 없습니다.' };
    const lines = raw.split('\n').filter(l => l.trim() !== '');
    const delim = lines[0].indexOf('\t') >= 0 ? '\t' : (lines[0].indexOf(',') >= 0 ? ',' : '\t');
    const cells = lines.map(l => l.split(delim).map(c => c.trim().replace(/^"(.*)"$/, '$1')));

    // 머리글 감지
    const mapping = {};
    let headerDetected = false;
    const first = cells[0];
    const matched = first.map(matchColumn);
    if (matched.filter(Boolean).length >= Math.min(2, first.length)) {
      headerDetected = true;
      matched.forEach((f, i) => { if (f) mapping[f] = i; });
    } else {
      // 머리글이 없으면 위치 기준: 품목번호, LOT, SN, MFG, 매수
      ['item', 'lot', 'sn', 'mfg', 'copies'].forEach((f, i) => { if (i < first.length) mapping[f] = i; });
    }

    const body = headerDetected ? cells.slice(1) : cells;
    const out = [];
    for (const c of body) {
      const pick = (f) => (mapping[f] != null && c[mapping[f]] != null ? String(c[mapping[f]]).trim() : '');
      const item = pick('item');
      const lot = pick('lot');
      if (!item && !lot) continue;
      const r = Object.assign({}, defaults || {}, {
        item, lot, sn: pick('sn'),
        mfg: normDate(pick('mfg')),
        copies: Math.max(1, Number(pick('copies')) || 1),
      });
      const exp = normDate(pick('exp'));
      const months = Number(pick('months'));
      if (exp) { r.exp = exp; r.expAuto = false; }
      if (Number.isFinite(months) && months > 0) r.months = months;
      out.push(r);
    }
    if (!out.length) return { rows: [], mapping, headerDetected, error: '인식된 데이터 행이 없습니다. 품목번호 열이 있는지 확인하세요.' };
    return { rows: out, mapping, headerDetected };
  }

  /** CSV/XLSX 파일 → 행 */
  async function parseFile(file, defaults) {
    const name = String(file.name || '').toLowerCase();
    if (name.endsWith('.csv') || name.endsWith('.txt') || name.endsWith('.tsv')) {
      return parseTable(await file.text(), defaults);
    }
    const buf = await file.arrayBuffer();
    if (typeof XLSX === 'undefined') throw new Error('SheetJS를 찾을 수 없습니다.');
    const wb = XLSX.read(buf, { type: 'array' });
    const ws = wb.Sheets[wb.SheetNames[0]];
    const csv = XLSX.utils.sheet_to_csv(ws, { FS: '\t' });
    return parseTable(csv, defaults);
  }

  /** 큐를 CSV로 (템플릿 배포/재사용용) */
  function toCsv() {
    const head = ['품목번호', 'LOT', 'SN', '제조일', '유효기간개월', '유효일', '매수'];
    const esc = (v) => {
      const s = v == null ? '' : String(v);
      return /[",\n\t]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
    };
    const lines = [head.join(',')];
    for (const r of state.rows) {
      lines.push([r.item, r.lot, r.sn, r.mfg, r.expAuto ? r.months : '', r.expAuto ? '' : r.exp, r.copies]
        .map(esc).join(','));
    }
    return '\uFEFF' + lines.join('\r\n');
  }

  /* ---------------- 검증 ---------------- */

  /**
   * 큐 전체를 검증한다. 각 행에 issues/status를 채우고 요약을 돌려준다.
   * @param {object} ctx {index(품목 인덱스), objects, label, rules, dpi}
   */
  function validateAll(ctx) {
    const summary = { total: state.rows.length, errorRows: 0, warnRows: 0, okRows: 0, issues: [] };
    const dupes = new Map();
    for (const r of state.rows) {
      const key = [r.item, r.lot, r.sn].join('|');
      dupes.set(key, (dupes.get(key) || 0) + 1);
    }
    for (const r of state.rows) {
      const row = ctx.index && ctx.index.byRef ? ctx.index.byRef.get(String(r.item).trim()) : null;
      const fields = LB.data.computeFields(row, r);
      const pf = LB.exporter.preflight({
        objects: ctx.objects, label: ctx.label, fields, row,
        rules: ctx.rules, dpi: ctx.dpi,
      });
      r.issues = pf.all;
      r._fields = fields;
      r._row = row;
      if (pf.errors.length) { r.status = r.status === 'done' ? 'done' : 'error'; summary.errorRows++; }
      else if (pf.warnings.length) { if (r.status !== 'done') r.status = 'pending'; summary.warnRows++; }
      else { if (r.status !== 'done') r.status = 'pending'; summary.okRows++; }

      const key = [r.item, r.lot, r.sn].join('|');
      if (dupes.get(key) > 1) {
        r.issues.push({ level: 'warn', code: 'DUPLICATE', msg: '같은 품목·LOT·SN 조합이 큐에 중복되어 있습니다.' });
      }
      r.error = pf.errors.length ? pf.errors[0].msg : '';
    }
    // 대표 이슈 집계
    const byCode = new Map();
    for (const r of state.rows) {
      for (const i of r.issues) {
        const k = i.level + '|' + i.code;
        if (!byCode.has(k)) byCode.set(k, { level: i.level, code: i.code, msg: i.msg, count: 0 });
        byCode.get(k).count++;
      }
    }
    summary.issues = [...byCode.values()].sort((a, b) =>
      (a.level === b.level ? b.count - a.count : (a.level === 'error' ? -1 : 1)));
    emit();
    return summary;
  }

  /* ---------------- 실행 ---------------- */

  /**
   * 큐를 출력한다.
   * @param {object} ctx {editor, index, objects, label, skipErrors, onProgress, onRow}
   */
  async function run(ctx) {
    if (state.running) return { ok: false, error: '이미 실행 중입니다.' };
    const targets = state.rows.filter(r => r.status !== 'done' && (ctx.skipErrors ? r.status !== 'error' : true));
    if (!targets.length) return { ok: false, error: '출력할 행이 없습니다.' };

    state.running = true;
    state.paused = false;
    state.cancelRequested = false;
    state.progress = { index: 0, total: targets.reduce((s, r) => s + (Number(r.copies) || 1), 0) };
    emit();

    const jobs = targets.map(r => ({
      id: r.id, copies: Number(r.copies) || 1,
      fields: r._fields || LB.data.computeFields(
        ctx.index && ctx.index.byRef ? ctx.index.byRef.get(String(r.item).trim()) : null, r),
      row: r._row || null,
      objects: ctx.objects,
      _queueRow: r,
    }));

    // 행마다 다른 데이터로 그려야 하므로 exporter 에 넘기기 전에 resolver 를 바꿔 끼운다
    const editor = ctx.editor;
    const origResolver = editor.resolver;
    const origBcCtx = editor.barcodeCtx;

    const result = await LB.exporter.exportBatch(editor, jobs.map(j => {
      return {
        id: j.id, copies: j.copies, fields: j.fields, row: j.row, objects: j.objects,
      };
    }), {
      mode: ctx.mode,
      dpi: ctx.dpi,
      includeBg: ctx.includeBg,
      pattern: ctx.pattern,
      conflict: ctx.conflict,
      shouldCancel: () => state.cancelRequested,
      isPaused: () => state.paused,
      onProgress: (i, total, job, res) => {
        state.progress = { index: i, total };
        const r = state.rows.find(x => x.id === job.id);
        if (r) {
          if (res && res.ok === false) { r.status = 'error'; r.error = res.error || '출력 실패'; }
          else if (res) { r.status = 'done'; r.fileName = res.fileName || ''; }
          else r.status = 'running';
        }
        if (ctx.onProgress) ctx.onProgress(i, total, r);
        emit();
      },
      // exportBatch 가 각 job 을 그리기 직전에 부를 훅
      beforeJob: (job) => {
        editor.resolver = (t) => LB.data.resolveText(t, job.fields, job.row);
        editor.barcodeCtx = () => ({
          fields: job.fields, objects: ctx.objects,
          resolveText: (t) => LB.data.resolveText(t, job.fields, job.row),
        });
      },
    });

    editor.resolver = origResolver;
    editor.barcodeCtx = origBcCtx;

    // merged 모드에서는 개별 상태가 안 잡히므로 일괄 처리
    if (ctx.mode === 'merged' && !result.cancelled) {
      for (const r of targets) if (r.status !== 'error') { r.status = 'done'; r.fileName = result.fileName || ''; }
    }
    for (const r of state.rows) if (r.status === 'running') r.status = state.cancelRequested ? 'pending' : 'done';

    state.running = false;
    state.paused = false;
    emit();
    return Object.assign({ ok: true }, result);
  }

  function pause() { state.paused = true; emit(); }
  function resume() { state.paused = false; emit(); }
  function cancel() { state.cancelRequested = true; state.paused = false; emit(); }

  return {
    STATUS, COLUMN_ALIASES,
    get, rows, count, totalLabels, onChange,
    add, addMany, update, remove, duplicate, move, clear, removeCompleted, resetStatus,
    expandSn, parseTable, parseFile, toCsv, normDate, matchColumn,
    validateAll, run, pause, resume, cancel,
  };
})();
