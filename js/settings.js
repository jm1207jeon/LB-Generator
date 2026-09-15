/* settings.js — 설정 · 폴더 경로 관리
 *
 * 폴더 3종을 각각 따로 지정한다.
 *   dbDir   라벨DB가 있는 폴더 (+ 파일명) — 프로그램 시작 시 자동 로딩
 *   imgDir  제품 이미지 폴더
 *   outDir  PDF 저장 폴더
 *
 * 브라우저 제약과 대응
 *   - UNC 경로(\\서버\공유)를 문자열로 직접 넣을 수 없다. Windows에서 드라이브 문자로
 *     매핑(예: Z:)한 뒤 폴더 선택 창에서 고르면 네트워크 폴더도 그대로 쓸 수 있다.
 *   - 핸들은 IndexedDB에 저장되어 재방문 시 살아남지만, **권한은 'prompt'로 돌아간다**.
 *     requestPermission()은 사용자 제스처 안에서만 되므로 자동 복구가 불가능하다.
 *     → 시작 시 조용히 검사하고, 필요하면 '폴더 다시 연결' 배너를 띄운다.
 */
window.LB = window.LB || {};

LB.settings = (() => {
  'use strict';

  const SUPPORTED = typeof window.showDirectoryPicker === 'function';

  /* ---------------- 기본값 ---------------- */
  const DEFAULTS = {
    paths: {
      dbFileName: '',          // dbDir 안의 파일명
      dbAutoLoad: true,        // 시작 시 자동 로딩
      dbLastModified: 0,       // 변경 감지용
    },
    output: {
      dpi: 300,                // 라벨 인쇄 표준. 600은 고정밀이 필요할 때만.
      rasterFormat: 'auto',    // 'auto' | 'png'(무손실) | 'jpeg'(빠름)
      jpegQuality: 0.95,
      includeBg: true,
      mode: 'separate',        // 'separate' 파일마다 1장 | 'merged' 한 PDF에 여러 쪽
      pattern: '{ITEM}_{LOT}_{DATE}',
      target: 'pdf',           // 'pdf' | 'zebra'
      conflict: 'increment',   // 'increment' | 'overwrite' | 'ask'
      confirmBeforeExport: true,
    },
    imaging: {
      autoTransparent: true,
      tolerance: 30,
    },
    textDefaults: {
      font: 'Arial', sizePt: 8, letterSpacing: 0, lineHeight: 1.15, hScale: 100,
      align: 'left', vAlign: 'top', autoShrink: false, color: '#000000',
    },
    barcodeDefaults: {
      symbology: 'gs1datamatrix', source: 'field', binding: 'UDI_FULL',
      humanReadable: false, fitMode: 'module', fit: 'center',
    },
    printer: {
      dpi: 203,                // 제브라 프린터 해상도 (203 / 300 / 600)
      darkness: null,          // 인쇄 농도 -30~30 (null = 프린터 설정 유지)
      speed: null,             // 인치/초
      mediaMode: null,         // 'T' 티어오프 | 'P' 필오프 | 'C' 커터
      threshold: 160,          // 흑백 변환 기준값
      dither: true,            // 사진의 회색조를 점으로 표현
      invert: false,           // 180도 회전 출력
      method: 'browserprint',  // 'browserprint' | 'usb' | 'file'
      deviceUid: '',           // Browser Print 에서 고른 프린터
    },
    ui: {
      theme: 'system',         // 'system' | 'light' | 'dark'
      linkMode: 'selection',   // 'off' | 'selection' | 'all'
      showRulers: true,
      showGrid: false,
      gridMm: 5,
      snap: true,
      snapPx: 6,
      onboardingDone: false,
    },
    validation: {
      requireItem: true, requireLot: true, requireMfg: true, requireSn: false,
      checkGtin: true, checkExpPast: true, checkExpOrder: true,
      warnMissingImage: true, warnUnresolved: true, warnOverflow: true, warnOutOfBounds: true,
    },
  };

  const state = deepClone(DEFAULTS);
  const handles = { dbDir: null, imgDir: null, outDir: null };
  const listeners = [];

  function deepClone(o) { return JSON.parse(JSON.stringify(o)); }
  function deepMerge(target, src) {
    for (const k in src) {
      if (src[k] && typeof src[k] === 'object' && !Array.isArray(src[k])) {
        if (!target[k] || typeof target[k] !== 'object') target[k] = {};
        deepMerge(target[k], src[k]);
      } else if (src[k] !== undefined) {
        target[k] = src[k];
      }
    }
    return target;
  }

  function get() { return state; }
  /** 'output.dpi' 같은 경로로 읽기 */
  function value(path, fallback) {
    let v = state;
    for (const k of String(path).split('.')) {
      if (v == null) return fallback;
      v = v[k];
    }
    return v === undefined ? fallback : v;
  }
  /** 'output.dpi' 같은 경로로 쓰기 + 저장 */
  function put(path, v) {
    const ks = String(path).split('.');
    let t = state;
    for (let i = 0; i < ks.length - 1; i++) {
      if (!t[ks[i]] || typeof t[ks[i]] !== 'object') t[ks[i]] = {};
      t = t[ks[i]];
    }
    t[ks[ks.length - 1]] = v;
    save();
    emit(path, v);
  }
  function onChange(fn) { listeners.push(fn); }
  function emit(path, v) { for (const fn of listeners) { try { fn(path, v); } catch (_) {} } }

  let saveTimer = null;
  function save() {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => { LB.store.set('settings', state).catch(() => {}); }, 300);
  }

  async function load() {
    const saved = await LB.store.get('settings').catch(() => null);
    if (saved) deepMerge(state, saved);
    const dirs = await LB.store.get('dirs').catch(() => null);
    if (dirs) {
      for (const k of ['dbDir', 'imgDir', 'outDir']) if (dirs[k]) handles[k] = dirs[k];
    }
    return state;
  }

  async function reset() {
    deepMerge(state, deepClone(DEFAULTS));
    // 기본값에 없는 키 제거
    for (const k in state) if (!(k in DEFAULTS)) delete state[k];
    await LB.store.set('settings', state);
    emit('*', state);
  }

  /* ---------------- 폴더 핸들 ---------------- */

  const DIR_LABELS = { dbDir: '라벨DB 폴더', imgDir: '이미지 폴더', outDir: 'PDF 저장 폴더' };

  function handle(kind) { return handles[kind]; }
  function dirName(kind) { return handles[kind] ? handles[kind].name : ''; }

  async function saveHandles() {
    await LB.store.set('dirs', { dbDir: handles.dbDir, imgDir: handles.imgDir, outDir: handles.outDir })
      .catch(() => {});
  }

  /**
   * 폴더 선택. **반드시 사용자 클릭 핸들러 안에서 호출해야 한다.**
   * @returns {{ok:boolean, cancelled?:boolean, name?:string, error?:string}}
   */
  async function pickDir(kind) {
    if (!SUPPORTED) {
      return { ok: false, error: '이 브라우저는 폴더 지정을 지원하지 않습니다. Chrome 또는 Edge를 사용하세요.' };
    }
    try {
      const h = await window.showDirectoryPicker({
        id: 'lb-' + kind,
        mode: kind === 'outDir' ? 'readwrite' : 'read',
        startIn: handles[kind] || 'documents',
      });
      handles[kind] = h;
      await saveHandles();
      emit('paths.' + kind, h.name);
      return { ok: true, name: h.name };
    } catch (e) {
      if (e && (e.name === 'AbortError' || e.name === 'NotAllowedError')) return { ok: false, cancelled: true };
      return { ok: false, error: e.message || String(e) };
    }
  }

  async function clearDir(kind) {
    handles[kind] = null;
    if (kind === 'dbDir') { state.paths.dbFileName = ''; state.paths.dbLastModified = 0; save(); }
    await saveHandles();
    emit('paths.' + kind, '');
  }

  const modeOf = (kind) => (kind === 'outDir' ? 'readwrite' : 'read');

  /** 권한 상태만 조용히 확인 (프롬프트 없음) */
  async function permission(kind) {
    const h = handles[kind];
    if (!h) return 'none';
    try { return await h.queryPermission({ mode: modeOf(kind) }); }
    catch (_) { return 'unknown'; }
  }

  /** 권한 요청 — 사용자 제스처 안에서만 성공한다 */
  async function requestPermission(kind) {
    const h = handles[kind];
    if (!h) return false;
    try {
      if (await h.queryPermission({ mode: modeOf(kind) }) === 'granted') return true;
      return await h.requestPermission({ mode: modeOf(kind) }) === 'granted';
    } catch (_) { return false; }
  }

  /** 지정되어 있지만 권한이 끊긴 폴더 목록 — 배너 표시용 */
  async function needsReconnect() {
    const out = [];
    for (const k of ['dbDir', 'imgDir', 'outDir']) {
      if (!handles[k]) continue;
      const p = await permission(k);
      if (p !== 'granted') out.push({ kind: k, label: DIR_LABELS[k], name: handles[k].name, state: p });
    }
    return out;
  }

  /** 모든 폴더 권한을 한 번에 재요청 (버튼 클릭에서 호출) */
  async function reconnectAll() {
    const res = [];
    for (const k of ['dbDir', 'imgDir', 'outDir']) {
      if (!handles[k]) continue;
      const ok = await requestPermission(k);
      res.push({ kind: k, label: DIR_LABELS[k], ok });
    }
    return res;
  }

  /* ---------------- 파일 읽기/쓰기 ---------------- */

  /** 이미지 폴더에서 파일 하나 읽기 */
  async function readImage(fileName) {
    const h = handles.imgDir;
    if (!h) throw new Error('이미지 폴더가 지정되지 않았습니다. 설정에서 지정하세요.');
    if (await permission('imgDir') !== 'granted') throw new Error('이미지 폴더 권한이 없습니다. 다시 연결하세요.');
    try {
      const fh = await h.getFileHandle(fileName);
      return await fh.getFile();
    } catch (e) {
      if (e && e.name === 'NotFoundError') throw new Error(`파일 없음: ${fileName}`);
      throw e;
    }
  }

  /** 이미지 폴더의 파일 목록 (진단/자동완성용) */
  async function listImages(limit = 4000) {
    const h = handles.imgDir;
    if (!h || await permission('imgDir') !== 'granted') return null;
    const out = [];
    try {
      for await (const [name, entry] of h.entries()) {
        if (entry.kind === 'file' && /\.(png|jpe?g|gif|bmp|webp|svg)$/i.test(name)) out.push(name);
        if (out.length >= limit) break;
      }
    } catch (_) { return null; }
    return out.sort();
  }

  /** 라벨DB 폴더의 스프레드시트 목록 */
  async function listDbFiles() {
    const h = handles.dbDir;
    if (!h || await permission('dbDir') !== 'granted') return null;
    const out = [];
    try {
      for await (const [name, entry] of h.entries()) {
        if (entry.kind === 'file' && /\.(xlsx|xlsm|xls|csv)$/i.test(name)) out.push(name);
      }
    } catch (_) { return null; }
    return out.sort();
  }

  /**
   * 지정된 라벨DB 파일을 읽는다.
   * @returns {{file:File, changed:boolean}|null}  changed=false면 캐시를 그대로 써도 된다
   */
  async function readDbFile() {
    const h = handles.dbDir;
    const name = state.paths.dbFileName;
    if (!h || !name) return null;
    if (await permission('dbDir') !== 'granted') return null;
    let fh;
    try { fh = await h.getFileHandle(name); }
    catch (_) { return null; }
    const file = await fh.getFile();
    const changed = file.lastModified !== state.paths.dbLastModified;
    return { file, changed };
  }

  function markDbLoaded(file) {
    state.paths.dbLastModified = file ? file.lastModified : 0;
    save();
  }

  /**
   * PDF 저장. outDir이 없으면 null을 반환해 호출부가 다운로드로 처리하게 한다.
   * @returns {{saved:boolean, fileName:string}|null}
   */
  async function writeOutput(blob, fileName, conflict) {
    const h = handles.outDir;
    if (!h) return null;
    if (await permission('outDir') !== 'granted') return null;

    let name = fileName;
    const mode = conflict || state.output.conflict;
    if (mode === 'increment') name = await uniqueName(h, fileName);
    const fh = await h.getFileHandle(name, { create: true });
    const ws = await fh.createWritable();
    await ws.write(blob);
    await ws.close();
    return { saved: true, fileName: name };
  }

  /** 같은 이름이 있으면 (2), (3) … 을 붙인다 */
  async function uniqueName(dirHandle, fileName) {
    const m = fileName.match(/^(.*?)(\.[^.]*)?$/);
    const base = m[1], ext = m[2] || '';
    let name = fileName, n = 1;
    for (let i = 0; i < 500; i++) {
      let exists = true;
      try { await dirHandle.getFileHandle(name); }
      catch (e) { if (e && e.name === 'NotFoundError') exists = false; }
      if (!exists) return name;
      n++;
      name = `${base} (${n})${ext}`;
    }
    return `${base} (${Date.now()})${ext}`;
  }

  async function fileExists(dirHandle, fileName) {
    if (!dirHandle) return false;
    try { await dirHandle.getFileHandle(fileName); return true; }
    catch (_) { return false; }
  }

  /* ---------------- 내보내기/가져오기 ---------------- */

  /** 폴더 핸들은 직렬화할 수 없으므로 값 설정만 내보낸다 */
  function exportJson() {
    const out = deepClone(state);
    delete out.paths.dbLastModified;
    return JSON.stringify({ _lb: 'settings', version: 2, settings: out }, null, 2);
  }
  function importJson(json) {
    let d;
    try { d = JSON.parse(json); } catch (_) { throw new Error('JSON 형식이 아닙니다.'); }
    const s = d && d._lb === 'settings' ? d.settings : d;
    if (!s || typeof s !== 'object') throw new Error('설정 파일이 아닙니다.');
    deepMerge(state, s);
    save();
    emit('*', state);
    return true;
  }

  return {
    SUPPORTED, DEFAULTS, DIR_LABELS,
    get, value, put, onChange, load, save, reset,
    handle, dirName, pickDir, clearDir, permission, requestPermission,
    needsReconnect, reconnectAll,
    readImage, listImages, listDbFiles, readDbFile, markDbLoaded,
    writeOutput, uniqueName, fileExists,
    exportJson, importJson,
  };
})();
