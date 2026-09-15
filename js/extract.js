/* extract.js — A3 원판에서 라벨 한 장을 떼어내기
 *
 * 원본 엑셀의 서식은 A3(297×420) 한 장에 12종의 라벨이 모여 있는 "원판"이다.
 * 실제 작업은 라벨 한 장 단위로 하므로, 원판에서 원하는 라벨만 잘라
 * 그 라벨의 실제 크기를 가진 독립 서식으로 만든다.
 *
 * 잘라내는 것
 *   1) 영역 안에 들어가는 객체 — 좌표를 라벨 원점 기준으로 옮긴다
 *   2) 배경 서식 이미지 — 해당 영역만 잘라 새 이미지로 만든다
 */
window.LB = window.LB || {};

LB.extract = (() => {
  'use strict';

  /** 객체가 영역 안에 있는가 (일부만 걸쳐도 포함할지는 tolerance로) */
  function inside(o, r, tol) {
    const t = tol == null ? 0.8 : tol;
    return o.x >= r.x - t && o.y >= r.y - t &&
           o.x + o.w <= r.x + r.w + t && o.y + o.h <= r.y + r.h + t;
  }

  /** 영역과 조금이라도 겹치는가 */
  function overlaps(o, r) {
    return o.x < r.x + r.w && o.x + o.w > r.x && o.y < r.y + r.h && o.y + o.h > r.y;
  }

  /**
   * 배경 이미지의 일부를 잘라 새 data URL로.
   * @param {string} bgUrl   원판 배경 (data URL)
   * @param {{w,h}} sheet    원판 크기 (mm)
   * @param {{x,y,w,h}} rect 잘라낼 영역 (mm)
   */
  function cropBackground(bgUrl, sheet, rect) {
    return new Promise((resolve, reject) => {
      if (!bgUrl) { resolve(''); return; }
      const img = new Image();
      img.onload = () => {
        try {
          const sx = img.naturalWidth / sheet.w;      // px per mm
          const sy = img.naturalHeight / sheet.h;
          const cv = document.createElement('canvas');
          cv.width = Math.max(1, Math.round(rect.w * sx));
          cv.height = Math.max(1, Math.round(rect.h * sy));
          const ctx = cv.getContext('2d');
          ctx.fillStyle = '#fff';
          ctx.fillRect(0, 0, cv.width, cv.height);
          ctx.drawImage(img,
            Math.round(rect.x * sx), Math.round(rect.y * sy),
            cv.width, cv.height,
            0, 0, cv.width, cv.height);
          const url = cv.toDataURL('image/png');
          cv.width = cv.height = 1;
          resolve(url);
        } catch (e) { reject(e); }
      };
      img.onerror = () => reject(new Error('배경 이미지를 읽을 수 없습니다.'));
      img.src = bgUrl;
    });
  }

  /**
   * 원판 서식에서 라벨 한 장을 추출한다.
   * @param {{label, objects}} sheetTpl   원판 서식
   * @param {{x,y,w,h}} rect              잘라낼 영역 (mm, 원판 좌표)
   * @param {object} opt {includePartial, keepBackground, name}
   * @returns {{label:{w,h,bg,bgInclude}, objects:Array, stats:{taken,partial,dropped}}}
   */
  async function extract(sheetTpl, rect, opt = {}) {
    const sheet = { w: sheetTpl.label.w, h: sheetTpl.label.h };
    const takeFull = [], takePartial = [];
    for (const o of sheetTpl.objects) {
      if (inside(o, rect)) takeFull.push(o);
      else if (opt.includePartial && overlaps(o, rect)) takePartial.push(o);
    }
    const src = takeFull.concat(takePartial);
    const objects = src.map((o) => {
      const c = JSON.parse(JSON.stringify(o));
      c.x = Math.round((o.x - rect.x) * 100) / 100;
      c.y = Math.round((o.y - rect.y) * 100) / 100;
      return c;
    });

    let bg = '';
    if (opt.keepBackground !== false && sheetTpl.label.bg) {
      bg = await cropBackground(sheetTpl.label.bg, sheet, rect);
    }
    return {
      label: {
        w: Math.round(rect.w * 100) / 100,
        h: Math.round(rect.h * 100) / 100,
        bg, bgInclude: sheetTpl.label.bgInclude !== false,
      },
      objects,
      stats: {
        taken: takeFull.length,
        partial: takePartial.length,
        dropped: sheetTpl.objects.length - src.length,
      },
    };
  }

  /** 프리셋(LB.paper.PRODUCT_LABELS 항목)으로 추출 */
  function extractPreset(sheetTpl, preset, opt) {
    if (!preset || !preset.src) throw new Error('이 프리셋에는 원판 좌표가 없습니다.');
    return extract(sheetTpl, { x: preset.src.x, y: preset.src.y, w: preset.w, h: preset.h }, opt);
  }

  /**
   * 원판에서 각 프리셋이 몇 개의 객체를 가져가는지 미리 계산 (선택 화면용)
   */
  function preview(sheetTpl, presets) {
    return presets.map(p => {
      if (!p.src) return { preset: p, n: 0, partial: 0 };
      const r = { x: p.src.x, y: p.src.y, w: p.w, h: p.h };
      let n = 0, partial = 0;
      for (const o of sheetTpl.objects) {
        if (inside(o, r)) n++;
        else if (overlaps(o, r)) partial++;
      }
      return { preset: p, n, partial };
    });
  }

  return { extract, extractPreset, cropBackground, preview, inside, overlaps };
})();
