/* link.js — 데이터 링크 시각화
 *
 * 라벨 한 장에는 같은 값(LOT, REF, UDI …)이 여러 곳에 반복해서 찍힌다.
 * 어떤 객체가 어떤 데이터에 묶여 있는지 눈으로 바로 확인할 수 있어야
 * "한 곳만 고치고 다른 곳을 빠뜨리는" 실수를 막을 수 있다.
 *
 * 표시 규칙 — 화면이 어지러워지지 않도록 최소한으로만 그린다.
 *   · 링크 표시가 켜져 있고
 *   · 객체를 **정확히 하나** 선택했을 때만
 *   그 객체와 같은 값을 쓰는 객체들을 주황 테두리로 표시한다.
 *   여러 개를 선택하거나 아무것도 선택하지 않으면 아무 것도 그리지 않는다.
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
   * 링크 오버레이. 선택이 정확히 하나일 때만 그린다.
   * @param mode 'on' | 'off'
   */
  function draw(ctx, ed, mode) {
    if (mode === 'off' || mode === false) return;
    if (ed.selection.size !== 1) return;                 // 하나만 골랐을 때만

    const objs = ed.state.objects;
    const selId = [...ed.selection][0];
    const self = objs.find(o => o.id === selId);
    if (!self) return;

    const { ids, tokens } = related(objs, [selId]);
    const direct = directLinks(objs).filter(l => l.from === selId || l.to === selId);
    if (!ids.size && !direct.length) return;

    const s = ed.view.scale, ox = ed.view.ox, oy = ed.view.oy;
    const rect = (o) => ({ x: o.x * s + ox, y: o.y * s + oy, w: o.w * s, h: o.h * s });
    const byId = new Map(objs.map(o => [o.id, o]));
    const ACC = '#E67E00';

    ctx.save();

    // 같은 값을 쓰는 객체 — 은은한 채움 + 실선 테두리
    for (const id of ids) {
      const o = byId.get(id);
      if (!o || o.visible === false) continue;
      const r = rect(o);
      ctx.fillStyle = 'rgba(230,126,0,.10)';
      ctx.fillRect(r.x, r.y, r.w, r.h);
      ctx.strokeStyle = ACC;
      ctx.lineWidth = 1.5;
      ctx.strokeRect(Math.round(r.x) - 1.5, Math.round(r.y) - 1.5, r.w + 3, r.h + 3);
    }

    // 바코드 ↔ 객체 직접 참조는 화살표 하나로
    for (const l of direct) {
      const a = byId.get(l.from), b2 = byId.get(l.to);
      if (!a || !b2 || a.visible === false || b2.visible === false) continue;
      const ra = rect(a), rb = rect(b2);
      const ax = ra.x + ra.w / 2, ay = ra.y + ra.h / 2;
      const bx = rb.x + rb.w / 2, by = rb.y + rb.h / 2;
      ctx.strokeStyle = '#9C27B0';
      ctx.lineWidth = 1.6;
      ctx.setLineDash([5, 3]);
      ctx.beginPath(); ctx.moveTo(bx, by); ctx.lineTo(ax, ay); ctx.stroke();
      ctx.setLineDash([]);
      const ang = Math.atan2(ay - by, ax - bx);
      ctx.fillStyle = '#9C27B0';
      ctx.beginPath();
      ctx.moveTo(ax, ay);
      ctx.lineTo(ax - 10 * Math.cos(ang - 0.4), ay - 10 * Math.sin(ang - 0.4));
      ctx.lineTo(ax - 10 * Math.cos(ang + 0.4), ay - 10 * Math.sin(ang + 0.4));
      ctx.closePath(); ctx.fill();
    }

    // 선택 객체 위에 이름표 하나만 — "LOT · 11곳"
    if (tokens.size) {
      const t = [...tokens][0];
      const label = shortLabel(t) + ' · ' + (ids.size + 1) + '곳'
        + (tokens.size > 1 ? ` 외 ${tokens.size - 1}종` : '');
      const r = rect(self);
      ctx.font = '11px "Noto Sans KR", sans-serif';
      const w = ctx.measureText(label).width + 12;
      const bx = Math.min(Math.max(4, r.x), ctx.canvas.width - w - 4);
      const by = r.y - 20 < 4 ? r.y + r.h + 4 : r.y - 20;
      ctx.fillStyle = ACC;
      if (ctx.roundRect) { ctx.beginPath(); ctx.roundRect(bx, by, w, 17, 4); ctx.fill(); }
      else ctx.fillRect(bx, by, w, 17);
      ctx.fillStyle = '#fff';
      ctx.textBaseline = 'middle';
      ctx.fillText(label, bx + 6, by + 9);
    }
    ctx.restore();
  }

  /** 이름표에 쓸 짧은 이름 */
  function shortLabel(token) {
    const t = String(token);
    if (t.startsWith('@')) return t.slice(1) + '열';
    return LB.data.FIELD_LABELS[t] || t;
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

  return { tokensOf, groups, repeatedGroups, directLinks, related, colorOf, labelOf, shortLabel, draw, summary };
})();
