/* mapper.js — 데이터 매칭 편집기
 *
 * 라벨DB를 별도 창에서 표처럼 펼쳐 놓고, 프로그램의 각 항목이
 * 어느 열을 읽을지 직접 지정한다.
 *
 * 쓰는 곳 두 가지
 *   1) 전체 매핑 편집 (mode 'map')
 *      설정 › 데이터 매칭. 규격·GTIN·제품명 … 이 각각 몇 번째 열인지 정한다.
 *   2) 열 하나 고르기 (mode 'pick')
 *      객체를 편집하다 "이 값은 DB의 어느 열인가"를 그 자리에서 고른다.
 *      고른 열은 텍스트에는 {@열} 로, 이미지 슬롯·바코드에는 소스로 꽂힌다.
 */
window.LB = window.LB || {};

LB.mapper = (() => {
  'use strict';

  const U = () => LB.ui;
  const PREVIEW_ROWS = 6;

  let ctx = null;        // { rows, header, colCount, keyCol, sampleRow }
  let draft = {};        // 편집 중인 매핑
  let draftKey = 'H';
  let selectedField = null;
  let onPick = null;     // 'pick' 모드 콜백
  let mode = 'map';
  let els = {};

  /** app.js 가 DB를 로딩할 때마다 알려 준다 */
  function setSource(src) { ctx = src || null; }
  function hasSource() { return !!(ctx && ctx.rows && ctx.rows.length); }
  /** 화면에서 고른 품목이 바뀌면 미리보기 첫 줄도 따라간다 */
  function setSampleRow(r) { if (ctx && r) ctx.sampleRow = r; }

  /* ---------------- 열 그리드 ---------------- */

  function buildGrid() {
    const wrap = document.createElement('div');
    wrap.className = 'map-grid-wrap scroll-thin';
    if (!hasSource()) {
      wrap.innerHTML = '<div class="insp-empty">라벨DB를 먼저 불러오세요.<br>'
        + '설정 › 폴더 경로에서 DB를 지정하거나 샘플 데이터를 불러올 수 있습니다.</div>';
      return wrap;
    }
    const table = document.createElement('table');
    table.className = 'map-grid';
    const n = Math.min(ctx.colCount || 55, 200);

    // 1행: 열 문자
    const trCol = document.createElement('tr');
    trCol.className = 'r-col';
    // 2행: DB 머리글
    const trHead = document.createElement('tr');
    trHead.className = 'r-head';

    const corner1 = document.createElement('th'); corner1.className = 'corner'; corner1.textContent = '열';
    const corner2 = document.createElement('th'); corner2.className = 'corner'; corner2.textContent = '머리글';
    trCol.appendChild(corner1); trHead.appendChild(corner2);

    for (let i = 0; i < n; i++) {
      const cn = LB.data.colName(i);
      const th = document.createElement('th');
      th.className = 'colh';
      th.dataset.col = cn;
      th.textContent = cn;
      th.title = `${cn}열 — 클릭하면 지정합니다`;
      th.onclick = () => assign(cn);
      trCol.appendChild(th);

      const hd = document.createElement('th');
      hd.className = 'headcell';
      hd.dataset.col = cn;
      const ht = ctx.header ? String(ctx.header[cn] || '') : '';
      hd.textContent = ht;
      hd.title = ht || `${cn}열 (머리글 없음)`;
      hd.onclick = () => assign(cn);
      trHead.appendChild(hd);
    }
    table.append(trCol, trHead);

    // 데이터 행 — 현재 선택 품목을 맨 위에
    const sample = [];
    if (ctx.sampleRow) sample.push(ctx.sampleRow);
    for (const r of ctx.rows) {
      if (sample.length >= PREVIEW_ROWS) break;
      if (r !== ctx.sampleRow) sample.push(r);
    }
    for (let ri = 0; ri < sample.length; ri++) {
      const r = sample[ri];
      const tr = document.createElement('tr');
      if (ri === 0 && ctx.sampleRow) tr.className = 'r-sample';
      const lab = document.createElement('th');
      lab.className = 'rowh';
      lab.textContent = ri === 0 && ctx.sampleRow ? '선택 품목' : `예시 ${ri + (ctx.sampleRow ? 0 : 1)}`;
      tr.appendChild(lab);
      for (let i = 0; i < n; i++) {
        const cn = LB.data.colName(i);
        const td = document.createElement('td');
        td.dataset.col = cn;
        const v = String(r[cn] == null ? '' : r[cn]);
        td.textContent = v;
        if (v) td.title = v;
        td.onclick = () => assign(cn);
        tr.appendChild(td);
      }
      table.appendChild(tr);
    }
    wrap.appendChild(table);
    return wrap;
  }

  /** 열 하이라이트 갱신 — 지정된 열/선택 필드의 열을 표시 */
  function paintGrid() {
    if (!els.grid) return;
    const used = new Map();           // 열문자 → 필드 목록
    for (const k in draft) {
      const c = draft[k];
      if (!c) continue;
      if (!used.has(c)) used.set(c, []);
      used.get(c).push(k);
    }
    const cur = selectedField ? draft[selectedField] : null;
    els.grid.querySelectorAll('[data-col]').forEach(el => {
      const c = el.dataset.col;
      el.classList.toggle('is-used', used.has(c));
      el.classList.toggle('is-current', !!cur && c === cur);
      el.classList.toggle('is-key', c === draftKey);
      if (el.classList.contains('colh')) {
        const f = used.get(c);
        el.title = (c === draftKey ? '품목번호 조회 키\n' : '')
          + (f ? f.map(k => LB.data.FIELD_LABELS[k] || k).join(', ') : `${c}열`);
      }
    });
    // 현재 열로 가로 스크롤
    if (cur) {
      const t = els.grid.querySelector(`.colh[data-col="${cur}"]`);
      if (t && t.scrollIntoView) t.scrollIntoView({ block: 'nearest', inline: 'center' });
    }
  }

  /** 열 지정 */
  function assign(colName) {
    if (mode === 'pick') {
      if (onPick) onPick(colName, ctx && ctx.sampleRow ? String(ctx.sampleRow[colName] || '') : '');
      return;
    }
    if (!selectedField) {
      U().toast('왼쪽에서 항목을 먼저 고르세요.', 'warn');
      return;
    }
    if (selectedField === '__KEY__') { draftKey = colName; }
    else draft[selectedField] = colName;
    refreshFields();
    paintGrid();
  }

  /* ---------------- 필드 목록 ---------------- */

  function buildFields() {
    const box = document.createElement('div');
    box.className = 'map-fields scroll-thin';

    // 조회 키
    const keyG = document.createElement('div');
    keyG.className = 'map-group';
    keyG.innerHTML = '<div class="map-group-t">조회 키</div>';
    keyG.appendChild(fieldRow('__KEY__', '품목번호 (조회 키)', true));
    box.appendChild(keyG);

    for (const g of LB.data.FIELD_GROUPS) {
      const gd = document.createElement('div');
      gd.className = 'map-group';
      const t = document.createElement('div');
      t.className = 'map-group-t';
      t.textContent = g.g;
      gd.appendChild(t);
      for (const k of g.keys) gd.appendChild(fieldRow(k, LB.data.FIELD_LABELS[k] || k));
      box.appendChild(gd);
    }
    return box;
  }

  function fieldRow(key, label, isKey) {
    const row = document.createElement('div');
    row.className = 'map-row';
    row.dataset.field = key;

    const nm = document.createElement('span');
    nm.className = 'map-name';
    nm.textContent = label;

    const col = document.createElement('input');
    col.className = 'map-col';
    col.type = 'text';
    col.maxLength = 3;
    col.placeholder = '—';
    col.value = isKey ? draftKey : (draft[key] || '');
    col.oninput = () => {
      const v = col.value.trim().toUpperCase().replace(/[^A-Z]/g, '');
      col.value = v;
      if (isKey) draftKey = v || 'H';
      else draft[key] = v;
      updateRowValue(row, key, isKey);
      paintGrid();
    };
    col.onfocus = () => selectField(key);

    const val = document.createElement('span');
    val.className = 'map-val';

    row.append(nm, col, val);
    row.onclick = (e) => { if (e.target !== col) { selectField(key); col.focus(); } };
    updateRowValue(row, key, isKey);
    return row;
  }

  function updateRowValue(row, key, isKey) {
    const val = row.querySelector('.map-val');
    const c = isKey ? draftKey : draft[key];
    if (!c) { val.textContent = '(사용 안 함)'; val.className = 'map-val none'; return; }
    const r = ctx && ctx.sampleRow;
    const v = r ? String(r[c] == null ? '' : r[c]) : '';
    val.textContent = v || '(값 없음)';
    val.className = 'map-val' + (v ? '' : ' none');
    val.title = v;
  }

  function selectField(key) {
    selectedField = key;
    if (els.fields) {
      els.fields.querySelectorAll('.map-row').forEach(r =>
        r.classList.toggle('sel', r.dataset.field === key));
    }
    paintGrid();
  }

  function refreshFields() {
    if (!els.fields) return;
    els.fields.querySelectorAll('.map-row').forEach(row => {
      const k = row.dataset.field;
      const isKey = k === '__KEY__';
      const inp = row.querySelector('.map-col');
      const want = isKey ? draftKey : (draft[k] || '');
      if (inp.value !== want) inp.value = want;
      updateRowValue(row, k, isKey);
    });
  }

  /* ---------------- 열기 ---------------- */

  /**
   * 전체 매핑 편집
   * @param {object} opt {sampleRow, onSaved}
   */
  async function openEditor(opt = {}) {
    mode = 'map';
    onPick = null;
    selectedField = null;
    draft = Object.assign({}, LB.data.FIELD_COLS);
    draftKey = LB.data.keyCol();
    if (ctx) ctx.sampleRow = opt.sampleRow || ctx.sampleRow || (ctx.rows && ctx.rows[0]) || null;

    const body = document.createElement('div');
    const info = document.createElement('div');
    info.className = 'hint';
    info.style.marginBottom = '8px';
    info.innerHTML = hasSource()
      ? `왼쪽에서 항목을 고른 뒤 오른쪽 표에서 <b>열을 클릭</b>하면 지정됩니다. 열 문자를 직접 입력해도 됩니다.`
      : '라벨DB를 먼저 불러오면 실제 값을 보면서 지정할 수 있습니다.';
    body.appendChild(info);

    const split = document.createElement('div');
    split.className = 'map-split';
    els.fields = buildFields();
    els.grid = buildGrid();
    split.append(els.fields, els.grid);
    body.appendChild(split);
    // 그리드 안에서 좌표 표시
    const foot = document.createElement('div');
    foot.className = 'hint';
    foot.style.marginTop = '6px';
    foot.textContent = hasSource()
      ? `시트 "${ctx.sheet || ''}" · ${ctx.colCount}개 열 · ${ctx.rows.length.toLocaleString()}행`
      : '';
    body.appendChild(foot);

    const res = await U().modal({
      title: `데이터 매칭 편집기 — ${LB.data.profileDef().n}`, body, wide: true, defaultValue: null, noAutofocus: true,
      onMount: () => { setTimeout(() => { selectField('__KEY__'); paintGrid(); }, 30); },
      buttons: [
        { label: '기본값으로', value: 'reset' },
        { label: '취소', value: null },
        { label: '저장', value: 'save', primary: true },
      ],
    });

    if (res === 'reset') {
      LB.data.resetFieldMap();
      LB.data.setKeyCol('H');
      await persist();
      if (opt.onSaved) opt.onSaved({ reset: true });
      U().toast('열 매칭을 기본값으로 되돌렸습니다.', 'ok');
      return true;
    }
    if (res === 'save') {
      LB.data.applyFieldMap(draft);
      LB.data.setKeyCol(draftKey);
      await persist();
      if (opt.onSaved) opt.onSaved({ keyChanged: draftKey !== 'H' });
      U().toast('열 매칭을 저장했습니다.', 'ok');
      return true;
    }
    return false;
  }

  /** 열 매칭은 DB 프로필(일반 / BSC)마다 따로 저장한다 */
  async function persist() {
    const pf = LB.data.profile();
    LB.settings.put(`data.profiles.${pf}.fieldMap`, LB.data.fieldMapDiff());
    LB.settings.put(`data.profiles.${pf}.keyCol`, LB.data.keyCol());
  }

  /**
   * 열 하나만 고르기 (객체 편집 중 호출)
   * @param {object} opt {title, hint, sampleRow, current}
   * @returns {Promise<{col:string, value:string}|null>}
   */
  function pickColumn(opt = {}) {
    mode = 'pick';
    selectedField = null;
    if (ctx) ctx.sampleRow = opt.sampleRow || ctx.sampleRow || (ctx.rows && ctx.rows[0]) || null;
    draft = Object.assign({}, LB.data.FIELD_COLS);
    draftKey = LB.data.keyCol();

    return new Promise((resolve) => {
      let picked = null;
      const body = document.createElement('div');
      const info = document.createElement('div');
      info.className = 'hint';
      info.style.marginBottom = '8px';
      info.textContent = opt.hint || '표에서 열을 클릭하면 그 열이 지정됩니다.';
      body.appendChild(info);

      const chosen = document.createElement('div');
      chosen.className = 'map-chosen';
      chosen.textContent = '아직 고르지 않았습니다.';
      body.appendChild(chosen);

      els.fields = null;
      els.grid = buildGrid();
      body.appendChild(els.grid);

      onPick = (col, value) => {
        picked = { col, value };
        chosen.innerHTML = '';
        const b = document.createElement('b');
        b.textContent = `${col}열`;
        const hd = ctx && ctx.header ? String(ctx.header[col] || '') : '';
        const s = document.createElement('span');
        s.textContent = (hd ? ` — ${hd}` : '') + (value ? `  ▸ ${value}` : '  ▸ (값 없음)');
        chosen.append(b, s);
        chosen.classList.add('ok');
        els.grid.querySelectorAll('[data-col]').forEach(el =>
          el.classList.toggle('is-current', el.dataset.col === col));
      };
      if (opt.current) setTimeout(() => onPick(opt.current, ctx && ctx.sampleRow ? String(ctx.sampleRow[opt.current] || '') : ''), 20);

      U().modal({
        title: opt.title || 'DB 열 고르기', body, wide: true, defaultValue: null, noAutofocus: true,
        buttons: [
          { label: '취소', value: null },
          { label: '이 열 사용', value: 'ok', primary: true },
        ],
      }).then(r => {
        mode = 'map'; onPick = null;
        resolve(r === 'ok' && picked ? picked : null);
      });
    });
  }

  return { setSource, setSampleRow, hasSource, openEditor, pickColumn };
})();
