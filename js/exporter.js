/* exporter.js — 프리플라이트 검증 · PDF 출력 (단일 / 연속)
 *
 * 출력 형태
 *   separate  라벨마다 개별 PDF 파일
 *   merged    전체를 한 PDF에 여러 쪽으로
 *
 * 품질
 *   래스터 DPI는 설정값(기본 600). 바코드는 LB.barcode가 모듈 정수 배율로 그리므로
 *   리샘플링되지 않는다. 텍스트는 LB.text가 mm 레이아웃을 그대로 확대해 화면과 일치한다.
 */
window.LB = window.LB || {};

LB.exporter = (() => {
  'use strict';

  /* ================= 프리플라이트 ================= */

  /**
   * 라벨 1건이 출력 가능한 상태인지 전부 검사한다.
   * @param {object} ctx {objects, label, fields, row, rules, dpi, imagesReady}
   * @returns {{errors:Array, warnings:Array, infos:Array, all:Array}}
   *          errors 가 있으면 출력을 막는다.
   */
  function preflight(ctx) {
    const { objects = [], label = {}, fields = {}, row = null, rules = {}, dpi = 600 } = ctx;
    const all = [];
    const add = (level, code, msg, objId) => all.push({ level, code, msg, objId });

    // 1) 데이터 자체
    for (const v of LB.data.validateRecord(fields, row, rules)) {
      all.push({ level: v.level, code: v.code, msg: v.msg, field: v.field });
    }

    // 2) 객체별
    const L = { w: label.w || 0, h: label.h || 0 };
    for (const o of objects) {
      if (o.visible === false) continue;
      const name = LB.barcode.objectLabel ? (o.name || o.id) : o.id;

      // 라벨 밖으로 나감
      if (rules.warnOutOfBounds !== false) {
        const out = o.x < -0.05 || o.y < -0.05 || o.x + o.w > L.w + 0.05 || o.y + o.h > L.h + 0.05;
        if (out) add('warn', 'OUT_OF_BOUNDS', `"${name}" 객체가 라벨 영역을 벗어났습니다.`, o.id);
      }

      if (o.type === 'text') {
        const raw = String(o.text || '');
        const txt = LB.data.resolveText(raw, fields, row);
        if (rules.warnUnresolved !== false) {
          const left = LB.data.unresolvedPlaceholders(raw, fields, row);
          if (left.length) {
            add('warn', 'UNRESOLVED', `"${name}" 의 ${left.join(', ')} 항목이 치환되지 않았습니다. 필드 이름을 확인하세요.`, o.id);
          }
        }
        if (rules.warnOverflow !== false && txt.trim()) {
          const b = LB.text.bounds(o, txt);
          if (b.overflowX || b.overflowY) {
            add('warn', 'TEXT_OVERFLOW',
              `"${name}" 텍스트가 영역을 넘칩니다. 영역을 키우거나 자동 축소를 켜세요.`, o.id);
          }
        }
      } else if (o.type === 'image') {
        if (o.sourceField && !o.dataUrl) {
          const fn = String(o.sourceField).startsWith('@')
            ? (row ? row[o.sourceField.slice(1).toUpperCase()] : '')
            : fields[o.sourceField];
          if (!fn) {
            if (rules.warnMissingImage !== false) {
              add('warn', 'IMG_NO_NAME', `"${name}" 슬롯: 이 품목의 ${o.sourceField} 파일명이 라벨DB에 없습니다.`, o.id);
            }
          } else {
            add('warn', 'IMG_MISSING', `"${name}" 슬롯: 이미지 파일을 불러오지 못했습니다 — ${fn}`, o.id);
          }
        }
      } else if (o.type === 'barcode') {
        const objCtx = { fields, objects, resolveText: (t) => LB.data.resolveText(t, fields, row) };
        const data = LB.barcode.resolveData(o, objCtx);
        if (!String(data || '').trim()) {
          add('error', 'BC_EMPTY', `"${LB.barcode.byId(o.symbology).n}" 바코드의 데이터가 비어 있습니다.`, o.id);
          continue;
        }
        for (const v of LB.barcode.validateData(o.symbology, data)) {
          add(v.level, v.code, `바코드: ${v.msg}`, o.id);
        }
        // 실제 생성이 되는지 (bwip-js가 최종 판정)
        const g = LB.barcode.generate(o.symbology, data, { scale: 2, humanReadable: !!o.humanReadable, heightMm: 10 });
        if (!g.canvas) add('error', 'BC_FAIL', `바코드를 만들 수 없습니다: ${g.err}`, o.id);
        else for (const v of LB.barcode.checkPhysical(o, data, dpi)) add(v.level, v.code, `바코드: ${v.msg}`, o.id);
      }
    }

    if (!objects.length) add('warn', 'NO_OBJECTS', '라벨에 객체가 없습니다.');

    return {
      all,
      errors: all.filter(i => i.level === 'error'),
      warnings: all.filter(i => i.level === 'warn'),
      infos: all.filter(i => i.level === 'info'),
    };
  }

  /* ================= 렌더 ================= */

  /** 배경/객체 이미지가 모두 디코딩될 때까지 기다린다 */
  function ensureImages(editor, objects, label) {
    const urls = [];
    if (label && label.bg) urls.push(label.bg);
    for (const o of objects) if (o.type === 'image' && o.dataUrl) urls.push(o.dataUrl);
    return Promise.all(urls.map(u => new Promise(res => {
      let img = editor.imageCache.get(u);
      if (!img) { img = new Image(); img.src = u; editor.imageCache.set(u, img); }
      if (img.complete) return res();
      img.addEventListener('load', res, { once: true });
      img.addEventListener('error', res, { once: true });
    })));
  }

  /**
   * 라벨 한 장을 지정 DPI 캔버스로 렌더링.
   * editor.drawObject 를 그대로 쓰므로 화면과 출력이 같은 코드 경로를 탄다.
   */
  function renderToCanvas(editor, { objects, label, dpi = 600, includeBg = true }) {
    const LW = label.w, LH = label.h;
    const pxPerMm = dpi / 25.4;
    const cv = document.createElement('canvas');
    cv.width = Math.max(1, Math.round(LW * pxPerMm));
    cv.height = Math.max(1, Math.round(LH * pxPerMm));
    const ctx = cv.getContext('2d');
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, cv.width, cv.height);
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, cv.width, cv.height);
    ctx.clip();
    if (includeBg && label.bg) {
      const bg = editor.getImage(label.bg);
      if (bg) ctx.drawImage(bg, 0, 0, cv.width, cv.height);
    }
    for (const o of objects) {
      if (o.visible === false) continue;
      editor.drawObject(ctx, o, pxPerMm, 0, 0, true);
    }
    ctx.restore();
    return cv;
  }

  /* 큰 라벨을 높은 DPI로 만들면 캔버스가 수천만 픽셀이 되어 브라우저가 멎는다.
   * A3(297x420mm) @600dpi = 7016x9921 = 약 7천만 픽셀.
   * 상한을 넘으면 DPI를 자동으로 낮추고 호출부에 알린다. */
  const MAX_PIXELS = 42e6;
  function effectiveDpi(label, dpi) {
    const px = (label.w / 25.4 * dpi) * (label.h / 25.4 * dpi);
    if (px <= MAX_PIXELS) return { dpi, reduced: false };
    const scale = Math.sqrt(MAX_PIXELS / px);
    const capped = Math.max(150, Math.floor(dpi * scale / 25) * 25);
    return { dpi: capped, reduced: true, requested: dpi };
  }

  /* 래스터 형식 결정.
   * PNG는 무손실이지만 jsPDF의 자체 인코더가 매우 느리다(17MP에서 약 3초).
   * JPEG는 같은 조건에서 약 0.3초이고, 실측 결과 이 템플릿의 바코드 9종이
   * 300dpi/품질 0.92에서도 모두 정상 판독되었다.
   * 그래서 작은 라벨은 무손실 PNG, 큰 라벨은 고품질 JPEG를 기본으로 한다. */
  const PNG_PIXEL_LIMIT = 8e6;
  function pickFormat(cv) {
    const mode = LB.settings.value('output.rasterFormat', 'auto');
    if (mode === 'png') return { fmt: 'PNG', q: null };
    if (mode === 'jpeg') return { fmt: 'JPEG', q: LB.settings.value('output.jpegQuality', 0.95) };
    return (cv.width * cv.height <= PNG_PIXEL_LIMIT)
      ? { fmt: 'PNG', q: null }
      : { fmt: 'JPEG', q: LB.settings.value('output.jpegQuality', 0.95) };
  }
  function encode(cv) {
    const f = pickFormat(cv);
    return { fmt: f.fmt, data: f.fmt === 'PNG' ? cv.toDataURL('image/png') : cv.toDataURL('image/jpeg', f.q) };
  }

  /* ================= 용지 배치(면付) =================
   * 라벨은 언제나 실물 크기로 그리고, 용지 모드일 때만
   * 그 결과를 용지 캔버스의 각 칸에 합성한다.
   */

  /** 라벨 한 장을 그려 캔버스로 (합성용) */
  function renderLabelTile(editor, { objects, label, dpi, includeBg }) {
    return renderToCanvas(editor, { objects, label, dpi, includeBg });
  }

  /**
   * 용지 한 페이지를 만든다.
   * @param slots [{tile:HTMLCanvasElement}] 페이지에 앉힐 라벨들 (최대 perPage개)
   */
  function composeSheet(slots, planObj, label, dpi, opt = {}) {
    const pxPerMm = dpi / 25.4;
    const cv = document.createElement('canvas');
    cv.width = Math.max(1, Math.round(planObj.paperW * pxPerMm));
    cv.height = Math.max(1, Math.round(planObj.paperH * pxPerMm));
    const ctx = cv.getContext('2d');
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, cv.width, cv.height);
    for (let i = 0; i < slots.length && i < planObj.perPage; i++) {
      const s = LB.paper.slotAt(planObj, i);
      const x = Math.round(s.x * pxPerMm), y = Math.round(s.y * pxPerMm);
      const w = Math.round(label.w * pxPerMm), h = Math.round(label.h * pxPerMm);
      if (slots[i] && slots[i].tile) ctx.drawImage(slots[i].tile, x, y, w, h);
      if (opt.outline) {
        ctx.save();
        ctx.strokeStyle = '#999';
        ctx.lineWidth = Math.max(1, 0.15 * pxPerMm);
        ctx.strokeRect(x + .5, y + .5, w, h);
        ctx.restore();
      }
      if (opt.cropMarks) LB.paper.drawCropMarks(ctx, planObj, i, label, pxPerMm);
    }
    return cv;
  }

  function newPdf(LW, LH) {
    const { jsPDF } = window.jspdf;
    return new jsPDF({
      unit: 'mm',
      format: [LW, LH],
      orientation: LW >= LH ? 'landscape' : 'portrait',
      compress: true,
    });
  }

  /* ================= 파일명 ================= */

  /** 파일명 규칙 치환. 파일명에 못 쓰는 문자는 제거한다. */
  function buildFileName(pattern, fields, label, row) {
    const base = LB.data.resolveText(pattern || '{ITEM}_{LOT}_{DATE}', fields, row);
    let name = base
      .replace(/[\\/:*?"<>|]/g, '-')
      .replace(/[\x00-\x1F]/g, '')
      .replace(/\s+/g, ' ')
      .replace(/^[.\s]+|[.\s]+$/g, '')
      .trim();
    if (!name) name = 'label';
    if (name.length > 120) name = name.slice(0, 120);
    return /\.pdf$/i.test(name) ? name : name + '.pdf';
  }

  /** 다운로드 폴백 */
  function download(blob, fileName) {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 4000);
  }

  /* ================= 단일 출력 ================= */

  /**
   * 용지 모드 단일 출력 — 한 페이지를 같은 라벨로 채우거나(fill) 1장만 앉힌다.
   */
  async function exportOneOnPaper(editor, opt, layout) {
    const S = LB.settings;
    const { objects, label, fields, row, pattern, conflict } = opt;
    const dpi = opt.dpi || S.value('output.dpi', 300);
    const includeBg = opt.includeBg !== undefined ? opt.includeBg : S.value('output.includeBg', true);
    const planObj = LB.paper.plan(label, layout);
    if (!planObj.fits) throw new Error(planObj.reason);

    if (opt.beforeJob) await opt.beforeJob(opt);
    await ensureImages(editor, objects, label);
    const eff = effectiveDpi({ w: planObj.paperW, h: planObj.paperH }, dpi);
    const tile = renderLabelTile(editor, { objects, label, dpi: eff.dpi, includeBg });
    const count = layout.repeat === 'one' ? 1 : planObj.perPage;
    const slots = Array.from({ length: count }, () => ({ tile }));
    const sheet = composeSheet(slots, planObj, label, eff.dpi, layout);
    tile.width = tile.height = 1;

    const enc = encode(sheet);
    const pdf = newPdf(planObj.paperW, planObj.paperH);
    pdf.addImage(enc.data, enc.fmt, 0, 0, planObj.paperW, planObj.paperH, undefined, 'FAST');
    const blob = pdf.output('blob');
    sheet.width = sheet.height = 1;

    const fileName = buildFileName(pattern || S.value('output.pattern'), fields, label, row);
    const written = await S.writeOutput(blob, fileName, conflict);
    const meta = { dpi: eff.dpi, format: enc.fmt, perPage: count, paper: `${planObj.paperW}×${planObj.paperH}mm` };
    if (written) return Object.assign({ ok: true, fileName: written.fileName, saved: 'folder', bytes: blob.size }, meta);
    download(blob, fileName);
    return Object.assign({ ok: true, fileName, saved: 'download', bytes: blob.size }, meta);
  }

  /**
   * @returns {{ok, fileName, saved:'folder'|'download', bytes}}
   */
  async function exportOne(editor, opt) {
    const layout = opt.layout || LB.settings.value('layout', LB.paper.DEFAULT_LAYOUT);
    if (layout && layout.paper && layout.paper !== 'label') {
      return exportOneOnPaper(editor, opt, layout);
    }
    const {
      objects = editor.state.objects, label = editor.state.label,
      fields = {}, row = null, dpi, includeBg, pattern, conflict,
    } = opt;
    const S = LB.settings;
    const useDpi = dpi || S.value('output.dpi', 600);
    const useBg = includeBg !== undefined ? includeBg : S.value('output.includeBg', true);

    if (opt.beforeJob) await opt.beforeJob(opt);
    await ensureImages(editor, objects, label);
    const eff = effectiveDpi(label, useDpi);
    const cv = renderToCanvas(editor, { objects, label, dpi: eff.dpi, includeBg: useBg });
    const enc = encode(cv);

    const pdf = newPdf(label.w, label.h);
    pdf.addImage(enc.data, enc.fmt, 0, 0, label.w, label.h, undefined, 'FAST');
    const blob = pdf.output('blob');
    cv.width = cv.height = 1;      // 큰 캔버스를 곧바로 해제

    const fileName = buildFileName(pattern || S.value('output.pattern'), fields, label, row);
    const written = await S.writeOutput(blob, fileName, conflict);
    const meta = { dpi: eff.dpi, dpiReduced: eff.reduced, requestedDpi: eff.requested, format: enc.fmt };
    if (written) return Object.assign({ ok: true, fileName: written.fileName, saved: 'folder', bytes: blob.size }, meta);
    download(blob, fileName);
    return Object.assign({ ok: true, fileName, saved: 'download', bytes: blob.size }, meta);
  }

  /* ================= 연속 출력 ================= */

  /**
   * 큐를 순차 처리한다. UI가 멈추지 않도록 각 건 사이에 양보한다.
   *
   * @param {Array} jobs  [{id, fields, row, objects?, copies?}]
   * @param {object} opt  {mode:'separate'|'merged', dpi, includeBg, pattern, conflict,
   *                       onProgress(i,total,job,result), shouldCancel(), isPaused()}
   * @returns {{done:number, failed:number, results:Array, cancelled:boolean, fileName?:string}}
   */
  /**
   * 용지 모드 연속 출력 — 큐의 여러 건을 한 용지에 차례로 앉히고,
   * 칸이 차면 다음 페이지로 넘긴다. 결과는 언제나 한 PDF다.
   */
  async function exportBatchOnPaper(editor, jobs, opt, layout) {
    const S = LB.settings;
    const label = editor.state.label;
    const dpi = opt.dpi || S.value('output.dpi', 300);
    const includeBg = opt.includeBg !== undefined ? opt.includeBg : S.value('output.includeBg', true);
    const planObj = LB.paper.plan(label, layout);
    if (!planObj.fits) throw new Error(planObj.reason);

    const eff = effectiveDpi({ w: planObj.paperW, h: planObj.paperH }, dpi);
    const expanded = [];
    for (const j of jobs) {
      const c = Math.max(1, j.copies || 1);
      for (let i = 0; i < c; i++) expanded.push(j);
    }
    const total = expanded.length;
    let pdf = null, pages = 0, cancelled = false, done = 0, failed = 0;
    const results = [];
    let slots = [];

    const flush = () => {
      if (!slots.length) return;
      const sheet = composeSheet(slots, planObj, label, eff.dpi, layout);
      if (!pdf) pdf = newPdf(planObj.paperW, planObj.paperH);
      else pdf.addPage([planObj.paperW, planObj.paperH], planObj.paperW >= planObj.paperH ? 'landscape' : 'portrait');
      const enc = encode(sheet);
      pdf.addImage(enc.data, enc.fmt, 0, 0, planObj.paperW, planObj.paperH, undefined, 'FAST');
      sheet.width = sheet.height = 1;
      for (const s of slots) if (s.tile) { s.tile.width = s.tile.height = 1; }
      slots = [];
      pages++;
    };

    for (let i = 0; i < expanded.length; i++) {
      if (opt.shouldCancel && opt.shouldCancel()) { cancelled = true; break; }
      while (opt.isPaused && opt.isPaused()) {
        await sleep(120);
        if (opt.shouldCancel && opt.shouldCancel()) { cancelled = true; break; }
      }
      if (cancelled) break;
      const job = expanded[i];
      try {
        if (opt.beforeJob) await opt.beforeJob(job);
        LB.text.invalidate();
        const objects = job.objects || editor.state.objects;
        await ensureImages(editor, objects, label);
        const tile = renderLabelTile(editor, { objects, label, dpi: eff.dpi, includeBg });
        slots.push({ tile, job });
        done++;
        if (slots.length >= planObj.perPage) flush();
        if (opt.onProgress) opt.onProgress(i + 1, total, job, { ok: true });
      } catch (e) {
        failed++;
        if (opt.onProgress) opt.onProgress(i + 1, total, job, { ok: false, error: e.message });
      }
      await sleep(0);
    }
    if (!cancelled) flush();

    let fileName = null;
    if (pdf && pages > 0) {
      const first = jobs[0] || {};
      const pat = (opt.pattern || S.value('output.pattern') || '') + `_${pages}쪽`;
      fileName = buildFileName(pat, first.fields || {}, label, first.row);
      const blob = pdf.output('blob');
      const written = await S.writeOutput(blob, fileName, opt.conflict);
      if (written) fileName = written.fileName;
      else download(blob, fileName);
    }
    return { done, failed, results, cancelled, fileName, pages, perPage: planObj.perPage, onPaper: true };
  }

  async function exportBatch(editor, jobs, opt = {}) {
    const S = LB.settings;
    const layoutCfg = opt.layout || S.value('layout', LB.paper.DEFAULT_LAYOUT);
    if (layoutCfg && layoutCfg.paper && layoutCfg.paper !== 'label') {
      return exportBatchOnPaper(editor, jobs, opt, layoutCfg);
    }
    const mode = opt.mode || S.value('output.mode', 'separate');
    const dpi = opt.dpi || S.value('output.dpi', 600);
    const includeBg = opt.includeBg !== undefined ? opt.includeBg : S.value('output.includeBg', true);
    const pattern = opt.pattern || S.value('output.pattern');
    const label = editor.state.label;

    const results = [];
    let done = 0, failed = 0, cancelled = false;
    let mergedPdf = null, mergedPages = 0;

    const total = jobs.reduce((s, j) => s + (j.copies || 1), 0);
    let index = 0;

    for (const job of jobs) {
      if (opt.shouldCancel && opt.shouldCancel()) { cancelled = true; break; }
      while (opt.isPaused && opt.isPaused()) {
        await sleep(120);
        if (opt.shouldCancel && opt.shouldCancel()) { cancelled = true; break; }
      }
      if (cancelled) break;

      const copies = Math.max(1, job.copies || 1);
      const objects = job.objects || editor.state.objects;

      let res;
      try {
        // 이 건의 데이터로 치환기를 바꿔 끼운다 (행마다 LOT/SN/날짜가 다르다)
        if (opt.beforeJob) await opt.beforeJob(job);
        LB.text.invalidate();          // 텍스트 레이아웃 캐시는 문자열 기준이므로 안전하지만
                                       // 폰트/치환 결과가 바뀌었을 수 있어 초기화한다
        await ensureImages(editor, objects, label);
        const eff = effectiveDpi(label, dpi);
        const cv = renderToCanvas(editor, { objects, label, dpi: eff.dpi, includeBg });
        const enc = encode(cv);
        const png = enc.data, imgFmt = enc.fmt;
        cv.width = cv.height = 1;

        if (mode === 'merged') {
          if (!mergedPdf) mergedPdf = newPdf(label.w, label.h);
          for (let c = 0; c < copies; c++) {
            if (mergedPages > 0) mergedPdf.addPage([label.w, label.h], label.w >= label.h ? 'landscape' : 'portrait');
            mergedPdf.addImage(png, imgFmt, 0, 0, label.w, label.h, undefined, 'FAST');
            mergedPages++;
            index++;
            if (opt.onProgress) opt.onProgress(index, total, job, null);
          }
          res = { ok: true, pages: copies };
        } else {
          const pdf = newPdf(label.w, label.h);
          pdf.addImage(png, imgFmt, 0, 0, label.w, label.h, undefined, 'FAST');
          // 같은 라벨을 여러 장 낼 때 파일명이 겹치지 않도록 한다.
          // 규칙에 {COPY}가 없으면 뒤에 붙여 준다.
          const multiPattern = (copies > 1 && !/\{COPY\}/.test(pattern || ''))
            ? (pattern || '') + '_{COPY}' : pattern;
          for (let c = 0; c < copies; c++) {
            const f = Object.assign({}, job.fields, copies > 1 ? { COPY: String(c + 1) } : null);
            const fileName = buildFileName(copies > 1 ? multiPattern : pattern, f, label, job.row);
            const blob = pdf.output('blob');
            const written = await S.writeOutput(blob, fileName, opt.conflict);
            const finalName = written ? written.fileName : fileName;
            if (!written) download(blob, finalName);
            index++;
            res = { ok: true, fileName: finalName, saved: written ? 'folder' : 'download' };
            if (opt.onProgress) opt.onProgress(index, total, job, res);
            if (c < copies - 1) await sleep(0);
          }
        }
        done += copies;
      } catch (e) {
        failed++;
        res = { ok: false, error: e.message || String(e) };
        index += copies;
        if (opt.onProgress) opt.onProgress(index, total, job, res);
      }

      results.push(Object.assign({ jobId: job.id }, res));

      // 이력 기록
      try {
        await LB.store.addHistory({
          item: job.fields && job.fields.ITEM, ref: job.fields && job.fields.REF,
          lot: job.fields && job.fields.LOT, sn: job.fields && job.fields.SN,
          mfg: job.fields && job.fields.MFG, exp: job.fields && job.fields.EXP,
          udi: job.fields && job.fields.UDI_FULL,
          copies, fileName: res && res.fileName, ok: !!(res && res.ok),
          error: res && res.error, mode,
        });
      } catch (_) {}

      await sleep(0);          // UI 양보
    }

    let mergedName = null;
    if (mergedPdf && mergedPages > 0 && !cancelled) {
      const first = jobs[0] || {};
      mergedName = buildFileName(
        (pattern || '') + (/\{/.test(pattern || '') ? `_${mergedPages}매` : `배치_${mergedPages}매`),
        first.fields || {}, label, first.row);
      const blob = mergedPdf.output('blob');
      const written = await S.writeOutput(blob, mergedName, opt.conflict);
      if (written) mergedName = written.fileName;
      else download(blob, mergedName);
    }

    return { done, failed, results, cancelled, fileName: mergedName, pages: mergedPages };
  }

  const sleep = (ms) => new Promise(r => setTimeout(r, ms));

  /** 미리보기용 PNG data URL (확인 대화상자 썸네일) */
  async function previewPng(editor, opt = {}) {
    const objects = opt.objects || editor.state.objects;
    const label = opt.label || editor.state.label;
    await ensureImages(editor, objects, label);
    const dpi = opt.dpi || Math.min(150, Math.max(72, 1200 / Math.max(label.w, label.h) * 25.4));
    const cv = renderToCanvas(editor, { objects, label, dpi, includeBg: opt.includeBg !== false });
    const url = cv.toDataURL('image/png');
    cv.width = cv.height = 1;
    return url;
  }

  return { preflight, renderToCanvas, buildFileName, exportOne, exportBatch, previewPng,
           ensureImages, download, effectiveDpi, pickFormat, MAX_PIXELS,
           composeSheet, renderLabelTile, exportOneOnPaper, exportBatchOnPaper };
})();
