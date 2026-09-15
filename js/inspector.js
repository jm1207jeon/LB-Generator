/* inspector.js — 우측 속성 패널 + 객체 트리
 *
 * 선택된 객체의 모든 속성을 편집한다.
 *   텍스트  : 내용(플레이스홀더) · 글꼴 · 크기 · 굵게/기울임 · 자간 · 행간 · 좌우 압축 · 정렬 · 자동축소
 *   이미지  : DB 소스 필드 · 맞춤 방식 · 가로/세로 정렬
 *   바코드  : 심볼로지 · 데이터 바인딩 3종 · HRI · 모듈 배율
 * 여러 개를 선택하면 공통 속성만 일괄 적용한다.
 */
window.LB = window.LB || {};

LB.inspector = (() => {
  'use strict';

  const $ = (id) => document.getElementById(id);
  let ed = null;
  let ctx = { fields: {}, row: null, resolveText: (t) => t, onChange: () => {} };
  let syncing = false;

  const TYPE_META = {
    text: { icon: 'T', name: '텍스트' },
    image: { icon: '🖼', name: '이미지' },
    barcode: { icon: '▦', name: '바코드' },
  };

  function init(editor, context) {
    ed = editor;
    Object.assign(ctx, context || {});
    fillStaticOptions();
    bind();
  }
  function setContext(context) { Object.assign(ctx, context || {}); }

  /* ---------------- 선택지 채우기 ---------------- */
  function fillStaticOptions() {
    const font = $('selFont');
    font.innerHTML = '';
    for (const f of LB.text.FONTS) {
      const o = document.createElement('option');
      o.value = f.v;
      o.textContent = f.n + (LB.text.fontAvailable(f.v) ? '' : ' (설치 안 됨)');
      font.appendChild(o);
    }

    const sym = $('selSymbology');
    const symNew = $('selNewBarcode');
    sym.innerHTML = ''; symNew.innerHTML = '';
    for (const s of LB.barcode.SYMBOLOGIES) {
      const o = document.createElement('option');
      o.value = s.v; o.textContent = s.n; o.title = s.note;
      sym.appendChild(o);
      symNew.appendChild(o.cloneNode(true));
    }

    const bind_ = $('selBcBinding');
    bind_.innerHTML = '';
    for (const f of LB.barcode.FIELD_CHOICES) {
      const o = document.createElement('option');
      o.value = f.v; o.textContent = f.n;
      bind_.appendChild(o);
    }

    const src = $('propSource');
    src.innerHTML = '';
    const none = document.createElement('option');
    none.value = ''; none.textContent = '(수동 파일)';
    src.appendChild(none);
    const IMG_LABELS = {
      IMG_NAME1: '제품명 그림 1줄 (J열)', IMG_NAME2: '제품명 그림 2줄 (K열)',
      IMG_STENT: '스텐트 그림 (O열)', IMG_DELIVERY: '딜리버리 그림 (P열)',
      IMG_AM: 'STENT OD 그림 (AM열)', IMG_AP: 'CI 그림 (AP열)',
    };
    for (const f of LB.data.IMAGE_FIELDS) {
      const o = document.createElement('option');
      o.value = f; o.textContent = IMG_LABELS[f] || f;
      src.appendChild(o);
    }

    const ph = $('selPlaceholder');
    ph.innerHTML = '';
    for (const p of LB.data.placeholderList()) {
      const o = document.createElement('option');
      o.value = p.token; o.textContent = `${p.token}  ${p.label}`;
      ph.appendChild(o);
    }

    const zoom = $('selZoom');
    zoom.innerHTML = '';
    for (const z of [25, 50, 75, 100, 150, 200, 400]) {
      const o = document.createElement('option');
      o.value = String(z); o.textContent = z + '%';
      zoom.appendChild(o);
    }
  }

  /* ---------------- 값 적용 헬퍼 ---------------- */
  function apply(label, fn, types) {
    if (syncing) return;
    const sel = ed.selectedObjects().filter(o => !o.locked && (!types || types.includes(o.type)));
    if (!sel.length) return;
    ed.commit(label, () => { for (const o of sel) fn(o); });
    ctx.onChange();
    refresh();
  }

  /** 여러 객체에서 공통값을 뽑는다. 다르면 undefined */
  function common(sel, key) {
    let v;
    for (let i = 0; i < sel.length; i++) {
      const x = sel[i][key];
      if (i === 0) v = x;
      else if (x !== v) return undefined;
    }
    return v;
  }

  function num(id, v, fb) {
    const el = $(id);
    el.value = (v === undefined || v === null) ? '' : v;
    el.placeholder = v === undefined ? '여러 값' : (fb != null ? String(fb) : '');
  }
  function pair(numId, rngId, v, fb) {
    num(numId, v, fb);
    const r = $(rngId);
    if (r) r.value = v === undefined ? (fb || 0) : v;
  }

  /* ---------------- 새로 고침 ---------------- */
  function refresh() {
    if (!ed) return;
    syncing = true;
    try { refreshInner(); } finally { syncing = false; }
    refreshTree();
  }

  function refreshInner() {
    const sel = ed.selectedObjects();
    const empty = $('propsEmpty'), body = $('propsBody');
    const ovl = $('ovlSel');

    if (!sel.length) {
      empty.hidden = false; body.hidden = true;
      if (ovl) ovl.hidden = true;
      return;
    }
    empty.hidden = true; body.hidden = false;

    const types = [...new Set(sel.map(o => o.type))];
    const multi = sel.length > 1;
    const one = sel[0];
    const meta = TYPE_META[types.length === 1 ? types[0] : 'text'] || TYPE_META.text;

    $('propIcon').textContent = types.length === 1 ? meta.icon : '⬚';
    $('propTitle').textContent = multi
      ? `${sel.length}개 선택 (${types.map(t => (TYPE_META[t] || {}).name || t).join(', ')})`
      : meta.name;
    const badge = $('propBadge');
    if (one.locked) { badge.hidden = false; badge.textContent = '잠김'; badge.className = 'badge warn'; }
    else { badge.hidden = true; }

    if (ovl) {
      ovl.hidden = false;
      const b = ed.selectionBounds();
      ovl.textContent = multi
        ? `${sel.length}개 · ${b.w.toFixed(1)}×${b.h.toFixed(1)}mm`
        : `${one.x.toFixed(1)}, ${one.y.toFixed(1)} · ${one.w.toFixed(1)}×${one.h.toFixed(1)}mm`;
    }

    // 공통: 이름 / 위치 / 크기
    $('propName').value = multi ? '' : (one.name || '');
    $('propName').placeholder = multi ? '여러 객체' : '(자동: ' + ed.labelOf(one) + ')';
    $('propName').disabled = multi;
    num('propX', common(sel, 'x')); num('propY', common(sel, 'y'));
    num('propW', common(sel, 'w')); num('propH', common(sel, 'h'));
    $('propLocked').checked = sel.every(o => o.locked);
    $('propVisible').checked = sel.every(o => o.visible !== false);

    const hasText = types.includes('text');
    const hasImg = types.includes('image');
    const hasBc = types.includes('barcode');
    $('propTextWrap').hidden = !hasText;
    $('propImgWrap').hidden = !hasImg || multi;
    $('propBcWrap').hidden = !hasBc || multi;

    if (hasText) {
      const ts = sel.filter(o => o.type === 'text');
      $('propText').value = ts.length === 1 ? (ts[0].text || '') : '';
      $('propText').disabled = ts.length !== 1;
      $('propText').placeholder = ts.length === 1 ? '{PRODUCT}' : '여러 객체는 내용을 함께 바꿀 수 없습니다';
      const prev = $('propTextPreview');
      if (ts.length === 1) {
        const out = ctx.resolveText(ts[0].text || '');
        const b = LB.text.bounds(ts[0], out);
        prev.textContent = out ? `→ ${out.replace(/\n/g, ' ⏎ ')}` : '(빈 내용)';
        prev.className = 'hint' + ((b.overflowX || b.overflowY) ? ' warn' : '');
        if (b.overflowX || b.overflowY) prev.textContent += '  ⚠ 영역을 넘칩니다';
        else if (b.shrunk) prev.textContent += `  (자동 축소 ${b.sizePt}pt)`;
      } else prev.textContent = '';

      const f = common(ts, 'font');
      $('selFont').value = f === undefined ? '' : (f || 'Arial');
      num('inpFontSize', common(ts, 'sizePt'), 8);
      $('btnBold').classList.toggle('on', ts.every(o => o.bold));
      $('btnItalic').classList.toggle('on', ts.every(o => o.italic));
      $('propColor').value = common(ts, 'color') || '#000000';
      pair('inpLetterSpacing', 'rngLetterSpacing', common(ts, 'letterSpacing'), 0);
      pair('inpWordSpacing', 'rngWordSpacing', common(ts, 'wordSpacing'), 0);
      pair('inpLineHeight', 'rngLineHeight', common(ts, 'lineHeight'), 1.15);
      $('chkKerning').checked = ts.every(o => o.kerning !== false);
      $('kerningHint').textContent = ts.every(o => o.kerning !== false) ? '' : '끔 — 글자 간격이 일정해집니다';
      const hs = common(ts, 'hScale');
      pair('inpHScale', 'rngHScale', hs === undefined ? undefined : (hs == null ? 100 : hs), 100);
      $('selAlign').value = common(ts, 'align') || 'left';
      $('selVAlign').value = common(ts, 'vAlign') || 'top';
      $('chkWrap').checked = ts.every(o => o.wrap !== false);
      $('chkAutoShrink').checked = ts.every(o => !!o.autoShrink);
    }

    // 데이터 링크
    renderLinkPanel(sel);

    if (hasImg && !multi) {
      $('propSource').value = one.sourceField || '';
      const fn = $('propFileName');
      if (one.sourceField) {
        const name = ctx.fields[one.sourceField] || '';
        if (!name) { fn.textContent = `이 품목에 ${one.sourceField} 파일명이 없습니다.`; fn.className = 'hint warn'; }
        else if (one.dataUrl) { fn.textContent = `✓ ${name}`; fn.className = 'hint ok'; }
        else { fn.textContent = `✕ ${name} — ${one.error || '불러오지 못했습니다'}`; fn.className = 'hint err'; }
      } else if (one.fileName) {
        fn.textContent = `파일: ${one.fileName}`; fn.className = 'hint ok';
      } else { fn.textContent = '연결된 이미지가 없습니다.'; fn.className = 'hint'; }
      $('selImgFitMode').value = one.fitMode || 'contain';
      $('selImgFit').value = one.fit || 'center';
      $('selImgVFit').value = one.vFit || 'middle';
    }

    if (hasBc && !multi) {
      $('selSymbology').value = one.symbology || 'gs1datamatrix';
      const src = one.source || 'field';
      $('selBcSource').value = src;
      $('bcFieldWrap').hidden = src !== 'field';
      $('bcExprWrap').hidden = src !== 'expression';
      $('bcObjWrap').hidden = src !== 'object';
      $('selBcBinding').value = one.binding || 'UDI_FULL';
      $('inpBcExpr').value = one.expression || '';

      const link = $('selBcLink');
      link.innerHTML = '';
      const opts = LB.barcode.linkableObjects(ed.state.objects, one.id);
      if (!opts.length) {
        const o = document.createElement('option');
        o.value = ''; o.textContent = '(링크할 객체가 없습니다)';
        link.appendChild(o);
      }
      for (const t of opts) {
        const o = document.createElement('option');
        o.value = t.id; o.textContent = t.label;
        link.appendChild(o);
      }
      link.value = one.linkObjectId || '';

      const data = LB.barcode.resolveData(one, {
        fields: ctx.fields, objects: ed.state.objects, resolveText: ctx.resolveText,
      });
      const pv = $('bcPreview');
      pv.textContent = data ? data : '(데이터 없음)';
      const iss = $('bcIssues');
      const list = data ? LB.barcode.validateData(one.symbology, data) : [];
      const phys = data ? LB.barcode.checkPhysical(one, data, LB.settings.value('output.dpi', 600)) : [];
      const g = data ? LB.barcode.generate(one.symbology, data, { scale: 2, humanReadable: !!one.humanReadable, heightMm: 10 }) : null;
      const all = list.concat(phys);
      if (g && !g.canvas) all.unshift({ level: 'error', msg: g.err });
      if (!data) { iss.textContent = ''; iss.className = 'hint'; }
      else if (!all.length) { iss.textContent = '✓ 검증 통과'; iss.className = 'hint ok'; }
      else {
        iss.textContent = all.map(i => (i.level === 'error' ? '✕ ' : '⚠ ') + i.msg).join('  /  ');
        iss.className = 'hint ' + (all.some(i => i.level === 'error') ? 'err' : 'warn');
      }

      $('chkHri').checked = !!one.humanReadable;
      $('selBcFitMode').value = one.fitMode || 'module';
      $('selBcFit').value = one.fit || 'center';
      $('selBcVFit').value = one.vFit || 'middle';
    }
  }

  /* ---------------- 데이터 링크 패널 ---------------- */
  function renderLinkPanel(sel) {
    const wrap = $('propLinkWrap');
    const list = $('linkList');
    if (!wrap || !list) return;
    if (sel.length !== 1) { wrap.hidden = true; return; }
    const info = LB.link.summary(ed.state.objects, sel[0]);
    const direct = LB.link.directLinks(ed.state.objects)
      .filter(l => l.from === sel[0].id || l.to === sel[0].id);
    if (!info.length && !direct.length) { wrap.hidden = true; return; }
    wrap.hidden = false;

    const repeated = info.filter(i => i.count > 1).length;
    $('linkSummary').textContent = repeated ? `${repeated}개 값이 라벨에서 반복됨` : '';

    LB.ui.clear(list);
    for (const i of info) {
      const rowEl = document.createElement('div');
      rowEl.className = 'link-row';
      const sw = document.createElement('span');
      sw.className = 'link-dot';
      sw.style.background = i.color;
      const nm = document.createElement('span');
      nm.className = 'link-name';
      nm.textContent = i.label;
      const cnt = document.createElement('span');
      cnt.className = 'badge' + (i.count > 1 ? '' : ' pending');
      cnt.textContent = i.count > 1 ? `${i.count}곳` : '1곳';
      rowEl.append(sw, nm, cnt);
      if (i.others.length) {
        rowEl.title = '클릭하면 같은 데이터를 쓰는 객체를 모두 선택합니다.';
        rowEl.classList.add('clickable');
        rowEl.onclick = () => ed.select([sel[0].id, ...i.others]);
      } else {
        rowEl.title = '이 라벨에서 한 번만 쓰이는 값입니다.';
      }
      list.appendChild(rowEl);
    }
    for (const l of direct) {
      const other = l.from === sel[0].id ? l.to : l.from;
      const o = ed.byId(other);
      if (!o) continue;
      const rowEl = document.createElement('div');
      rowEl.className = 'link-row clickable';
      const sw = document.createElement('span');
      sw.className = 'link-dot'; sw.style.background = '#9C27B0';
      const nm = document.createElement('span');
      nm.className = 'link-name';
      nm.textContent = (l.from === sel[0].id ? '→ 참조: ' : '← 참조됨: ') + ed.labelOf(o);
      rowEl.append(sw, nm);
      rowEl.onclick = () => ed.select([other]);
      rowEl.title = '클릭하면 연결된 객체로 이동합니다.';
      list.appendChild(rowEl);
    }
  }

  /* ---------------- 객체 트리 ---------------- */
  function refreshTree() {
    const root = $('objTree');
    if (!root) return;
    const q = ($('treeSearch').value || '').trim().toLowerCase();
    LB.ui.clear(root);
    const objs = ed.state.objects;
    $('treeCount').textContent = objs.length ? `(${objs.length})` : '';

    // 위에 있는 객체가 위에 보이도록 역순
    for (let i = objs.length - 1; i >= 0; i--) {
      const o = objs[i];
      const label = ed.labelOf(o);
      if (q && !(label.toLowerCase().includes(q) || (o.id || '').toLowerCase().includes(q))) continue;

      const row = document.createElement('div');
      row.className = 'tree-item' + (ed.selection.has(o.id) ? ' sel' : '') + (o.visible === false ? ' hidden-obj' : '');
      const issues = ed.issues.get(o.id);
      if (issues && issues.length) {
        row.classList.add(issues.some(x => x.level === 'error') ? 'issue-err' : 'issue');
        row.title = issues.map(x => x.msg).join('\n');
      }

      const ic = document.createElement('span');
      ic.className = 'ic';
      ic.textContent = (TYPE_META[o.type] || {}).icon || '·';
      const nm = document.createElement('span');
      nm.className = 'nm'; nm.textContent = label;
      if (!row.title) row.title = label;

      const vis = document.createElement('button');
      vis.className = 'tog' + (o.visible === false ? '' : ' act');
      vis.textContent = o.visible === false ? '◌' : '◉';
      vis.title = o.visible === false ? '숨김 — 클릭하면 표시' : '표시 중 — 클릭하면 숨김';
      vis.onclick = (e) => {
        e.stopPropagation();
        ed.commit('표시 전환', () => { o.visible = o.visible === false; });
        ctx.onChange(); refresh();
      };

      const lk = document.createElement('button');
      lk.className = 'tog' + (o.locked ? ' act' : '');
      lk.textContent = o.locked ? '🔒' : '🔓';
      lk.title = o.locked ? '잠김 — 클릭하면 해제' : '편집 가능 — 클릭하면 잠금';
      lk.onclick = (e) => {
        e.stopPropagation();
        ed.commit('잠금 전환', () => { o.locked = !o.locked; });
        ctx.onChange(); refresh();
      };

      row.append(ic, nm, vis, lk);
      row.onclick = (e) => {
        if (e.shiftKey || e.ctrlKey) {
          const s = new Set(ed.selection);
          if (s.has(o.id)) s.delete(o.id); else s.add(o.id);
          ed.select([...s]);
        } else ed.select([o.id]);
      };
      root.appendChild(row);
    }
    if (!root.children.length) {
      const e = document.createElement('div');
      e.className = 'insp-empty';
      e.textContent = objs.length ? '검색 결과가 없습니다.' : '객체가 없습니다.';
      root.appendChild(e);
    }
  }

  /* ---------------- 이벤트 ---------------- */
  function bind() {
    // 탭
    document.querySelectorAll('.insp-tabs button').forEach(b => {
      b.onclick = () => {
        document.querySelectorAll('.insp-tabs button').forEach(x => x.classList.toggle('on', x === b));
        $('paneProps').classList.toggle('on', b.dataset.pane === 'props');
        $('paneTree').classList.toggle('on', b.dataset.pane === 'tree');
      };
    });
    $('treeSearch').addEventListener('input', refreshTree);

    // 이름 / 위치 / 크기
    $('propName').addEventListener('input', () => {
      const sel = ed.selectedObjects();
      if (sel.length !== 1 || syncing) return;
      sel[0].name = $('propName').value;
      ctx.onChange(); refreshTree();
    });
    for (const [id, key] of [['propX', 'x'], ['propY', 'y'], ['propW', 'w'], ['propH', 'h']]) {
      $(id).addEventListener('input', () => {
        const v = Number($(id).value);
        if (!Number.isFinite(v) || syncing) return;
        const min = (key === 'w' || key === 'h') ? 0.5 : -9999;
        apply('속성 변경', (o) => { o[key] = Math.max(min, v); });
      });
    }
    $('propLocked').addEventListener('change', () => {
      const v = $('propLocked').checked;
      if (syncing) return;
      const sel = ed.selectedObjects();
      ed.commit('잠금 전환', () => { for (const o of sel) o.locked = v; });
      ctx.onChange(); refresh();
    });
    $('propVisible').addEventListener('change', () => {
      const v = $('propVisible').checked;
      if (syncing) return;
      const sel = ed.selectedObjects();
      ed.commit('표시 전환', () => { for (const o of sel) o.visible = v; });
      ctx.onChange(); refresh();
    });

    // 텍스트 내용
    $('propText').addEventListener('input', () => {
      const sel = ed.selectedObjects().filter(o => o.type === 'text');
      if (sel.length !== 1 || syncing) return;
      sel[0].text = $('propText').value;
      ctx.onChange();
      ed.render();
      syncing = true; try { refreshInner(); } finally { syncing = false; }
      refreshTree();
    });
    $('propText').addEventListener('change', () => { if (!syncing) ed.commit('내용 변경', () => {}); });
    $('btnInsertPh').onclick = () => {
      const t = $('propText');
      const tok = $('selPlaceholder').value;
      const s = t.selectionStart || 0, e = t.selectionEnd || 0;
      t.value = t.value.slice(0, s) + tok + t.value.slice(e);
      t.selectionStart = t.selectionEnd = s + tok.length;
      t.dispatchEvent(new Event('input'));
      t.focus();
    };

    // 글꼴
    $('selFont').addEventListener('change', () => apply('글꼴 변경', o => { o.font = $('selFont').value; }, ['text']));
    $('inpFontSize').addEventListener('input', () => {
      const v = Number($('inpFontSize').value);
      if (Number.isFinite(v) && v > 0) apply('크기 변경', o => { o.sizePt = v; }, ['text']);
    });
    $('btnBold').onclick = () => {
      const sel = ed.selectedObjects().filter(o => o.type === 'text');
      const all = sel.length && sel.every(o => o.bold);
      apply('굵게', o => { o.bold = !all; }, ['text']);
    };
    $('btnItalic').onclick = () => {
      const sel = ed.selectedObjects().filter(o => o.type === 'text');
      const all = sel.length && sel.every(o => o.italic);
      apply('기울임', o => { o.italic = !all; }, ['text']);
    };
    $('propColor').addEventListener('input', () => apply('색 변경', o => { o.color = $('propColor').value; }, ['text']));

    // 자간 · 행간 · 좌우 압축 (슬라이더 ↔ 숫자 동기화)
    const link = (numId, rngId, key, label, min, max) => {
      const setv = (v) => {
        if (!Number.isFinite(v)) return;
        v = Math.min(max, Math.max(min, v));
        apply(label, o => { o[key] = v; }, ['text']);
      };
      $(numId).addEventListener('input', () => { if (!syncing) { $(rngId).value = $(numId).value; setv(Number($(numId).value)); } });
      $(rngId).addEventListener('input', () => { if (!syncing) { $(numId).value = $(rngId).value; setv(Number($(rngId).value)); } });
    };
    link('inpLetterSpacing', 'rngLetterSpacing', 'letterSpacing', '자간 변경', -5, 30);
    link('inpWordSpacing', 'rngWordSpacing', 'wordSpacing', '어간 변경', -10, 60);
    link('inpLineHeight', 'rngLineHeight', 'lineHeight', '행간 변경', 0.4, 6);
    link('inpHScale', 'rngHScale', 'hScale', '장평 변경', 10, 500);
    $('chkKerning').addEventListener('change', () =>
      apply('커닝', o => { o.kerning = $('chkKerning').checked; }, ['text']));

    $('selAlign').addEventListener('change', () => apply('정렬', o => { o.align = $('selAlign').value; }, ['text']));
    $('selVAlign').addEventListener('change', () => apply('세로 정렬', o => { o.vAlign = $('selVAlign').value; }, ['text']));
    $('chkWrap').addEventListener('change', () => apply('줄바꿈', o => { o.wrap = $('chkWrap').checked; }, ['text']));
    $('chkAutoShrink').addEventListener('change', () => apply('자동 축소', o => { o.autoShrink = $('chkAutoShrink').checked; }, ['text']));

    // 이미지
    $('propSource').addEventListener('change', () => {
      const sel = ed.selectedObjects();
      if (sel.length !== 1) return;
      ed.commit('이미지 소스 변경', () => {
        sel[0].sourceField = $('propSource').value;
        sel[0].fileName = ''; sel[0].dataUrl = ''; sel[0].error = '';
      });
      if (ctx.onImageSourceChange) ctx.onImageSourceChange();
      ctx.onChange(); refresh();
    });
    $('selImgFitMode').addEventListener('change', () => apply('맞춤 방식', o => { o.fitMode = $('selImgFitMode').value; }, ['image']));
    $('selImgFit').addEventListener('change', () => apply('가로 정렬', o => { o.fit = $('selImgFit').value; }, ['image']));
    $('selImgVFit').addEventListener('change', () => apply('세로 정렬', o => { o.vFit = $('selImgVFit').value; }, ['image']));
    $('btnPickImgFile').onclick = () => { if (ctx.onPickImageFile) ctx.onPickImageFile(); };

    // 바코드
    $('selSymbology').addEventListener('change', () => apply('바코드 종류', o => { o.symbology = $('selSymbology').value; }, ['barcode']));
    $('selBcSource').addEventListener('change', () => apply('데이터 소스', o => { o.source = $('selBcSource').value; }, ['barcode']));
    $('selBcBinding').addEventListener('change', () => apply('바코드 필드', o => { o.binding = $('selBcBinding').value; }, ['barcode']));
    $('inpBcExpr').addEventListener('input', () => {
      const sel = ed.selectedObjects().filter(o => o.type === 'barcode');
      if (sel.length !== 1 || syncing) return;
      sel[0].expression = $('inpBcExpr').value;
      ctx.onChange(); ed.render();
      syncing = true; try { refreshInner(); } finally { syncing = false; }
    });
    $('selBcLink').addEventListener('change', () => apply('객체 링크', o => { o.linkObjectId = $('selBcLink').value; }, ['barcode']));
    $('chkHri').addEventListener('change', () => apply('HRI', o => { o.humanReadable = $('chkHri').checked; }, ['barcode']));
    $('selBcFitMode').addEventListener('change', () => apply('배율 방식', o => { o.fitMode = $('selBcFitMode').value; }, ['barcode']));
    $('selBcFit').addEventListener('change', () => apply('가로 정렬', o => { o.fit = $('selBcFit').value; }, ['barcode']));
    $('selBcVFit').addEventListener('change', () => apply('세로 정렬', o => { o.vFit = $('selBcVFit').value; }, ['barcode']));
  }

  return { init, setContext, refresh, refreshTree };
})();
