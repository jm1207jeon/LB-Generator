/* store.js — IndexedDB 영속화
 *
 * 저장하는 것
 *   settings   설정 전체 (경로 핸들 제외한 값)
 *   dirs       디렉터리 핸들 (FileSystemDirectoryHandle — 구조화 복제로 그대로 저장된다)
 *   db         라벨DB 캐시 {meta, rows}
 *   template   현재 라벨 레이아웃 {label, objects}
 *   templates  이름 붙인 템플릿 목록
 *   session    작업 중 입력값 / 큐
 *   history    출력 이력 (append-only, 최근 5000건)
 */
window.LB = window.LB || {};

LB.store = (() => {
  'use strict';

  const DB_NAME = 'lb-generator';
  const DB_VER = 2;
  const STORES = ['kv', 'history'];
  let dbp = null;

  function open() {
    if (dbp) return dbp;
    dbp = new Promise((resolve, reject) => {
      const req = indexedDB.open(DB_NAME, DB_VER);
      req.onupgradeneeded = (e) => {
        const db = req.result;
        if (!db.objectStoreNames.contains('kv')) db.createObjectStore('kv');
        if (!db.objectStoreNames.contains('history')) {
          const h = db.createObjectStore('history', { keyPath: 'id', autoIncrement: true });
          h.createIndex('at', 'at');
        }
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error || new Error('IndexedDB를 열 수 없습니다.'));
      req.onblocked = () => reject(new Error('다른 탭에서 이 프로그램이 열려 있어 저장소를 갱신할 수 없습니다.'));
    });
    return dbp;
  }

  function tx(store, mode, fn) {
    return open().then(db => new Promise((resolve, reject) => {
      const t = db.transaction(store, mode);
      const s = t.objectStore(store);
      let result;
      try { result = fn(s); } catch (e) { reject(e); return; }
      t.oncomplete = () => resolve(result && result.__req ? result.__req.result : result);
      t.onerror = () => reject(t.error);
      t.onabort = () => reject(t.error || new Error('저장소 트랜잭션이 취소되었습니다.'));
    }));
  }

  const get = (key) => tx('kv', 'readonly', (s) => ({ __req: s.get(key) }));
  const set = (key, value) => tx('kv', 'readwrite', (s) => { s.put(value, key); });
  const del = (key) => tx('kv', 'readwrite', (s) => { s.delete(key); });
  const keys = () => tx('kv', 'readonly', (s) => ({ __req: s.getAllKeys() }));

  /* ---------------- 출력 이력 ---------------- */

  /**
   * 출력 1건 기록. 품질기록 관점에서 누가·언제·무엇을 출력했는지 남긴다.
   * @param {object} rec {item, ref, lot, sn, mfg, exp, udi, fileName, ok, error, copies}
   */
  async function addHistory(rec) {
    const row = Object.assign({ at: new Date().toISOString() }, rec);
    await tx('history', 'readwrite', (s) => { s.add(row); });
    return row;
  }

  /** 최근 N건 (최신 순) */
  function listHistory(limit = 500) {
    return open().then(db => new Promise((resolve, reject) => {
      const t = db.transaction('history', 'readonly');
      const idx = t.objectStore('history').index('at');
      const out = [];
      const req = idx.openCursor(null, 'prev');
      req.onsuccess = () => {
        const c = req.result;
        if (!c || out.length >= limit) { resolve(out); return; }
        out.push(c.value);
        c.continue();
      };
      req.onerror = () => reject(req.error);
    }));
  }

  async function clearHistory() {
    await tx('history', 'readwrite', (s) => { s.clear(); });
  }

  /** 이력 CSV 문자열 */
  function historyToCsv(rows) {
    const cols = ['at', 'item', 'ref', 'lot', 'sn', 'mfg', 'exp', 'copies', 'fileName', 'ok', 'error', 'udi'];
    const head = ['출력일시', '품목번호', '규격', 'LOT', 'SN', '제조일', '유효일', '매수', '파일명', '성공', '오류', 'UDI'];
    const esc = (v) => {
      const s = v == null ? '' : String(v);
      return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
    };
    const lines = [head.join(',')];
    for (const r of rows) lines.push(cols.map(c => esc(r[c])).join(','));
    return '\uFEFF' + lines.join('\r\n');     // BOM — 엑셀에서 한글이 깨지지 않도록
  }

  /** 저장소 사용량 추정 */
  async function usage() {
    try {
      if (navigator.storage && navigator.storage.estimate) {
        const e = await navigator.storage.estimate();
        return { used: e.usage || 0, quota: e.quota || 0 };
      }
    } catch (_) {}
    return null;
  }

  /** 전체 초기화 (설정 초기화용) */
  async function wipe() {
    for (const s of STORES) {
      await tx(s, 'readwrite', (st) => { st.clear(); });
    }
  }

  return { get, set, del, keys, addHistory, listHistory, clearHistory, historyToCsv, usage, wipe };
})();
