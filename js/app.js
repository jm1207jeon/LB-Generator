/* app.js — 전체 연결
 *
 * 화면 구성 (모드 없는 단일 워크스페이스)
 *   좌측 레일   작업 입력 → DB 참조값 → 출력 전 점검 → 출력 버튼  (위에서 아래가 곧 작업 순서)
 *   중앙        캔버스 (입력하면 즉시 반영되는 실제 미리보기)
 *   우측        선택 객체 속성 / 객체 트리
 *   하단        연속 작업 큐 (비어 있으면 접힌 상태 = 1장 작업)
 *
 * 단일 작업과 연속 작업의 경계를 두지 않는다.
 * 큐가 비어 있으면 [PDF 출력]은 1장, 행이 쌓이면 그 건수를 출력한다.
 */
(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const U = () => LB.ui;

  /* ================= 상태 ================= */
  const S = {
    dbMeta: null,
    rows: [],
    index: null,
    inputs: { item: '', lot: '', sn: '', mfg: '', months: 36, expAuto: true, exp: '', copies: 1 },
    label: { w: 297, h: 420, bg: '', bgInclude: true },
    objects: [],
    activeQueueId: null,
    locked: true,
    preview: false,
    dirty: false,
    templateName: '기본 A3 라벨 세트',
  };
  let fields = {};
  let row = null;
  let ed = null;
  const imgCache = new Map();          // fileName|tol|auto -> {dataUrl,w,h}

  const resolveText = (t) => LB.data.resolveText(t, fields, row);

  /* 이미지 폴더가 지정되지 않았을 때, 저장소에 동봉된 샘플 이미지로 대신한다.
   * (샘플 데이터 체험용. 실제 운영에서는 설정에서 이미지 폴더를 지정한다.) */
  let sampleImagesOk = null;      // null=미확인, true/false=확인됨
  async function readImageSmart(fileName) {
    if (LB.settings.handle('imgDir')) {
      try { return await LB.settings.readImage(fileName); }
      catch (e) { if (sampleImagesOk === false) throw e; }
    }
    if (sampleImagesOk === false) throw new Error('이미지 폴더가 지정되지 않았습니다. 설정에서 지정하세요.');
    const res = await fetch('sample/images/' + encodeURIComponent(fileName)).catch(() => null);
    if (!res || !res.ok) {
      if (sampleImagesOk === null) sampleImagesOk = false;
      throw new Error(LB.settings.handle('imgDir') ? `파일 없음: ${fileName}`
                                                  : '이미지 폴더가 지정되지 않았습니다. 설정에서 지정하세요.');
    }
    sampleImagesOk = true;
    return await res.blob();
  }

  /* ================= 객체 스키마 정규화 =================
   * 기존 템플릿(v1)의 객체에 v2 속성을 채워 넣는다. */
  function normalizeObject(o, i) {
    o.id = o.id || ('o' + (i + 1));
    if (o.visible === undefined) o.visible = true;
    if (o.locked === undefined) o.locked = false;
    if (o.type === 'text') {
      o.font = o.font || 'Arial';
      o.sizePt = o.sizePt || 8;
      o.bold = !!o.bold; o.italic = !!o.italic;
      o.letterSpacing = o.letterSpacing || 0;
      o.wordSpacing = o.wordSpacing || 0;
      if (o.kerning === undefined) o.kerning = true;
      o.lineHeight = o.lineHeight || 1.15;
      if (o.hScale == null) o.hScale = 100;
      o.align = o.align || 'left';
      o.vAlign = o.vAlign || 'top';
      if (o.wrap === undefined) o.wrap = true;
      o.autoShrink = !!o.autoShrink;
      o.color = o.color || '#000000';
    } else if (o.type === 'image') {
      o.fit = o.fit || 'center';
      o.vFit = o.vFit || 'middle';
      o.fitMode = o.fitMode || 'contain';
      o.sourceField = o.sourceField || '';
      o.fileName = o.fileName || '';
      o.dataUrl = o.dataUrl || '';
    } else if (o.type === 'barcode') {
      o.symbology = o.symbology || 'gs1datamatrix';
      o.source = o.source || 'field';
      o.binding = o.binding || 'UDI_FULL';
      o.expression = o.expression || '';
      o.fit = o.fit || 'center';
      o.vFit = o.vFit || 'middle';
      o.fitMode = o.fitMode || 'module';
      o.humanReadable = !!o.humanReadable;
    }
    return o;
  }
  function normalizeAll() { S.objects.forEach(normalizeObject); }

  /* ================= 필드 / 갱신 ================= */
  function recompute() {
    row = S.index && S.index.byRef ? S.index.byRef.get(String(S.inputs.item).trim()) || null : null;
    fields = LB.data.computeFields(row, S.inputs);
    if (S.inputs.expAuto) S.inputs.exp = fields.EXP;
  }

  let refreshTimer = null;
  function refresh(opts = {}) {
    recompute();
    renderRefTable();
    LB.inspector.setContext({ fields, row, resolveText });
    ed.render();
    if (!opts.skipImages) loadSlotImages(false);
    renderChecks();
    LB.inspector.refresh();
    updatePrintButton();
    scheduleSave();
  }
  function refreshSoon(opts) {
    clearTimeout(refreshTimer);
    refreshTimer = setTimeout(() => refresh(opts), 90);
  }

  /* ================= DB ================= */
  async function loadDbFromFile(file, { silent } = {}) {
    try {
      U().status(`DB 읽는 중… ${file.name}`);
      const buf = await file.arrayBuffer();
      const parsed = LB.data.parseWorkbook(buf);
      applyDb({
        fileName: file.name, sheet: parsed.sheet, count: parsed.rows.length,
        loadedAt: new Date().toISOString(), lastModified: file.lastModified,
      }, parsed.rows);
      LB.settings.markDbLoaded(file);
      await LB.store.set('db', { meta: S.dbMeta, rows: parsed.rows }).catch(() => {});
      if (!silent) U().toast(`라벨DB 로딩 완료 — ${parsed.rows.length.toLocaleString()}개 품목`, 'ok');
      U().status(`라벨DB 로딩 완료: ${file.name} · 시트 "${parsed.sheet}" · ${parsed.rows.length.toLocaleString()}개 품목`);
      return true;
    } catch (e) {
      U().status('DB 로딩 실패: ' + e.message, 'error');
      U().toast('DB 로딩 실패: ' + e.message, 'err');
      return false;
    }
  }

  function applyDb(meta, rows) {
    S.dbMeta = meta;
    S.rows = rows;
    S.index = LB.data.buildIndex(rows);
    updateChips();
    refresh();
    // 온보딩이 떠 있으면 진행 상태를 반영하고, 다 끝났으면 닫는다
    const ob = $('onboard');
    if (ob) {
      if (S.dbMeta && (LB.settings.handle('imgDir') || sampleImagesOk)) {
        ob.remove();
        LB.settings.put('ui.onboardingDone', true);
      } else showOnboard();
    }
  }

  /** 설정된 DB 폴더에서 자동 로딩 (변경 없으면 캐시 사용) */
  async function autoLoadDb() {
    if (!LB.settings.value('paths.dbAutoLoad', true)) return;
    const got = await LB.settings.readDbFile().catch(() => null);
    if (got) {
      if (!got.changed && S.dbMeta) { U().status('라벨DB 변경 없음 — 캐시 사용'); return; }
      const ok = await loadDbFromFile(got.file, { silent: !got.changed });
      if (ok && got.changed && S.dbMeta) {
        U().toast('라벨DB가 갱신되어 다시 읽었습니다.', 'ok');
      }
      return;
    }
    const cached = await LB.store.get('db').catch(() => null);
    if (cached && cached.rows) {
      applyDb(cached.meta, cached.rows);
      U().status(`라벨DB (저장된 사본): ${cached.meta.fileName} · ${cached.meta.count.toLocaleString()}개 품목`);
    }
  }

  /* ================= 품목 검색 ================= */
  let popIndex = -1;
  function showItemPop() {
    const pop = $('itemPop');
    const q = $('inpItem').value.trim();
    if (!S.index) {
      pop.innerHTML = '<div class="none">라벨DB를 먼저 불러오세요.<br>상단 ⚙ 설정에서 DB 폴더를 지정합니다.</div>';
      pop.hidden = false;
      return;
    }
    const list = LB.data.searchProducts(S.index, q, 40);
    if (!list.length) {
      pop.innerHTML = `<div class="none">"${q}"에 해당하는 품목이 없습니다.</div>`;
      pop.hidden = false;
      return;
    }
    LB.ui.clear(pop);
    list.forEach((it, i) => {
      const d = document.createElement('div');
      d.className = 'opt' + (i === popIndex ? ' sel' : '');
      const b = document.createElement('b'); b.textContent = it.key;
      const s = document.createElement('span');
      s.textContent = [it.ref, it.name].filter(Boolean).join(' · ') || '(정보 없음)';
      d.append(b, s);
      d.onmousedown = (e) => { e.preventDefault(); pickItem(it.key); };
      pop.appendChild(d);
    });
    pop.hidden = false;
  }
  function hideItemPop() { $('itemPop').hidden = true; popIndex = -1; }

  function pickItem(key) {
    $('inpItem').value = key;
    S.inputs.item = key;
    hideItemPop();
    refresh();
    $('inpLot').focus();
    $('inpLot').select();
    const r = S.index && S.index.byRef.get(key);
    U().status(r ? `${key} 조회 완료 · ${fields.PRODUCT || ''}` : `${key} — 라벨DB에 없는 품목입니다`, r ? 'info' : 'warn');
  }

  /* ================= DB 참조값 표 ================= */
  const REF_ROWS = [
    ['제품명', 'PRODUCT'], ['상단 문구', 'MDR'], ['규격 REF', 'REF'], ['GTIN', 'GTIN'],
    ['스텐트', null, (f) => [f.STENT_OD, f.STENT_LEN].filter(Boolean).join(' × ')],
    ['딜리버리', null, (f) => [f.DD_FR, f.DD_MM && `(${f.DD_MM})`, f.DD_LEN].filter(Boolean).join(' ')],
    ['가이드와이어', null, (f) => [f.GW_INCH, f.GW_MM && `(${f.GW_MM})`].filter(Boolean).join(' ')],
    ['Cover', 'COVER'], ['LIFE TIME', 'LIFETIME'],
  ];
  function renderRefTable() {
    const tb = $('tblRef');
    LB.ui.clear(tb);
    for (const [label, key, fn] of REF_ROWS) {
      const v = fn ? fn(fields) : (fields[key] || '');
      const tr = document.createElement('tr');
      const a = document.createElement('td'); a.textContent = label;
      const b = document.createElement('td');
      b.textContent = v || '—';
      if (!v) b.className = 'empty';
      tr.append(a, b);
      tb.appendChild(tr);
    }
    $('udiFull').textContent = fields.UDI_FULL || '—';
  }

  /* ================= 출력 전 점검 ================= */
  let lastPreflight = { errors: [], warnings: [], all: [] };
  function renderChecks() {
    const pf = LB.exporter.preflight({
      objects: S.objects, label: S.label, fields, row,
      rules: LB.settings.value('validation', {}), dpi: LB.settings.value('output.dpi', 600),
    });
    lastPreflight = pf;

    // 객체별 이슈를 편집기/트리에 전달
    ed.issues = new Map();
    for (const i of pf.all) {
      if (!i.objId) continue;
      if (!ed.issues.has(i.objId)) ed.issues.set(i.objId, []);
      ed.issues.get(i.objId).push(i);
    }

    const box = $('checks');
    LB.ui.clear(box);
    const grouped = new Map();
    for (const i of pf.all) {
      const k = i.level + '|' + i.code;
      if (!grouped.has(k)) grouped.set(k, { level: i.level, msg: i.msg, n: 0 });
      grouped.get(k).n++;
    }
    const items = [...grouped.values()];
    if (!items.length) {
      const d = document.createElement('div');
      d.className = 'check-item ok';
      d.innerHTML = '<span class="ic">✓</span><span class="msg">모든 점검을 통과했습니다.</span>';
      box.appendChild(d);
    } else {
      items.sort((a, b) => (a.level === b.level ? 0 : a.level === 'error' ? -1 : 1));
      for (const it of items.slice(0, 9)) {
        const d = document.createElement('div');
        d.className = 'check-item ' + (it.level === 'error' ? 'err' : 'warn');
        const ic = document.createElement('span'); ic.className = 'ic'; ic.textContent = it.level === 'error' ? '✕' : '⚠';
        const m = document.createElement('span'); m.className = 'msg';
        m.textContent = it.msg + (it.n > 1 ? ` (${it.n}건)` : '');
        d.append(ic, m);
        box.appendChild(d);
      }
      if (items.length > 9) {
        const d = document.createElement('div');
        d.className = 'check-item'; d.innerHTML = `<span class="ic">…</span><span class="msg">그 외 ${items.length - 9}종</span>`;
        box.appendChild(d);
      }
    }
    const sum = $('checkSummary');
    sum.textContent = pf.errors.length ? `오류 ${pf.errors.length}` : (pf.warnings.length ? `경고 ${pf.warnings.length}` : '통과');
    sum.style.color = pf.errors.length ? 'var(--fail)' : (pf.warnings.length ? 'var(--warn)' : 'var(--pass)');
  }

  function updatePrintButton() {
    const n = LB.batch.count();
    const total = LB.batch.totalLabels();
    const btn = $('btnPrint');
    const blocked = lastPreflight.errors.length > 0 && n === 0;
    const isZebra = LB.settings.value('output.target', 'pdf') === 'zebra';
    const what = isZebra ? 'ZEBRA 출력' : 'PDF 출력';
    btn.textContent = n ? `큐 ${total}장 ${isZebra ? 'ZEBRA 출력' : '출력'}` : what;
    btn.disabled = blocked || LB.batch.get().running;
    const hint = $('printHint');
    if (LB.batch.get().running) hint.textContent = '출력 중…';
    else if (blocked) { hint.textContent = '오류를 해결해야 출력할 수 있습니다'; hint.className = 'hint err'; }
    else if (n) { hint.textContent = `큐 ${n}행 · 총 ${total}장`; hint.className = 'hint'; }
    else { hint.textContent = LB.settings.handle('outDir') ? `→ ${LB.settings.dirName('outDir')}` : '저장 폴더 미지정 → 다운로드'; hint.className = 'hint'; }
  }

  /* ================= 이미지 슬롯 ================= */
  async function loadSlotImages(force) {
    const tol = LB.settings.value('imaging.tolerance', 30);
    const auto = LB.settings.value('imaging.autoTransparent', true);
    let changed = false;
    for (const o of S.objects) {
      if (o.type !== 'image' || !o.sourceField) continue;
      const fileName = fields[o.sourceField] || '';
      if (!force && o.fileName === fileName && (o.dataUrl || !fileName)) continue;
      o.fileName = fileName;
      o.error = '';
      if (!fileName) { o.dataUrl = ''; changed = true; continue; }
      const key = `${fileName}|${tol}|${auto}`;
      try {
        let entry = imgCache.get(key);
        if (!entry) {
          const file = await readImageSmart(fileName);
          entry = await LB.imaging.processBlob(file, { autoTransparent: auto, tolerance: tol });
          imgCache.set(key, entry);
          if (imgCache.size > 150) imgCache.delete(imgCache.keys().next().value);
        }
        o.dataUrl = entry.dataUrl;
      } catch (e) {
        o.dataUrl = '';
        o.error = e.message;
      }
      changed = true;
    }
    if (changed) { ed.render(); renderChecks(); LB.inspector.refresh(); scheduleSave(); }
  }

  /* ================= 저장 / 복원 ================= */
  let saveTimer = null;
  function scheduleSave() {
    S.dirty = true;
    $('statusSaved').textContent = '변경됨';
    $('statusSaved').classList.add('dirty');
    clearTimeout(saveTimer);
    saveTimer = setTimeout(async () => {
      try {
        await LB.store.set('template', { label: S.label, objects: S.objects, name: S.templateName });
        await LB.store.set('session', { inputs: S.inputs, queue: LB.batch.rows(), locked: S.locked });
        S.dirty = false;
        const t = new Date();
        $('statusSaved').textContent = `저장됨 ${String(t.getHours()).padStart(2, '0')}:${String(t.getMinutes()).padStart(2, '0')}`;
        $('statusSaved').classList.remove('dirty');
      } catch (_) {}
    }, 600);
  }

  async function restore() {
    await LB.settings.load();
    applyUiSettings();

    const tpl = await LB.store.get('template').catch(() => null);
    if (tpl && Array.isArray(tpl.objects) && tpl.objects.length) {
      Object.assign(S.label, tpl.label || {});
      S.objects.length = 0;
      for (const o of tpl.objects) S.objects.push(o);
      S.templateName = tpl.name || S.templateName;
    } else {
      applyDefaultTemplate(false);
    }
    normalizeAll();

    const ses = await LB.store.get('session').catch(() => null);
    if (ses) {
      Object.assign(S.inputs, ses.inputs || {});
      if (Array.isArray(ses.queue) && ses.queue.length) LB.batch.addMany(ses.queue.map(r => Object.assign({}, r, { status: r.status === 'running' ? 'pending' : r.status })));
      if (ses.locked !== undefined) S.locked = ses.locked;
    }
    if (!S.inputs.mfg) S.inputs.mfg = LB.data.fmtISO(new Date());

    syncInputsToUi();
    setLocked(S.locked, true);
    updateChips();

    await autoLoadDb();
    refresh();
    ed.zoomFit();
    await checkReconnect();
    maybeOnboard();
  }

  function applyUiSettings() {
    const th = LB.settings.value('ui.theme', 'system');
    document.documentElement.setAttribute('data-theme', th === 'system' ? '' : th);
    ed && (ed.showRulers = LB.settings.value('ui.showRulers', true));
    ed && (ed.showGrid = LB.settings.value('ui.showGrid', false));
    ed && (ed.gridMm = LB.settings.value('ui.gridMm', 5));
    ed && (ed.snapEnabled = LB.settings.value('ui.snap', true));
    ed && (ed.snapPx = LB.settings.value('ui.snapPx', 6));
    if (ed) ed.linkMode = LB.settings.value('ui.linkMode', 'selection');
    const lb = $('btnLink');
    if (lb) lb.classList.toggle('on', LB.settings.value('ui.linkMode', 'selection') === 'all');
    const tg = $('selTarget');
    if (tg) tg.value = LB.settings.value('output.target', 'pdf');
    $('btnSnap').classList.toggle('on', LB.settings.value('ui.snap', true));
    $('btnGrid').classList.toggle('on', LB.settings.value('ui.showGrid', false));
    $('btnRuler').classList.toggle('on', LB.settings.value('ui.showRulers', true));
    $('selQueueMode').value = LB.settings.value('output.mode', 'separate');
  }

  function syncInputsToUi() {
    $('inpItem').value = S.inputs.item || '';
    $('inpLot').value = S.inputs.lot || '';
    $('inpSn').value = S.inputs.sn || '';
    $('inpMfg').value = S.inputs.mfg || '';
    $('inpMonths').value = S.inputs.months || 36;
    $('chkExpAuto').checked = S.inputs.expAuto !== false;
    $('inpExp').disabled = S.inputs.expAuto !== false;
    $('inpExp').value = S.inputs.exp || '';
    $('inpCopies').value = S.inputs.copies || 1;
    $('inpLabelW').value = S.label.w;
    $('inpLabelH').value = S.label.h;
  }

  /* ================= 템플릿 ================= */
  function applyDefaultTemplate(confirmFirst = true) {
    const go = () => {
      const tpl = JSON.parse(JSON.stringify(LB.DEFAULT_TEMPLATE));
      S.label.w = tpl.label.w; S.label.h = tpl.label.h;
      S.label.bg = (tpl.useTemplateBg && LB.TEMPLATE_BG) ? LB.TEMPLATE_BG : '';
      S.label.bgInclude = true;
      S.objects.length = 0;
      for (const o of tpl.objects) S.objects.push(o);
      normalizeAll();
      S.templateName = '기본 A3 라벨 세트';
      ed.select([]);
      ed.resetHistory();
      syncInputsToUi();
      refresh();
      ed.zoomFit();
      U().status('기본 서식을 적용했습니다.');
    };
    if (!confirmFirst || !S.objects.length) { go(); return Promise.resolve(true); }
    return U().confirm('현재 레이아웃을 기본 서식으로 되돌릴까요?', {
      title: '서식 되돌리기', danger: true, okLabel: '되돌리기',
      detail: '현재 배치한 객체는 사라집니다. 되돌린 뒤 Ctrl+Z로 취소할 수 있습니다.',
    }).then(ok => { if (ok) go(); return ok; });
  }

  async function refreshTemplateList() {
    const list = (await LB.store.get('templates').catch(() => null)) || [];
    const sel = $('selTemplate');
    LB.ui.clear(sel);
    const def = document.createElement('option');
    def.value = '__default__'; def.textContent = '기본 A3 라벨 세트';
    sel.appendChild(def);
    for (const t of list) {
      const o = document.createElement('option');
      o.value = t.name; o.textContent = t.name;
      sel.appendChild(o);
    }
    sel.value = list.some(t => t.name === S.templateName) ? S.templateName : '__default__';
  }

  async function saveTemplate() {
    const name = await U().prompt('서식 이름을 입력하세요.', {
      title: '서식 저장', value: S.templateName === '기본 A3 라벨 세트' ? '' : S.templateName,
      placeholder: '예: PML-001 A3 세트',
    });
    if (!name) return;
    const list = (await LB.store.get('templates').catch(() => null)) || [];
    const item = { name, label: JSON.parse(JSON.stringify(S.label)), objects: JSON.parse(JSON.stringify(S.objects)), at: new Date().toISOString() };
    const i = list.findIndex(t => t.name === name);
    if (i >= 0) {
      const ok = await U().confirm(`"${name}" 서식이 이미 있습니다. 덮어쓸까요?`, { title: '덮어쓰기', danger: true, okLabel: '덮어쓰기' });
      if (!ok) return;
      list[i] = item;
    } else list.push(item);
    await LB.store.set('templates', list);
    S.templateName = name;
    await refreshTemplateList();
    U().toast(`서식 "${name}" 저장됨`, 'ok');
    U().status(`서식 저장: ${name}`);
  }

  async function loadTemplate(name) {
    if (name === '__default__') { await applyDefaultTemplate(true); await refreshTemplateList(); return; }
    const list = (await LB.store.get('templates').catch(() => null)) || [];
    const t = list.find(x => x.name === name);
    if (!t) return;
    Object.assign(S.label, t.label);
    S.objects.length = 0;
    for (const o of t.objects) S.objects.push(JSON.parse(JSON.stringify(o)));
    normalizeAll();
    S.templateName = name;
    ed.select([]); ed.resetHistory();
    syncInputsToUi();
    refresh(); ed.zoomFit();
    U().status(`서식 불러옴: ${name}`);
  }

  /* ================= 잠금 / 미리보기 ================= */
  function setLocked(v, silent) {
    S.locked = v;
    document.body.classList.toggle('locked', v);
    $('btnLock').classList.toggle('on', v);
    $('btnLock').setAttribute('aria-pressed', String(v));
    $('btnLock').textContent = v ? '🔒 잠금' : '✎ 편집';
    $('statusLock').textContent = v ? '🔒 잠금' : '✎ 편집 가능';
    $('statusLock').style.color = v ? '' : 'var(--warn)';
    for (const id of ['tgrpInsert', 'tgrpArrange']) {
      $(id).querySelectorAll('button,select').forEach(b => { b.disabled = v; });
    }
    if (v) ed.select([]);
    ed.render();
    if (!silent) {
      U().status(v ? '레이아웃이 잠겼습니다. 객체를 옮길 수 없습니다.'
                   : '레이아웃 편집이 가능합니다. 변경은 서식으로 저장해야 유지됩니다.', v ? 'info' : 'warn');
    }
    scheduleSave();
  }

  function setPreview(v) {
    S.preview = v;
    $('btnPreview').classList.toggle('on', v);
    $('btnPreview').setAttribute('aria-pressed', String(v));
    ed.previewMode = v;
    ed.render();
    U().status(v ? '실물 미리보기 — 편집 보조선을 숨겼습니다. (F5로 복귀)' : '편집 화면으로 돌아왔습니다.');
  }

  /* ================= 칩 / 배너 ================= */
  function updateChips() {
    const db = $('chipDb'), img = $('chipImg'), out = $('chipOut');
    if (S.dbMeta) {
      db.className = 'chip ok';
      db.querySelector('.txt').textContent = `${S.dbMeta.fileName} · ${Number(S.dbMeta.count).toLocaleString()}`;
      db.title = `${S.dbMeta.fileName}\n시트: ${S.dbMeta.sheet}\n${Number(S.dbMeta.count).toLocaleString()}개 품목`;
    } else {
      db.className = 'chip bad';
      db.querySelector('.txt').textContent = 'DB 없음';
      db.title = '라벨DB가 없습니다. 클릭해 설정에서 지정하세요.';
    }
    const setDir = (el, kind, label) => {
      const name = LB.settings.dirName(kind);
      el.className = 'chip ' + (name ? 'ok' : 'warn');
      el.querySelector('.txt').textContent = name || label;
      el.title = name ? `${label}: ${name}` : `${label}가 지정되지 않았습니다.`;
    };
    setDir(img, 'imgDir', '이미지 폴더');
    setDir(out, 'outDir', '저장 폴더');
    if (!LB.settings.handle('outDir')) {
      out.className = 'chip';
      out.querySelector('.txt').textContent = '저장 폴더 (다운로드)';
    }
  }

  async function checkReconnect() {
    const need = await LB.settings.needsReconnect();
    const existing = $('reconnectBanner');
    if (existing) existing.remove();
    if (!need.length) return;
    const b = document.createElement('div');
    b.className = 'banner';
    b.id = 'reconnectBanner';
    const t = document.createElement('span');
    t.className = 'grow';
    t.textContent = `폴더 권한이 해제되었습니다 — ${need.map(n => n.label).join(', ')}. 브라우저를 다시 열면 보안상 권한을 다시 허용해야 합니다.`;
    const btn = document.createElement('button');
    btn.className = 'btn sm primary';
    btn.textContent = '다시 연결';
    btn.onclick = async () => {
      const res = await LB.settings.reconnectAll();
      const bad = res.filter(r => !r.ok);
      if (!bad.length) {
        b.remove();
        U().toast('폴더를 다시 연결했습니다.', 'ok');
        U().status('폴더 권한이 복구되었습니다.');
        imgCache.clear();
        await autoLoadDb();
        loadSlotImages(true);
        updateChips();
      } else {
        U().toast(`${bad.map(r => r.label).join(', ')} 연결에 실패했습니다.`, 'err');
      }
    };
    const dis = document.createElement('button');
    dis.className = 'btn sm ghost';
    dis.textContent = '나중에';
    dis.onclick = () => b.remove();
    b.append(t, btn, dis);
    $('appbar').insertAdjacentElement('afterend', b);
    document.getElementById('app').insertBefore(b, document.getElementById('rail'));
    b.style.gridColumn = '1 / -1';
  }

  /* ================= 온보딩 ================= */
  function maybeOnboard() {
    if (LB.settings.value('ui.onboardingDone', false)) return;
    const hasDb = !!S.dbMeta;
    const hasImg = !!LB.settings.handle('imgDir');
    if (hasDb && hasImg) { LB.settings.put('ui.onboardingDone', true); return; }
    showOnboard();
  }

  function showOnboard() {
    const wrap = $('stageWrap');
    const old = $('onboard');
    if (old) old.remove();
    const box = document.createElement('div');
    box.id = 'onboard';
    const steps = [
      { t: '라벨DB 지정', d: 'DB 파일이 있는 폴더와 파일명을 고릅니다.', done: !!S.dbMeta, act: 'db' },
      { t: '이미지 폴더 지정', d: '제품 그림이 있는 폴더를 고릅니다. (네트워크 드라이브 가능)', done: !!LB.settings.handle('imgDir'), act: 'img' },
      { t: 'PDF 저장 폴더 지정', d: '선택 사항 — 지정하지 않으면 다운로드 폴더로 저장됩니다.', done: !!LB.settings.handle('outDir'), act: 'out' },
    ];
    box.innerHTML = `<h3>처음 설정</h3><p class="sub">아래 3가지만 지정하면 바로 쓸 수 있습니다. 한 번 지정하면 계속 유지됩니다.</p>`;
    const ol = document.createElement('ol');
    ol.className = 'steps';
    for (const s of steps) {
      const li = document.createElement('li');
      if (s.done) li.className = 'done';
      const c = document.createElement('div');
      c.innerHTML = `<div class="t">${s.t}</div><div class="d">${s.d}</div>`;
      const b = document.createElement('button');
      b.className = 'btn sm' + (s.done ? ' ghost' : ' primary');
      b.textContent = s.done ? '변경' : '지정';
      b.onclick = () => openSettings(s.act === 'db' ? 'paths' : 'paths');
      li.append(c, b);
      ol.appendChild(li);
    }
    box.appendChild(ol);
    const foot = document.createElement('div');
    foot.style.display = 'flex'; foot.style.gap = '8px';
    const skip = document.createElement('button');
    skip.className = 'btn ghost'; skip.textContent = '나중에';
    skip.onclick = () => box.remove();
    const never = document.createElement('button');
    never.className = 'btn'; never.textContent = '다시 보지 않기';
    never.onclick = () => { LB.settings.put('ui.onboardingDone', true); box.remove(); };
    const sample = document.createElement('button');
    sample.className = 'btn primary'; sample.textContent = '샘플 데이터로 체험';
    sample.style.marginLeft = 'auto';
    sample.onclick = async () => { box.remove(); await loadSampleData(); };
    foot.append(skip, never, sample);
    box.appendChild(foot);
    wrap.appendChild(box);
  }

  /** 저장소에 포함된 샘플 DB로 즉시 체험 (폴더 지정 없이) */
  async function loadSampleData() {
    try {
      U().status('샘플 데이터를 불러오는 중…');
      const res = await fetch('sample/' + encodeURIComponent('샘플_라벨DB.xlsx'));
      if (!res.ok) throw new Error('샘플 파일을 찾을 수 없습니다.');
      const blob = await res.blob();
      const file = new File([blob], '샘플_라벨DB.xlsx', { lastModified: Date.now() });
      await loadDbFromFile(file);
      S.inputs.item = '16-0401';
      S.inputs.lot = '26041086';
      S.inputs.sn = '1';
      S.inputs.mfg = LB.data.fmtISO(new Date());
      syncInputsToUi();
      refresh();
      const ob = $('onboard'); if (ob) ob.remove();
      U().toast('샘플 데이터를 불러왔습니다. 실제 DB와 이미지 폴더는 ⚙ 설정에서 지정하세요.', 'ok', 5000);
    } catch (e) {
      U().toast('샘플 데이터를 불러오지 못했습니다: ' + e.message, 'err');
    }
  }

  /* ================= 큐 렌더링 ================= */
  const selectedRows = new Set();

  let queueRenderPending = false;
  function renderQueue() {
    const st = LB.batch.get();
    // 출력 중에는 표를 새로 만들지 않는다. 입력칸이 매번 새로 생기면
    // 포커스가 튀고 검증이 반복 실행되어 UI가 멎는다.
    if (st.running) { updateQueueStatusCells(); return; }
    renderQueueFull();
  }

  /** 출력 중 가벼운 갱신 — 상태 배지·메시지·진행률만 */
  function updateQueueStatusCells() {
    const st = LB.batch.get();
    const body = $('queueBody');
    if (body) {
      for (const tr of body.children) {
        const r = st.rows.find(x => x.id === tr.dataset.id);
        if (!r) continue;
        tr.classList.toggle('row-error', r.status === 'error');
        tr.classList.toggle('row-done', r.status === 'done');
        const tds = tr.children;
        const bd = tds[tds.length - 2] && tds[tds.length - 2].firstChild;
        if (bd) {
          const meta = LB.batch.STATUS[r.status] || LB.batch.STATUS.pending;
          bd.className = 'badge ' + meta.cls;
          bd.textContent = meta.label;
        }
        const msg = tds[tds.length - 1];
        if (msg) {
          if (r.status === 'done' && r.fileName) { msg.textContent = r.fileName; msg.style.color = 'var(--ink3)'; }
          else if (r.error) { msg.textContent = r.error; msg.style.color = ''; }
        }
      }
    }
    const done = st.rows.filter(r => r.status === 'done').length;
    const err = st.rows.filter(r => r.status === 'error').length;
    $('cntDone').hidden = !done; $('cntDone').querySelector('b').textContent = String(done);
    $('cntErr').hidden = !err; $('cntErr').querySelector('b').textContent = String(err);
    const pr = $('queueProgress'), prt = $('queueProgressText');
    pr.hidden = false;
    pr.querySelector('i').style.width = st.progress.total ? (st.progress.index / st.progress.total * 100) + '%' : '0%';
    prt.textContent = `${st.progress.index} / ${st.progress.total}`;
    $('btnQPause').hidden = false;
    $('btnQPause').textContent = st.paused ? '계속' : '일시정지';
    $('btnQCancel').hidden = false;
  }

  function renderQueueFull() {
    const st = LB.batch.get();
    const n = st.rows.length;
    $('queueEmpty').hidden = n > 0;
    $('queueTable').hidden = n === 0;
    $('cntTotal').querySelector('b').textContent = String(n);
    const done = st.rows.filter(r => r.status === 'done').length;
    const err = st.rows.filter(r => r.status === 'error').length;
    $('cntDone').hidden = !done; $('cntDone').querySelector('b').textContent = String(done);
    $('cntErr').hidden = !err; $('cntErr').querySelector('b').textContent = String(err);
    $('drawerHint').hidden = n > 0;

    const body = $('queueBody');
    LB.ui.clear(body);
    st.rows.forEach((r, i) => {
      const tr = document.createElement('tr');
      tr.dataset.id = r.id;
      if (selectedRows.has(r.id)) tr.classList.add('sel');
      if (r.id === S.activeQueueId) tr.classList.add('active');
      if (r.status === 'error') tr.classList.add('row-error');
      if (r.status === 'done') tr.classList.add('row-done');

      const tdChk = document.createElement('td');
      const chk = document.createElement('input');
      chk.type = 'checkbox'; chk.checked = selectedRows.has(r.id);
      chk.onclick = (e) => {
        e.stopPropagation();
        if (chk.checked) selectedRows.add(r.id); else selectedRows.delete(r.id);
        tr.classList.toggle('sel', chk.checked);
      };
      tdChk.appendChild(chk);

      const tdN = document.createElement('td'); tdN.textContent = String(i + 1); tdN.className = 'mini';

      const cell = (key, type, width) => {
        const td = document.createElement('td');
        if (type === 'number') td.className = 'num';
        const inp = document.createElement('input');
        inp.type = type === 'number' ? 'number' : (type === 'date' ? 'date' : 'text');
        inp.value = r[key] == null ? '' : r[key];
        if (type === 'number') { inp.min = '1'; }
        inp.onchange = () => {
          const v = type === 'number' ? Math.max(1, Number(inp.value) || 1) : inp.value;
          LB.batch.update(r.id, { [key]: v });
          revalidateQueue();
        };
        inp.onfocus = () => { S.activeQueueId = r.id; };   // 포커스만으로 전체를 다시 계산하지 않는다
        td.appendChild(inp);
        return td;
      };

      const tdMonths = document.createElement('td'); tdMonths.className = 'num';
      const mi = document.createElement('input');
      mi.type = 'number'; mi.min = '1'; mi.value = r.months || 36;
      mi.disabled = !r.expAuto;
      mi.onchange = () => { LB.batch.update(r.id, { months: Number(mi.value) || 36 }); revalidateQueue(); };
      tdMonths.appendChild(mi);

      const tdExp = document.createElement('td');
      const ei = document.createElement('input');
      ei.type = 'date';
      const f = LB.data.computeFields(S.index ? S.index.byRef.get(String(r.item).trim()) : null, r);
      ei.value = r.expAuto ? f.EXP : (r.exp || '');
      ei.disabled = !!r.expAuto;
      ei.title = r.expAuto ? '제조일과 개월 수로 자동 계산됩니다' : '';
      ei.onchange = () => { LB.batch.update(r.id, { exp: ei.value, expAuto: false }); revalidateQueue(); };
      tdExp.appendChild(ei);

      const tdSt = document.createElement('td');
      const bd = document.createElement('span');
      const meta = LB.batch.STATUS[r.status] || LB.batch.STATUS.pending;
      bd.className = 'badge ' + meta.cls;
      bd.textContent = meta.label;
      tdSt.appendChild(bd);

      const tdMsg = document.createElement('td');
      tdMsg.className = 'msg';
      if (r.status === 'done' && r.fileName) { tdMsg.textContent = r.fileName; tdMsg.style.color = 'var(--ink3)'; }
      else if (r.error) { tdMsg.textContent = r.error; tdMsg.style.color = ''; }
      else {
        const warns = (r.issues || []).filter(x => x.level === 'warn');
        if (warns.length) { tdMsg.textContent = `⚠ ${warns[0].msg}` + (warns.length > 1 ? ` 외 ${warns.length - 1}` : ''); tdMsg.style.color = 'var(--warn)'; }
        else tdMsg.textContent = '';
      }
      tdMsg.title = (r.issues || []).map(x => x.msg).join('\n');

      tr.append(tdChk, tdN, cell('item', 'text'), cell('lot', 'text'), cell('sn', 'text'),
        cell('mfg', 'date'), tdMonths, tdExp, cell('copies', 'number'), tdSt, tdMsg);
      tr.onclick = (e) => {
        if (e.target.matches('input')) return;      // 셀 편집 중에는 행을 다시 불러오지 않는다
        if (S.activeQueueId === r.id) return;
        S.activeQueueId = r.id;
        loadRowToInputs(r);
        renderQueue();
      };
      body.appendChild(tr);
    });

    // 진행률
    const pr = $('queueProgress'), prt = $('queueProgressText');
    if (st.running) {
      pr.hidden = false;
      pr.querySelector('i').style.width = st.progress.total ? (st.progress.index / st.progress.total * 100) + '%' : '0%';
      prt.textContent = `${st.progress.index} / ${st.progress.total}`;
      $('btnQPause').hidden = false;
      $('btnQPause').textContent = st.paused ? '계속' : '일시정지';
      $('btnQCancel').hidden = false;
    } else {
      pr.hidden = true; prt.textContent = '';
      $('btnQPause').hidden = true; $('btnQCancel').hidden = true;
    }
    updatePrintButton();
  }

  function loadRowToInputs(r) {
    Object.assign(S.inputs, {
      item: r.item, lot: r.lot, sn: r.sn, mfg: r.mfg,
      months: r.months, expAuto: r.expAuto, exp: r.exp, copies: r.copies,
    });
    syncInputsToUi();
    $('jobOrigin').textContent = `큐 ${LB.batch.rows().indexOf(r) + 1}행 편집 중`;
    refresh();
  }

  let revalTimer = null;
  function revalidateQueue() {
    clearTimeout(revalTimer);
    revalTimer = setTimeout(() => {
      if (!LB.batch.count()) { renderQueue(); return; }
      LB.batch.validateAll({
        index: S.index, objects: S.objects, label: S.label,
        rules: LB.settings.value('validation', {}), dpi: LB.settings.value('output.dpi', 600),
      });
      renderQueue();
    }, 160);
  }

  function openDrawer(open) {
    $('drawer').classList.toggle('open', open);
    setTimeout(() => ed && ed._resize(), 180);
  }

  /* ================= 출력 ================= */

  async function doPrint() {
    if (LB.batch.get().running) return;
    const n = LB.batch.count();
    if (LB.settings.value('output.target', 'pdf') === 'zebra') return printZebra(n);
    if (n === 0) return printSingle();
    return printQueue();
  }

  async function printSingle() {
    const pf = lastPreflight;
    if (pf.errors.length) {
      U().toast('오류를 해결해야 출력할 수 있습니다.', 'err');
      return;
    }
    if (LB.settings.value('output.confirmBeforeExport', true)) {
      const ok = await confirmDialog([{ fields, row, copies: S.inputs.copies || 1 }], pf);
      if (!ok) return;
    }
    try {
      U().status('PDF 만드는 중…');
      $('btnPrint').disabled = true;
      ed.resolver = resolveText;
      const res = await LB.exporter.exportOne(ed, {
        objects: S.objects, label: S.label, fields, row,
        includeBg: S.label.bgInclude !== false,
      });
      await LB.store.addHistory({
        item: fields.ITEM, ref: fields.REF, lot: fields.LOT, sn: fields.SN,
        mfg: fields.MFG, exp: fields.EXP, udi: fields.UDI_FULL,
        copies: 1, fileName: res.fileName, ok: true, mode: 'single',
      }).catch(() => {});
      U().status(`저장 완료: ${res.fileName}${res.saved === 'folder' ? ' → ' + LB.settings.dirName('outDir') : ' (다운로드)'}`);
      U().toast('출력 완료: ' + res.fileName, 'ok');
      $('inpLot').focus(); $('inpLot').select();
    } catch (e) {
      U().status('출력 실패: ' + e.message, 'error');
      U().toast('출력 실패: ' + e.message, 'err');
    } finally {
      $('btnPrint').disabled = false;
      updatePrintButton();
    }
  }

  async function printQueue() {
    const summary = LB.batch.validateAll({
      index: S.index, objects: S.objects, label: S.label,
      rules: LB.settings.value('validation', {}), dpi: LB.settings.value('output.dpi', 600),
    });
    renderQueue();

    const rowsAll = LB.batch.rows();
    const pending = rowsAll.filter(r => r.status !== 'done');
    if (!pending.length) { U().toast('출력할 행이 없습니다. 모두 완료 상태입니다.', 'warn'); return; }

    const errN = pending.filter(r => r.status === 'error').length;
    const okN = pending.length - errN;
    if (!okN) { U().toast('모든 행에 오류가 있어 출력할 수 없습니다.', 'err'); return; }

    const jobs = pending.filter(r => r.status !== 'error')
      .map(r => ({ fields: r._fields, row: r._row, copies: r.copies }));
    const ok = await confirmDialog(jobs, { errors: [], warnings: summary.issues.filter(i => i.level === 'warn') }, {
      queueMode: true, skipped: errN, total: jobs.reduce((s, j) => s + (j.copies || 1), 0),
    });
    if (!ok) return;

    openDrawer(true);
    const origResolver = ed.resolver, origBc = ed.barcodeCtx;
    try {
      const res = await LB.batch.run({
        editor: ed, index: S.index, objects: S.objects,
        skipErrors: true,
        mode: $('selQueueMode').value,
        includeBg: S.label.bgInclude !== false,
        onProgress: (i, total, r) => {
          if (r) U().status(`${i}/${total} 출력 중: ${r.item} LOT ${r.lot}`);
          renderQueue();
        },
      });
      renderQueue();
      if (res.cancelled) {
        U().status(`출력을 중지했습니다. ${res.done}장 완료.`, 'warn');
        U().toast(`중지됨 — ${res.done}장 출력`, 'warn');
      } else {
        U().status(`연속 출력 완료: 성공 ${res.done}장${res.failed ? `, 실패 ${res.failed}건` : ''}${errN ? `, 오류로 건너뜀 ${errN}행` : ''}`);
        U().toast(`출력 완료 — ${res.done}장${res.failed ? ` (실패 ${res.failed})` : ''}`, res.failed ? 'warn' : 'ok');
      }
    } catch (e) {
      U().status('연속 출력 실패: ' + e.message, 'error');
      U().toast('연속 출력 실패: ' + e.message, 'err');
    } finally {
      ed.resolver = origResolver; ed.barcodeCtx = origBc;
      ed.render();
      updatePrintButton();
    }
  }

  /* ================= ZEBRA 출력 ================= */

  let usbDevice = null;

  /**
   * 제브라 프린터로 보낸다. 큐가 있으면 큐 전체, 없으면 현재 1건.
   * 라벨을 프린터 해상도의 흑백 비트맵으로 만들어 ^GFA 로 전송하므로
   * 화면·PDF와 완전히 같은 그림이 찍힌다.
   */
  async function printZebra(queueCount) {
    const P = LB.settings.value('printer', {});
    const dpi = P.dpi || 203;
    const jobs = queueCount
      ? LB.batch.rows().filter(r => r.status !== 'done' && r.status !== 'error')
      : [{ _single: true }];
    if (!jobs.length) { U().toast('출력할 행이 없습니다.', 'warn'); return; }

    // 첫 건으로 ZPL을 만들어 크기/미리보기를 확인시킨다
    U().status('ZPL 만드는 중…');
    let first;
    try {
      ed.resolver = resolveText;
      first = await LB.zpl.fromEditor(ed, {
        objects: S.objects, label: S.label, includeBg: S.label.bgInclude !== false,
        dpi, threshold: P.threshold, dither: P.dither, invert: P.invert,
        darkness: P.darkness, speed: P.speed, mediaMode: P.mediaMode,
        quantity: queueCount ? 1 : (S.inputs.copies || 1),
      });
    } catch (e) {
      U().status('ZPL 생성 실패: ' + e.message, 'error');
      U().toast('ZPL 생성 실패: ' + e.message, 'err');
      return;
    }
    const issues = LB.zpl.sanityCheck(first, dpi, S.label);

    const ok = await confirmZebra(first, issues, jobs.length, P);
    if (!ok) return;

    // 전송
    const method = P.method || 'browserprint';
    let sender = null;
    try {
      if (method === 'browserprint') {
        const printers = await LB.zpl.bpPrinters();
        if (!printers.length) throw new Error('Zebra Browser Print 서비스를 찾지 못했습니다. 설치 후 실행 중인지 확인하세요.');
        const dev = printers.find(x => x.uid === P.deviceUid) || printers[0];
        sender = (data) => LB.zpl.bpSend(dev, data);
        U().status(`프린터 연결: ${dev.name}`);
      } else if (method === 'usb') {
        if (!usbDevice) {
          const list = await LB.zpl.usbList();
          usbDevice = list[0] || await LB.zpl.usbRequest();
        }
        sender = (data) => LB.zpl.usbSend(usbDevice, data);
        U().status('USB 프린터로 전송합니다.');
      } else {
        sender = null;      // 파일 저장
      }
    } catch (e) {
      U().status('프린터 연결 실패: ' + e.message, 'error');
      U().toast(e.message, 'err');
      return;
    }

    let done = 0, failed = 0;
    const parts = [];
    for (let i = 0; i < jobs.length; i++) {
      const j = jobs[i];
      try {
        let info = first;
        if (!j._single) {
          const r = j;
          const rowData = S.index ? S.index.byRef.get(String(r.item).trim()) : null;
          const f = LB.data.computeFields(rowData, r);
          ed.resolver = (t) => LB.data.resolveText(t, f, rowData);
          ed.barcodeCtx = () => ({ fields: f, objects: S.objects, resolveText: ed.resolver });
          LB.text.invalidate();
          info = await LB.zpl.fromEditor(ed, {
            objects: S.objects, label: S.label, includeBg: S.label.bgInclude !== false,
            dpi, threshold: P.threshold, dither: P.dither, invert: P.invert,
            darkness: P.darkness, speed: P.speed, mediaMode: P.mediaMode,
            quantity: r.copies || 1,
          });
          LB.batch.update(r.id, { status: 'running' });
          renderQueue();
        } else if ((S.inputs.copies || 1) > 1) {
          // 단일 건의 매수는 ^PQ 로 이미 반영되어 있다
        }
        if (sender) await sender(info.zpl);
        else parts.push(info.zpl);
        done++;
        if (!j._single) { LB.batch.update(j.id, { status: 'done', fileName: 'ZPL 전송' }); renderQueue(); }
        U().status(`${i + 1}/${jobs.length} 전송 완료`);
      } catch (e) {
        failed++;
        if (!j._single) { LB.batch.update(j.id, { status: 'error', error: e.message }); renderQueue(); }
        U().status('전송 실패: ' + e.message, 'error');
      }
      await new Promise(r => setTimeout(r, 0));
    }

    ed.resolver = resolveText;
    ed.barcodeCtx = () => ({ fields, objects: S.objects, resolveText });
    ed.render();

    if (!sender && parts.length) {
      U().downloadText(parts.join('\n'), `label_${LB.data.fmtISO(new Date())}.zpl`, 'application/octet-stream');
      U().toast(`ZPL ${parts.length}건을 파일로 저장했습니다.`, 'ok');
      U().status(`ZPL 파일 저장 완료 — ${parts.length}건`);
    } else {
      U().toast(`전송 완료 ${done}건${failed ? ` / 실패 ${failed}건` : ''}`, failed ? 'warn' : 'ok');
      U().status(`ZEBRA 출력 완료: 성공 ${done}건${failed ? `, 실패 ${failed}건` : ''}`);
    }
    updatePrintButton();
  }

  async function confirmZebra(info, issues, jobCount, P) {
    const body = document.createElement('div');
    const head = document.createElement('p');
    head.style.cssText = 'margin:0 0 8px;font-size:13.5px';
    head.innerHTML = `<b>${jobCount}건</b>을 ZEBRA 프린터로 보냅니다.`;
    body.appendChild(head);

    const grid = document.createElement('div');
    grid.className = 'confirm-grid';
    const cells = [
      ['품목', fields.ITEM], ['LOT', fields.LOT],
      ['프린터 해상도', `${P.dpi || 203} dpi`],
      ['라벨 크기', `${S.label.w}×${S.label.h} mm`],
      ['도트', info.dots],
      ['전송량', U().fmtBytes(info.bytes)],
    ];
    for (const [k, v] of cells) {
      const c = document.createElement('div');
      c.className = 'confirm-cell';
      c.innerHTML = '<div class="k"></div><div class="v"></div>';
      c.querySelector('.k').textContent = k;
      c.querySelector('.v').textContent = v || '—';
      c.querySelector('.v').style.fontSize = '13px';
      grid.appendChild(c);
    }
    body.appendChild(grid);

    if (issues.length) {
      const ul = document.createElement('ul');
      ul.className = 'issue-list';
      for (const i of issues) {
        const li = document.createElement('li');
        li.className = i.level === 'error' ? 'error' : 'warn';
        li.innerHTML = '<span class="n">⚠</span><span></span>';
        li.querySelector('span:last-child').textContent = i.msg;
        ul.appendChild(li);
      }
      body.appendChild(ul);
    }

    const det = document.createElement('details');
    det.style.marginTop = '10px';
    const sum = document.createElement('summary');
    sum.style.cssText = 'cursor:pointer;font-size:12px;color:var(--ink3)';
    sum.textContent = 'ZPL 코드 미리보기';
    const pre = document.createElement('div');
    pre.className = 'zpl-preview';
    pre.textContent = info.zpl.length > 2000
      ? info.zpl.slice(0, 900) + `\n… (전체 ${info.zpl.length.toLocaleString()}자) …\n` + info.zpl.slice(-260)
      : info.zpl;
    det.append(sum, pre);
    body.appendChild(det);

    const methodName = { browserprint: 'Zebra Browser Print', usb: 'USB 직접 연결', file: '.zpl 파일로 저장' }[P.method || 'browserprint'];
    const mrow = document.createElement('div');
    mrow.className = 'hint';
    mrow.style.marginTop = '8px';
    mrow.textContent = '전송 방법: ' + methodName + ' (설정 › 프린터에서 변경)';
    body.appendChild(mrow);

    return U().modal({
      title: 'ZEBRA 출력 확인', body, wide: true, defaultValue: false,
      buttons: [
        { label: '취소', value: false },
        { label: `${jobCount}건 보내기`, value: true, primary: true, autofocus: true },
      ],
    });
  }

  /** 출력 직전 확인 — 핵심 값을 크게 다시 보여준다 (의료기기 라벨 오류 방지) */
  async function confirmDialog(jobs, pf, opt = {}) {
    const body = document.createElement('div');
    const f = jobs[0] ? jobs[0].fields : {};

    if (opt.queueMode) {
      const h = document.createElement('p');
      h.style.margin = '0 0 4px'; h.style.fontSize = '14px';
      h.innerHTML = `<b>${jobs.length}행 · 총 ${opt.total}장</b>을 출력합니다.`;
      body.appendChild(h);
      if (opt.skipped) {
        const s = document.createElement('div');
        s.className = 'hint err';
        s.textContent = `오류가 있는 ${opt.skipped}행은 건너뜁니다.`;
        body.appendChild(s);
      }
    }

    const grid = document.createElement('div');
    grid.className = 'confirm-grid';
    const cells = opt.queueMode
      ? [['첫 품목', f.ITEM], ['규격', f.REF], ['LOT', f.LOT], ['제조일', f.MFG], ['유효일', f.EXP]]
      : [['품목번호', f.ITEM], ['규격 REF', f.REF], ['LOT', f.LOT], ['SN', f.SN || '—'],
         ['제조일', f.MFG], ['유효일', f.EXP], ['매수', String(jobs[0] ? jobs[0].copies || 1 : 1) + '장']];
    for (const [k, v] of cells) {
      const c = document.createElement('div');
      c.className = 'confirm-cell';
      c.innerHTML = `<div class="k"></div><div class="v"></div>`;
      c.querySelector('.k').textContent = k;
      c.querySelector('.v').textContent = v || '—';
      grid.appendChild(c);
    }
    const udi = document.createElement('div');
    udi.className = 'confirm-cell wide';
    udi.innerHTML = `<div class="k">UDI</div><div class="v"></div>`;
    udi.querySelector('.v').textContent = f.UDI_FULL || '—';
    grid.appendChild(udi);
    body.appendChild(grid);

    const warns = (pf.warnings || []).slice(0, 6);
    if (warns.length) {
      const ul = document.createElement('ul');
      ul.className = 'issue-list';
      for (const w of warns) {
        const li = document.createElement('li');
        li.className = 'warn';
        li.innerHTML = '<span class="n">⚠</span><span></span>';
        li.querySelector('span:last-child').textContent = w.msg + (w.count > 1 ? ` (${w.count}건)` : '');
        ul.appendChild(li);
      }
      body.appendChild(ul);
    }

    // 미리보기 썸네일
    if (!opt.queueMode) {
      try {
        ed.resolver = resolveText;
        const png = await LB.exporter.previewPng(ed, { objects: S.objects, label: S.label, includeBg: S.label.bgInclude !== false });
        const img = document.createElement('img');
        img.className = 'preview-img'; img.src = png; img.alt = '출력 미리보기';
        img.style.marginTop = '10px';
        body.appendChild(img);
      } catch (_) {}
    }

    return U().modal({
      title: opt.queueMode ? '연속 출력 확인' : '출력 확인',
      body, defaultValue: false,
      buttons: [
        { label: '취소', value: false },
        { label: opt.queueMode ? `${opt.total}장 출력` : '출력', value: true, primary: true, autofocus: true },
      ],
    });
  }

  /* ================= 설정 대화상자 ================= */
  async function openSettings(page) {
    const body = document.createElement('div');
    body.className = 'settings-grid';
    const nav = document.createElement('div'); nav.className = 'settings-nav';
    const cont = document.createElement('div');
    const PAGES = [
      ['paths', '폴더 경로'], ['output', '출력'], ['printer', 'ZEBRA 프린터'], ['imaging', '이미지'],
      ['validation', '검증 규칙'], ['ui', '화면'], ['history', '출력 이력'], ['about', '정보'],
    ];
    const pages = {};
    for (const [id, label] of PAGES) {
      const b = document.createElement('button');
      b.textContent = label;
      b.onclick = () => {
        nav.querySelectorAll('button').forEach(x => x.classList.toggle('on', x === b));
        Object.entries(pages).forEach(([k, el]) => el.classList.toggle('on', k === id));
      };
      nav.appendChild(b);
      const pg = document.createElement('div');
      pg.className = 'settings-page';
      pages[id] = pg;
      cont.appendChild(pg);
    }
    body.append(nav, cont);
    buildSettingsPages(pages);
    const initial = page && pages[page] ? page : 'paths';
    nav.querySelectorAll('button')[PAGES.findIndex(p => p[0] === initial)].click();

    await U().modal({
      title: '설정', body, wide: true, defaultValue: null, noAutofocus: true,
      buttons: [{ label: '닫기', value: null, primary: true }],
    });
    applyUiSettings();
    updateChips();
    refresh();
  }

  function buildSettingsPages(pages) {
    /* --- 폴더 경로 --- */
    const p = pages.paths;
    p.innerHTML = '<h3>폴더 경로</h3>';
    if (!LB.settings.SUPPORTED) {
      const w = document.createElement('div');
      w.className = 'banner err';
      w.style.borderRadius = '8px'; w.style.marginBottom = '10px';
      w.textContent = '이 브라우저는 폴더 지정을 지원하지 않습니다. Chrome 또는 Edge를 사용하세요.';
      p.appendChild(w);
    }
    const note = document.createElement('div');
    note.className = 'hint';
    note.style.marginBottom = '10px';
    note.innerHTML = '네트워크 폴더는 Windows에서 드라이브 문자로 연결(예: <b>Z:</b>)한 뒤 아래에서 그 드라이브를 고르면 됩니다. 브라우저 보안상 <code>\\\\서버\\공유</code> 경로를 직접 입력할 수는 없습니다.';
    p.appendChild(note);

    const mkPath = (kind, label, desc) => {
      const rowEl = document.createElement('div');
      rowEl.className = 'path-row';
      const dot = document.createElement('span'); dot.className = 'dot';
      const info = document.createElement('div'); info.className = 'info';
      const nm = document.createElement('div'); nm.className = 'nm'; nm.textContent = label;
      const pv = document.createElement('div'); pv.className = 'pv';
      info.append(nm, pv);
      const pick = document.createElement('button');
      pick.className = 'btn sm'; pick.textContent = '폴더 선택…';
      const clr = document.createElement('button');
      clr.className = 'btn sm ghost'; clr.textContent = '해제';
      const sync = async () => {
        const name = LB.settings.dirName(kind);
        pv.textContent = name || desc;
        const perm = name ? await LB.settings.permission(kind) : 'none';
        rowEl.className = 'path-row ' + (name ? (perm === 'granted' ? 'ok' : 'warn') : '');
        clr.hidden = !name;
      };
      pick.onclick = async () => {
        const r = await LB.settings.pickDir(kind);
        if (r.error) U().toast(r.error, 'err');
        else if (r.ok) {
          U().toast(`${label}: ${r.name}`, 'ok');
          if (kind === 'imgDir') { imgCache.clear(); sampleImagesOk = null; loadSlotImages(true); }
          if (kind === 'dbDir') await refreshDbFileSelect();
        }
        await sync(); updateChips();
      };
      clr.onclick = async () => { await LB.settings.clearDir(kind); await sync(); updateChips(); };
      rowEl.append(dot, info, pick, clr);
      sync();
      return rowEl;
    };

    const g1 = document.createElement('section');
    g1.className = 'gbox';
    g1.innerHTML = '<div class="legend">라벨DB</div>';
    g1.appendChild(mkPath('dbDir', '라벨DB 폴더', '지정되지 않음'));
    const fileRow = document.createElement('div');
    fileRow.className = 'row'; fileRow.style.marginTop = '8px';
    const fl = document.createElement('span'); fl.className = 'mini'; fl.textContent = 'DB 파일';
    const fsel = document.createElement('select'); fsel.id = 'setDbFile';
    const freload = document.createElement('button');
    freload.className = 'btn sm'; freload.textContent = '지금 읽기';
    fileRow.append(fl, fsel, freload);
    g1.appendChild(fileRow);
    const autoRow = document.createElement('label');
    autoRow.className = 'chk'; autoRow.style.marginTop = '6px';
    const autoChk = document.createElement('input');
    autoChk.type = 'checkbox'; autoChk.checked = LB.settings.value('paths.dbAutoLoad', true);
    autoChk.onchange = () => LB.settings.put('paths.dbAutoLoad', autoChk.checked);
    autoRow.append(autoChk, document.createTextNode(' 프로그램을 열 때 자동으로 읽기 (파일이 바뀌었으면 다시 읽음)'));
    g1.appendChild(autoRow);
    const manual = document.createElement('div');
    manual.className = 'row'; manual.style.marginTop = '8px';
    const mb = document.createElement('button');
    mb.className = 'btn sm'; mb.textContent = '파일에서 직접 불러오기…';
    const mi = document.createElement('input');
    mi.type = 'file'; mi.accept = '.xlsx,.xlsm,.xls,.csv'; mi.hidden = true;
    mb.onclick = () => mi.click();
    mi.onchange = async () => { if (mi.files[0]) await loadDbFromFile(mi.files[0]); mi.value = ''; };
    const sb = document.createElement('button');
    sb.className = 'btn sm ghost'; sb.textContent = '샘플 DB 불러오기';
    sb.onclick = () => loadSampleData();
    manual.append(mb, mi, sb);
    g1.appendChild(manual);
    p.appendChild(g1);

    async function refreshDbFileSelect() {
      const files = await LB.settings.listDbFiles();
      LB.ui.clear(fsel);
      if (!files) {
        const o = document.createElement('option');
        o.value = ''; o.textContent = '(폴더를 먼저 지정하세요)';
        fsel.appendChild(o);
        fsel.disabled = true;
        return;
      }
      fsel.disabled = false;
      const none = document.createElement('option');
      none.value = ''; none.textContent = '(선택 안 함)';
      fsel.appendChild(none);
      for (const f of files) {
        const o = document.createElement('option');
        o.value = f; o.textContent = f;
        fsel.appendChild(o);
      }
      fsel.value = LB.settings.value('paths.dbFileName', '');
    }
    fsel.onchange = () => LB.settings.put('paths.dbFileName', fsel.value);
    freload.onclick = async () => {
      const got = await LB.settings.readDbFile();
      if (!got) { U().toast('DB 폴더와 파일을 먼저 지정하세요.', 'warn'); return; }
      await loadDbFromFile(got.file);
      updateChips();
    };
    refreshDbFileSelect();

    const g2 = document.createElement('section');
    g2.className = 'gbox';
    g2.innerHTML = '<div class="legend">그 밖의 폴더</div>';
    g2.appendChild(mkPath('imgDir', '이미지 폴더', '지정되지 않음 — 제품 그림이 표시되지 않습니다'));
    g2.appendChild(mkPath('outDir', 'PDF 저장 폴더', '지정되지 않음 — 다운로드 폴더로 저장됩니다'));
    const diag = document.createElement('div');
    diag.className = 'row'; diag.style.marginTop = '8px';
    const db2 = document.createElement('button');
    db2.className = 'btn sm ghost'; db2.textContent = '이미지 폴더 점검';
    db2.onclick = async () => {
      const list = await LB.settings.listImages();
      if (!list) { U().toast('이미지 폴더가 없거나 권한이 없습니다.', 'warn'); return; }
      const needed = new Set();
      for (const o of S.objects) if (o.type === 'image' && o.sourceField) {
        for (const r of (S.rows || [])) {
          const v = r[LB.data.FIELD_COLS[o.sourceField]];
          if (v) needed.add(String(v).trim());
        }
      }
      const have = new Set(list);
      const missing = [...needed].filter(n => !have.has(n));
      U().modal({
        title: '이미지 폴더 점검',
        body: `<p>폴더의 이미지 <b>${list.length}</b>개 · DB가 참조하는 파일 <b>${needed.size}</b>개</p>` +
              (missing.length
                ? `<p class="hint err">없는 파일 ${missing.length}개</p><div class="mono" style="max-height:220px;overflow:auto;font-size:11px;background:var(--paper);padding:8px;border-radius:6px">${missing.slice(0, 200).map(m => m.replace(/[<>&]/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;' }[c]))).join('<br>')}</div>`
                : '<p class="hint ok">DB가 참조하는 파일이 모두 있습니다.</p>'),
        buttons: [{ label: '닫기', value: null, primary: true }],
      });
    };
    diag.append(db2);
    g2.appendChild(diag);
    p.appendChild(g2);

    /* --- 출력 --- */
    const o = pages.output;
    o.innerHTML = '<h3>출력</h3>';
    o.appendChild(settingRow('해상도 (DPI)', selectCtl('output.dpi',
      [[200, '200 — 초안'], [300, '300 — 권장 (라벨 인쇄 표준)'], [400, '400'], [600, '600 — 고정밀']], true)));
    o.appendChild(settingRow('래스터 형식', selectCtl('output.rasterFormat',
      [['auto', '자동 — 작은 라벨은 무손실, 큰 라벨은 고품질'], ['png', '항상 무손실 (PNG, 느림)'], ['jpeg', '항상 고품질 (JPEG, 빠름)']])));
    const rf = document.createElement('div');
    rf.className = 'hint'; rf.style.margin = '-4px 0 10px';
    rf.textContent = 'A3처럼 큰 라벨을 무손실로 만들면 한 장에 3초 이상 걸립니다. 자동으로 두면 큰 라벨만 고품질 JPEG로 만들어 약 10배 빨라집니다. (실측: 이 서식의 바코드 9종이 두 방식 모두에서 정상 판독되었습니다.)';
    o.appendChild(rf);
    const dpiWarn = document.createElement('div');
    dpiWarn.className = 'hint warn'; dpiWarn.style.margin = '-4px 0 10px';
    const updDpiWarn = () => {
      const dpi = LB.settings.value('output.dpi', 300);
      const eff = LB.exporter.effectiveDpi(S.label, dpi);
      const mp = ((S.label.w / 25.4 * eff.dpi) * (S.label.h / 25.4 * eff.dpi) / 1e6).toFixed(0);
      dpiWarn.textContent = eff.reduced
        ? `현재 용지(${S.label.w}×${S.label.h}mm)에서 ${dpi}dpi는 너무 커서 ${eff.dpi}dpi로 자동 조정됩니다 (약 ${mp}백만 화소).`
        : `현재 용지에서 약 ${mp}백만 화소로 만들어집니다.`;
      dpiWarn.className = 'hint' + (eff.reduced ? ' warn' : '');
    };
    updDpiWarn();
    o.appendChild(dpiWarn);
    o.appendChild(settingRow('출력 방식', selectCtl('output.mode', [['separate', '라벨마다 개별 PDF'], ['merged', '한 PDF에 여러 쪽']])));
    o.appendChild(settingRow('파일명 규칙', textCtl('output.pattern', '{ITEM}_{LOT}_{DATE}')));
    const phHint = document.createElement('div');
    phHint.className = 'hint';
    phHint.style.margin = '-4px 0 10px';
    phHint.textContent = '쓸 수 있는 항목: {ITEM} {REF} {LOT} {SN} {MFG} {EXP} {EXP6} {PRODUCT} {DATE} {TIME} {COPY} · 날짜 형식 지정: {EXP:YYMMDD}';
    o.appendChild(phHint);
    o.appendChild(settingRow('같은 이름이 있을 때', selectCtl('output.conflict', [['increment', '번호를 붙여 새 파일로'], ['overwrite', '덮어쓰기']])));
    o.appendChild(settingRow('배경 서식 포함', checkCtl('output.includeBg')));
    o.appendChild(settingRow('출력 전 확인 대화상자', checkCtl('output.confirmBeforeExport')));

    /* --- ZEBRA 프린터 --- */
    const pr = pages.printer;
    pr.innerHTML = '<h3>ZEBRA 프린터</h3>';
    const prNote = document.createElement('div');
    prNote.className = 'hint';
    prNote.style.marginBottom = '10px';
    prNote.innerHTML = '라벨을 프린터 해상도의 흑백 그림으로 바꿔 ZPL 그래픽 명령(^GFA)으로 보냅니다. ' +
      '화면·PDF와 <b>완전히 같은 그림</b>이 찍히며, 프린터에 한글 글꼴을 올릴 필요가 없습니다.';
    pr.appendChild(prNote);

    const gp1 = document.createElement('section');
    gp1.className = 'gbox';
    gp1.innerHTML = '<div class="legend">연결</div>';
    gp1.appendChild(settingRow('전송 방법', selectCtl('printer.method', [
      ['browserprint', 'Zebra Browser Print (권장)'],
      ['usb', 'USB 직접 연결 (WebUSB)'],
      ['file', '.zpl 파일로 저장'],
    ], false, () => refreshConn())));
    const connBox = document.createElement('div');
    connBox.className = 'hint';
    connBox.style.margin = '6px 0';
    gp1.appendChild(connBox);
    const connBtns = document.createElement('div');
    connBtns.className = 'row';
    gp1.appendChild(connBtns);
    const devSel = document.createElement('select');
    devSel.style.marginTop = '6px';
    devSel.hidden = true;
    devSel.onchange = () => LB.settings.put('printer.deviceUid', devSel.value);
    gp1.appendChild(devSel);
    pr.appendChild(gp1);

    async function refreshConn() {
      const m = LB.settings.value('printer.method', 'browserprint');
      LB.ui.clear(connBtns);
      devSel.hidden = true;
      if (m === 'browserprint') {
        connBox.textContent = '확인 중…';
        connBox.className = 'hint';
        const printers = await LB.zpl.bpPrinters();
        if (!printers.length) {
          connBox.className = 'hint warn';
          connBox.innerHTML = 'Browser Print 서비스를 찾지 못했습니다. 제브라 홈페이지에서 <b>Zebra Browser Print</b>를 내려받아 설치하고 실행한 뒤 [다시 찾기]를 누르세요.';
        } else {
          connBox.className = 'hint ok';
          connBox.textContent = `프린터 ${printers.length}대를 찾았습니다.`;
          LB.ui.clear(devSel);
          for (const d of printers) {
            const o = document.createElement('option');
            o.value = d.uid;
            o.textContent = `${d.name}${d.connection ? ' (' + d.connection + ')' : ''}`;
            devSel.appendChild(o);
          }
          devSel.value = LB.settings.value('printer.deviceUid', '') || printers[0].uid;
          devSel.hidden = false;
        }
        const again = document.createElement('button');
        again.className = 'btn sm'; again.textContent = '다시 찾기';
        again.onclick = refreshConn;
        connBtns.appendChild(again);
      } else if (m === 'usb') {
        if (!LB.zpl.usbSupported()) {
          connBox.className = 'hint err';
          connBox.textContent = '이 브라우저는 WebUSB를 지원하지 않습니다. Chrome 또는 Edge를 사용하세요.';
        } else {
          const list = await LB.zpl.usbList();
          connBox.className = 'hint' + (list.length ? ' ok' : '');
          connBox.textContent = list.length
            ? `허용된 USB 장치 ${list.length}대 — ${list.map(d => d.productName || 'Zebra').join(', ')}`
            : '아직 허용된 USB 장치가 없습니다. [USB 프린터 선택]을 눌러 장치를 허용하세요.';
          const pick = document.createElement('button');
          pick.className = 'btn sm'; pick.textContent = 'USB 프린터 선택…';
          pick.onclick = async () => {
            try { usbDevice = await LB.zpl.usbRequest(); U().toast('USB 프린터를 연결했습니다.', 'ok'); refreshConn(); }
            catch (e) { if (e && e.name !== 'NotFoundError') U().toast(e.message, 'err'); }
          };
          connBtns.appendChild(pick);
        }
      } else {
        connBox.className = 'hint';
        connBox.textContent = '출력할 때 .zpl 파일을 내려받습니다. 기존 라벨 출력 시스템이나 프린터 스풀러에 그대로 넘길 수 있습니다.';
      }
    }
    refreshConn();

    const gp2 = document.createElement('section');
    gp2.className = 'gbox';
    gp2.innerHTML = '<div class="legend">인쇄 설정</div>';
    gp2.appendChild(settingRow('프린터 해상도', selectCtl('printer.dpi',
      LB.zpl.DPI_CHOICES.map(d => [d.v, d.n]), true)));
    gp2.appendChild(settingRow('인쇄 농도 (-30~30)', numCtl('printer.darkness', -30, 30, 1)));
    gp2.appendChild(settingRow('인쇄 속도 (인치/초)', numCtl('printer.speed', 1, 14, 1)));
    gp2.appendChild(settingRow('용지 처리', selectCtl('printer.mediaMode', [
      ['', '프린터 설정 유지'], ['T', '티어오프'], ['P', '필오프'], ['C', '커터'],
    ])));
    gp2.appendChild(settingRow('180도 회전', checkCtl('printer.invert')));
    const dkHint = document.createElement('div');
    dkHint.className = 'hint';
    dkHint.style.marginTop = '4px';
    dkHint.textContent = '농도와 속도를 비워 두면 프린터에 저장된 값을 그대로 씁니다. 바코드가 흐리면 농도를 올리고 속도를 낮추세요.';
    gp2.appendChild(dkHint);
    pr.appendChild(gp2);

    const gp3 = document.createElement('section');
    gp3.className = 'gbox';
    gp3.innerHTML = '<div class="legend">흑백 변환</div>';
    gp3.appendChild(settingRow('기준값 (0~255)', numCtl('printer.threshold', 0, 255, 5)));
    gp3.appendChild(settingRow('디더링 (사진 회색조 표현)', checkCtl('printer.dither')));
    const thHint = document.createElement('div');
    thHint.className = 'hint'; thHint.style.marginTop = '4px';
    thHint.textContent = '제브라 프린터는 검정 아니면 흰색만 찍습니다. 디더링을 켜면 회색을 점 밀도로 표현하고, 끄면 기준값보다 어두운 곳만 검정으로 찍습니다. 바코드와 글자는 어느 쪽이든 또렷합니다.';
    gp3.appendChild(thHint);
    pr.appendChild(gp3);

    const gp4 = document.createElement('section');
    gp4.className = 'gbox';
    gp4.innerHTML = '<div class="legend">진단</div>';
    const diagRow = document.createElement('div');
    diagRow.className = 'row';
    diagRow.style.flexWrap = 'wrap';
    const sendRaw = async (zpl, label) => {
      const m = LB.settings.value('printer.method', 'browserprint');
      try {
        if (m === 'usb') {
          if (!usbDevice) { const l = await LB.zpl.usbList(); usbDevice = l[0] || await LB.zpl.usbRequest(); }
          await LB.zpl.usbSend(usbDevice, zpl);
        } else if (m === 'browserprint') {
          const ps = await LB.zpl.bpPrinters();
          if (!ps.length) throw new Error('Browser Print 서비스를 찾지 못했습니다.');
          const d = ps.find(x => x.uid === LB.settings.value('printer.deviceUid', '')) || ps[0];
          await LB.zpl.bpSend(d, zpl);
        } else {
          U().downloadText(zpl, label + '.zpl', 'application/octet-stream');
        }
        U().toast(label + ' 전송 완료', 'ok');
        U().status('프린터로 ' + label + ' 전송');
      } catch (e) { U().toast(e.message, 'err'); U().status('전송 실패: ' + e.message, 'error'); }
    };
    for (const [label, zpl] of [
      ['테스트 라벨', LB.zpl.DIAGNOSTIC.testLabel],
      ['설정 라벨 인쇄', LB.zpl.DIAGNOSTIC.printConfig],
      ['용지 캘리브레이션', LB.zpl.DIAGNOSTIC.calibrate],
    ]) {
      const bq = document.createElement('button');
      bq.className = 'btn sm'; bq.textContent = label;
      bq.onclick = () => sendRaw(zpl, label);
      diagRow.appendChild(bq);
    }
    gp4.appendChild(diagRow);
    const dgHint = document.createElement('div');
    dgHint.className = 'hint'; dgHint.style.marginTop = '6px';
    dgHint.textContent = '테스트 라벨에는 DataMatrix가 포함되어 있어 스캐너로 판독해 인쇄 품질을 확인할 수 있습니다.';
    gp4.appendChild(dgHint);
    pr.appendChild(gp4);

    /* --- 이미지 --- */
    const im = pages.imaging;
    im.innerHTML = '<h3>이미지</h3>';
    im.appendChild(settingRow('배경 자동 투명화', checkCtl('imaging.autoTransparent', () => { imgCache.clear(); loadSlotImages(true); })));
    const tolHint = document.createElement('div');
    tolHint.className = 'hint'; tolHint.style.margin = '-4px 0 10px';
    tolHint.textContent = 'JPG나 배경이 불투명한 PNG는 가장자리에서 이어진 배경색만 자동으로 지웁니다. 제품 안쪽의 흰색은 남습니다.';
    im.appendChild(tolHint);
    im.appendChild(settingRow('배경 인식 허용치', numCtl('imaging.tolerance', 0, 120, 1, () => { imgCache.clear(); loadSlotImages(true); })));

    /* --- 검증 --- */
    const v = pages.validation;
    v.innerHTML = '<h3>검증 규칙</h3>';
    const vHint = document.createElement('div');
    vHint.className = 'hint'; vHint.style.marginBottom = '10px';
    vHint.textContent = '끄면 해당 항목을 검사하지 않습니다. 의료기기 라벨 특성상 켜 두기를 권장합니다.';
    v.appendChild(vHint);
    const VR = [
      ['requireItem', '품목번호 필수 (오류)'], ['requireLot', 'LOT 필수 (오류)'],
      ['requireMfg', '제조일 필수 (오류)'], ['requireSn', 'SN 필수 (오류)'],
      ['checkGtin', 'GTIN 체크디짓 검증 (오류)'], ['checkExpOrder', '유효일이 제조일보다 빠른지 (오류)'],
      ['checkExpPast', '유효일이 이미 지났는지 (경고)'], ['warnMissingImage', '이미지 누락 (경고)'],
      ['warnUnresolved', '치환되지 않은 항목 (경고)'], ['warnOverflow', '텍스트 넘침 (경고)'],
      ['warnOutOfBounds', '라벨 밖 객체 (경고)'],
    ];
    for (const [k, label] of VR) v.appendChild(settingRow(label, checkCtl('validation.' + k)));

    /* --- 화면 --- */
    const u = pages.ui;
    u.innerHTML = '<h3>화면</h3>';
    u.appendChild(settingRow('테마', selectCtl('ui.theme', [['system', '시스템 설정 따름'], ['light', '밝게'], ['dark', '어둡게']], false, () => applyUiSettings())));
    u.appendChild(settingRow('눈금자', checkCtl('ui.showRulers', () => applyUiSettings())));
    u.appendChild(settingRow('격자', checkCtl('ui.showGrid', () => applyUiSettings())));
    u.appendChild(settingRow('격자 간격 (mm)', numCtl('ui.gridMm', 0.5, 50, 0.5, () => applyUiSettings())));
    u.appendChild(settingRow('맞춤 안내선 (스냅)', checkCtl('ui.snap', () => applyUiSettings())));
    u.appendChild(settingRow('스냅 민감도 (px)', numCtl('ui.snapPx', 1, 24, 1, () => applyUiSettings())));
    const ob = document.createElement('button');
    ob.className = 'btn sm'; ob.style.marginTop = '10px'; ob.textContent = '처음 설정 안내 다시 보기';
    ob.onclick = () => { LB.settings.put('ui.onboardingDone', false); showOnboard(); };
    u.appendChild(ob);

    /* --- 이력 --- */
    const h = pages.history;
    h.innerHTML = '<h3>출력 이력</h3>';
    const hbox = document.createElement('div');
    hbox.className = 'hint'; hbox.textContent = '불러오는 중…';
    h.appendChild(hbox);
    const hbtns = document.createElement('div');
    hbtns.className = 'row'; hbtns.style.marginTop = '10px';
    const hcsv = document.createElement('button');
    hcsv.className = 'btn sm'; hcsv.textContent = 'CSV로 내보내기';
    hcsv.onclick = async () => {
      const rows = await LB.store.listHistory(5000);
      if (!rows.length) { U().toast('이력이 없습니다.', 'warn'); return; }
      U().downloadText(LB.store.historyToCsv(rows), `출력이력_${LB.data.fmtISO(new Date())}.csv`, 'text/csv;charset=utf-8');
    };
    const hclr = document.createElement('button');
    hclr.className = 'btn sm danger ghost'; hclr.textContent = '이력 지우기';
    hclr.onclick = async () => {
      const ok = await U().confirm('출력 이력을 모두 지울까요?', { title: '이력 삭제', danger: true, okLabel: '지우기' });
      if (ok) { await LB.store.clearHistory(); U().toast('이력을 지웠습니다.', 'ok'); loadHist(); }
    };
    hbtns.append(hcsv, hclr);
    h.appendChild(hbtns);
    const hlist = document.createElement('div');
    hlist.style.cssText = 'margin-top:10px;max-height:300px;overflow:auto;font-size:11.5px';
    h.appendChild(hlist);
    async function loadHist() {
      const rows = await LB.store.listHistory(200).catch(() => []);
      hbox.textContent = `최근 ${rows.length}건 (최대 200건 표시)`;
      LB.ui.clear(hlist);
      const t = document.createElement('table');
      t.className = 'kvtable';
      for (const r of rows) {
        const tr = document.createElement('tr');
        const a = document.createElement('td');
        a.textContent = new Date(r.at).toLocaleString();
        a.style.whiteSpace = 'nowrap';
        const b = document.createElement('td');
        b.textContent = `${r.item || ''} LOT ${r.lot || ''}${r.sn ? ' SN ' + r.sn : ''} — ${r.ok ? (r.fileName || '완료') : '실패: ' + (r.error || '')}`;
        if (!r.ok) b.style.color = 'var(--fail)';
        tr.append(a, b); t.appendChild(tr);
      }
      hlist.appendChild(t);
    }
    loadHist();

    /* --- 정보 --- */
    const ab = pages.about;
    ab.innerHTML = `<h3>정보</h3>
      <p class="hint">LB Generator v2.0 — 의료기기 라벨 PDF 생성기<br>
      브라우저 단독 실행. 모든 데이터는 이 PC에만 저장되며 외부로 전송되지 않습니다.</p>`;
    const abtns = document.createElement('div');
    abtns.className = 'row'; abtns.style.marginTop = '12px'; abtns.style.flexWrap = 'wrap';
    const exp = document.createElement('button');
    exp.className = 'btn sm'; exp.textContent = '설정 내보내기';
    exp.onclick = () => U().downloadText(LB.settings.exportJson(), 'lb-generator-settings.json', 'application/json');
    const imp = document.createElement('button');
    imp.className = 'btn sm'; imp.textContent = '설정 가져오기';
    const impFile = document.createElement('input');
    impFile.type = 'file'; impFile.accept = '.json'; impFile.hidden = true;
    imp.onclick = () => impFile.click();
    impFile.onchange = async () => {
      const f = impFile.files[0]; impFile.value = '';
      if (!f) return;
      try { LB.settings.importJson(await f.text()); applyUiSettings(); U().toast('설정을 가져왔습니다.', 'ok'); }
      catch (e) { U().toast('가져오기 실패: ' + e.message, 'err'); }
    };
    const rst = document.createElement('button');
    rst.className = 'btn sm danger ghost'; rst.textContent = '설정 초기화';
    rst.onclick = async () => {
      const ok = await U().confirm('모든 설정을 기본값으로 되돌릴까요?', {
        title: '설정 초기화', danger: true, okLabel: '초기화',
        detail: '폴더 지정과 서식, 출력 이력은 그대로 유지됩니다.',
      });
      if (ok) { await LB.settings.reset(); applyUiSettings(); U().toast('설정을 초기화했습니다.', 'ok'); }
    };
    abtns.append(exp, imp, impFile, rst);
    ab.appendChild(abtns);
    LB.store.usage().then(us => {
      if (!us) return;
      const d = document.createElement('div');
      d.className = 'hint'; d.style.marginTop = '10px';
      d.textContent = `저장소 사용량 ${U().fmtBytes(us.used)} / ${U().fmtBytes(us.quota)}`;
      ab.appendChild(d);
    });
  }

  /* 설정 컨트롤 헬퍼 */
  function settingRow(label, ctl) {
    const d = document.createElement('div');
    d.className = 'row';
    d.style.cssText = 'justify-content:space-between;gap:12px;padding:5px 0';
    const l = document.createElement('span');
    l.style.fontSize = '12.5px';
    l.textContent = label;
    d.append(l, ctl);
    return d;
  }
  function selectCtl(path, opts, numeric, after) {
    const s = document.createElement('select');
    s.style.width = 'auto';
    for (const [v, t] of opts) {
      const o = document.createElement('option');
      o.value = String(v); o.textContent = t;
      s.appendChild(o);
    }
    s.value = String(LB.settings.value(path));
    s.onchange = () => { LB.settings.put(path, numeric ? Number(s.value) : s.value); if (after) after(); };
    return s;
  }
  function textCtl(path, ph) {
    const i = document.createElement('input');
    i.type = 'text'; i.placeholder = ph || ''; i.style.width = '260px';
    i.value = LB.settings.value(path, '');
    i.oninput = () => LB.settings.put(path, i.value);
    return i;
  }
  function numCtl(path, min, max, step, after) {
    const i = document.createElement('input');
    i.type = 'number'; i.min = min; i.max = max; i.step = step; i.style.width = '80px';
    i.value = LB.settings.value(path, min);
    i.onchange = () => { LB.settings.put(path, Number(i.value)); if (after) after(); };
    return i;
  }
  function checkCtl(path, after) {
    const l = document.createElement('label');
    l.className = 'chk';
    const c = document.createElement('input');
    c.type = 'checkbox';
    c.checked = !!LB.settings.value(path, false);
    c.onchange = () => { LB.settings.put(path, c.checked); if (after) after(); };
    l.appendChild(c);
    return l;
  }

  /* ================= 도움말 ================= */
  function showHelp() {
    const K = [
      ['작업', ''],
      ['Ctrl + Enter', '현재 입력을 연속 작업 큐에 추가'],
      ['Ctrl + P', '출력 (큐가 비었으면 1장, 있으면 큐 전체)'],
      ['Ctrl + Shift + P', '큐와 관계없이 현재 입력 1장만 출력'],
      ['Ctrl + V (큐)', '엑셀에서 복사한 표를 큐로 붙여넣기'],
      ['Ctrl + ↓ / ↑', '큐 펼치기 / 접기'],
      ['화면', ''],
      ['Ctrl + L', '레이아웃 잠금 켜기/끄기'],
      ['F5', '실물 미리보기 (편집 보조선 숨김)'],
      ['Ctrl + 0 / Ctrl + 1', '화면 맞춤 / 실측 100%'],
      ['휠', '커서 기준 확대·축소'],
      ['Space + 드래그', '화면 이동'],
      ['Ctrl + ,', '설정'],
      ['편집 (잠금 해제 상태)', ''],
      ['Ctrl + Z / Ctrl + Shift + Z', '실행 취소 / 다시 실행'],
      ['Ctrl + D', '선택 객체 복제'],
      ['Ctrl + A', '전체 선택'],
      ['방향키', '0.1mm 이동 (Shift 1mm, Alt 0.01mm)'],
      ['Delete', '선택 객체 삭제'],
      ['Shift + 클릭', '다중 선택'],
      ['빈 곳 드래그', '사각형 선택'],
      ['Alt (드래그 중)', '스냅 일시 해제'],
      ['Tab', '다음 객체 선택'],
    ];
    const t = document.createElement('table');
    t.className = 'kvtable';
    for (const [k, v] of K) {
      const tr = document.createElement('tr');
      if (!v) {
        const td = document.createElement('td');
        td.colSpan = 2; td.textContent = k;
        td.style.cssText = 'font-weight:700;padding-top:12px;color:var(--accent-deep)';
        tr.appendChild(td);
      } else {
        const a = document.createElement('td');
        a.innerHTML = `<span class="kbd">${k}</span>`;
        a.style.whiteSpace = 'nowrap';
        const b = document.createElement('td'); b.textContent = v;
        tr.append(a, b);
      }
      t.appendChild(tr);
    }
    U().modal({ title: '단축키', body: t, buttons: [{ label: '닫기', value: null, primary: true }] });
  }

  /* ================= 이벤트 ================= */
  function bind() {
    /* --- 입력 --- */
    $('inpItem').addEventListener('input', () => {
      S.inputs.item = $('inpItem').value.trim();
      popIndex = -1;
      showItemPop();
      refreshSoon();
    });
    $('inpItem').addEventListener('focus', () => showItemPop());
    $('inpItem').addEventListener('blur', () => setTimeout(hideItemPop, 120));
    $('inpItem').addEventListener('keydown', (e) => {
      const pop = $('itemPop');
      const opts = pop.hidden ? [] : [...pop.querySelectorAll('.opt')];
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        if (!opts.length) return;
        e.preventDefault();
        popIndex = Math.max(0, Math.min(opts.length - 1, popIndex + (e.key === 'ArrowDown' ? 1 : -1)));
        opts.forEach((o, i) => o.classList.toggle('sel', i === popIndex));
        opts[popIndex].scrollIntoView({ block: 'nearest' });
      } else if (e.key === 'Enter') {
        e.preventDefault();
        if (opts.length && popIndex >= 0) pickItem(opts[popIndex].querySelector('b').textContent);
        else if (opts.length === 1) pickItem(opts[0].querySelector('b').textContent);
        else { hideItemPop(); $('inpLot').focus(); }
      } else if (e.key === 'Escape') hideItemPop();
    });

    const onInput = () => {
      S.inputs.lot = $('inpLot').value.trim();
      S.inputs.sn = $('inpSn').value.trim();
      S.inputs.mfg = $('inpMfg').value;
      S.inputs.months = Number($('inpMonths').value) || 36;
      S.inputs.expAuto = $('chkExpAuto').checked;
      S.inputs.copies = Math.max(1, Number($('inpCopies').value) || 1);
      if (!S.inputs.expAuto) S.inputs.exp = $('inpExp').value;
      $('inpExp').disabled = S.inputs.expAuto;
      refreshSoon();
      if (S.inputs.expAuto) setTimeout(() => { $('inpExp').value = S.inputs.exp || ''; }, 100);
      // 큐 행을 편집 중이면 그 행에도 반영
      if (S.activeQueueId) {
        LB.batch.update(S.activeQueueId, {
          item: S.inputs.item, lot: S.inputs.lot, sn: S.inputs.sn, mfg: S.inputs.mfg,
          months: S.inputs.months, expAuto: S.inputs.expAuto, exp: S.inputs.exp, copies: S.inputs.copies,
        });
        revalidateQueue();
      }
    };
    for (const id of ['inpLot', 'inpSn', 'inpMfg', 'inpMonths', 'inpExp', 'inpCopies']) {
      $(id).addEventListener('input', onInput);
    }
    $('chkExpAuto').addEventListener('change', onInput);
    $('inpLot').addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.ctrlKey) { e.preventDefault(); $('inpMfg').focus(); }
    });

    $('btnClearJob').onclick = () => {
      Object.assign(S.inputs, { lot: '', sn: '', copies: 1 });
      S.activeQueueId = null;
      $('jobOrigin').textContent = '새 작업';
      syncInputsToUi(); refresh();
      $('inpLot').focus();
    };

    /* --- 큐 --- */
    $('btnQueueAdd').onclick = () => addCurrentToQueue();
    $('drawerHead').onclick = (e) => {
      if (e.target.closest('button')) return;
      openDrawer(!$('drawer').classList.contains('open'));
    };
    $('btnQAddRow').onclick = () => {
      LB.batch.add({ item: S.inputs.item, mfg: S.inputs.mfg, months: S.inputs.months });
      revalidateQueue(); openDrawer(true);
    };
    $('btnQPaste').onclick = async () => pasteToQueue();
    $('btnQImport').onclick = () => $('fileQueue').click();
    $('fileQueue').onchange = async (e) => {
      const f = e.target.files[0]; e.target.value = '';
      if (!f) return;
      try {
        const r = await LB.batch.parseFile(f, { mfg: S.inputs.mfg, months: S.inputs.months });
        if (r.error) { U().toast(r.error, 'err'); return; }
        LB.batch.addMany(r.rows);
        revalidateQueue(); openDrawer(true);
        U().toast(`${r.rows.length}행을 큐에 추가했습니다.`, 'ok');
        U().status(`파일에서 ${r.rows.length}행을 불러왔습니다: ${f.name}`);
      } catch (err) { U().toast('불러오기 실패: ' + err.message, 'err'); }
    };
    $('btnQDup').onclick = () => {
      if (!selectedRows.size) { U().toast('복제할 행을 선택하세요.', 'warn'); return; }
      LB.batch.duplicate([...selectedRows]);
      revalidateQueue();
    };
    $('btnQDel').onclick = () => {
      if (!selectedRows.size) { U().toast('삭제할 행을 선택하세요.', 'warn'); return; }
      LB.batch.remove([...selectedRows]);
      selectedRows.clear();
      revalidateQueue();
    };
    $('btnQClearDone').onclick = () => { LB.batch.removeCompleted(); revalidateQueue(); };
    $('btnQClear').onclick = async () => {
      if (!LB.batch.count()) return;
      const ok = await U().confirm(`큐의 ${LB.batch.count()}행을 모두 지울까요?`, {
        title: '큐 비우기', danger: true, okLabel: '비우기',
      });
      if (ok) { LB.batch.clear(); selectedRows.clear(); S.activeQueueId = null; revalidateQueue(); }
    };
    $('btnQExpandSn').onclick = async () => expandSnDialog();
    $('btnQExport').onclick = () => {
      if (!LB.batch.count()) { U().toast('큐가 비어 있습니다.', 'warn'); return; }
      U().downloadText(LB.batch.toCsv(), `작업큐_${LB.data.fmtISO(new Date())}.csv`, 'text/csv;charset=utf-8');
    };
    $('qCheckAll').onclick = () => {
      const on = $('qCheckAll').checked;
      selectedRows.clear();
      if (on) for (const r of LB.batch.rows()) selectedRows.add(r.id);
      renderQueue();
    };
    $('selQueueMode').onchange = () => LB.settings.put('output.mode', $('selQueueMode').value);
    $('btnQPause').onclick = () => { const st = LB.batch.get(); st.paused ? LB.batch.resume() : LB.batch.pause(); renderQueue(); };
    $('btnQCancel').onclick = () => { LB.batch.cancel(); U().status('출력 중지를 요청했습니다…', 'warn'); };

    /* --- 출력 --- */
    $('btnPrint').onclick = () => doPrint();

    /* --- 앱바 --- */
    $('btnLock').onclick = () => setLocked(!S.locked);
    $('btnPreview').onclick = () => setPreview(!S.preview);
    $('btnSettings').onclick = () => openSettings();
    $('btnHelp').onclick = showHelp;
    $('chipDb').onclick = () => openSettings('paths');
    $('chipImg').onclick = () => openSettings('paths');
    $('chipOut').onclick = () => openSettings('paths');
    $('btnTplSave').onclick = saveTemplate;
    $('selTemplate').onchange = () => loadTemplate($('selTemplate').value);
    $('btnTplManage').onclick = manageTemplates;

    /* --- 스테이지 툴바 --- */
    $('btnAddText').onclick = () => {
      const d = LB.settings.value('textDefaults', {});
      ed.addObject(normalizeObject(Object.assign({
        type: 'text', x: 10, y: 10, w: 50, h: 10, text: '새 텍스트',
      }, d), S.objects.length), '텍스트 추가');
    };
    $('btnAddSlot').onclick = () => ed.addObject(normalizeObject({
      type: 'image', x: 10, y: 10, w: 40, h: 20, sourceField: 'IMG_STENT',
    }, S.objects.length), '이미지 슬롯 추가');
    $('btnAddBarcode').onclick = () => {
      const sym = $('selNewBarcode').value || 'gs1datamatrix';
      const is2d = LB.barcode.byId(sym).d2;
      ed.addObject(normalizeObject({
        type: 'barcode', x: 10, y: 10, w: is2d ? 20 : 50, h: is2d ? 20 : 14,
        symbology: sym, source: 'field', binding: 'UDI_FULL',
      }, S.objects.length), '바코드 추가');
    };
    $('btnAddImage').onclick = () => $('fileImg').click();
    $('fileImg').onchange = async (e) => {
      const f = e.target.files[0]; e.target.value = '';
      if (!f) return;
      try {
        const entry = await LB.imaging.processBlob(f, {
          autoTransparent: LB.settings.value('imaging.autoTransparent', true),
          tolerance: LB.settings.value('imaging.tolerance', 30),
        });
        const ar = entry.w / entry.h;
        const w = Math.min(60, S.label.w / 3);
        const sel = ed.selectedObjects();
        if (sel.length === 1 && sel[0].type === 'image') {
          ed.commit('이미지 지정', () => {
            sel[0].dataUrl = entry.dataUrl; sel[0].fileName = f.name; sel[0].sourceField = ''; sel[0].error = '';
          });
        } else {
          ed.addObject(normalizeObject({
            type: 'image', x: 10, y: 10, w: Math.round(w * 10) / 10, h: Math.round(w / ar * 10) / 10,
            fileName: f.name, dataUrl: entry.dataUrl,
          }, S.objects.length), '이미지 추가');
        }
        refresh();
      } catch (err) { U().toast('이미지를 불러오지 못했습니다: ' + err.message, 'err'); }
    };
    document.querySelectorAll('[data-align]').forEach(b => {
      b.onclick = () => ed.align(b.dataset.align, ed.selection.size === 1 ? 'label' : 'selection');
    });
    document.querySelectorAll('[data-dist]').forEach(b => {
      b.onclick = () => ed.distribute(b.dataset.dist);
    });
    $('btnFront').onclick = () => ed.bringToFront();
    $('btnBack').onclick = () => ed.sendToBack();
    $('btnDup').onclick = () => ed.duplicate();
    $('btnDelete').onclick = () => ed.deleteSelection();
    $('btnUndo').onclick = () => ed.undo();
    $('btnRedo').onclick = () => ed.redo();
    $('btnSnap').onclick = () => { LB.settings.put('ui.snap', !LB.settings.value('ui.snap', true)); applyUiSettings(); };
    $('btnGrid').onclick = () => { LB.settings.put('ui.showGrid', !LB.settings.value('ui.showGrid', false)); applyUiSettings(); ed.render(); };
    $('btnRuler').onclick = () => { LB.settings.put('ui.showRulers', !LB.settings.value('ui.showRulers', true)); applyUiSettings(); ed.render(); };
    $('btnLink').onclick = () => {
      const next = ed.linkMode === 'all' ? 'selection' : 'all';
      ed.linkMode = next;
      LB.settings.put('ui.linkMode', next);
      $('btnLink').classList.toggle('on', next === 'all');
      ed.render();
      U().status(next === 'all'
        ? '링크 보기 — 라벨에서 반복되는 값을 데이터별 색으로 묶어 표시합니다.'
        : '링크 보기를 껐습니다. 객체를 선택하면 같은 데이터를 쓰는 곳이 함께 표시됩니다.');
    };
    $('selTarget').onchange = () => {
      LB.settings.put('output.target', $('selTarget').value);
      updatePrintButton();
    };
    $('btnPrinterSetup').onclick = () => openSettings('printer');
    $('btnZoomFit').onclick = () => ed.zoomFit();
    $('btnZoomIn').onclick = () => ed.zoomTo(Math.min(2000, ed.zoomPercent * 1.25));
    $('btnZoomOut').onclick = () => ed.zoomTo(Math.max(5, ed.zoomPercent / 1.25));
    $('selZoom').onchange = () => ed.zoomTo(Number($('selZoom').value));

    const onSize = () => {
      S.label.w = Math.max(5, Number($('inpLabelW').value) || 297);
      S.label.h = Math.max(5, Number($('inpLabelH').value) || 420);
      ed.render(); renderChecks(); scheduleSave();
    };
    $('inpLabelW').addEventListener('change', onSize);
    $('inpLabelH').addEventListener('change', onSize);

    /* --- 전역 키 --- */
    window.addEventListener('keydown', (e) => {
      if (U().isModalOpen()) return;
      const inField = e.target.matches('input,textarea,select');
      const mod = e.ctrlKey || e.metaKey;

      if (mod && e.key === 'Enter') { e.preventDefault(); addCurrentToQueue(); return; }
      if (mod && e.key.toLowerCase() === 'p') {
        e.preventDefault();
        if (e.shiftKey) printSingle(); else doPrint();
        return;
      }
      if (mod && e.key === ',') { e.preventDefault(); openSettings(); return; }
      if (e.key === 'F1') { e.preventDefault(); showHelp(); return; }
      if (e.key === 'F5') { e.preventDefault(); setPreview(!S.preview); return; }
      if (mod && e.key.toLowerCase() === 'l') { e.preventDefault(); setLocked(!S.locked); return; }
      if (mod && e.key.toLowerCase() === 's') { e.preventDefault(); saveTemplate(); return; }
      if (mod && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
        e.preventDefault(); openDrawer(e.key === 'ArrowDown'); return;
      }
      if (mod && e.key.toLowerCase() === 'b' && !inField) {
        e.preventDefault(); $('btnBold').click(); return;
      }
      if (mod && e.key.toLowerCase() === 'i' && !inField) {
        e.preventDefault(); $('btnItalic').click(); return;
      }
      if (inField) return;
      if (S.locked) {
        // 잠금 상태에서도 보기 관련 키는 살려 둔다
        if (mod && (e.key === '0' || e.key === '1')) { e.preventDefault(); e.key === '0' ? ed.zoomFit() : ed.zoomTo(100); }
        return;
      }
      if (ed.handleKey(e)) e.preventDefault();
    });
    window.addEventListener('keyup', (e) => ed.handleKeyUp(e));

    // 큐 영역에 붙여넣기
    window.addEventListener('paste', async (e) => {
      if (U().isModalOpen()) return;
      if (e.target.matches('input,textarea')) return;
      const txt = (e.clipboardData || window.clipboardData).getData('text');
      if (!txt || txt.indexOf('\t') < 0 && txt.indexOf('\n') < 0) return;
      e.preventDefault();
      pasteToQueue(txt);
    });
  }

  function addCurrentToQueue() {
    if (!S.inputs.item) { U().toast('품목번호를 먼저 입력하세요.', 'warn'); $('inpItem').focus(); return; }
    // 여러 줄 LOT 지원
    const lots = String(S.inputs.lot || '').split(/\n+/).map(s => s.trim()).filter(Boolean);
    const list = (lots.length > 1 ? lots : [S.inputs.lot]).map(lot => ({
      item: S.inputs.item, lot, sn: S.inputs.sn, mfg: S.inputs.mfg,
      months: S.inputs.months, expAuto: S.inputs.expAuto, exp: S.inputs.exp,
      copies: S.inputs.copies || 1,
    }));
    LB.batch.addMany(list);
    S.activeQueueId = null;
    $('jobOrigin').textContent = '새 작업';
    S.inputs.lot = ''; S.inputs.sn = '';
    syncInputsToUi();
    revalidateQueue();
    openDrawer(true);
    refresh();
    $('inpLot').focus();
    U().status(`큐에 ${list.length}행 추가 — 총 ${LB.batch.count()}행 / ${LB.batch.totalLabels()}장`);
  }

  async function pasteToQueue(pre) {
    let text = pre;
    if (!text) {
      try { text = await navigator.clipboard.readText(); }
      catch (_) { text = null; }
    }
    if (!text) {
      const ta = document.createElement('textarea');
      ta.rows = 8; ta.className = 'mono';
      ta.placeholder = '품목번호\tLOT\tSN\t제조일\n16-0401\t26041086\t1\t2026-06-01';
      const body = document.createElement('div');
      const h = document.createElement('div');
      h.className = 'hint'; h.style.marginBottom = '6px';
      h.textContent = '엑셀에서 복사한 표를 여기에 붙여넣으세요. 첫 줄이 머리글이면 자동으로 인식합니다.';
      body.append(h, ta);
      const ok = await U().modal({
        title: '엑셀 붙여넣기', body, defaultValue: false,
        buttons: [{ label: '취소', value: false }, { label: '큐에 추가', value: true, primary: true }],
      });
      if (!ok) return;
      text = ta.value;
    }
    const r = LB.batch.parseTable(text, { mfg: S.inputs.mfg, months: S.inputs.months });
    if (r.error) { U().toast(r.error, 'err'); return; }

    // 미리보기 확인
    const body = document.createElement('div');
    const info = document.createElement('div');
    info.className = 'hint';
    info.style.marginBottom = '8px';
    info.textContent = `${r.rows.length}행 인식${r.headerDetected ? ' (첫 줄을 머리글로 인식)' : ''} · 매핑: ` +
      Object.keys(r.mapping).map(k => ({ item: '품목번호', lot: 'LOT', sn: 'SN', mfg: '제조일', exp: '유효일', months: '개월', copies: '매수' }[k] || k)).join(', ');
    body.appendChild(info);
    const t = document.createElement('table');
    t.className = 'queue';
    t.innerHTML = '<thead><tr><th>#</th><th>품목번호</th><th>LOT</th><th>SN</th><th>제조일</th><th>매수</th><th>확인</th></tr></thead>';
    const tb = document.createElement('tbody');
    r.rows.slice(0, 60).forEach((x, i) => {
      const tr = document.createElement('tr');
      const found = S.index && S.index.byRef.has(String(x.item).trim());
      if (!found) tr.classList.add('row-error');
      for (const v of [i + 1, x.item, x.lot, x.sn, x.mfg, x.copies]) {
        const td = document.createElement('td');
        td.textContent = v == null ? '' : v;
        tr.appendChild(td);
      }
      const td = document.createElement('td');
      td.textContent = found ? '✓' : '품목 없음';
      tr.appendChild(td);
      tb.appendChild(tr);
    });
    t.appendChild(tb);
    const wrap = document.createElement('div');
    wrap.style.cssText = 'max-height:300px;overflow:auto;border:1px solid var(--line);border-radius:6px';
    wrap.appendChild(t);
    body.appendChild(wrap);
    if (r.rows.length > 60) {
      const m = document.createElement('div');
      m.className = 'hint'; m.style.marginTop = '6px';
      m.textContent = `… 외 ${r.rows.length - 60}행`;
      body.appendChild(m);
    }

    const ok2 = await U().modal({
      title: '붙여넣기 확인', body, wide: true, defaultValue: false,
      buttons: [{ label: '취소', value: false }, { label: `${r.rows.length}행 추가`, value: true, primary: true }],
    });
    if (!ok2) return;
    LB.batch.addMany(r.rows);
    revalidateQueue();
    openDrawer(true);
    U().toast(`${r.rows.length}행을 큐에 추가했습니다.`, 'ok');
    U().status(`붙여넣기로 ${r.rows.length}행 추가 — 총 ${LB.batch.count()}행`);
  }

  async function expandSnDialog() {
    if (selectedRows.size !== 1) {
      U().toast('SN 연번으로 펼칠 행을 하나만 선택하세요.', 'warn');
      return;
    }
    const id = [...selectedRows][0];
    const r = LB.batch.rows().find(x => x.id === id);
    const body = document.createElement('div');
    body.innerHTML = `<div class="hint" style="margin-bottom:8px">선택한 행(${r.item} / LOT ${r.lot || '—'})을 SN 범위만큼 여러 행으로 펼칩니다.</div>`;
    const g = document.createElement('div');
    g.className = 'grid2';
    const from = document.createElement('input'); from.type = 'number'; from.value = Number(r.sn) || 1;
    const to = document.createElement('input'); to.type = 'number'; to.value = (Number(r.sn) || 1) + 9;
    const l1 = document.createElement('label'); l1.textContent = '시작 SN';
    const l2 = document.createElement('label'); l2.textContent = '끝 SN';
    g.append(l1, from, l2, to);
    body.appendChild(g);
    const ok = await U().modal({
      title: 'SN 연번 전개', body, defaultValue: false,
      buttons: [{ label: '취소', value: false }, { label: '펼치기', value: true, primary: true }],
    });
    if (!ok) return;
    const res = LB.batch.expandSn(id, from.value, to.value);
    if (!res.ok) { U().toast(res.error, 'err'); return; }
    selectedRows.clear();
    revalidateQueue();
    U().toast(`${res.added}행으로 펼쳤습니다.`, 'ok');
  }

  async function manageTemplates() {
    const list = (await LB.store.get('templates').catch(() => null)) || [];
    const body = document.createElement('div');
    if (!list.length) body.innerHTML = '<p class="hint">저장된 서식이 없습니다. 레이아웃을 만든 뒤 상단 [저장]을 누르세요.</p>';
    const t = document.createElement('table');
    t.className = 'kvtable';
    for (const item of list) {
      const tr = document.createElement('tr');
      const a = document.createElement('td');
      a.textContent = item.name;
      a.style.fontWeight = '600';
      const b = document.createElement('td');
      b.textContent = `${item.objects.length}개 객체 · ${item.label.w}×${item.label.h}mm`;
      const c = document.createElement('td');
      c.style.whiteSpace = 'nowrap';
      const del = document.createElement('button');
      del.className = 'btn sm danger ghost'; del.textContent = '삭제';
      del.onclick = async () => {
        const ok = await U().confirm(`서식 "${item.name}"을(를) 삭제할까요?`, { title: '서식 삭제', danger: true, okLabel: '삭제' });
        if (!ok) return;
        const next = list.filter(x => x.name !== item.name);
        await LB.store.set('templates', next);
        await refreshTemplateList();
        tr.remove();
        U().toast('삭제했습니다.', 'ok');
      };
      const exp = document.createElement('button');
      exp.className = 'btn sm ghost'; exp.textContent = '내보내기';
      exp.onclick = () => U().downloadText(JSON.stringify({ _lb: 'template', ...item }, null, 2), `${item.name}.json`, 'application/json');
      c.append(exp, del);
      tr.append(a, b, c);
      t.appendChild(tr);
    }
    body.appendChild(t);
    const imp = document.createElement('div');
    imp.className = 'row'; imp.style.marginTop = '12px';
    const ib = document.createElement('button');
    ib.className = 'btn sm'; ib.textContent = '서식 파일 가져오기…';
    const ifl = document.createElement('input');
    ifl.type = 'file'; ifl.accept = '.json'; ifl.hidden = true;
    ib.onclick = () => ifl.click();
    ifl.onchange = async () => {
      const f = ifl.files[0]; ifl.value = '';
      if (!f) return;
      try {
        const d = JSON.parse(await f.text());
        const item = d._lb === 'template' ? d : d;
        if (!Array.isArray(item.objects)) throw new Error('서식 파일이 아닙니다.');
        const cur = (await LB.store.get('templates').catch(() => null)) || [];
        cur.push({ name: item.name || f.name.replace(/\.json$/i, ''), label: item.label, objects: item.objects, at: new Date().toISOString() });
        await LB.store.set('templates', cur);
        await refreshTemplateList();
        U().toast('서식을 가져왔습니다.', 'ok');
      } catch (e) { U().toast('가져오기 실패: ' + e.message, 'err'); }
    };
    imp.append(ib, ifl);
    body.appendChild(imp);
    await U().modal({ title: '서식 관리', body, buttons: [{ label: '닫기', value: null, primary: true }] });
  }

  /* ================= 시작 ================= */
  window.addEventListener('DOMContentLoaded', async () => {
    ed = new LB.Editor($('stage'), { label: S.label, objects: S.objects });
    ed.resolver = resolveText;
    ed.barcodeCtx = () => ({ fields, objects: S.objects, resolveText });
    ed.onSelectionChange = () => LB.inspector.refresh();
    ed.onModelChange = () => { scheduleSave(); renderChecks(); LB.inspector.refresh(); };
    ed.onViewChange = () => {
      const z = Math.round(ed.zoomPercent);
      $('statusZoom').textContent = `${S.label.w}×${S.label.h}mm · ${z}%`;
      const sel = $('selZoom');
      if (sel && !sel.matches(':focus')) {
        let exact = [...sel.options].find(o => Number(o.value) === z);
        if (!exact) {
          let cur = sel.querySelector('option[data-cur]');
          if (!cur) {
            cur = document.createElement('option');
            cur.dataset.cur = '1';
            sel.insertBefore(cur, sel.firstChild);
          }
          cur.value = String(z);
          cur.textContent = z + '%';
          exact = cur;
        }
        sel.value = exact.value;
      }
      $('btnUndo').disabled = !ed.canUndo();
      $('btnRedo').disabled = !ed.canRedo();
      $('btnUndo').title = ed.canUndo() ? `실행 취소: ${ed.undoLabel()} (Ctrl+Z)` : '실행 취소 (Ctrl+Z)';
      $('btnRedo').title = ed.canRedo() ? `다시 실행: ${ed.redoLabel()}` : '다시 실행';
    };
    ed.onHover = (mx, my) => {
      $('ovlPos').textContent = `${mx.toFixed(1)}, ${my.toFixed(1)} mm`;
    };
    ed.onDoubleClick = () => {
      const sel = ed.selectedObjects();
      if (sel.length === 1 && sel[0].type === 'text') {
        document.querySelector('.insp-tabs button[data-pane="props"]').click();
        $('propText').focus(); $('propText').select();
      }
    };

    LB.inspector.init(ed, {
      fields, row, resolveText,
      onChange: () => { scheduleSave(); renderChecks(); },
      onImageSourceChange: () => loadSlotImages(true),
      onPickImageFile: () => $('fileImg').click(),
    });
    LB.batch.onChange(() => { renderQueue(); });

    bind();
    await refreshTemplateList();
    await restore();
    renderQueue();
    U().status('준비됨');
    $('inpItem').focus();
  });

  // 개발/테스트용 진입점
  window.__LB_APP = { S, get fields() { return fields; }, get row() { return row; }, get editor() { return ed; },
    refresh, loadDbFromFile, loadSampleData, addCurrentToQueue, pasteToQueue, doPrint, printSingle, printQueue,
    openSettings, setLocked, normalizeAll, renderQueue, revalidateQueue, get preflight() { return lastPreflight; } };
})();
