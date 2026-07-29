/* app.js — 메인 로직
 *
 * 엑셀 원본( PML-001 Rev.1 시트 ) 수식과의 대응:
 *   EXP  = EDATE(MFG, months) - 1일
 *   EXP6 = TEXT(EXP,"YYMMDD")
 *   UDI  = "(01)"&GTIN & "(10)"&LOT & "(17)"&EXP6 & "(240)"&품목번호 & "(21)"&SN
 *   참조값 = INDEX(라벨DB!<열>:<열>, MATCH(품목번호, 라벨DB!H:H, 0))
 */
(() => {
  'use strict';

  /* ---------------- 상태 ---------------- */
  const S = {
    dbMeta: null,          // { fileName, loadedAt, count }
    rows: [],              // [{A:..,B:..,...}] 열문자 키
    byRef: new Map(),      // 품목번호(H) -> row
    product: '',
    inputs: { lot: '', sn: '', mfg: '', months: 36, expAuto: true, exp: '' },
    settings: { pattern: '{ITEM}_{LOT}_{DATE}', dpi: 600, tol: 30, autoTransparent: true },
    label: { w: 100, h: 70 },
    objects: [],
  };
  let fields = {};
  let editor = null;
  let imgDirHandle = null, outDirHandle = null;
  let sessionFiles = null;              // 폴더 API 미지원 브라우저 폴백: Map(name -> File)
  const imgProcCache = new Map();       // fileName|tol|auto -> {dataUrl,w,h}

  const $ = (id) => document.getElementById(id);

  /* 엑셀 참조 열 정의 (라벨DB 시트 열문자) */
  const FIELD_COLS = {
    PRODUCT: 'AU',   // 제품명 한줄(텍스트)
    MDR: 'BB',       // MDR 추가 문구
    REF: 'L',        // 3. Reference
    GTIN: 'AJ',      // aa23.GTIN-13
    STENT_OD: 'Q', STENT_LEN: 'R', HEAD_OD: 'S', HEAD_LEN_D: 'T', HEAD_LEN_P: 'U', DIM_B: 'B',
    GW_INCH: 'V', GW_MM: 'W',
    DD_FR: 'X', DD_MM: 'Y', DD_LEN: 'Z',
    LIFETIME: 'AY', PIC_NOTE: 'AZ', COVER: 'BC',
    KOREA_NO: 'AA', KOREA_NAME: 'AB',
    IMG_NAME1: 'J', IMG_NAME2: 'K', IMG_STENT: 'O', IMG_DELIVERY: 'P', IMG_AM: 'AM', IMG_AP: 'AP',
  };

  /* ---------------- 날짜 유틸 ---------------- */
  function edate(date, months) {   // 엑셀 EDATE와 동일 (말일 클램프)
    const y = date.getFullYear(), m = date.getMonth() + months, d = date.getDate();
    const last = new Date(y, m + 1, 0).getDate();
    return new Date(y, m, Math.min(d, last));
  }
  const fmtISO = (d) => d ? `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}` : '';
  const fmt6 = (d) => d ? `${String(d.getFullYear()).slice(2)}${String(d.getMonth() + 1).padStart(2, '0')}${String(d.getDate()).padStart(2, '0')}` : '';
  const parseISO = (s) => { if (!s) return null; const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d); };

  /* ---------------- 필드 계산 ---------------- */
  function computeFields() {
    const row = S.byRef.get(S.product) || {};
    const f = { ITEM: S.product, LOT: S.inputs.lot, SN: S.inputs.sn };

    for (const [name, col] of Object.entries(FIELD_COLS)) {
      const v = row[col];
      f[name] = (v == null) ? '' : String(v).trim();
    }

    const mfg = parseISO(S.inputs.mfg);
    let exp = null;
    if (S.inputs.expAuto) {
      if (mfg) { exp = edate(mfg, Number(S.inputs.months) || 36); exp.setDate(exp.getDate() - 1); }
    } else {
      exp = parseISO(S.inputs.exp);
    }
    f.MFG = fmtISO(mfg); f.MFG6 = fmt6(mfg);
    f.EXP = fmtISO(exp); f.EXP6 = fmt6(exp);
    S.inputs.exp = f.EXP;

    // UDI: 값이 없는 AI 그룹은 생략
    const g = (ai, v) => (v ? `(${ai})${v}` : '');
    f.GTIN01 = g('01', f.GTIN);
    f.UDI_L1 = f.GTIN01 + g('10', f.LOT);
    f.UDI_L2 = g('17', f.EXP6) + g('240', f.ITEM) + g('21', f.SN);
    f.UDI_FULL = f.UDI_L1 + f.UDI_L2;
    return f;
  }

  /* 플레이스홀더 치환: {NAME} 또는 {@열문자} */
  function resolve(text) {
    return String(text).replace(/\{(@?)([\w가-힣]+)\}/g, (m, at, key) => {
      if (at) {
        const row = S.byRef.get(S.product) || {};
        const v = row[key.toUpperCase()];
        return v == null ? '' : String(v).trim();
      }
      return fields[key] != null ? String(fields[key]) : m;
    });
  }

  /* ---------------- DB 로딩 ---------------- */
  async function loadDbFile(file) {
    $('dbStatus').textContent = 'DB 파싱 중…';
    const buf = await file.arrayBuffer();
    const wb = XLSX.read(buf, { type: 'array' });
    const sheetName = wb.SheetNames.includes('라벨DB') ? '라벨DB' : wb.SheetNames[0];
    const ws = wb.Sheets[sheetName];
    const rows = XLSX.utils.sheet_to_json(ws, { header: 'A', raw: false, defval: '' });
    const data = rows.slice(1).filter(r => r.H != null && String(r.H).trim() !== '');
    if (!data.length) {
      $('dbStatus').textContent = `시트 "${sheetName}"의 H열(품목번호)에서 데이터를 찾지 못했습니다.`;
      $('dbStatus').className = 'hint err';
      return;
    }
    applyDb({ fileName: file.name, loadedAt: new Date().toISOString(), count: data.length }, data);
    await LB.store.set('db', { meta: S.dbMeta, rows: data });
  }

  function applyDb(meta, rows) {
    S.dbMeta = meta;
    S.rows = rows;
    S.byRef = new Map();
    for (const r of rows) S.byRef.set(String(r.H).trim(), r);
    const d = new Date(meta.loadedAt);
    $('dbStatus').textContent = `"${meta.fileName}" — ${meta.count.toLocaleString()}개 품목 (${d.toLocaleString()} 로딩)`;
    $('dbStatus').className = 'hint ok';
    refreshAll();
  }

  function updateRefList() {
    const q = $('inpRef').value.trim().toLowerCase();
    const dl = $('refList');
    dl.innerHTML = '';
    if (!S.byRef.size) return;
    let n = 0;
    for (const key of S.byRef.keys()) {
      if (!q || key.toLowerCase().includes(q)) {
        const opt = document.createElement('option');
        opt.value = key;
        dl.appendChild(opt);
        if (++n >= 200) break;
      }
    }
  }

  /* ---------------- 참조값 표 / UDI ---------------- */
  const REF_TABLE = [
    ['제품명', 'PRODUCT'], ['상단 문구 (MDR)', 'MDR'], ['규격 (REF)', 'REF'], ['GTIN', 'GTIN'],
    ['스텐트 직경', 'STENT_OD'], ['스텐트 길이', 'STENT_LEN'], ['Cover Type', 'COVER'],
    ['Guidewire', 'GW_INCH'], ['D/D 직경(Fr)', 'DD_FR'], ['D/D 직경(mm)', 'DD_MM'], ['D/D 길이', 'DD_LEN'],
    ['LIFE TIME (MDR)', 'LIFETIME'],
    ['제품명 그림(1줄)', 'IMG_NAME1'], ['제품명 그림(2줄)', 'IMG_NAME2'],
    ['Stent Picture', 'IMG_STENT'], ['Delivery Picture', 'IMG_DELIVERY'],
  ];

  function updateRefTable() {
    const tb = $('tblRef').querySelector('tbody');
    tb.innerHTML = '';
    for (const [label, key] of REF_TABLE) {
      const tr = document.createElement('tr');
      const td1 = document.createElement('td'); td1.textContent = label;
      const td2 = document.createElement('td'); td2.textContent = fields[key] || '-';
      tr.append(td1, td2);
      tb.appendChild(tr);
    }
    $('udiFull').textContent = fields.UDI_FULL || '-';
  }

  /* ---------------- DataMatrix (bwip-js, 엑셀 VBA의 gs1datamatrix 대응) ---------------- */
  let lastBarcodeText = null;
  function updateBarcode() {
    const text = fields.GTIN ? fields.UDI_FULL : '';
    if (text === lastBarcodeText) return;
    lastBarcodeText = text;
    if (!text) { editor.setBarcode(null, null); return; }
    try {
      const cv = document.createElement('canvas');
      bwipjs.toCanvas(cv, { bcid: 'gs1datamatrix', text, scale: 10 });
      editor.setBarcode(text, cv);
    } catch (e) {
      console.warn('DataMatrix 생성 실패:', e);
      editor.setBarcode(null, null);
    }
  }

  /* ---------------- 이미지 폴더 ---------------- */
  async function pickDir(kind) {   // kind: 'imgDir' | 'outDir'
    if (window.showDirectoryPicker) {
      try {
        const handle = await window.showDirectoryPicker({ mode: kind === 'outDir' ? 'readwrite' : 'read' });
        if (kind === 'imgDir') imgDirHandle = handle; else outDirHandle = handle;
        await LB.store.set(kind, handle);
        updateDirLabels();
        if (kind === 'imgDir') { imgProcCache.clear(); await loadSlotImages(true); }
      } catch (e) { /* 사용자 취소 */ }
    } else if (kind === 'imgDir') {
      // 폴백: webkitdirectory (세션 동안만 유효)
      const inp = document.createElement('input');
      inp.type = 'file'; inp.webkitdirectory = true;
      inp.onchange = async () => {
        sessionFiles = new Map();
        for (const f of inp.files) sessionFiles.set(f.name, f);
        $('imgDirName').textContent = `${sessionFiles.size}개 파일 (세션 한정)`;
        imgProcCache.clear();
        await loadSlotImages(true);
      };
      inp.click();
    } else {
      alert('이 브라우저는 폴더 저장을 지원하지 않습니다. PDF는 다운로드 폴더로 저장됩니다.');
    }
  }

  async function ensurePermission(handle, mode) {
    if (!handle) return false;
    if (await handle.queryPermission({ mode }) === 'granted') return true;
    try { return await handle.requestPermission({ mode }) === 'granted'; }
    catch (e) { return false; }
  }

  function updateDirLabels() {
    $('imgDirName').textContent = imgDirHandle ? imgDirHandle.name : (sessionFiles ? `${sessionFiles.size}개 파일` : '미지정');
    $('outDirName').textContent = outDirHandle ? outDirHandle.name : '미지정 (미지정 시 다운로드)';
  }

  async function readImageFile(fileName) {
    if (imgDirHandle) {
      if (!(await ensurePermission(imgDirHandle, 'read'))) throw new Error('폴더 권한 없음');
      const fh = await imgDirHandle.getFileHandle(fileName);
      return await fh.getFile();
    }
    if (sessionFiles && sessionFiles.has(fileName)) return sessionFiles.get(fileName);
    throw new Error('이미지 폴더 미지정');
  }

  /* 슬롯 이미지 로딩 (엑셀 VBA UpdateImagesKeepAspectRatio 대응) */
  async function loadSlotImages(force) {
    let changed = false;
    for (const o of S.objects) {
      if (o.type !== 'image' || !o.sourceField) continue;
      const fileName = fields[o.sourceField] || '';
      if (!force && o.fileName === fileName && (o.dataUrl || !fileName)) continue;
      o.fileName = fileName;
      o.error = '';
      if (!fileName) { o.dataUrl = ''; changed = true; continue; }
      const cacheKey = `${fileName}|${S.settings.tol}|${S.settings.autoTransparent}`;
      try {
        let entry = imgProcCache.get(cacheKey);
        if (!entry) {
          const file = await readImageFile(fileName);
          entry = await LB.imaging.processBlob(file, {
            autoTransparent: S.settings.autoTransparent, tolerance: S.settings.tol,
          });
          imgProcCache.set(cacheKey, entry);
        }
        o.dataUrl = entry.dataUrl;
      } catch (e) {
        o.dataUrl = '';
        o.error = e.message;
      }
      changed = true;
    }
    if (changed) { editor.render(); saveProject(); updatePropBox(); }
  }

  /* ---------------- 전체 갱신 ---------------- */
  function refreshAll() {
    fields = computeFields();
    updateRefTable();
    updateBarcode();
    loadSlotImages(false);
    editor.render();
    saveProject();
  }

  /* ---------------- 프로젝트 저장/복원 ---------------- */
  let saveTimer = null;
  function saveProject() {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
      LB.store.set('project', {
        product: S.product, inputs: S.inputs, settings: S.settings,
        label: S.label, objects: S.objects,
      }).catch(() => {});
    }, 400);
  }

  async function restore() {
    const saved = await LB.store.get('project').catch(() => null);
    if (saved) {
      Object.assign(S.inputs, saved.inputs || {});
      Object.assign(S.settings, saved.settings || {});
      Object.assign(S.label, saved.label || {});
      S.objects = saved.objects || [];
      S.product = saved.product || '';
      editor.state.label = S.label;
      editor.state.objects = S.objects;
    }
    if (!S.objects.length) applyDefaultTemplate(false);

    const db = await LB.store.get('db').catch(() => null);
    if (db) applyDb(db.meta, db.rows);

    imgDirHandle = await LB.store.get('imgDir').catch(() => null) || null;
    outDirHandle = await LB.store.get('outDir').catch(() => null) || null;
    updateDirLabels();

    // UI 반영
    $('inpRef').value = S.product;
    $('inpLot').value = S.inputs.lot;
    $('inpSn').value = S.inputs.sn;
    $('inpMfg').value = S.inputs.mfg;
    $('inpMonths').value = S.inputs.months;
    $('chkExpAuto').checked = S.inputs.expAuto;
    $('inpExp').disabled = S.inputs.expAuto;
    $('inpPattern').value = S.settings.pattern;
    $('selDpi').value = String(S.settings.dpi);
    $('inpTol').value = S.settings.tol;
    $('chkAutoTransparent').checked = S.settings.autoTransparent;
    $('inpLabelW').value = S.label.w;
    $('inpLabelH').value = S.label.h;

    refreshAll();
    $('inpExp').value = S.inputs.exp;
    editor.zoomFit();
  }

  /* ---------------- 기본 템플릿 ---------------- */
  function applyDefaultTemplate(confirmFirst = true) {
    if (confirmFirst && S.objects.length && !confirm('현재 레이아웃을 기본 템플릿으로 교체할까요?')) return;
    const T = (x, y, w, h, text, opt = {}) => ({
      type: 'text', x, y, w, h, text,
      font: opt.font || 'Arial', sizePt: opt.size || 7, bold: !!opt.bold, italic: false,
      letterSpacing: 0, align: opt.align || 'left', lineHeight: 1.2, color: '#000',
      id: 'o' + Math.random().toString(36).slice(2, 10),
    });
    const I = (x, y, w, h, sourceField, fit = 'left') => ({
      type: 'image', x, y, w, h, sourceField, fit, fileName: '', dataUrl: '',
      id: 'o' + Math.random().toString(36).slice(2, 10),
    });
    S.objects = [
      T(2, 1.5, 96, 5, '{MDR}', { size: 8, bold: true }),
      I(2, 7, 62, 12, 'IMG_NAME2', 'left'),
      T(2, 20.5, 62, 4.5, 'REF  {REF}', { size: 8, bold: true }),
      T(2, 26, 62, 14, 'Stent Ø {STENT_OD} × {STENT_LEN}\nDelivery {DD_FR} ({DD_MM}) × {DD_LEN}\nGuidewire {GW_INCH} ({GW_MM})\n{COVER}', { size: 7 }),
      I(66, 7, 32, 20, 'IMG_STENT', 'center'),
      I(66, 29, 32, 10, 'IMG_DELIVERY', 'center'),
      T(2, 43, 30, 4.5, 'LOT  {LOT}', { size: 7.5, bold: true }),
      T(2, 48, 30, 4.5, 'SN  {SN}', { size: 7.5 }),
      T(34, 43, 30, 4.5, 'MFG  {MFG}', { size: 7.5 }),
      T(34, 48, 30, 4.5, 'EXP  {EXP}', { size: 7.5, bold: true }),
      T(2, 58, 72, 10, '{UDI_L1}\n{UDI_L2}', { size: 6.5, font: 'Courier New' }),
      { type: 'barcode', x: 76, y: 45, w: 22, h: 22, fit: 'center', id: 'obc' + Math.random().toString(36).slice(2, 8) },
    ];
    editor.state.objects = S.objects;
    editor.select([]);
    refreshAll();
  }

  /* ---------------- 속성 패널 ---------------- */
  function updatePropBox() {
    const sel = editor.selectedObjects();
    const box = $('propBox');
    if (sel.length !== 1) {
      box.classList.toggle('hidden', sel.length === 0);
      if (sel.length > 1) {
        $('propTitle').textContent = `${sel.length}개 객체 선택됨`;
        $('propTextWrap').style.display = 'none';
        $('propSrcWrap').style.display = 'none';
        $('propX').value = $('propY').value = $('propW').value = $('propH').value = '';
      }
      return;
    }
    box.classList.remove('hidden');
    const o = sel[0];
    $('propTitle').textContent = { text: '텍스트', image: '이미지 슬롯', barcode: 'DataMatrix' }[o.type] || '객체';
    $('propX').value = o.x; $('propY').value = o.y; $('propW').value = o.w; $('propH').value = o.h;
    $('propTextWrap').style.display = o.type === 'text' ? '' : 'none';
    $('propSrcWrap').style.display = o.type === 'image' ? '' : 'none';
    if (o.type === 'text') $('propText').value = o.text || '';
    if (o.type === 'image') {
      $('propSource').value = o.sourceField || '';
      $('propFileName').textContent = o.fileName
        ? (o.dataUrl ? `파일: ${o.fileName}` : `파일 없음: ${o.fileName}${o.error ? ' (' + o.error + ')' : ''}`)
        : '(연결된 파일 없음)';
      $('propFileName').className = o.dataUrl ? 'hint ok' : 'hint err';
    }
    // 폰트 툴바 동기화
    if (o.type === 'text') {
      $('selFont').value = o.font || 'Arial';
      $('inpFontSize').value = o.sizePt;
      $('btnBold').classList.toggle('on', !!o.bold);
      $('btnItalic').classList.toggle('on', !!o.italic);
      $('inpSpacing').value = o.letterSpacing || 0;
      $('selAlign').value = o.align || 'left';
    }
    if (o.type === 'image' || o.type === 'barcode') $('selFit').value = o.fit || 'center';
  }

  function applyToSelectedText(fn) {
    let n = 0;
    for (const o of editor.selectedObjects()) if (o.type === 'text') { fn(o); n++; }
    if (n) { editor.changed(); }
  }

  /* ---------------- 이벤트 바인딩 ---------------- */
  function bindUI() {
    // DB
    $('btnLoadDb').onclick = () => $('fileDb').click();
    $('fileDb').onchange = (e) => { if (e.target.files[0]) loadDbFile(e.target.files[0]); e.target.value = ''; };
    $('inpRef').addEventListener('input', () => {
      updateRefList();
      const v = $('inpRef').value.trim();
      if (S.byRef.has(v)) { S.product = v; refreshAll(); $('inpExp').value = S.inputs.exp; }
    });
    $('inpRef').addEventListener('change', () => {
      const v = $('inpRef').value.trim();
      S.product = v;
      refreshAll();
      $('inpExp').value = S.inputs.exp;
    });

    // 사용자 입력
    const onInput = () => {
      S.inputs.lot = $('inpLot').value.trim();
      S.inputs.sn = $('inpSn').value.trim();
      S.inputs.mfg = $('inpMfg').value;
      S.inputs.months = Number($('inpMonths').value) || 36;
      S.inputs.expAuto = $('chkExpAuto').checked;
      if (!S.inputs.expAuto) S.inputs.exp = $('inpExp').value;
      $('inpExp').disabled = S.inputs.expAuto;
      refreshAll();
      if (S.inputs.expAuto) $('inpExp').value = S.inputs.exp;
    };
    for (const id of ['inpLot', 'inpSn', 'inpMfg', 'inpMonths', 'inpExp']) $(id).addEventListener('input', onInput);
    $('chkExpAuto').addEventListener('change', onInput);

    // 설정
    $('btnImgDir').onclick = () => pickDir('imgDir');
    $('btnOutDir').onclick = () => pickDir('outDir');
    $('inpPattern').addEventListener('input', () => { S.settings.pattern = $('inpPattern').value; saveProject(); });
    $('selDpi').addEventListener('change', () => { S.settings.dpi = Number($('selDpi').value); saveProject(); });
    $('inpTol').addEventListener('change', () => {
      S.settings.tol = Number($('inpTol').value) || 30;
      imgProcCache.clear(); loadSlotImages(true);
    });
    $('chkAutoTransparent').addEventListener('change', () => {
      S.settings.autoTransparent = $('chkAutoTransparent').checked;
      imgProcCache.clear(); loadSlotImages(true);
    });
    const onLabelSize = () => {
      S.label.w = Number($('inpLabelW').value) || 100;
      S.label.h = Number($('inpLabelH').value) || 70;
      editor.render(); saveProject();
    };
    $('inpLabelW').addEventListener('input', onLabelSize);
    $('inpLabelH').addEventListener('input', onLabelSize);

    // 출력
    $('btnExport').onclick = async () => {
      const st = $('exportStatus');
      try {
        st.textContent = 'PDF 생성 중…'; st.className = 'hint';
        if (outDirHandle && !(await ensurePermission(outDirHandle, 'readwrite'))) {
          st.textContent = '저장 폴더 권한이 거부되어 다운로드로 저장합니다.';
        }
        const name = await LB.exporter.exportPdf(editor, {
          dpi: S.settings.dpi,
          pattern: S.settings.pattern,
          fields,
          outDirHandle: (outDirHandle && await outDirHandle.queryPermission({ mode: 'readwrite' }) === 'granted') ? outDirHandle : null,
        });
        st.textContent = `저장 완료: ${name}${outDirHandle ? ' → ' + outDirHandle.name : ' (다운로드)'}`;
        st.className = 'hint ok';
      } catch (e) {
        st.textContent = '출력 실패: ' + e.message;
        st.className = 'hint err';
      }
    };

    // 툴바 — 객체 추가
    $('btnAddText').onclick = () => editor.addObject({
      type: 'text', x: 5, y: 5, w: 40, h: 8, text: '텍스트',
      font: 'Arial', sizePt: 8, bold: false, italic: false, letterSpacing: 0, align: 'left', lineHeight: 1.2, color: '#000',
    });
    $('btnAddSlot').onclick = () => editor.addObject({
      type: 'image', x: 5, y: 5, w: 30, h: 15, sourceField: '', fit: 'center', fileName: '', dataUrl: '',
    });
    $('btnAddBarcode').onclick = () => editor.addObject({ type: 'barcode', x: 5, y: 5, w: 20, h: 20, fit: 'center' });
    $('btnAddImage').onclick = () => $('fileImg').click();
    $('fileImg').onchange = async (e) => {
      const file = e.target.files[0]; e.target.value = '';
      if (!file) return;
      const entry = await LB.imaging.processBlob(file, {
        autoTransparent: S.settings.autoTransparent, tolerance: S.settings.tol,
      });
      const ar = entry.w / entry.h;
      const w = Math.min(40, S.label.w / 2);
      editor.addObject({
        type: 'image', x: 5, y: 5, w: Math.round(w * 10) / 10, h: Math.round(w / ar * 10) / 10,
        sourceField: '', fit: 'center', fileName: file.name, dataUrl: entry.dataUrl,
      });
    };
    $('btnDelete').onclick = () => editor.deleteSelection();
    $('btnFront').onclick = () => editor.bringToFront();
    $('btnBack').onclick = () => editor.sendToBack();
    $('btnZoomFit').onclick = () => editor.zoomFit();

    // 폰트 컨트롤 (선택된 텍스트 객체 전체 적용)
    $('selFont').addEventListener('change', () => applyToSelectedText(o => o.font = $('selFont').value));
    $('inpFontSize').addEventListener('input', () => applyToSelectedText(o => o.sizePt = Number($('inpFontSize').value) || 8));
    $('btnBold').onclick = () => { applyToSelectedText(o => o.bold = !o.bold); updatePropBox(); };
    $('btnItalic').onclick = () => { applyToSelectedText(o => o.italic = !o.italic); updatePropBox(); };
    $('inpSpacing').addEventListener('input', () => applyToSelectedText(o => o.letterSpacing = Number($('inpSpacing').value) || 0));
    $('selAlign').addEventListener('change', () => applyToSelectedText(o => o.align = $('selAlign').value));
    $('selFit').addEventListener('change', () => {
      for (const o of editor.selectedObjects()) if (o.type === 'image' || o.type === 'barcode') o.fit = $('selFit').value;
      editor.changed();
    });

    // 속성 패널
    for (const [id, key] of [['propX', 'x'], ['propY', 'y'], ['propW', 'w'], ['propH', 'h']]) {
      $(id).addEventListener('input', () => {
        const sel = editor.selectedObjects();
        if (sel.length !== 1) return;
        const v = Number($(id).value);
        if (Number.isFinite(v)) { sel[0][key] = v; editor.changed(); }
      });
    }
    $('propText').addEventListener('input', () => {
      const sel = editor.selectedObjects();
      if (sel.length === 1 && sel[0].type === 'text') { sel[0].text = $('propText').value; editor.changed(); }
    });
    $('propSource').addEventListener('change', () => {
      const sel = editor.selectedObjects();
      if (sel.length === 1 && sel[0].type === 'image') {
        sel[0].sourceField = $('propSource').value;
        sel[0].fileName = ''; sel[0].dataUrl = '';
        loadSlotImages(true);
      }
    });

    // 템플릿
    $('btnTplDefault').onclick = () => applyDefaultTemplate(true);
    $('btnTplExport').onclick = () => {
      const data = JSON.stringify({ label: S.label, objects: S.objects, settings: S.settings }, null, 2);
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([data], { type: 'application/json' }));
      a.download = 'label-template.json';
      a.click();
      URL.revokeObjectURL(a.href);
    };
    $('btnTplImport').onclick = () => $('fileTpl').click();
    $('fileTpl').onchange = async (e) => {
      const file = e.target.files[0]; e.target.value = '';
      if (!file) return;
      try {
        const tpl = JSON.parse(await file.text());
        if (tpl.label) Object.assign(S.label, tpl.label);
        if (Array.isArray(tpl.objects)) { S.objects = tpl.objects; editor.state.objects = S.objects; }
        if (tpl.settings) Object.assign(S.settings, tpl.settings);
        $('inpLabelW').value = S.label.w; $('inpLabelH').value = S.label.h;
        editor.select([]);
        refreshAll();
        editor.zoomFit();
      } catch (err) { alert('템플릿 파일을 읽을 수 없습니다: ' + err.message); }
    };
  }

  /* ---------------- 초기화 ---------------- */
  window.addEventListener('DOMContentLoaded', async () => {
    editor = new LB.Editor($('stage'), { label: S.label, objects: S.objects });
    editor.resolver = resolve;
    editor.onSelectionChange = updatePropBox;
    editor.onModelChange = () => { saveProject(); updatePropBox(); };
    $('stage').addEventListener('dblclick', () => {
      const sel = editor.selectedObjects();
      if (sel.length === 1 && sel[0].type === 'text') $('propText').focus();
    });
    bindUI();
    await restore();
  });
})();
