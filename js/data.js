/* data.js — 라벨DB 로딩 · 필드 계산 · 플레이스홀더 치환
 *
 * 엑셀 원본(PML-001 Rev.1 시트)의 수식과 1:1로 대응한다.
 *   EXP       = EDATE(MFG, 개월) - 1일
 *   EXP6      = TEXT(EXP, "YYMMDD")
 *   UDI       = "(01)"&GTIN & "(10)"&LOT & "(17)"&EXP6 & "(240)"&품목번호 & "(21)"&SN
 *   참조값    = INDEX(라벨DB!<열>, MATCH(품목번호, 라벨DB!H, 0))
 */
window.LB = window.LB || {};

LB.data = (() => {
  'use strict';

  /* ---------------- 라벨DB 열 매핑 ---------------- */
  /* 값: 라벨DB 시트의 열 문자. 엑셀 수식에서 실제로 참조하던 열을 그대로 옮겼다. */
  const FIELD_COLS = {
    // 식별
    REF: 'L', GTIN: 'AJ', PRODUCT: 'AU', PRODUCT_EN: 'I', MDR: 'BB', REV: 'AK',
    REF_MTW: 'M', REF_CN: 'N',
    // 치수 — 스텐트
    STENT_OD: 'Q', STENT_LEN: 'R', HEAD_OD: 'S', HEAD_LEN_D: 'T', HEAD_LEN_P: 'U', DIM_B: 'B',
    // 치수 — 딜리버리
    GW_INCH: 'V', GW_MM: 'W', DD_FR: 'X', DD_MM: 'Y', DD_LEN: 'Z',
    // 규제/문구
    LIFETIME: 'AY', MDD_LIFE: 'AX', PIC_NOTE: 'AZ', COVER: 'BC', MDD_NOTE: 'AV',
    TERM: 'AW', DEVICE: 'BA',
    // 국가별 인허가
    KOREA_NO: 'AA', KOREA_NAME: 'AB', JAPAN_NO: 'AC', JAPAN_NAME: 'AD',
    CHINA_NO: 'AE', CHINA_STD: 'AG', CHINA_NAME: 'AH', UKR: 'AQ', DOMESTIC: 'AR',
    // 이미지 파일명
    IMG_NAME1: 'J', IMG_NAME2: 'K', IMG_STENT: 'O', IMG_DELIVERY: 'P',
    IMG_AM: 'AM', IMG_AP: 'AP',
  };

  /* 인스펙터/도움말에 보여줄 한국어 이름 */
  const FIELD_LABELS = {
    ITEM: '품목번호', LOT: 'LOT', SN: 'SN', MFG: '제조일', EXP: '유효일', EXP6: '유효일(YYMMDD)',
    MFG6: '제조일(YYMMDD)', REF: '규격(REF)', GTIN: 'GTIN', PRODUCT: '제품명', PRODUCT_EN: '제품명(영문)',
    MDR: 'MDR 추가문구', REV: '개정번호', STENT_OD: '스텐트 외경', STENT_LEN: '스텐트 길이',
    HEAD_OD: '헤드 외경', HEAD_LEN_D: '헤드 길이(원위)', HEAD_LEN_P: '헤드 길이(근위)',
    GW_INCH: '가이드와이어(inch)', GW_MM: '가이드와이어(mm)', DD_FR: '딜리버리 외경(Fr)',
    DD_MM: '딜리버리 외경(mm)', DD_LEN: '딜리버리 유효길이', LIFETIME: 'MDR 유지일',
    MDD_LIFE: 'MDD 유지일', PIC_NOTE: '환자카드 추가문구', COVER: 'Cover type',
    KOREA_NO: '국내 허가번호', KOREA_NAME: '국내 제품명', JAPAN_NO: '일본 허가번호',
    JAPAN_NAME: '일본 제품명', CHINA_NO: '중국 허가번호', CHINA_NAME: '중국 제품명',
    UDI_FULL: 'UDI 전체', UDI_L1: 'UDI 1행', UDI_L2: 'UDI 2행', GTIN01: 'UDI-DI (01)',
    IMG_NAME1: '제품명 그림(1줄)', IMG_NAME2: '제품명 그림(2줄)', IMG_STENT: '스텐트 그림',
    IMG_DELIVERY: '딜리버리 그림', IMG_AM: 'STENT OD 그림', IMG_AP: 'CI 그림',
    TODAY: '오늘 날짜', NOW: '현재 시각',
  };

  const IMAGE_FIELDS = ['IMG_NAME1', 'IMG_NAME2', 'IMG_STENT', 'IMG_DELIVERY', 'IMG_AM', 'IMG_AP'];

  /* ---------------- 날짜 ---------------- */

  /** 엑셀 EDATE와 동일 (말일 클램프) */
  function edate(date, months) {
    const y = date.getFullYear(), m = date.getMonth() + months, d = date.getDate();
    const last = new Date(y, m + 1, 0).getDate();
    return new Date(y, m, Math.min(d, last));
  }
  const p2 = (n) => String(n).padStart(2, '0');
  const MONTHS_EN = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];

  function parseISO(s) {
    if (!s) return null;
    const m = String(s).match(/^(\d{4})-(\d{2})-(\d{2})$/);
    if (!m) return null;
    const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
    return isNaN(d.getTime()) ? null : d;
  }
  const fmtISO = (d) => d ? `${d.getFullYear()}-${p2(d.getMonth() + 1)}-${p2(d.getDate())}` : '';
  const fmt6 = (d) => d ? `${String(d.getFullYear()).slice(2)}${p2(d.getMonth() + 1)}${p2(d.getDate())}` : '';

  /** 날짜 포맷 지시자 — 플레이스홀더 {EXP:YYYY-MM-DD} 등에서 사용 */
  function formatDate(d, pattern) {
    if (!d) return '';
    const map = {
      YYYY: String(d.getFullYear()), YY: String(d.getFullYear()).slice(2),
      MMM: MONTHS_EN[d.getMonth()], MM: p2(d.getMonth() + 1), M: String(d.getMonth() + 1),
      DD: p2(d.getDate()), D: String(d.getDate()),
      HH: p2(d.getHours()), mm: p2(d.getMinutes()), ss: p2(d.getSeconds()),
    };
    return String(pattern).replace(/YYYY|YY|MMM|MM|M|DD|D|HH|mm|ss/g, (t) => map[t]);
  }

  /* ---------------- DB 로딩 ---------------- */

  /**
   * 스프레드시트 ArrayBuffer를 파싱해 품목 행 배열을 얻는다.
   * 시트 이름과 무관하게 **H열(품목번호) 데이터가 가장 많은 시트**를 고른다.
   * @returns {{rows:Array<object>, sheet:string, sheets:Array<{name,count}>}}
   */
  function parseWorkbook(buf) {
    if (typeof XLSX === 'undefined') throw new Error('SheetJS 라이브러리를 찾을 수 없습니다.');
    const wb = XLSX.read(buf, { type: 'array', cellDates: false });
    const sheets = [];
    let best = null, bestRows = null, bestScore = -1;

    for (const name of wb.SheetNames) {
      let rows;
      try {
        rows = XLSX.utils.sheet_to_json(wb.Sheets[name], { header: 'A', raw: false, defval: '' });
      } catch (_) { continue; }
      if (!rows.length) { sheets.push({ name, count: 0 }); continue; }
      // 헤더 행 감지: H열에 'Product Number' 류 텍스트가 있으면 첫 줄은 헤더
      const headerish = /product\s*number|품목\s*번호/i.test(String(rows[0].H || ''));
      const data = rows.slice(headerish ? 1 : 0)
        .filter(r => r.H != null && String(r.H).trim() !== '');
      sheets.push({ name, count: data.length });
      const score = data.length + (name === '라벨DB' ? 1e9 : 0);
      if (score > bestScore) { bestScore = score; best = name; bestRows = data; }
    }
    if (!bestRows || !bestRows.length) {
      const detail = sheets.map(s => `${s.name}(${s.count})`).join(', ');
      throw new Error(`H열(품목번호)에서 데이터를 찾지 못했습니다. 시트: ${detail || '없음'}`);
    }
    // 열 문자 키를 정규화(공백 제거) + 문자열화
    const rows = bestRows.map(r => {
      const o = {};
      for (const k in r) {
        const v = r[k];
        o[k] = v == null ? '' : String(v).trim();
      }
      return o;
    });
    return { rows, sheet: best, sheets };
  }

  /** 품목번호 → 행 인덱스 + 검색용 소문자 캐시 */
  function buildIndex(rows) {
    const byRef = new Map();
    const search = [];
    for (const r of rows) {
      const key = String(r.H || '').trim();
      if (!key) continue;
      if (!byRef.has(key)) byRef.set(key, r);
      search.push({
        key,
        lk: key.toLowerCase(),
        ref: String(r[FIELD_COLS.REF] || ''),
        lref: String(r[FIELD_COLS.REF] || '').toLowerCase(),
        name: String(r[FIELD_COLS.PRODUCT] || r[FIELD_COLS.PRODUCT_EN] || ''),
        lname: String(r[FIELD_COLS.PRODUCT] || r[FIELD_COLS.PRODUCT_EN] || '').toLowerCase(),
      });
    }
    return { byRef, search };
  }

  /**
   * 품목 검색. 품목번호 → 규격 → 제품명 순으로 가중치.
   * @returns Array<{key, ref, name}>
   */
  function searchProducts(index, query, limit = 60) {
    const q = String(query || '').trim().toLowerCase();
    if (!index || !index.search.length) return [];
    if (!q) return index.search.slice(0, limit).map(s => ({ key: s.key, ref: s.ref, name: s.name }));
    const exact = [], prefix = [], contains = [];
    for (const s of index.search) {
      if (s.lk === q) exact.push(s);
      else if (s.lk.startsWith(q)) prefix.push(s);
      else if (s.lk.includes(q) || s.lref.includes(q) || s.lname.includes(q)) contains.push(s);
      if (exact.length + prefix.length >= limit) break;
    }
    return exact.concat(prefix, contains).slice(0, limit)
      .map(s => ({ key: s.key, ref: s.ref, name: s.name }));
  }

  /* ---------------- 필드 계산 ---------------- */

  /**
   * 한 건의 라벨에 들어갈 모든 값을 계산한다.
   * @param {object} row    라벨DB 행 (없으면 빈 객체)
   * @param {object} inputs {item, lot, sn, mfg(ISO), months, expAuto, exp(ISO)}
   * @returns {object} 필드 맵 (플레이스홀더 이름 → 문자열)
   */
  function computeFields(row, inputs) {
    const r = row || {};
    const f = {
      ITEM: String(inputs.item || '').trim(),
      LOT: String(inputs.lot || '').trim(),
      SN: String(inputs.sn || '').trim(),
    };
    for (const name in FIELD_COLS) {
      const v = r[FIELD_COLS[name]];
      f[name] = v == null ? '' : String(v).trim();
    }
    // 엑셀에서 '-' 는 "해당 없음"을 뜻한다. 표시용으로는 그대로 두되 빈값 판정에 쓰도록 표시.
    const mfg = parseISO(inputs.mfg);
    let exp = null;
    if (inputs.expAuto !== false) {
      if (mfg) {
        exp = edate(mfg, Number(inputs.months) || 36);
        exp.setDate(exp.getDate() - 1);          // 엑셀 =EDATE(D1,36)-1
      }
    } else {
      exp = parseISO(inputs.exp);
    }
    f.MFG = fmtISO(mfg); f.MFG6 = fmt6(mfg);
    f.EXP = fmtISO(exp); f.EXP6 = fmt6(exp);
    f._mfgDate = mfg; f._expDate = exp;

    const now = new Date();
    f.TODAY = fmtISO(now);
    f.NOW = `${p2(now.getHours())}:${p2(now.getMinutes())}`;
    f.DATE = `${String(now.getFullYear()).slice(2)}${p2(now.getMonth() + 1)}${p2(now.getDate())}`;
    f.TIME = `${p2(now.getHours())}${p2(now.getMinutes())}${p2(now.getSeconds())}`;

    // UDI — 값이 없는 AI 그룹은 통째로 생략한다
    const g = (ai, v) => (v ? `(${ai})${v}` : '');
    f.GTIN01 = g('01', f.GTIN);
    f.UDI_L1 = f.GTIN01 + g('10', f.LOT);
    f.UDI_L2 = g('17', f.EXP6) + g('240', f.ITEM) + g('21', f.SN);
    f.UDI_FULL = f.UDI_L1 + f.UDI_L2;
    return f;
  }

  /**
   * 플레이스홀더 치환.
   *   {FIELD}             필드 값
   *   {FIELD:YYYY-MM-DD}  날짜 필드를 지정 형식으로 (MFG/EXP만)
   *   {@AJ}               라벨DB 열 문자 직접 참조
   *   {FIELD|대체값}      값이 비었을 때 대체 문자열
   */
  function resolveText(text, fields, row) {
    if (text == null) return '';
    return String(text).replace(/\{(@?)([A-Za-z0-9_가-힣]+)(?::([^}|]+))?(?:\|([^}]*))?\}/g,
      (m, at, key, fmt, fallback) => {
        let v;
        if (at) {
          const col = key.toUpperCase();
          v = row ? row[col] : '';
        } else if (fmt && (key === 'MFG' || key === 'EXP' || key === 'TODAY')) {
          const d = key === 'TODAY' ? new Date() : fields['_' + key.toLowerCase() + 'Date'];
          v = formatDate(d, fmt);
        } else {
          v = fields ? fields[key] : undefined;
        }
        if (v == null || v === '') return fallback != null ? fallback : (fields && key in fields ? '' : m);
        return String(v);
      });
  }

  /** 치환되지 않고 남은 플레이스홀더를 찾는다 (검증용) */
  function unresolvedPlaceholders(text, fields, row) {
    const out = [];
    const resolved = resolveText(text, fields, row);
    const re = /\{@?[A-Za-z0-9_가-힣]+(?::[^}|]+)?(?:\|[^}]*)?\}/g;
    let m;
    while ((m = re.exec(resolved))) out.push(m[0]);
    return out;
  }

  /** 플레이스홀더 자동완성 목록 */
  function placeholderList() {
    const out = [];
    const seen = new Set();
    const push = (k) => {
      if (seen.has(k)) return;
      seen.add(k);
      out.push({ key: k, token: '{' + k + '}', label: FIELD_LABELS[k] || k });
    };
    ['ITEM', 'LOT', 'SN', 'MFG', 'EXP', 'EXP6', 'MFG6'].forEach(push);
    ['UDI_FULL', 'UDI_L1', 'UDI_L2', 'GTIN01', 'GTIN'].forEach(push);
    Object.keys(FIELD_COLS).forEach(push);
    ['TODAY', 'NOW', 'DATE', 'TIME'].forEach(push);
    return out;
  }

  /* ---------------- 검증 ---------------- */

  /**
   * 한 건의 라벨 데이터를 검증한다.
   * @returns Array<{level:'error'|'warn'|'info', code, msg, field?}>
   *          level 'error' = 출력 차단, 'warn' = 경고 후 진행 가능
   */
  function validateRecord(fields, row, rules) {
    const R = Object.assign({
      requireItem: true, requireLot: true, requireMfg: true, requireSn: false,
      checkGtin: true, checkExpPast: true, checkExpOrder: true,
    }, rules || {});
    const out = [];
    const add = (level, code, msg, field) => out.push({ level, code, msg, field });

    if (R.requireItem && !fields.ITEM) add('error', 'NO_ITEM', '품목번호를 입력하세요.', 'item');
    else if (fields.ITEM && !row) add('error', 'ITEM_NOT_FOUND', `품목번호 "${fields.ITEM}"를 라벨DB에서 찾을 수 없습니다.`, 'item');

    if (R.requireLot && !fields.LOT) add('error', 'NO_LOT', 'LOT 번호를 입력하세요.', 'lot');
    if (R.requireSn && !fields.SN) add('error', 'NO_SN', 'SN을 입력하세요.', 'sn');
    if (R.requireMfg && !fields.MFG) add('error', 'NO_MFG', '제조일(MFG)을 입력하세요.', 'mfg');

    if (!fields.EXP && fields.MFG) add('warn', 'NO_EXP', '유효일(EXP)이 계산되지 않았습니다. 유효기간 개월 수를 확인하세요.', 'exp');

    const mfg = fields._mfgDate, exp = fields._expDate;
    if (R.checkExpOrder && mfg && exp && exp <= mfg) {
      add('error', 'EXP_ORDER', `유효일(${fields.EXP})이 제조일(${fields.MFG})보다 빠르거나 같습니다.`, 'exp');
    }
    if (R.checkExpPast && exp) {
      const today = new Date(); today.setHours(0, 0, 0, 0);
      if (exp < today) add('warn', 'EXP_PAST', `유효일(${fields.EXP})이 이미 지났습니다.`, 'exp');
    }
    if (row) {
      if (!fields.GTIN) add('warn', 'NO_GTIN', '이 품목에 GTIN이 없습니다. UDI 바코드를 만들 수 없습니다.');
      else if (R.checkGtin && LB.barcode && !LB.barcode.gtinValid(fields.GTIN)) {
        const fix = LB.barcode.gtinCheckDigit(fields.GTIN.slice(0, -1));
        add('error', 'GTIN_CHECK', `GTIN 체크디짓이 틀렸습니다: ${fields.GTIN} (올바른 마지막 자리: ${fix})`);
      }
      if (!fields.REF) add('warn', 'NO_REF', '이 품목에 규격(REF)이 없습니다.');
      if (!fields.PRODUCT) add('warn', 'NO_PRODUCT', '이 품목에 제품명이 없습니다.');
    }
    return out;
  }

  /* LOT 형식 힌트 — 사내 규칙(YYMMDDNN 등)을 강제하지는 않고 안내만 */
  function lotHint(lot) {
    const s = String(lot || '');
    if (!s) return null;
    if (/\s/.test(s)) return 'LOT에 공백이 들어 있습니다.';
    if (/[^\w\-]/.test(s)) return 'LOT에 특수문자가 들어 있습니다. 바코드 판독에 문제가 될 수 있습니다.';
    return null;
  }

  return {
    FIELD_COLS, FIELD_LABELS, IMAGE_FIELDS,
    parseWorkbook, buildIndex, searchProducts,
    computeFields, resolveText, unresolvedPlaceholders, placeholderList,
    validateRecord, lotHint,
    edate, parseISO, fmtISO, fmt6, formatDate,
  };
})();
