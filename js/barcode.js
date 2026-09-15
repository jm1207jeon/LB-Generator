/* barcode.js — 바코드 엔진 (DataMatrix / GS1-128 / Code128 / QR)
 *
 * 핵심 설계
 *  1) 데이터 바인딩 3종
 *     - field      : 계산된 필드 직접 (UDI_FULL, GTIN01, LOT …)
 *     - expression : 플레이스홀더 템플릿 자유 조합  예) "(01){GTIN}(10){LOT}"
 *     - object     : **캔버스의 다른 객체 텍스트를 그대로 링크** (순환 참조 차단)
 *  2) 모듈 정수 배율 렌더링
 *     바코드는 리샘플링되면 판독이 깨진다. 모듈 1개가 정수 픽셀이 되도록 배율을
 *     역산해 그린다(fitMode 'module'). 'stretch'는 영역을 꽉 채우지만 품질을 포기한다.
 *  3) GS1 검증
 *     bwip-js 4.11 이 AI 길이/형식을 자체 검증하고 구체적 오류를 던진다
 *     (예: "AI 01: Too short"). 그 메시지를 사용자에게 그대로 전달하고,
 *     GTIN 체크디짓은 여기서 따로 계산해 검증한다.
 *
 * 검증된 bwip-js 4.11.2 동작 (이 저장소의 Chromium에서 실측)
 *  - bcid 'gs1-128' 은 괄호 표기 "(01)…(10)…" 를 그대로 받는다. parsefnc/^FNC1 는 오류.
 *  - scale 은 완전 선형 (scale 2 → 64px, scale 4 → 128px).
 *  - height 단위는 mm (72dpi 기준). 픽셀 높이 = height * 2.8346 * scale.
 *  - includetext 로 HRI(사람이 읽는 문자) 표시 가능.
 */
window.LB = window.LB || {};

LB.barcode = (() => {
  'use strict';

  const MM_PER_PX_AT_SCALE1 = 1 / 2.8346;   // bwip-js: 1mm = 2.8346px (scale 1)

  const SYMBOLOGIES = [
    { v: 'gs1datamatrix', n: 'GS1 DataMatrix', d2: true,  gs1: true,  note: 'UDI 표준 (의료기기)' },
    { v: 'gs1-128',       n: 'GS1-128',        d2: false, gs1: true,  note: '물류/포장 1D' },
    { v: 'datamatrix',    n: 'DataMatrix (일반)', d2: true,  gs1: false, note: '자유 텍스트' },
    { v: 'code128',       n: 'Code 128',       d2: false, gs1: false, note: '자유 텍스트 1D' },
    { v: 'qrcode',        n: 'QR Code',        d2: true,  gs1: false, note: '자유 텍스트 2D' },
  ];
  const byId = (v) => SYMBOLOGIES.find(s => s.v === v) || SYMBOLOGIES[0];

  /* ---------------- GS1 유틸 ---------------- */

  /** GTIN-8/12/13/14 체크디짓 검증 */
  function gtinValid(gtin) {
    const g = String(gtin || '').replace(/\D/g, '');
    if (![8, 12, 13, 14].includes(g.length)) return false;
    const body = g.slice(0, -1), chk = Number(g.slice(-1));
    let sum = 0;
    // 오른쪽에서 왼쪽으로 3,1,3,1…  (본문 길이에 따라 시작 가중치가 달라진다)
    for (let i = 0; i < body.length; i++) {
      const fromRight = body.length - 1 - i;
      sum += Number(body[i]) * (fromRight % 2 === 0 ? 3 : 1);
    }
    return (10 - (sum % 10)) % 10 === chk;
  }

  /** 본문으로부터 체크디짓 계산 */
  function gtinCheckDigit(body) {
    const b = String(body || '').replace(/\D/g, '');
    let sum = 0;
    for (let i = 0; i < b.length; i++) {
      const fromRight = b.length - 1 - i;
      sum += Number(b[i]) * (fromRight % 2 === 0 ? 3 : 1);
    }
    return (10 - (sum % 10)) % 10;
  }

  /* GS1 응용식별자(AI) 사전 — 고정길이 여부가 FNC1 구분자 필요성을 결정한다 */
  const AI_TABLE = {
    '00': { n: 'SSCC', len: 18, fixed: true },
    '01': { n: 'GTIN', len: 14, fixed: true },
    '02': { n: '포함 GTIN', len: 14, fixed: true },
    '10': { n: 'LOT', max: 20, fixed: false },
    '11': { n: '제조일 YYMMDD', len: 6, fixed: true, date: true },
    '12': { n: '지급기일', len: 6, fixed: true, date: true },
    '13': { n: '포장일', len: 6, fixed: true, date: true },
    '15': { n: '품질유지기한', len: 6, fixed: true, date: true },
    '16': { n: '판매기한', len: 6, fixed: true, date: true },
    '17': { n: '사용기한 YYMMDD', len: 6, fixed: true, date: true },
    '20': { n: '변형', len: 2, fixed: true },
    '21': { n: 'SN(일련번호)', max: 20, fixed: false },
    '30': { n: '수량', max: 8, fixed: false },
    '240': { n: '추가 품목식별', max: 30, fixed: false },
    '241': { n: '고객 품번', max: 30, fixed: false },
    '10x': { n: '기타', max: 30, fixed: false },
  };

  /** "(01)0880…(10)26041086" → [{ai,value,def}] */
  function parseAIs(text) {
    const out = [];
    const re = /\((\d{2,4})\)([^(]*)/g;
    let m;
    while ((m = re.exec(String(text || '')))) {
      out.push({ ai: m[1], value: m[2], def: AI_TABLE[m[1]] || null });
    }
    return out;
  }

  /**
   * 바코드 데이터 사전 검증. bwip-js 호출 전에 사람이 읽을 수 있는 문제를 잡는다.
   * @returns {Array<{level:'error'|'warn', code:string, msg:string}>}
   */
  function validateData(symbology, text) {
    const sym = byId(symbology);
    const issues = [];
    const s = String(text || '');
    if (!s.trim()) {
      issues.push({ level: 'error', code: 'EMPTY', msg: '바코드 데이터가 비어 있습니다.' });
      return issues;
    }
    if (!sym.gs1) return issues;

    if (s.indexOf('(') < 0) {
      issues.push({ level: 'warn', code: 'NO_AI', msg: 'GS1 심볼로지인데 응용식별자 괄호 표기가 없습니다. 예: (01)08806367087911' });
      return issues;
    }
    const ais = parseAIs(s);
    if (!ais.length) {
      issues.push({ level: 'error', code: 'AI_PARSE', msg: '응용식별자를 해석할 수 없습니다. (01)…(10)… 형식인지 확인하세요.' });
      return issues;
    }
    for (const a of ais) {
      const d = a.def;
      if (!d) { issues.push({ level: 'warn', code: 'AI_UNKNOWN', msg: `AI (${a.ai})는 사전에 없는 식별자입니다.` }); continue; }
      if (!a.value) { issues.push({ level: 'error', code: 'AI_EMPTY', msg: `AI (${a.ai}) ${d.n} 값이 비어 있습니다.` }); continue; }
      if (d.fixed && d.len && a.value.length !== d.len) {
        issues.push({ level: 'error', code: 'AI_LEN', msg: `AI (${a.ai}) ${d.n}는 정확히 ${d.len}자리여야 합니다. 현재 ${a.value.length}자리.` });
      }
      if (!d.fixed && d.max && a.value.length > d.max) {
        issues.push({ level: 'error', code: 'AI_LEN', msg: `AI (${a.ai}) ${d.n}는 최대 ${d.max}자리입니다. 현재 ${a.value.length}자리.` });
      }
      if (d.date && /^\d{6}$/.test(a.value)) {
        const mo = Number(a.value.slice(2, 4)), dy = Number(a.value.slice(4, 6));
        if (mo < 1 || mo > 12) issues.push({ level: 'error', code: 'AI_DATE', msg: `AI (${a.ai}) 날짜의 월이 올바르지 않습니다: ${a.value}` });
        else if (dy > 31) issues.push({ level: 'error', code: 'AI_DATE', msg: `AI (${a.ai}) 날짜의 일이 올바르지 않습니다: ${a.value}` });
      }
      if (a.ai === '01' && /^\d+$/.test(a.value) && !gtinValid(a.value)) {
        issues.push({
          level: 'error', code: 'GTIN_CHECK',
          msg: `GTIN 체크디짓이 틀렸습니다: ${a.value} (올바른 마지막 자리: ${gtinCheckDigit(a.value.slice(0, -1))})`,
        });
      }
    }
    return issues;
  }

  /* ---------------- 데이터 바인딩 ---------------- */

  const FIELD_CHOICES = [
    { v: 'UDI_FULL', n: 'UDI 전체 (01)(10)(17)(240)(21)' },
    { v: 'UDI_L1',   n: 'UDI 1행 (01)(10)' },
    { v: 'UDI_L2',   n: 'UDI 2행 (17)(240)(21)' },
    { v: 'GTIN01',   n: 'UDI-DI (01)만' },
    { v: 'GTIN',     n: 'GTIN 숫자만' },
    { v: 'LOT',      n: 'LOT' },
    { v: 'SN',       n: 'SN' },
    { v: 'ITEM',     n: '품목번호' },
    { v: 'REF',      n: '규격 (REF)' },
  ];

  /**
   * 바코드 객체의 실제 데이터 문자열을 구한다.
   * @param {object} o   바코드 객체
   * @param {object} ctxt {fields, resolveText(str), objects, textOf(obj)}
   * @param {Set}    seen 순환 참조 감지용
   */
  function resolveData(o, ctxt, seen) {
    const src = o.source || 'field';
    if (src === 'field') {
      const v = (ctxt.fields || {})[o.binding || 'UDI_FULL'];
      return v == null ? '' : String(v);
    }
    if (src === 'expression') {
      return ctxt.resolveText ? ctxt.resolveText(o.expression || '') : String(o.expression || '');
    }
    if (src === 'object') {
      const guard = seen || new Set();
      if (guard.has(o.id)) return '';                       // 순환 참조 차단
      guard.add(o.id);
      const target = (ctxt.objects || []).find(t => t.id === o.linkObjectId);
      if (!target) return '';
      if (target.type === 'text') {
        return ctxt.resolveText ? ctxt.resolveText(target.text || '') : String(target.text || '');
      }
      if (target.type === 'barcode') return resolveData(target, ctxt, guard);
      return '';
    }
    return '';
  }

  /** 링크 대상으로 고를 수 있는 객체 목록 (자기 자신과 순환은 제외) */
  function linkableObjects(objects, selfId) {
    const out = [];
    for (const t of objects || []) {
      if (t.id === selfId) continue;
      if (t.type === 'text') out.push({ id: t.id, label: objectLabel(t) });
      else if (t.type === 'barcode' && t.source !== 'object') out.push({ id: t.id, label: objectLabel(t) });
    }
    return out;
  }
  function objectLabel(t) {
    if (t.name) return t.name;
    if (t.type === 'text') {
      const s = String(t.text || '').replace(/\n/g, ' ').trim();
      return s ? (s.length > 24 ? s.slice(0, 24) + '…' : s) : '(빈 텍스트)';
    }
    if (t.type === 'barcode') return byId(t.symbology).n;
    return t.type;
  }

  /* ---------------- 생성 & 캐시 ---------------- */

  const cache = new Map();          // key -> {canvas, modulesW, modulesH, err}
  function cacheKey(sym, text, scale, hri, heightMm) {
    return [sym, scale, hri ? 1 : 0, heightMm || 0, text].join('|');
  }
  function invalidate() { cache.clear(); }

  /**
   * bwip-js 로 실제 생성. 실패하면 {err} 를 담아 반환(예외를 던지지 않는다).
   * @returns {{canvas:HTMLCanvasElement|null, modulesW:number, modulesH:number, err:string|null}}
   */
  function generate(symbology, text, { scale = 4, humanReadable = false, heightMm = 10 } = {}) {
    const sym = byId(symbology);
    const key = cacheKey(sym.v, text, scale, humanReadable, sym.d2 ? 0 : heightMm);
    const hit = cache.get(key);
    if (hit) return hit;

    let res;
    try {
      if (typeof bwipjs === 'undefined') throw new Error('bwip-js 라이브러리를 찾을 수 없습니다.');
      const cv = document.createElement('canvas');
      const opts = {
        bcid: sym.v,
        text: String(text),
        scale: Math.max(1, Math.round(scale)),
        paddingwidth: sym.d2 ? 1 : 2,
        paddingheight: sym.d2 ? 1 : 2,
        backgroundcolor: 'FFFFFF',
      };
      if (!sym.d2) opts.height = Math.max(2, heightMm);
      if (humanReadable) { opts.includetext = true; opts.textxalign = 'center'; }
      bwipjs.toCanvas(cv, opts);
      res = {
        canvas: cv, err: null,
        modulesW: cv.width / opts.scale,
        modulesH: cv.height / opts.scale,
        scale: opts.scale,
      };
    } catch (e) {
      const raw = String((e && e.message) || e);
      res = { canvas: null, err: humanizeError(raw), rawErr: raw, modulesW: 0, modulesH: 0, scale };
    }
    cache.set(key, res);
    if (cache.size > 300) {
      // 오래된 절반 제거
      const keys = Array.from(cache.keys()).slice(0, 150);
      for (const k of keys) cache.delete(k);
    }
    return res;
  }

  /** bwip-js/BWIPP 오류 메시지를 한국어로 */
  function humanizeError(raw) {
    const m = String(raw);
    const map = [
      [/GS1valueTooShort.*AI (\d+)/, (g) => `AI (${g[1]}) 값이 너무 짧습니다.`],
      [/GS1valueTooLong.*AI (\d+)/, (g) => `AI (${g[1]}) 값이 너무 깁니다.`],
      [/GS1aiMissingOpenParen/, () => 'GS1 데이터는 (01) 같은 괄호 표기로 시작해야 합니다.'],
      [/GS1badAI|unknownAI/i, () => '알 수 없는 응용식별자(AI)입니다.'],
      [/badCharacter|characterNotAllowed/i, () => '이 심볼로지가 지원하지 않는 문자가 포함되어 있습니다.'],
      [/emptyText|textIsEmpty/i, () => '바코드 데이터가 비어 있습니다.'],
      [/unknown encoder|bcid/i, () => '지원하지 않는 바코드 종류입니다.'],
    ];
    for (const [re, fn] of map) {
      const g = m.match(re);
      if (g) return fn(g);
    }
    return m.replace(/^bwipp\.\w+#\d+:\s*/, '');
  }

  /**
   * 목표 픽셀 크기에 맞는 **정수 모듈 배율**을 역산한다.
   * 모듈이 정수 픽셀이어야 리샘플링 없이 선명하게 찍힌다.
   */
  function pickScale(symbology, text, targetPxW, humanReadable, heightMm) {
    const probe = generate(symbology, text, { scale: 1, humanReadable, heightMm });
    if (!probe.canvas) return { scale: 4, probe };
    const modules = probe.canvas.width;              // scale 1 → 1모듈 = 1px
    const k = Math.max(1, Math.floor(targetPxW / Math.max(1, modules)));
    return { scale: Math.min(40, k), probe, modules };
  }

  /**
   * 바코드를 캔버스에 그린다.
   * @param ctx    대상 2D 컨텍스트
   * @param o      바코드 객체
   * @param data   해석된 데이터 문자열
   * @param scale  px per mm
   * @param ox,oy  오프셋(px)
   * @returns {{ok:boolean, err:string|null, drawnW:number, drawnH:number, quality:string}}
   */
  function draw(ctx, o, data, scale, ox, oy) {
    const X = o.x * scale + ox, Y = o.y * scale + oy;
    const W = o.w * scale, H = o.h * scale;
    const sym = byId(o.symbology);
    const hri = !!o.humanReadable;
    const fitMode = o.fitMode || 'module';

    if (!String(data || '').trim()) return { ok: false, err: '데이터 없음', drawnW: 0, drawnH: 0 };

    const heightMm = sym.d2 ? 0 : Math.max(2, o.h * (hri ? 0.72 : 0.92));
    const { scale: k } = pickScale(sym.v, data, W, hri, heightMm);
    const g = generate(sym.v, data, { scale: k, humanReadable: hri, heightMm });
    if (!g.canvas) return { ok: false, err: g.err, drawnW: 0, drawnH: 0 };

    const cv = g.canvas;
    let dw, dh;
    if (fitMode === 'stretch') {
      dw = W; dh = H;
    } else if (sym.d2) {
      // 2D: 정사각 비율 유지 + 정수 모듈
      const f = Math.min(W / cv.width, H / cv.height);
      const modPx = Math.max(1, Math.floor(f * k)) / k;     // 모듈이 정수 px가 되도록
      dw = cv.width * modPx; dh = cv.height * modPx;
    } else {
      // 1D: 가로는 정수 모듈, 세로는 영역을 채움(막대는 세로로 균일해 늘려도 무해)
      const f = Math.min(1, W / cv.width);
      const modPx = Math.max(1, Math.floor(f * k)) / k;
      dw = cv.width * modPx; dh = H;
    }
    dw = Math.min(dw, W); dh = Math.min(dh, H);

    // 영역 내 정렬
    const fit = o.fit || 'center';
    let dx = X + (W - dw) / 2;
    if (fit === 'left') dx = X;
    else if (fit === 'right') dx = X + W - dw;
    const vfit = o.vFit || 'middle';
    let dy = Y + (H - dh) / 2;
    if (vfit === 'top') dy = Y;
    else if (vfit === 'bottom') dy = Y + H - dh;

    const prevSmooth = ctx.imageSmoothingEnabled;
    ctx.imageSmoothingEnabled = false;
    if (o.rotation) {
      ctx.save();
      ctx.translate(dx + dw / 2, dy + dh / 2);
      ctx.rotate((o.rotation * Math.PI) / 180);
      ctx.drawImage(cv, -dw / 2, -dh / 2, dw, dh);
      ctx.restore();
    } else {
      ctx.drawImage(cv, dx, dy, dw, dh);
    }
    ctx.imageSmoothingEnabled = prevSmooth;

    // 품질 판정: 1모듈이 화면/출력에서 몇 px인가
    const modulePx = dw / Math.max(1, cv.width / k);
    let quality = 'ok';
    if (fitMode === 'stretch') quality = 'stretched';
    else if (modulePx < 2) quality = 'low';
    return { ok: true, err: null, drawnW: dw, drawnH: dh, modulePx, quality };
  }

  /**
   * 물리 규격 점검 (mm 단위). 출력 전 프리플라이트에서 사용.
   * GS1-128 최소 높이: 6.35mm 또는 폭의 15% 중 큰 값 (GS1 General Specifications)
   * DataMatrix 최소 X-dimension(모듈): 0.254mm (의료기기 직접표시 기준 완화값 0.1mm)
   */
  function checkPhysical(o, data, dpi) {
    const sym = byId(o.symbology);
    const issues = [];
    if (!String(data || '').trim()) return issues;
    const probe = generate(sym.v, data, { scale: 1, humanReadable: !!o.humanReadable, heightMm: sym.d2 ? 0 : Math.max(2, o.h * 0.9) });
    if (!probe.canvas) return issues;

    const modulesW = probe.canvas.width;
    const xDimMm = o.w / Math.max(1, modulesW);
    if (sym.d2) {
      if (xDimMm < 0.25) {
        issues.push({
          level: xDimMm < 0.15 ? 'error' : 'warn', code: 'BC_XDIM',
          msg: `바코드 모듈 크기가 ${xDimMm.toFixed(3)}mm로 작습니다. GS1 권장 최소 0.254mm — 영역을 키우거나 데이터를 줄이세요.`,
        });
      }
    } else {
      const minH = Math.max(6.35, o.w * 0.15);
      if (o.h < minH) {
        issues.push({
          level: 'warn', code: 'BC_HEIGHT',
          msg: `GS1-128 높이가 ${o.h.toFixed(1)}mm입니다. 권장 최소 ${minH.toFixed(1)}mm (6.35mm 또는 폭의 15%).`,
        });
      }
      if (xDimMm < 0.25) {
        issues.push({ level: 'warn', code: 'BC_XDIM', msg: `막대 폭이 ${xDimMm.toFixed(3)}mm로 좁습니다. 권장 최소 0.25mm.` });
      }
    }
    // 출력 해상도에서 모듈이 정수 픽셀인지
    if (dpi) {
      const modulePx = xDimMm * (dpi / 25.4);
      if (modulePx < 2) {
        issues.push({ level: 'warn', code: 'BC_DPI', msg: `${dpi}dpi에서 모듈이 ${modulePx.toFixed(1)}px입니다. 판독 안정성을 위해 해상도를 높이세요.` });
      }
    }
    return issues;
  }

  return {
    SYMBOLOGIES, FIELD_CHOICES, AI_TABLE, byId,
    gtinValid, gtinCheckDigit, parseAIs, validateData,
    resolveData, linkableObjects, objectLabel,
    generate, draw, checkPhysical, invalidate, humanizeError,
  };
})();
