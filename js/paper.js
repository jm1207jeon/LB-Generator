/* paper.js — 라벨 규격 · 용지 배치(면付)
 *
 * 설계 원칙
 *   캔버스와 출력의 기본 단위는 **라벨 한 장의 실제 크기**다.
 *   A4·A3 같은 용지는 "여러 장을 한 장에 앉혀 인쇄하는" 선택 사항일 뿐이다.
 *   그래서 label.w/h 는 언제나 실제 라벨 치수이고, 용지 배치는 출력 시점에만 개입한다.
 */
window.LB = window.LB || {};

LB.paper = (() => {
  'use strict';

  /* ---------------- 용지 규격 (mm) ---------------- */
  const PAPERS = [
    { v: 'label', n: '라벨 실물 크기 (용지 없이)', w: 0, h: 0 },
    { v: 'A4', n: 'A4 (210 × 297)', w: 210, h: 297 },
    { v: 'A3', n: 'A3 (297 × 420)', w: 297, h: 420 },
    { v: 'A5', n: 'A5 (148 × 210)', w: 148, h: 210 },
    { v: 'B4', n: 'B4 (257 × 364)', w: 257, h: 364 },
    { v: 'B5', n: 'B5 (182 × 257)', w: 182, h: 257 },
    { v: 'Letter', n: 'Letter (216 × 279)', w: 215.9, h: 279.4 },
    { v: 'Legal', n: 'Legal (216 × 356)', w: 215.9, h: 355.6 },
    { v: 'custom', n: '사용자 지정…', w: 0, h: 0 },
  ];
  const paperById = (v) => PAPERS.find(p => p.v === v) || PAPERS[0];

  /* ---------------- 라벨 규격 ----------------
   * 이 제품군의 A3 원판(297×420) 안에 배치된 실제 라벨들.
   * 배경 서식 이미지에서 경계를 실측해 얻은 값이다.
   * src = 원판에서의 위치 → 'A3 세트에서 라벨 추출' 기능이 이 좌표로 잘라낸다.
   */
  const SHEET = { w: 297, h: 420 };
  const PRODUCT_LABELS = [
    { v: 'PSL-001',   n: 'PSL-001 파우치 라벨',        w: 173.8, h: 26.3, src: { x: 59.5, y: 20.0 } },
    { v: 'BOX-PN',    n: '제품 라벨 (PN 포함)',         w: 173.8, h: 29.8, src: { x: 59.5, y: 46.3 } },
    { v: 'BOX-TAB',   n: '제품 라벨 (탭형)',            w: 173.8, h: 28.9, src: { x: 59.5, y: 76.1 } },
    { v: 'SPEC',      n: 'SPECIFICATION 규격표',        w: 173.8, h: 92.6, src: { x: 59.5, y: 105.0 } },
    { v: 'PML-001',   n: 'PML-001 UDI 라벨',            w: 173.8, h: 22.9, src: { x: 59.5, y: 197.6 } },
    { v: 'SMALL-1',   n: '소형 라벨 (좌상)',            w: 86.8,  h: 26.5, src: { x: 59.5, y: 220.5 } },
    { v: 'SMALL-2',   n: '소형 라벨 (우상)',            w: 87.0,  h: 26.5, src: { x: 146.3, y: 220.5 } },
    { v: 'SMALL-3',   n: '소형 라벨 (좌하)',            w: 86.8,  h: 23.5, src: { x: 59.5, y: 247.0 } },
    { v: 'SMALL-4',   n: '소형 라벨 (우하)',            w: 87.0,  h: 23.5, src: { x: 146.3, y: 247.0 } },
    { v: 'ICL-001-L', n: 'ICL-001 환자카드 (좌)',       w: 86.8,  h: 47.5, src: { x: 59.5, y: 270.5 } },
    { v: 'ICL-001-R', n: 'ICL-001 환자카드 (우)',       w: 87.0,  h: 47.5, src: { x: 146.3, y: 270.5 } },
    { v: 'PMFL-001',  n: 'PMFL-001 대형 라벨',          w: 173.8, h: 75.0, src: { x: 59.5, y: 318.0 } },
  ];

  /* 일반 라벨 규격 (감열 라벨 프린터에서 흔한 크기) */
  const COMMON_LABELS = [
    { v: 'C-100x150', n: '100 × 150 (4″×6″ 배송)', w: 100, h: 150 },
    { v: 'C-100x70',  n: '100 × 70',               w: 100, h: 70 },
    { v: 'C-100x50',  n: '100 × 50',               w: 100, h: 50 },
    { v: 'C-90x40',   n: '90 × 40',                w: 90,  h: 40 },
    { v: 'C-70x40',   n: '70 × 40',                w: 70,  h: 40 },
    { v: 'C-60x40',   n: '60 × 40',                w: 60,  h: 40 },
    { v: 'C-50x30',   n: '50 × 30',                w: 50,  h: 30 },
    { v: 'C-40x20',   n: '40 × 20',                w: 40,  h: 20 },
    { v: 'C-30x15',   n: '30 × 15',                w: 30,  h: 15 },
  ];

  const SHEET_PRESET = { v: 'SHEET-A3', n: 'A3 라벨 세트 원판 (전체)', w: 297, h: 420 };

  function allLabelPresets() {
    return [
      { group: '이 제품 라벨', items: PRODUCT_LABELS },
      { group: '일반 규격', items: COMMON_LABELS },
      { group: '원판', items: [SHEET_PRESET] },
    ];
  }
  function labelById(v) {
    return PRODUCT_LABELS.concat(COMMON_LABELS, [SHEET_PRESET]).find(l => l.v === v) || null;
  }
  /** 현재 치수와 일치하는 프리셋 찾기 (0.3mm 허용) */
  function matchPreset(w, h) {
    const all = PRODUCT_LABELS.concat(COMMON_LABELS, [SHEET_PRESET]);
    return all.find(l => Math.abs(l.w - w) < 0.3 && Math.abs(l.h - h) < 0.3) || null;
  }

  /* ---------------- 용지 배치(면付) 계산 ---------------- */

  const DEFAULT_LAYOUT = {
    paper: 'label',          // 'label' = 라벨 실물 크기로 1장씩
    customW: 210, customH: 297,
    orientation: 'auto',     // 'auto' | 'portrait' | 'landscape'
    marginMm: 8,
    gapX: 3, gapY: 3,
    align: 'center',         // 'center' | 'topleft'
    cropMarks: false,        // 재단선
    outline: false,          // 라벨 테두리선
    repeat: 'fill',          // 'fill' = 한 페이지를 같은 라벨로 채움 | 'one' = 1장만
  };

  /**
   * 용지에 라벨을 몇 개 앉힐 수 있는지 계산한다.
   * @param {{w,h}} label   라벨 실측 크기 (mm)
   * @param {object} opt    DEFAULT_LAYOUT 형태
   * @returns {{paperW,paperH,cols,rows,perPage,originX,originY,stepX,stepY,fits,reason}}
   */
  function plan(label, opt) {
    const o = Object.assign({}, DEFAULT_LAYOUT, opt || {});
    const p = paperById(o.paper);
    if (o.paper === 'label' || !p || (!p.w && o.paper !== 'custom')) {
      return {
        paperW: label.w, paperH: label.h, cols: 1, rows: 1, perPage: 1,
        originX: 0, originY: 0, stepX: label.w, stepY: label.h, fits: true, direct: true,
      };
    }
    let pw = o.paper === 'custom' ? Number(o.customW) || 210 : p.w;
    let ph = o.paper === 'custom' ? Number(o.customH) || 297 : p.h;

    // 방향 자동: 라벨이 더 많이 들어가는 쪽
    if (o.orientation === 'landscape' || (o.orientation === 'auto' && countFit(label, ph, pw, o) > countFit(label, pw, ph, o))) {
      const t = pw; pw = ph; ph = t;
    }
    const m = Math.max(0, Number(o.marginMm) || 0);
    const gx = Math.max(0, Number(o.gapX) || 0);
    const gy = Math.max(0, Number(o.gapY) || 0);
    const availW = pw - m * 2, availH = ph - m * 2;

    const cols = Math.max(0, Math.floor((availW + gx) / (label.w + gx)));
    const rows = Math.max(0, Math.floor((availH + gy) / (label.h + gy)));
    const fits = cols > 0 && rows > 0;

    const usedW = cols * label.w + (cols - 1) * gx;
    const usedH = rows * label.h + (rows - 1) * gy;
    let ox = m, oy = m;
    if (o.align === 'center' && fits) {
      ox = (pw - usedW) / 2;
      oy = (ph - usedH) / 2;
    }
    return {
      paperW: pw, paperH: ph, cols, rows, perPage: cols * rows,
      originX: ox, originY: oy, stepX: label.w + gx, stepY: label.h + gy,
      fits, direct: false,
      reason: fits ? '' : `라벨(${label.w}×${label.h}mm)이 여백을 뺀 인쇄 영역(${availW.toFixed(0)}×${availH.toFixed(0)}mm)보다 큽니다.`,
    };
  }

  function countFit(label, pw, ph, o) {
    const m = Math.max(0, Number(o.marginMm) || 0);
    const gx = Math.max(0, Number(o.gapX) || 0), gy = Math.max(0, Number(o.gapY) || 0);
    const c = Math.floor((pw - m * 2 + gx) / (label.w + gx));
    const r = Math.floor((ph - m * 2 + gy) / (label.h + gy));
    return Math.max(0, c) * Math.max(0, r);
  }

  /** i번째 칸의 좌상단 좌표 (mm) */
  function slotAt(planObj, i) {
    const c = i % planObj.cols, r = Math.floor(i / planObj.cols);
    return { x: planObj.originX + c * planObj.stepX, y: planObj.originY + r * planObj.stepY };
  }

  /** 사람이 읽을 요약 */
  function describe(label, opt) {
    const pl = plan(label, opt);
    if (pl.direct) return `라벨 실물 크기 ${label.w}×${label.h}mm 로 1장씩 출력`;
    if (!pl.fits) return `❌ ${pl.reason}`;
    return `${pl.paperW.toFixed(0)}×${pl.paperH.toFixed(0)}mm 용지에 ${pl.cols}열 × ${pl.rows}행 = 한 장에 ${pl.perPage}개`;
  }

  /** 재단선 그리기 (mm 좌표 → ctx는 px/mm 스케일이 적용된 상태로 받는다) */
  function drawCropMarks(ctx, planObj, i, label, pxPerMm) {
    const s = slotAt(planObj, i);
    const L = 3;                              // 재단선 길이 mm
    ctx.save();
    ctx.strokeStyle = '#000';
    ctx.lineWidth = Math.max(0.5, 0.2 * pxPerMm);
    const pts = [
      [s.x, s.y, -1, 0], [s.x, s.y, 0, -1],
      [s.x + label.w, s.y, 1, 0], [s.x + label.w, s.y, 0, -1],
      [s.x, s.y + label.h, -1, 0], [s.x, s.y + label.h, 0, 1],
      [s.x + label.w, s.y + label.h, 1, 0], [s.x + label.w, s.y + label.h, 0, 1],
    ];
    for (const [x, y, dx, dy] of pts) {
      ctx.beginPath();
      ctx.moveTo((x + dx * 0.8) * pxPerMm, (y + dy * 0.8) * pxPerMm);
      ctx.lineTo((x + dx * (0.8 + L)) * pxPerMm, (y + dy * (0.8 + L)) * pxPerMm);
      ctx.stroke();
    }
    ctx.restore();
  }

  return {
    PAPERS, PRODUCT_LABELS, COMMON_LABELS, SHEET, SHEET_PRESET, DEFAULT_LAYOUT,
    paperById, labelById, matchPreset, allLabelPresets,
    plan, slotAt, describe, drawCropMarks,
  };
})();
