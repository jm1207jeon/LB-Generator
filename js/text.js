/* text.js — 타이포그래피 엔진
 *
 * 목표: 화면(px/mm)과 PDF 출력(dpi/25.4)에서 **완전히 동일한** 텍스트 레이아웃.
 * 그래서 줄바꿈/정렬 계산은 mm 단위로 한 번만 하고(layout), 그리기만 배율을 곱한다(draw).
 *
 * 지원 속성 (한글 조판 표준 용어)
 *   sizePt        글자 크기 (pt)
 *   letterSpacing 자간  — 글자와 글자 사이 간격 (pt, 음수 가능)
 *   kerning       커닝  — 글자 쌍별 자동 간격 보정 (켬/끔)
 *   wordSpacing   어간  — 낱말과 낱말 사이 간격 (pt)
 *   lineHeight    행간  — 줄과 줄 사이 간격 (글자 크기 배수)
 *   hScale        장평  — 글자의 가로 비율 (%, 100 = 원본, 80 = 가로로 80%)
 *   align         좌/중앙/우/양쪽
 *   vAlign        상/중앙/하
 *   wrap          자동 줄바꿈
 *   autoShrink    영역을 넘치면 자동 축소
 *
 * 검증된 브라우저 사실 (이 저장소의 Chromium에서 실측)
 *   - ctx.letterSpacing 지원. 단 **마지막 글자 뒤에도 자간이 붙는다**.
 *     ("ABCDE" + 5px 자간 → 폭이 25px 증가 = 5글자×5px, 4칸×5px가 아님)
 *     따라서 정렬용 잉크 폭 = measureText().width - 자간 1개분.
 */
window.LB = window.LB || {};

LB.text = (() => {
  'use strict';

  const PT2MM = 25.4 / 72;
  const REF = 20;            // 측정 기준 배율 (px/mm). 이 배율로 재서 mm로 환산한다.
  const SEP = String.fromCharCode(1);

  /* 측정 전용 오프스크린 컨텍스트 (그리기와 분리) */
  let mctx = null;
  function measureCtx() {
    if (!mctx) {
      const cv = document.createElement('canvas');
      cv.width = cv.height = 8;
      mctx = cv.getContext('2d');
    }
    return mctx;
  }

  function fontString(o, px) {
    const style = o.italic ? 'italic ' : '';
    const weight = o.bold ? 'bold ' : '';
    const fam = o.font || 'Arial';
    return style + weight + px + 'px "' + fam + '"';
  }

  /* 자간·어간·커닝 적용 (미지원 브라우저에서는 조용히 무시) */
  function setSpacing(ctx, px, wordPx, kerning) {
    let ok = false;
    try { ctx.letterSpacing = px ? px + 'px' : '0px'; ok = true; } catch (_) {}
    try { ctx.wordSpacing = wordPx ? wordPx + 'px' : '0px'; } catch (_) {}
    try { ctx.fontKerning = (kerning === false) ? 'none' : 'normal'; } catch (_) {}
    return ok;
  }
  function clearSpacing(ctx) { setSpacing(ctx, 0, 0, true); }
  const SPACING_NATIVE = (() => {
    try { return 'letterSpacing' in measureCtx(); } catch (_) { return false; }
  })();

  /**
   * 문자열의 **잉크 폭**(mm). 자간이 마지막 글자 뒤에 붙는 것을 보정한다.
   * @param ctx REF 배율로 폰트가 설정된 컨텍스트
   */
  function inkWidthMm(ctx, str, lsPx) {
    if (!str) return 0;
    let w;
    if (SPACING_NATIVE) {
      w = ctx.measureText(str).width;
      if (lsPx) w -= lsPx;                       // ★ 후행 자간 보정
    } else {
      w = 0;
      const chars = Array.from(str);
      for (const ch of chars) w += ctx.measureText(ch).width;
      w += lsPx * (chars.length - 1);
    }
    return w / REF;
  }

  /* CJK(한중일) 판별 — 공백 없이도 어디서나 줄바꿈 가능 */
  const CJK_RE = /[ᄀ-ᇿ⺀-〿぀-ヿ㄰-㆏㐀-䶿一-鿿ꥠ-꥿가-퟿豈-﫿︰-﹏＀-｠￠-￦]/;
  /* 줄 앞에 올 수 없는 문자 (금칙) */
  const NO_LINE_START = '.,;:!?)]}>」』】〉》”’%‰°′″℃、。・ー';

  /**
   * 한 줄을 토큰으로 나눈다.
   *  - 공백: 그 뒤에서 줄바꿈 가능
   *  - CJK 글자: 한 글자가 토큰, 뒤에서 줄바꿈 가능
   *  - 그 외(영문/숫자): 단어 단위로 묶음
   */
  function tokenize(str) {
    const out = [];
    let buf = '';
    const flush = () => { if (buf) { out.push({ t: buf, brk: false }); buf = ''; } };
    for (const ch of Array.from(str)) {
      if (ch === ' ' || ch === '\t') { flush(); out.push({ t: ch, brk: true, sp: true }); }
      else if (CJK_RE.test(ch)) { flush(); out.push({ t: ch, brk: true, cjk: true }); }
      else if (ch === '-' || ch === '/') { buf += ch; flush(); if (out.length) out[out.length - 1].brk = true; }
      else buf += ch;
    }
    flush();
    return out;
  }

  /** 토큰 목록을 maxW(mm) 안에서 줄로 접는다. */
  function foldTokens(ctx, tokens, maxW, lsPx) {
    const lines = [];
    let cur = '';
    const wOf = (s) => inkWidthMm(ctx, s, lsPx);

    for (let i = 0; i < tokens.length; i++) {
      const tk = tokens[i];
      if (!cur && tk.sp) continue;               // 줄 맨 앞 공백은 버린다
      const cand = cur + tk.t;
      const candW = wOf(cand);

      if (candW <= maxW || !cur) {
        cur = cand;
        // 한 토큰 자체가 영역보다 길면 글자 단위로 강제 분해
        if (!tk.sp && cur === tk.t && candW > maxW && Array.from(tk.t).length > 1) {
          const chars = Array.from(tk.t);
          let part = '';
          for (let k = 0; k < chars.length; k++) {
            const test = part + chars[k];
            if (wOf(test) > maxW && part) { lines.push(part); part = chars[k]; }
            else part = test;
          }
          cur = part;
        }
      } else if (tk.t.length === 1 && NO_LINE_START.indexOf(tk.t) >= 0) {
        cur = cand;                              // 금칙: 줄 앞 금지 문자는 붙여 둔다
      } else {
        lines.push(cur.replace(/\s+$/, ''));
        cur = tk.sp ? '' : tk.t;
      }
    }
    if (cur) lines.push(cur.replace(/\s+$/, ''));
    return lines.length ? lines : [''];
  }

  /** 폰트 수직 메트릭(mm). 캐시. */
  const metricCache = new Map();
  function metrics(ctx, o, fontPx) {
    const key = [o.font, o.bold, o.italic, fontPx].join('|');
    let m = metricCache.get(key);
    if (!m) {
      const mm = ctx.measureText('Hg한');
      const asc = mm.actualBoundingBoxAscent || fontPx * 0.8;
      const desc = mm.actualBoundingBoxDescent || fontPx * 0.2;
      m = { ascMm: asc / REF, descMm: desc / REF };
      metricCache.set(key, m);
      if (metricCache.size > 400) metricCache.clear();
    }
    return m;
  }

  /**
   * 레이아웃 계산 — 배율과 무관한 mm 좌표계.
   * @param {object} o    텍스트 객체 (x,y,w,h,text,font,sizePt,...)
   * @param {string} text 치환이 끝난 실제 표시 문자열
   */
  function layoutRaw(o, text) {
    const ctx = measureCtx();
    const W = Math.max(0.1, o.w), H = Math.max(0.1, o.h);
    const hs = (o.hScale == null ? 100 : o.hScale) / 100 || 1;
    const wrap = o.wrap !== false;
    const lhMul = o.lineHeight || 1.15;

    // 압축을 고려한 실효 최대 폭: 가로로 hs배 눌리므로 원본 좌표계 한계는 W/hs
    const maxWEff = W / hs;

    const build = (sizePt) => {
      const fontPx = sizePt * PT2MM * REF;
      const lsPx = (o.letterSpacing || 0) * PT2MM * REF;
      const wsPx = (o.wordSpacing || 0) * PT2MM * REF;
      ctx.font = fontString(o, fontPx);
      setSpacing(ctx, SPACING_NATIVE ? lsPx : 0, wsPx, o.kerning !== false);

      const paras = String(text == null ? '' : text).split('\n');
      let raw = [];
      for (const p of paras) {
        if (!wrap) { raw.push(p); continue; }
        raw = raw.concat(foldTokens(ctx, tokenize(p), maxWEff, lsPx));
      }
      const lines = raw.map(t => ({ text: t, wMm: inkWidthMm(ctx, t, lsPx) }));
      const fontMm = sizePt * PT2MM;
      const met = metrics(ctx, o, fontPx);
      const lineHMm = fontMm * lhMul;
      const totalHMm = (lines.length - 1) * lineHMm + met.ascMm + met.descMm;
      const maxLineW = lines.reduce((a, l) => Math.max(a, l.wMm), 0);
      clearSpacing(ctx);
      return { lines, fontPx, lsPx, fontMm, lineHMm, totalHMm, maxLineW, met };
    };

    let sizePt = o.sizePt || 8;
    let r = build(sizePt);
    let shrunk = false;

    // 자동 축소: 이분 탐색 (줄바꿈이 크기에 의존하므로 매번 다시 접는다)
    if (o.autoShrink && (r.totalHMm > H + 0.01 || r.maxLineW * hs > W + 0.01)) {
      let lo = 1, hi = sizePt, best = null;
      for (let i = 0; i < 18 && hi - lo > 0.05; i++) {
        const mid = (lo + hi) / 2;
        const t = build(mid);
        if (t.totalHMm <= H && t.maxLineW * hs <= W) { best = mid; lo = mid; }
        else hi = mid;
      }
      if (best != null) { sizePt = Math.round(best * 10) / 10; r = build(sizePt); shrunk = true; }
    }

    // 세로 정렬
    const vAlign = o.vAlign || 'top';
    let startY;
    if (vAlign === 'middle') startY = (H - r.totalHMm) / 2 + r.met.ascMm;
    else if (vAlign === 'bottom') startY = H - r.totalHMm + r.met.ascMm;
    else startY = r.met.ascMm;

    // 가로 정렬 — 압축 후 시각 폭(wMm*hs) 기준으로 배치
    const align = o.align || 'left';
    const n = r.lines.length;
    const lines = r.lines.map((l, i) => {
      const visW = l.wMm * hs;
      let xMm = 0, spaceExtra = 0;
      if (align === 'center') xMm = (W - visW) / 2;
      else if (align === 'right') xMm = W - visW;
      else if (align === 'justify' && i < n - 1 && l.text.indexOf(' ') >= 0) {
        const gaps = l.text.split(' ').length - 1;
        if (gaps > 0) spaceExtra = (W - visW) / gaps / hs;   // 원본 좌표계 기준 추가 간격
      }
      return { text: l.text, xMm, wMm: l.wMm, visWMm: visW, spaceExtra };
    });

    return {
      lines, sizePt, fontPx: r.fontPx, lsPx: r.lsPx, fontMm: r.fontMm,
      lineHMm: r.lineHMm, totalHMm: r.totalHMm, startYMm: startY, hs,
      overflowX: r.maxLineW * hs > W + 0.01,
      overflowY: r.totalHMm > H + 0.01,
      shrunk,
    };
  }

  /* 레이아웃 캐시 — 같은 속성/문자열이면 재계산하지 않는다 */
  const layoutCache = new Map();
  function cacheKey(o, text) {
    return [o.font, o.sizePt, o.bold ? 1 : 0, o.italic ? 1 : 0, o.letterSpacing || 0,
      o.wordSpacing || 0, o.kerning === false ? 0 : 1,
      o.lineHeight || 1.15, o.hScale == null ? 100 : o.hScale, o.align, o.vAlign,
      o.wrap === false ? 0 : 1, o.autoShrink ? 1 : 0,
      Math.round(o.w * 100), Math.round(o.h * 100), text].join(SEP);
  }
  function layout(o, text) {
    const k = cacheKey(o, text);
    let v = layoutCache.get(k);
    if (!v) {
      v = layoutRaw(o, text);
      layoutCache.set(k, v);
      if (layoutCache.size > 800) layoutCache.clear();
    }
    return v;
  }
  function invalidate() { layoutCache.clear(); metricCache.clear(); }

  /**
   * 그리기. 레이아웃은 mm, 여기서만 배율을 곱한다.
   * @param ctx    대상 컨텍스트
   * @param o      텍스트 객체
   * @param text   치환된 문자열
   * @param scale  px per mm (화면=view.scale, 출력=dpi/25.4)
   * @param ox,oy  오프셋(px)
   */
  function draw(ctx, o, text, scale, ox, oy) {
    const L = layout(o, text);
    if (!L.lines.length) return L;
    const X = o.x * scale + ox, Y = o.y * scale + oy;
    const hs = L.hs;

    ctx.save();
    ctx.translate(X, Y);
    if (hs !== 1) ctx.scale(hs, 1);      // ★ 좌우 압축. 이후 u 좌표는 hs배로 눌려 그려진다
    ctx.font = fontString(o, L.sizePt * PT2MM * scale);
    ctx.fillStyle = o.color || '#000';
    ctx.textBaseline = 'alphabetic';
    ctx.textAlign = 'left';
    const lsPx = (o.letterSpacing || 0) * PT2MM * scale;
    const wsPx = (o.wordSpacing || 0) * PT2MM * scale;
    const native = setSpacing(ctx, lsPx, wsPx, o.kerning !== false) && SPACING_NATIVE;

    for (let i = 0; i < L.lines.length; i++) {
      const ln = L.lines[i];
      const u = (ln.xMm / hs) * scale;    // xMm은 압축 후 좌표 → 압축 전 좌표계로 환산
      const v = (L.startYMm + i * L.lineHMm) * scale;
      if (ln.spaceExtra) {
        let cx = u;
        const parts = ln.text.split(' ');
        const extra = ln.spaceExtra * scale;
        const spW = measureRun(ctx, ' ', lsPx, native);
        for (let k = 0; k < parts.length; k++) {
          drawRun(ctx, parts[k], cx, v, lsPx, native);
          cx += measureRun(ctx, parts[k], lsPx, native) + spW + extra + lsPx;
        }
      } else {
        drawRun(ctx, ln.text, u, v, lsPx, native);
      }
    }
    clearSpacing(ctx);
    ctx.restore();
    return L;
  }

  function drawRun(ctx, str, x, y, lsPx, native) {
    if (!str) return;
    if (native || !lsPx) { ctx.fillText(str, x, y); return; }
    let cx = x;
    for (const ch of Array.from(str)) {
      ctx.fillText(ch, cx, y);
      cx += ctx.measureText(ch).width + lsPx;
    }
  }
  function measureRun(ctx, str, lsPx, native) {
    if (!str) return 0;
    if (native) { const w = ctx.measureText(str).width; return lsPx ? w - lsPx : w; }
    let w = 0; const chars = Array.from(str);
    for (const ch of chars) w += ctx.measureText(ch).width;
    return w + lsPx * (chars.length - 1);
  }

  /** 객체가 실제로 차지하는 영역(mm) — 넘침 검사/가이드용 */
  function bounds(o, text) {
    const L = layout(o, text);
    const maxW = L.lines.reduce((a, l) => Math.max(a, l.visWMm), 0);
    return {
      wMm: maxW, hMm: L.totalHMm, overflowX: L.overflowX, overflowY: L.overflowY,
      sizePt: L.sizePt, shrunk: L.shrunk, lineCount: L.lines.length,
    };
  }

  /** 시스템에 실제로 설치된 폰트인지 확인 (폴백 감지) */
  function fontAvailable(family) {
    try {
      const ctx = measureCtx();
      const probe = 'mmmiiiWWW한글ABC';
      ctx.font = '40px "' + family + '", monospace';
      const a = ctx.measureText(probe).width;
      ctx.font = '40px monospace';
      const b = ctx.measureText(probe).width;
      ctx.font = '40px "' + family + '", serif';
      const c = ctx.measureText(probe).width;
      ctx.font = '40px serif';
      const d = ctx.measureText(probe).width;
      return !(Math.abs(a - b) < 0.5 && Math.abs(c - d) < 0.5);
    } catch (_) { return true; }
  }

  const FONTS = [
    { v: 'Arial', n: 'Arial' },
    { v: 'Helvetica', n: 'Helvetica' },
    { v: 'Times New Roman', n: 'Times New Roman' },
    { v: 'Courier New', n: 'Courier New' },
    { v: 'Tahoma', n: 'Tahoma' },
    { v: 'Verdana', n: 'Verdana' },
    { v: 'Calibri', n: 'Calibri' },
    { v: 'Segoe UI', n: 'Segoe UI' },
    { v: 'Malgun Gothic', n: '맑은 고딕' },
    { v: 'Batang', n: '바탕' },
    { v: 'Gulim', n: '굴림' },
    { v: 'Dotum', n: '돋움' },
  ];

  return {
    layout, draw, bounds, invalidate, fontString, inkWidthMm,
    FONTS, fontAvailable, PT2MM, REF, SPACING_NATIVE,
  };
})();
