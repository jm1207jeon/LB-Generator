/* link.js — 데이터 링크 시각화
 *
 * 라벨 한 장에는 같은 값(LOT, REF, UDI …)이 여러 곳에 반복해서 찍힌다.
 * 어떤 객체가 어떤 데이터에 묶여 있는지 눈으로 바로 확인할 수 있어야
 * "한 곳만 고치고 다른 곳을 빠뜨리는" 실수를 막을 수 있다.
 *
 * 세 가지 방식으로 보여준다.
 *   1) 선택 연동  — 객체를 고르면 같은 데이터를 쓰는 객체가 함께 강조된다.
 *   2) 링크 보기  — 전체를 데이터별 색으로 칠해 한눈에 묶음을 본다.
 *   3) 직접 링크  — 바코드가 특정 객체의 텍스트를 참조하면 둘을 선으로 잇는다.
 */
window.LB = window.LB || {};

LB.link = (() => {
  'use strict';

  const PH_RE = /\{(@?)([A-Za-z0-9_가-힣]+)(?::[^}|]+)?(?:\|[^}]*)?\}/g;

  /** 객체가 의존하는 데이터 토큰 집합 */
  function tokensOf(o) {
    const set = new Set();
    if (!o) return set;
    const scan = (str) => {
      PH_RE.lastIndex = 0;
      let m;
      while ((m = PH_RE.exec(String(str || '')))) set.add((m[1] ? '@' : '') + m[2].toUpperCase());
    };
    if (o.type === 'text') scan(o.text);
    else if (o.type === 'image') { if (o.sourceField) set.add(o.sourceField.toUpperCase()); }
    else if (o.type === 'barcode') {
      const src = o.source || 'field';
      if (src === 'field') set.add((o.binding || 'UDI_FULL').toUpperCase());
      else if (src === 'expression') scan(o.expression);
      // 'object' 는 토큰이 아니라 직접 링크로 다룬다
    }
    return set;
  }

  /** 토큰 → 그 토큰을 쓰는 객체 id 목록 */
  function groups(objects) {
    const map = new Map();
    for (const o of objects || []) {
      if (o.visible === false) continue;
      for (const t of tokensOf(o)) {
        if (!map.has(t)) map.set(t, []);
        map.get(t).push(o.id);
      }
    }
    return map;
  }

  /** 2개 이상에서 쓰이는 토큰만 (반복되는 값) */
  function repeatedGroups(objects) {
    const g = groups(objects);
    const out = new Map();
    for (const [t, ids] of g) if (ids.length > 1) out.set(t, ids);
    return out;
  }

  /** 바코드 → 참조 객체 직접 링크 [{from, to}] */
  function directLinks(objects) {
    const out = [];
    const byId = new Map((objects || []).map(o => [o.id, o]));
    for (const o of objects || []) {
      if (o.type === 'barcode' && o.source === 'object' && o.linkObjectId && byId.has(o.linkObjectId)) {
        out.push({ from: o.id, to: o.linkObjectId });
      }
    }
    return out;
  }

  /**
   * 선택된 객체들과 데이터를 공유하는 객체 id 집합.
   * @returns {{ids:Set, tokens:Set}}
   */
  function related(objects, selectedIds) {
    const sel = new Set(selectedIds || []);
    const tokens = new Set();
    const byId = new Map((objects || []).map(o => [o.id, o]));
    for (const id of sel) for (const t of tokensOf(byId.get(id))) tokens.add(t);

    const ids = new Set();
    if (tokens.size) {
      for (const o of objects || []) {
        if (sel.has(o.id) || o.visible === false) continue;
        for (const t of tokensOf(o)) if (tokens.has(t)) { ids.add(o.id); break; }
      }
    }
    // 직접 링크도 포함 (양방향)
    for (const l of directLinks(objects)) {
      if (sel.has(l.from) && !sel.has(l.to)) ids.add(l.to);
      if (sel.has(l.to) && !sel.has(l.from)) ids.add(l.from);
    }
    return { ids, tokens };
  }

  /** 토큰 이름 → 안정적인 색 (같은 데이터는 언제나 같은 색) */
  const COLORS = [
    '#E67E00', '#1A6FB5', '#2E9E5B', '#9C27B0', '#D9534F', '#0097A7',
    '#7B5E00', '#5D4037', '#3949AB', '#C2185B', '#558B2F', '#00695C',
  ];
  function colorOf(token) {
    let h = 0;
    const s = String(token);
    for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
    return COLORS[h % COLORS.length];
  }

  /** 사람이 읽을 토큰 이름 */
  function labelOf(token) {
    const t = String(token);
    if (t.startsWith('@')) return `${t.slice(1)}열 직접참조`;
    const L = LB.data.FIELD_LABELS[t];
    return L ? `${L} {${t}}` : `{${t}}`;
  }

  /**
   * 편집기에 링크 오버레이를 그린다.
   * @param ctx    캔버스 컨텍스트
   * @param ed     LB.Editor
   * @param mode   'selection' | 'all' | 'off'
   */
  function draw(ctx, ed, mode) {
    if (!mode || mode === 'off') return;
    const objs = ed.state.objects;
    const s = ed.view.scale, ox = ed.view.ox, oy = ed.view.oy;
    const rect = (o) => ({ x: o.x * s + ox, y: o.y * s + oy, w: o.w * s, h: o.h * s });
    const byId = new Map(objs.map(o => [o.id, o]));

    ctx.save();
    ctx.lineJoin = 'round';

    if (mode === 'all') {
      // 반복되는 값마다 색을 달리해 테두리를 칠한다
      const reps = repeatedGroups(objs);
      for (const [token, ids] of reps) {
        const col = colorOf(token);
        ctx.strokeStyle = col;
        ctx.lineWidth = 2;
        ctx.setLineDash([]);
        for (const id of ids) {
          const o = byId.get(id);
          if (!o || o.visible === false) continue;
          const r = rect(o);
          ctx.strokeRect(Math.round(r.x) - 1.5, Math.round(r.y) - 1.5, r.w + 3, r.h + 3);
        }
        // 그룹 첫 객체에 이름표
        const first = byId.get(ids[0]);
        if (first && s > 2) {
          const r = rect(first);
          const txt = labelOf(token) + ` ×${ids.length}`;
          ctx.font = '10px sans-serif';
          const w = ctx.measureText(txt).width + 8;
          ctx.fillStyle = col;
          ctx.fillRect(r.x - 1.5, r.y - 15, w, 13);
          ctx.fillStyle = '#fff';
          ctx.fillText(txt, r.x + 2.5, r.y - 5);
        }
      }
    } else {
      // 선택 연동: 같은 데이터를 쓰는 객체를 주황 테두리로
      const { ids, tokens } = related(objs, [...ed.selection]);
      if (!ids.size) { ctx.restore(); return; }
      ctx.strokeStyle = '#E67E00';
      ctx.lineWidth = 2;
      ctx.setLineDash([6, 3]);
      for (const id of ids) {
        const o = byId.get(id);
        if (!o) continue;
        const r = rect(o);
        ctx.strokeRect(Math.round(r.x) - 2.5, Math.round(r.y) - 2.5, r.w + 5, r.h + 5);
      }
      ctx.setLineDash([]);
      // 선택 객체 중심에서 연관 객체로 가는 가는 연결선
      const selObjs = [...ed.selection].map(id => byId.get(id)).filter(Boolean);
      if (selObjs.length === 1 && ids.size <= 24) {
        const a = rect(selObjs[0]);
        const ax = a.x + a.w / 2, ay = a.y + a.h / 2;
        ctx.strokeStyle = 'rgba(230,126,0,.45)';
        ctx.lineWidth = 1;
        for (const id of ids) {
          const o = byId.get(id);
          const r = rect(o);
          ctx.beginPath();
          ctx.moveTo(ax, ay);
          ctx.lineTo(r.x + r.w / 2, r.y + r.h / 2);
          ctx.stroke();
        }
      }
    }

    // 직접 링크(바코드 → 객체)는 항상 화살표로
    const sel = ed.selection;
    for (const l of directLinks(objs)) {
      if (mode === 'selection' && !sel.has(l.from) && !sel.has(l.to)) continue;
      const a = byId.get(l.from), b2 = byId.get(l.to);
      if (!a || !b2 || a.visible === false || b2.visible === false) continue;
      const ra = rect(a), rb = rect(b2);
      const ax = ra.x + ra.w / 2, ay = ra.y + ra.h / 2;
      const bx = rb.x + rb.w / 2, by = rb.y + rb.h / 2;
      ctx.strokeStyle = '#9C27B0';
      ctx.lineWidth = 1.6;
      ctx.setLineDash([4, 2]);
      ctx.beginPath(); ctx.moveTo(bx, by); ctx.lineTo(ax, ay); ctx.stroke();
      ctx.setLineDash([]);
      // 화살촉 (대상 → 바코드 방향)
      const ang = Math.atan2(ay - by, ax - bx);
      ctx.fillStyle = '#9C27B0';
      ctx.beginPath();
      ctx.moveTo(ax, ay);
      ctx.lineTo(ax - 9 * Math.cos(ang - 0.4), ay - 9 * Math.sin(ang - 0.4));
      ctx.lineTo(ax - 9 * Math.cos(ang + 0.4), ay - 9 * Math.sin(ang + 0.4));
      ctx.closePath(); ctx.fill();
    }
    ctx.restore();
  }

  /** 인스펙터용: 이 객체가 쓰는 데이터와, 같은 데이터를 쓰는 다른 객체 */
  function summary(objects, obj) {
    const out = [];
    const byId = new Map((objects || []).map(o => [o.id, o]));
    for (const t of tokensOf(obj)) {
      const users = (objects || []).filter(o => o.visible !== false && tokensOf(o).has(t));
      out.push({
        token: t, label: labelOf(t), color: colorOf(t),
        count: users.length,
        others: users.filter(o => o.id !== obj.id).map(o => o.id),
      });
    }
    out.sort((a, b) => b.count - a.count);
    return out;
  }

  return { tokensOf, groups, repeatedGroups, directLinks, related, colorOf, labelOf, draw, summary };
})();
