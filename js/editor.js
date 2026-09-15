/* editor.js — 캔버스 라벨 편집기
 *
 * 좌표계: 라벨 좌상단이 원점, 단위 mm.  화면 px = mm * view.scale + view.ox
 * 눈금자(RULER)가 캔버스 안쪽 좌/상단을 차지한다.
 *
 * 기능
 *  - 휠 줌(커서 기준) / 스페이스·중버튼 패닝 / 빈 곳 드래그 = 사각형 선택
 *  - 객체 이동·리사이즈(8핸들), 다중 선택 시 바운딩박스 단위 조작
 *  - 스냅: 다른 객체의 6개 모서리·중심 + 라벨 경계·중심선 (Alt로 일시 해제)
 *  - 실행취소/다시실행 (스냅샷 + 트랜잭션)
 *  - 정렬/균등분배/크기맞춤
 *  - 잠금/숨김/이름/z-order
 *  - 라벨 밖으로 나가거나 내용이 넘치는 객체 경고 표시
 */
window.LB = window.LB || {};

LB.Editor = class {
  constructor(canvas, state) {
    this.cv = canvas;
    this.ctx = canvas.getContext('2d');
    this.state = state;                 // { label:{w,h,bg,bgInclude}, objects:[] }
    this.view = { scale: 4, ox: 60, oy: 60 };
    this.selection = new Set();
    this.imageCache = new Map();
    this.resolver = (t) => t;           // 플레이스홀더 치환 (app.js 주입)
    this.barcodeCtx = () => ({ fields: {}, objects: this.state.objects, resolveText: this.resolver });
    this.onSelectionChange = () => {};
    this.onModelChange = () => {};
    this.onViewChange = () => {};
    this.onHover = () => {};
    this.drag = null;
    this.guides = [];
    this.showRulers = true;
    this.showGrid = false;
    this.gridMm = 5;
    this.snapEnabled = true;
    this.snapPx = 6;
    this.previewMode = false;   // true면 편집 보조선을 감추고 인쇄될 모습만 그린다
    this.linkMode = 'on';        // 'on' | 'off' — 데이터 링크 표시 (한 번에 한 객체)
    this.issues = new Map();            // objectId -> [{level,msg}]
    this._bgCache = null;
    this._undo = [];
    this._redo = [];
    this._tx = null;
    this._bind();
    this._resize();
    this._ro = new ResizeObserver(() => this._resize());
    try { this._ro.observe(canvas.parentElement || canvas); } catch (_) {}
  }

  static RULER = 22;

  get rulerSize() { return this.showRulers ? LB.Editor.RULER : 0; }

  /* ================= 좌표 ================= */
  mm2px(x, y) { return [x * this.view.scale + this.view.ox, y * this.view.scale + this.view.oy]; }
  px2mm(x, y) { return [(x - this.view.ox) / this.view.scale, (y - this.view.oy) / this.view.scale]; }

  /** 화면 표시용 배율(%) — 100% = 실제 물리 크기 (96dpi CSS 기준 3.7795 px/mm) */
  get zoomPercent() { return this.view.scale / (96 / 25.4) * 100; }

  zoomFit() {
    const { w, h } = this.state.label;
    const R = this.rulerSize;
    const cw = this.cv.clientWidth - R, ch = this.cv.clientHeight - R;
    if (cw <= 0 || ch <= 0) return;
    const scale = Math.min((cw - 40) / w, (ch - 40) / h);
    this.view.scale = Math.max(0.2, scale);
    this.view.ox = R + (cw - w * this.view.scale) / 2;
    this.view.oy = R + (ch - h * this.view.scale) / 2;
    this._bgCache = null;
    this.onViewChange();
    this.render();
  }

  /** 배율을 지정 값으로 (캔버스 중심 기준) */
  zoomTo(percent) {
    const R = this.rulerSize;
    const cx = R + (this.cv.clientWidth - R) / 2, cy = R + (this.cv.clientHeight - R) / 2;
    const [mx, my] = this.px2mm(cx, cy);
    this.view.scale = Math.min(200, Math.max(0.2, percent / 100 * (96 / 25.4)));
    this.view.ox = cx - mx * this.view.scale;
    this.view.oy = cy - my * this.view.scale;
    this._bgCache = null;
    this.onViewChange();
    this.render();
  }

  /* ================= 실행취소 ================= */
  _snapshot() {
    return JSON.stringify({ objects: this.state.objects, label: this.state.label });
  }
  _restore(snap) {
    const s = JSON.parse(snap);
    this.state.objects.length = 0;
    for (const o of s.objects) this.state.objects.push(o);
    Object.assign(this.state.label, s.label);
    const alive = new Set(this.state.objects.map(o => o.id));
    this.selection = new Set([...this.selection].filter(id => alive.has(id)));
    this._bgCache = null;
  }
  /** 트랜잭션 시작 — 드래그 전체를 하나의 실행취소 단위로 묶는다 */
  beginTx(label) {
    if (this._tx) return;
    this._tx = { label, snap: this._snapshot() };
  }
  endTx(changed = true) {
    if (!this._tx) return;
    const t = this._tx; this._tx = null;
    if (changed && t.snap !== this._snapshot()) {
      this._undo.push(t);
      if (this._undo.length > 60) this._undo.shift();
      this._redo.length = 0;
    }
  }
  /** 단발 변경용 — fn 실행 전후를 하나의 실행취소 단위로 */
  commit(label, fn) {
    this.beginTx(label);
    const r = fn();
    this.endTx(true);
    this.changed();
    return r;
  }
  canUndo() { return this._undo.length > 0; }
  canRedo() { return this._redo.length > 0; }
  undoLabel() { return this._undo.length ? this._undo[this._undo.length - 1].label : ''; }
  redoLabel() { return this._redo.length ? this._redo[this._redo.length - 1].label : ''; }
  undo() {
    if (!this._undo.length) return false;
    const t = this._undo.pop();
    this._redo.push({ label: t.label, snap: this._snapshot() });
    this._restore(t.snap);
    this.onSelectionChange(); this.changed();
    return true;
  }
  redo() {
    if (!this._redo.length) return false;
    const t = this._redo.pop();
    this._undo.push({ label: t.label, snap: this._snapshot() });
    this._restore(t.snap);
    this.onSelectionChange(); this.changed();
    return true;
  }
  resetHistory() { this._undo.length = 0; this._redo.length = 0; }

  /* ================= 객체 ================= */
  byId(id) { return this.state.objects.find(o => o.id === id); }
  selectedObjects() { return this.state.objects.filter(o => this.selection.has(o.id)); }
  visibleObjects() { return this.state.objects.filter(o => o.visible !== false); }

  newId(prefix) {
    let n = 1, id;
    const used = new Set(this.state.objects.map(o => o.id));
    do { id = (prefix || 'o') + n++; } while (used.has(id));
    return id;
  }

  addObject(o, label) {
    o.id = o.id || this.newId(o.type === 'text' ? 't' : o.type === 'image' ? 'i' : 'b');
    return this.commit(label || '객체 추가', () => {
      this.state.objects.push(o);
      this.selection = new Set([o.id]);
      this.onSelectionChange();
      return o;
    });
  }

  deleteSelection() {
    if (!this.selection.size) return;
    const n = this.selection.size;
    this.commit(`객체 ${n}개 삭제`, () => {
      const keep = this.state.objects.filter(o => !this.selection.has(o.id));
      this.state.objects.length = 0;
      for (const o of keep) this.state.objects.push(o);
      this.selection = new Set();
      this.onSelectionChange();
    });
  }

  duplicate() {
    const sel = this.selectedObjects();
    if (!sel.length) return;
    this.commit(`객체 ${sel.length}개 복제`, () => {
      const ids = [];
      for (const o of sel) {
        const c = JSON.parse(JSON.stringify(o));
        c.id = this.newId(o.type === 'text' ? 't' : o.type === 'image' ? 'i' : 'b');
        c.x = Math.round((c.x + 2) * 10) / 10;
        c.y = Math.round((c.y + 2) * 10) / 10;
        if (c.name) c.name = c.name + ' 복사본';
        this.state.objects.push(c);
        ids.push(c.id);
      }
      this.selection = new Set(ids);
      this.onSelectionChange();
    });
  }

  /** 클립보드용 JSON */
  copySelection() {
    return JSON.stringify({ _lb: 'objects', objects: this.selectedObjects() });
  }
  pasteObjects(json, offsetMm = 3) {
    let d;
    try { d = JSON.parse(json); } catch (_) { return 0; }
    if (!d || d._lb !== 'objects' || !Array.isArray(d.objects)) return 0;
    this.commit(`객체 ${d.objects.length}개 붙여넣기`, () => {
      const ids = [];
      for (const o of d.objects) {
        const c = JSON.parse(JSON.stringify(o));
        c.id = this.newId(c.type === 'text' ? 't' : c.type === 'image' ? 'i' : 'b');
        c.x = Math.round((c.x + offsetMm) * 10) / 10;
        c.y = Math.round((c.y + offsetMm) * 10) / 10;
        this.state.objects.push(c);
        ids.push(c.id);
      }
      this.selection = new Set(ids);
      this.onSelectionChange();
    });
    return d.objects.length;
  }

  select(ids, silent) {
    this.selection = new Set(ids);
    if (!silent) this.onSelectionChange();
    this.render();
  }
  selectAll() {
    this.select(this.state.objects.filter(o => !o.locked && o.visible !== false).map(o => o.id));
  }

  changed() {
    this.onModelChange();
    this.render();
  }

  /* z-order */
  bringToFront() { this._reorder('맨 앞으로', (rest, sel) => rest.concat(sel)); }
  sendToBack() { this._reorder('맨 뒤로', (rest, sel) => sel.concat(rest)); }
  _reorder(label, fn) {
    if (!this.selection.size) return;
    this.commit(label, () => {
      const sel = this.selectedObjects();
      const rest = this.state.objects.filter(o => !this.selection.has(o.id));
      const next = fn(rest, sel);
      this.state.objects.length = 0;
      for (const o of next) this.state.objects.push(o);
    });
  }
  moveLayer(dir) {   // dir: -1 뒤로, +1 앞으로
    if (this.selection.size !== 1) return;
    const id = [...this.selection][0];
    const i = this.state.objects.findIndex(o => o.id === id);
    const j = i + dir;
    if (i < 0 || j < 0 || j >= this.state.objects.length) return;
    this.commit(dir > 0 ? '앞으로' : '뒤로', () => {
      const a = this.state.objects;
      [a[i], a[j]] = [a[j], a[i]];
    });
  }
  /** 객체 트리에서 드래그로 순서 변경 */
  reorderTo(id, targetIndex) {
    const i = this.state.objects.findIndex(o => o.id === id);
    if (i < 0) return;
    this.commit('순서 변경', () => {
      const a = this.state.objects;
      const [o] = a.splice(i, 1);
      a.splice(Math.max(0, Math.min(a.length, targetIndex)), 0, o);
    });
  }

  /* ================= 정렬 / 분배 ================= */
  selectionBounds() {
    const sel = this.selectedObjects();
    if (!sel.length) return null;
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const o of sel) {
      x0 = Math.min(x0, o.x); y0 = Math.min(y0, o.y);
      x1 = Math.max(x1, o.x + o.w); y1 = Math.max(y1, o.y + o.h);
    }
    return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
  }

  /** mode: left|hcenter|right|top|vcenter|bottom  — ref: 'selection' | 'label' */
  align(mode, ref = 'selection') {
    const sel = this.selectedObjects().filter(o => !o.locked);
    if (!sel.length) return;
    const b = ref === 'label'
      ? { x: 0, y: 0, w: this.state.label.w, h: this.state.label.h }
      : this.selectionBounds();
    if (!b) return;
    this.commit('정렬', () => {
      for (const o of sel) {
        if (mode === 'left') o.x = b.x;
        else if (mode === 'right') o.x = b.x + b.w - o.w;
        else if (mode === 'hcenter') o.x = b.x + (b.w - o.w) / 2;
        else if (mode === 'top') o.y = b.y;
        else if (mode === 'bottom') o.y = b.y + b.h - o.h;
        else if (mode === 'vcenter') o.y = b.y + (b.h - o.h) / 2;
        o.x = Math.round(o.x * 100) / 100; o.y = Math.round(o.y * 100) / 100;
      }
      this.onSelectionChange();
    });
  }

  /** axis: 'h' | 'v' — 객체 사이 간격을 균등하게 */
  distribute(axis) {
    const sel = this.selectedObjects().filter(o => !o.locked);
    if (sel.length < 3) return;
    this.commit('균등 분배', () => {
      const key = axis === 'h' ? 'x' : 'y';
      const size = axis === 'h' ? 'w' : 'h';
      sel.sort((a, b) => a[key] - b[key]);
      const first = sel[0], last = sel[sel.length - 1];
      const span = (last[key] + last[size]) - first[key];
      const totalSize = sel.reduce((s, o) => s + o[size], 0);
      const gap = (span - totalSize) / (sel.length - 1);
      let cur = first[key];
      for (const o of sel) {
        o[key] = Math.round(cur * 100) / 100;
        cur += o[size] + gap;
      }
      this.onSelectionChange();
    });
  }

  /** dim: 'w' | 'h' | 'both' — 첫 선택 객체 크기에 맞춤 */
  matchSize(dim) {
    const sel = this.selectedObjects().filter(o => !o.locked);
    if (sel.length < 2) return;
    const ref = sel[0];
    this.commit('크기 맞춤', () => {
      for (let i = 1; i < sel.length; i++) {
        if (dim === 'w' || dim === 'both') sel[i].w = ref.w;
        if (dim === 'h' || dim === 'both') sel[i].h = ref.h;
      }
      this.onSelectionChange();
    });
  }

  /* ================= 히트 테스트 ================= */
  hitObject(mx, my) {
    for (let i = this.state.objects.length - 1; i >= 0; i--) {
      const o = this.state.objects[i];
      if (o.visible === false || o.locked) continue;
      if (mx >= o.x && mx <= o.x + o.w && my >= o.y && my <= o.y + o.h) return o;
    }
    return null;
  }

  _handleRects() {
    const b = this.selectionBounds();
    if (!b) return [];
    const [x1, y1] = this.mm2px(b.x, b.y);
    const [x2, y2] = this.mm2px(b.x + b.w, b.y + b.h);
    const cx = (x1 + x2) / 2, cy = (y1 + y2) / 2;
    return [
      { dir: 'nw', x: x1, y: y1 }, { dir: 'n', x: cx, y: y1 }, { dir: 'ne', x: x2, y: y1 },
      { dir: 'w', x: x1, y: cy }, { dir: 'e', x: x2, y: cy },
      { dir: 'sw', x: x1, y: y2 }, { dir: 's', x: cx, y: y2 }, { dir: 'se', x: x2, y: y2 },
    ];
  }
  hitHandle(px, py) {
    if (!this.selection.size) return null;
    if (this.selectedObjects().every(o => o.locked)) return null;
    const H = 8;
    for (const h of this._handleRects()) {
      if (Math.abs(px - h.x) <= H && Math.abs(py - h.y) <= H) return h.dir;
    }
    return null;
  }

  /* ================= 스냅 ================= */
  _snapTargets(excludeIds) {
    const xs = [], ys = [];
    const L = this.state.label;
    xs.push({ v: 0, kind: 'label' }, { v: L.w, kind: 'label' }, { v: L.w / 2, kind: 'center' });
    ys.push({ v: 0, kind: 'label' }, { v: L.h, kind: 'label' }, { v: L.h / 2, kind: 'center' });
    for (const o of this.state.objects) {
      if (excludeIds.has(o.id) || o.visible === false) continue;
      xs.push({ v: o.x, kind: 'obj' }, { v: o.x + o.w / 2, kind: 'obj' }, { v: o.x + o.w, kind: 'obj' });
      ys.push({ v: o.y, kind: 'obj' }, { v: o.y + o.h / 2, kind: 'obj' }, { v: o.y + o.h, kind: 'obj' });
    }
    return { xs, ys };
  }

  /** 이동 중 스냅: 바운딩박스의 좌/중/우, 상/중/하를 후보와 맞춘다 */
  _snapMove(b, excludeIds) {
    const tolMm = this.snapPx / this.view.scale;
    const { xs, ys } = this._snapTargets(excludeIds);
    const guides = [];
    let dx = 0, dy = 0, bestX = tolMm, bestY = tolMm;
    const edgesX = [b.x, b.x + b.w / 2, b.x + b.w];
    const edgesY = [b.y, b.y + b.h / 2, b.y + b.h];
    for (const e of edgesX) for (const t of xs) {
      const d = t.v - e;
      if (Math.abs(d) < bestX) { bestX = Math.abs(d); dx = d; }
    }
    for (const e of edgesY) for (const t of ys) {
      const d = t.v - e;
      if (Math.abs(d) < bestY) { bestY = Math.abs(d); dy = d; }
    }
    // 실제로 맞은 선만 가이드로 표시
    if (dx !== 0 || bestX < tolMm) {
      for (const e of edgesX) for (const t of xs) {
        if (Math.abs(t.v - (e + dx)) < 0.001) guides.push({ axis: 'x', v: t.v });
      }
    }
    if (dy !== 0 || bestY < tolMm) {
      for (const e of edgesY) for (const t of ys) {
        if (Math.abs(t.v - (e + dy)) < 0.001) guides.push({ axis: 'y', v: t.v });
      }
    }
    return { dx, dy, guides };
  }

  _snapValue(v, axis, excludeIds) {
    const tolMm = this.snapPx / this.view.scale;
    const { xs, ys } = this._snapTargets(excludeIds);
    const list = axis === 'x' ? xs : ys;
    let best = tolMm, out = v, hit = null;
    for (const t of list) {
      const d = Math.abs(t.v - v);
      if (d < best) { best = d; out = t.v; hit = t.v; }
    }
    return { v: out, guide: hit == null ? null : { axis, v: hit } };
  }

  /* ================= 입력 ================= */
  _bind() {
    const cv = this.cv;

    cv.addEventListener('contextmenu', (e) => e.preventDefault());

    cv.addEventListener('wheel', (e) => {
      e.preventDefault();
      const rect = cv.getBoundingClientRect();
      const px = e.clientX - rect.left, py = e.clientY - rect.top;
      if (e.ctrlKey || e.metaKey || !e.shiftKey) {
        const factor = e.deltaY < 0 ? 1.12 : 1 / 1.12;
        const [mx, my] = this.px2mm(px, py);
        this.view.scale = Math.min(200, Math.max(0.2, this.view.scale * factor));
        this.view.ox = px - mx * this.view.scale;
        this.view.oy = py - my * this.view.scale;
      } else {
        this.view.ox -= e.deltaX || 0;
        this.view.oy -= e.deltaY || 0;
      }
      this._bgCache = null;
      this.onViewChange();
      this.render();
    }, { passive: false });

    cv.addEventListener('pointerdown', (e) => {
      cv.setPointerCapture(e.pointerId);
      cv.focus();
      const rect = cv.getBoundingClientRect();
      const px = e.clientX - rect.left, py = e.clientY - rect.top;
      const [mx, my] = this.px2mm(px, py);

      if (e.button === 1 || e.button === 2 || this.spaceDown) {
        this.drag = { mode: 'pan', sx: px, sy: py, ox: this.view.ox, oy: this.view.oy };
        cv.style.cursor = 'grabbing';
        return;
      }
      if (e.button !== 0) return;

      const dir = this.hitHandle(px, py);
      if (dir) {
        const sel = this.selectedObjects().filter(o => !o.locked);
        this.beginTx('크기 조절');
        this.drag = {
          mode: 'resize', dir, sx: mx, sy: my,
          start: sel.map(o => ({ o, x: o.x, y: o.y, w: o.w, h: o.h })),
          box: this.selectionBounds(),
        };
        return;
      }

      const obj = this.hitObject(mx, my);
      if (obj) {
        if (e.shiftKey || e.ctrlKey) {
          if (this.selection.has(obj.id)) this.selection.delete(obj.id);
          else this.selection.add(obj.id);
          this.select([...this.selection]);
        } else if (!this.selection.has(obj.id)) {
          this.select([obj.id]);
        }
        this.beginTx('이동');
        this.drag = {
          mode: 'move', sx: mx, sy: my, moved: false,
          start: this.selectedObjects().filter(o => !o.locked).map(o => ({ o, x: o.x, y: o.y })),
          box: this.selectionBounds(),
        };
      } else {
        this.drag = { mode: 'marquee', sx: mx, sy: my, ex: mx, ey: my, add: e.shiftKey };
        if (!e.shiftKey) this.select([]);
      }
    });

    cv.addEventListener('pointermove', (e) => {
      const rect = cv.getBoundingClientRect();
      const px = e.clientX - rect.left, py = e.clientY - rect.top;
      const [mx, my] = this.px2mm(px, py);

      if (!this.drag) {
        const dir = this.hitHandle(px, py);
        cv.style.cursor = dir
          ? ({ n: 'ns-resize', s: 'ns-resize', e: 'ew-resize', w: 'ew-resize',
               nw: 'nwse-resize', se: 'nwse-resize', ne: 'nesw-resize', sw: 'nesw-resize' })[dir]
          : (this.spaceDown ? 'grab' : (this.hitObject(mx, my) ? 'move' : 'default'));
        this.onHover(mx, my);
        return;
      }

      const d = this.drag;
      if (d.mode === 'pan') {
        this.view.ox = d.ox + (px - d.sx);
        this.view.oy = d.oy + (py - d.sy);
        this._bgCache = null;
        this.onViewChange();
        this.render();
        return;
      }
      if (d.mode === 'marquee') {
        d.ex = mx; d.ey = my;
        this.render();
        return;
      }

      const snapOff = e.altKey || !this.snapEnabled;
      let dx = mx - d.sx, dy = my - d.sy;

      if (d.mode === 'move') {
        d.moved = true;
        const ids = new Set(d.start.map(s => s.o.id));
        let b = { x: d.box.x + dx, y: d.box.y + dy, w: d.box.w, h: d.box.h };
        this.guides = [];
        if (!snapOff) {
          const s = this._snapMove(b, ids);
          dx += s.dx; dy += s.dy;
          this.guides = s.guides;
        }
        if (e.shiftKey) { if (Math.abs(dx) > Math.abs(dy)) dy = 0; else dx = 0; }   // 직선 이동
        for (const s of d.start) {
          s.o.x = Math.round((s.x + dx) * 100) / 100;
          s.o.y = Math.round((s.y + dy) * 100) / 100;
        }
        this.render();
      } else if (d.mode === 'resize') {
        const b0 = d.box;
        let nx = b0.x, ny = b0.y, nw = b0.w, nh = b0.h;
        const dir = d.dir;
        const ids = new Set(d.start.map(s => s.o.id));
        this.guides = [];

        if (dir.includes('e')) {
          let v = b0.x + b0.w + dx;
          if (!snapOff) { const s = this._snapValue(v, 'x', ids); v = s.v; if (s.guide) this.guides.push(s.guide); }
          nw = v - b0.x;
        }
        if (dir.includes('w')) {
          let v = b0.x + dx;
          if (!snapOff) { const s = this._snapValue(v, 'x', ids); v = s.v; if (s.guide) this.guides.push(s.guide); }
          nx = v; nw = b0.x + b0.w - v;
        }
        if (dir.includes('s')) {
          let v = b0.y + b0.h + dy;
          if (!snapOff) { const s = this._snapValue(v, 'y', ids); v = s.v; if (s.guide) this.guides.push(s.guide); }
          nh = v - b0.y;
        }
        if (dir.includes('n')) {
          let v = b0.y + dy;
          if (!snapOff) { const s = this._snapValue(v, 'y', ids); v = s.v; if (s.guide) this.guides.push(s.guide); }
          ny = v; nh = b0.y + b0.h - v;
        }
        // 종횡비 고정: 모서리 핸들 + (이미지/바코드 또는 Shift)
        const only = d.start.length === 1 ? d.start[0].o : null;
        const keepAR = dir.length === 2 &&
          ((only && (only.type === 'image' || only.type === 'barcode') && !e.shiftKey) ||
           (e.shiftKey && !(only && (only.type === 'image' || only.type === 'barcode'))));
        if (keepAR && b0.w > 0 && b0.h > 0) {
          const ar = b0.w / b0.h;
          if (Math.abs(nw / b0.w) > Math.abs(nh / b0.h)) nh = nw / ar; else nw = nh * ar;
          if (dir.includes('n')) ny = b0.y + b0.h - nh;
          if (dir.includes('w')) nx = b0.x + b0.w - nw;
        }
        if (nw < 0.5 || nh < 0.5) return;

        const fx = nw / b0.w, fy = nh / b0.h;
        for (const s of d.start) {
          s.o.x = Math.round((nx + (s.x - b0.x) * fx) * 100) / 100;
          s.o.y = Math.round((ny + (s.y - b0.y) * fy) * 100) / 100;
          s.o.w = Math.round((s.w * fx) * 100) / 100;
          s.o.h = Math.round((s.h * fy) * 100) / 100;
        }
        this.render();
      }
    });

    const endDrag = (e) => {
      const d = this.drag;
      this.drag = null;
      this.guides = [];
      this.cv.style.cursor = 'default';
      if (!d) return;
      if (d.mode === 'marquee') {
        const x0 = Math.min(d.sx, d.ex), x1 = Math.max(d.sx, d.ex);
        const y0 = Math.min(d.sy, d.ey), y1 = Math.max(d.sy, d.ey);
        if (Math.abs(x1 - x0) > 0.5 || Math.abs(y1 - y0) > 0.5) {
          const ids = d.add ? [...this.selection] : [];
          for (const o of this.state.objects) {
            if (o.locked || o.visible === false) continue;
            if (o.x < x1 && o.x + o.w > x0 && o.y < y1 && o.y + o.h > y0) ids.push(o.id);
          }
          this.select(ids);
        }
        this.render();
        return;
      }
      if (d.mode === 'move' || d.mode === 'resize') {
        this.endTx(true);
        this.onSelectionChange();
        this.onModelChange();
      }
      this.render();
    };
    cv.addEventListener('pointerup', endDrag);
    cv.addEventListener('pointercancel', endDrag);

    cv.addEventListener('dblclick', () => {
      if (this.onDoubleClick) this.onDoubleClick();
    });
  }

  /** app.js에서 호출 — 편집기 단축키 (입력창에 포커스가 있으면 무시) */
  handleKey(e) {
    if (e.code === 'Space' && !e.repeat) { this.spaceDown = true; return true; }
    const mod = e.ctrlKey || e.metaKey;
    if (mod && e.key.toLowerCase() === 'z') { e.shiftKey ? this.redo() : this.undo(); return true; }
    if (mod && e.key.toLowerCase() === 'y') { this.redo(); return true; }
    if (mod && e.key.toLowerCase() === 'a') { this.selectAll(); return true; }
    if (mod && e.key.toLowerCase() === 'd') { this.duplicate(); return true; }
    if (mod && e.key === '0') { this.zoomFit(); return true; }
    if (mod && e.key === '1') { this.zoomTo(100); return true; }
    if (e.key === 'Delete' || e.key === 'Backspace') {
      if (this.selection.size) { this.deleteSelection(); return true; }
    }
    if (e.key === 'Escape') { this.select([]); return true; }
    if (e.key === 'Tab' && this.state.objects.length) {
      const list = this.state.objects.filter(o => !o.locked && o.visible !== false);
      if (!list.length) return true;
      const cur = [...this.selection][0];
      const i = list.findIndex(o => o.id === cur);
      const next = list[(i + (e.shiftKey ? -1 : 1) + list.length) % list.length];
      this.select([next.id]);
      return true;
    }
    const step = e.shiftKey ? 1 : (e.altKey ? 0.01 : 0.1);
    const mv = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[e.key];
    if (mv && this.selection.size) {
      this.commit('이동', () => {
        for (const o of this.selectedObjects()) {
          if (o.locked) continue;
          o.x = Math.round((o.x + mv[0]) * 100) / 100;
          o.y = Math.round((o.y + mv[1]) * 100) / 100;
        }
      });
      this.onSelectionChange();
      return true;
    }
    return false;
  }
  handleKeyUp(e) { if (e.code === 'Space') this.spaceDown = false; }

  _resize() {
    const dpr = window.devicePixelRatio || 1;
    const w = this.cv.clientWidth, h = this.cv.clientHeight;
    if (!w || !h) return;
    this.cv.width = Math.round(w * dpr);
    this.cv.height = Math.round(h * dpr);
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    this._bgCache = null;
    this.render();
  }

  /* ================= 이미지 ================= */
  getImage(dataUrl) {
    if (!dataUrl) return null;
    let img = this.imageCache.get(dataUrl);
    if (!img) {
      img = new Image();
      img.onload = () => { this._bgCache = null; this.render(); };
      img.src = dataUrl;
      this.imageCache.set(dataUrl, img);
      if (this.imageCache.size > 120) {
        const k = this.imageCache.keys().next().value;
        this.imageCache.delete(k);
      }
    }
    return img.complete && img.naturalWidth ? img : null;
  }

  /* ================= 렌더 ================= */
  render() {
    const ctx = this.ctx;
    const cw = this.cv.clientWidth, ch = this.cv.clientHeight;
    if (!cw || !ch) return;
    const css = getComputedStyle(document.documentElement);
    const C = (n, fb) => (css.getPropertyValue(n) || '').trim() || fb;

    ctx.clearRect(0, 0, cw, ch);
    ctx.fillStyle = C('--canvas-bg', '#8A97A6');
    ctx.fillRect(0, 0, cw, ch);

    const L = this.state.label;
    const s = this.view.scale;
    const PV0 = this.previewMode;
    const [lx, ly] = this.mm2px(0, 0);
    const LW = L.w * s, LH = L.h * s;

    // 용지
    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,.35)'; ctx.shadowBlur = 16; ctx.shadowOffsetY = 3;
    ctx.fillStyle = '#fff';
    ctx.fillRect(lx, ly, LW, LH);
    ctx.restore();

    // 배경 서식
    if (L.bg) {
      const bg = this.getImage(L.bg);
      if (bg) {
        ctx.save();
        ctx.beginPath(); ctx.rect(lx, ly, LW, LH); ctx.clip();
        ctx.drawImage(bg, lx, ly, LW, LH);
        ctx.restore();
      }
    }

    // 그리드
    if (this.showGrid && !PV0 && s * this.gridMm > 4) {
      ctx.save();
      ctx.beginPath(); ctx.rect(lx, ly, LW, LH); ctx.clip();
      ctx.strokeStyle = C('--canvas-grid', 'rgba(26,111,181,.16)');
      ctx.lineWidth = 1;
      for (let x = 0; x <= L.w + 0.001; x += this.gridMm) {
        const px = Math.round(lx + x * s) + 0.5;
        ctx.beginPath(); ctx.moveTo(px, ly); ctx.lineTo(px, ly + LH); ctx.stroke();
      }
      for (let y = 0; y <= L.h + 0.001; y += this.gridMm) {
        const py = Math.round(ly + y * s) + 0.5;
        ctx.beginPath(); ctx.moveTo(lx, py); ctx.lineTo(lx + LW, py); ctx.stroke();
      }
      ctx.restore();
    }

    // 객체 (실물 미리보기면 편집 보조선 없이)
    const PV = this.previewMode;
    for (const o of this.state.objects) {
      if (o.visible === false) continue;
      this.drawObject(ctx, o, s, this.view.ox, this.view.oy, PV);
    }

    // 라벨 외곽선
    ctx.strokeStyle = C('--canvas-edge', '#4B5563');
    ctx.lineWidth = 1;
    ctx.strokeRect(lx - 0.5, ly - 0.5, LW + 1, LH + 1);

    // 선택 표시
    const sel = PV0 ? [] : this.selectedObjects();
    if (sel.length) {
      const accent = C('--accent', '#1A6FB5');
      for (const o of sel) {
        const [x1, y1] = this.mm2px(o.x, o.y);
        ctx.strokeStyle = accent;
        ctx.lineWidth = 1;
        ctx.setLineDash([4, 3]);
        ctx.strokeRect(Math.round(x1) + .5, Math.round(y1) + .5, o.w * s, o.h * s);
        ctx.setLineDash([]);
      }
      const b = this.selectionBounds();
      if (b) {
        const [bx, by] = this.mm2px(b.x, b.y);
        if (sel.length > 1) {
          ctx.strokeStyle = accent; ctx.lineWidth = 1.5;
          ctx.strokeRect(Math.round(bx) + .5, Math.round(by) + .5, b.w * s, b.h * s);
        }
        const locked = sel.every(o => o.locked);
        for (const h of this._handleRects()) {
          ctx.fillStyle = locked ? '#bbb' : '#fff';
          ctx.strokeStyle = locked ? '#888' : accent;
          ctx.lineWidth = 1.5;
          ctx.fillRect(h.x - 4, h.y - 4, 8, 8);
          ctx.strokeRect(h.x - 4, h.y - 4, 8, 8);
        }
      }
    }

    // 데이터 링크 오버레이 (선택 표시 위, 가이드 아래)
    if (!PV0 && LB.link) {
      try { LB.link.draw(ctx, this, this.linkMode); } catch (_) {}
    }

    // 스냅 가이드
    if (this.guides.length && !PV0) {
      ctx.save();
      ctx.strokeStyle = C('--snap-guide', '#E0218A');
      ctx.lineWidth = 1;
      ctx.setLineDash([5, 3]);
      const seen = new Set();
      for (const g of this.guides) {
        const k = g.axis + g.v.toFixed(3);
        if (seen.has(k)) continue;
        seen.add(k);
        ctx.beginPath();
        if (g.axis === 'x') {
          const px = Math.round(lx + g.v * s) + 0.5;
          ctx.moveTo(px, ly - 20); ctx.lineTo(px, ly + LH + 20);
        } else {
          const py = Math.round(ly + g.v * s) + 0.5;
          ctx.moveTo(lx - 20, py); ctx.lineTo(lx + LW + 20, py);
        }
        ctx.stroke();
      }
      ctx.setLineDash([]);
      ctx.restore();
    }

    // 마키 선택
    if (this.drag && this.drag.mode === 'marquee' && !PV0) {
      const d = this.drag;
      const [ax, ay] = this.mm2px(Math.min(d.sx, d.ex), Math.min(d.sy, d.ey));
      const w = Math.abs(d.ex - d.sx) * s, h = Math.abs(d.ey - d.sy) * s;
      ctx.fillStyle = 'rgba(26,111,181,.12)';
      ctx.strokeStyle = C('--accent', '#1A6FB5');
      ctx.lineWidth = 1;
      ctx.fillRect(ax, ay, w, h);
      ctx.strokeRect(Math.round(ax) + .5, Math.round(ay) + .5, w, h);
    }

    if (this.showRulers) this._drawRulers(ctx, cw, ch, C);
    this.onViewChange();
  }

  _drawRulers(ctx, cw, ch, C) {
    const R = LB.Editor.RULER;
    const s = this.view.scale;
    const [lx, ly] = this.mm2px(0, 0);
    const bg = C('--paper', '#F4F6F9'), line = C('--line', '#CCD3DB'), ink = C('--ink3', '#6B7280');

    ctx.save();
    ctx.fillStyle = bg;
    ctx.fillRect(0, 0, cw, R);
    ctx.fillRect(0, 0, R, ch);
    ctx.strokeStyle = line; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(0, R + .5); ctx.lineTo(cw, R + .5);
    ctx.moveTo(R + .5, 0); ctx.lineTo(R + .5, ch); ctx.stroke();

    // 눈금 간격: 화면에서 최소 6px 이상 되는 mm 단위 선택
    const steps = [1, 2, 5, 10, 20, 50, 100];
    let step = steps.find(v => v * s >= 6) || 100;
    const labelEvery = step * (step * s >= 40 ? 1 : (step * s >= 20 ? 2 : 5));

    ctx.fillStyle = ink;
    ctx.font = '9px ' + C('--mono', 'monospace');
    ctx.textBaseline = 'top';

    const L = this.state.label;
    const startX = Math.floor(((R - lx) / s) / step) * step;
    for (let mm = startX; mm * s + lx < cw; mm += step) {
      const px = Math.round(lx + mm * s) + 0.5;
      if (px < R) continue;
      const major = Math.abs(mm % labelEvery) < 0.001;
      const inLabel = mm >= -0.001 && mm <= L.w + 0.001;
      ctx.strokeStyle = inLabel ? ink : line;
      ctx.beginPath();
      ctx.moveTo(px, major ? R - 9 : R - 4); ctx.lineTo(px, R);
      ctx.stroke();
      if (major) { ctx.textAlign = 'left'; ctx.fillText(String(Math.round(mm)), px + 2, 2); }
    }
    const startY = Math.floor(((R - ly) / s) / step) * step;
    ctx.textAlign = 'left';
    for (let mm = startY; mm * s + ly < ch; mm += step) {
      const py = Math.round(ly + mm * s) + 0.5;
      if (py < R) continue;
      const major = Math.abs(mm % labelEvery) < 0.001;
      const inLabel = mm >= -0.001 && mm <= L.h + 0.001;
      ctx.strokeStyle = inLabel ? ink : line;
      ctx.beginPath();
      ctx.moveTo(major ? R - 9 : R - 4, py); ctx.lineTo(R, py);
      ctx.stroke();
      if (major) {
        ctx.save(); ctx.translate(2, py + 2); ctx.rotate(-Math.PI / 2);
        ctx.textAlign = 'right'; ctx.fillText(String(Math.round(mm)), 0, 0);
        ctx.restore();
      }
    }
    // 선택 영역을 눈금자에 하이라이트
    const b = this.selectionBounds();
    if (b) {
      ctx.fillStyle = C('--accent-soft', '#E3EEF8');
      const [x1] = this.mm2px(b.x, 0), [x2] = this.mm2px(b.x + b.w, 0);
      const [, y1] = this.mm2px(0, b.y), [, y2] = this.mm2px(0, b.y + b.h);
      ctx.globalAlpha = .75;
      ctx.fillRect(Math.max(R, x1), R - 4, Math.max(1, x2 - x1), 4);
      ctx.fillRect(R - 4, Math.max(R, y1), 4, Math.max(1, y2 - y1));
      ctx.globalAlpha = 1;
    }
    ctx.fillStyle = bg;
    ctx.fillRect(0, 0, R, R);
    ctx.strokeStyle = line;
    ctx.strokeRect(0.5, 0.5, R, R);
    ctx.restore();
  }

  /**
   * 객체 하나 그리기.
   * @param forExport true면 편집용 보조선/플레이스홀더를 그리지 않는다
   */
  drawObject(ctx, o, scale, ox, oy, forExport) {
    const X = o.x * scale + ox, Y = o.y * scale + oy;
    const W = o.w * scale, H = o.h * scale;
    const css = getComputedStyle(document.documentElement);
    const C = (n, fb) => (css.getPropertyValue(n) || '').trim() || fb;

    if (o.type === 'text') {
      const txt = this.resolver(o.text || '');
      let L = null;
      ctx.save();
      if (o.clip !== false) { ctx.beginPath(); ctx.rect(X - 1, Y - 1, W + 2, H + 2); ctx.clip(); }
      L = LB.text.draw(ctx, o, txt, scale, ox, oy);
      ctx.restore();
      if (!forExport) {
        const over = L && (L.overflowX || L.overflowY);
        this._frame(ctx, X, Y, W, H, over ? C('--fail', '#9C0006') : 'rgba(26,111,181,.28)', over ? 1.2 : 1);
        if (!String(txt).trim()) this._ghost(ctx, X, Y, scale, '빈 텍스트', C('--ink3', '#6B7280'));
      }

    } else if (o.type === 'image') {
      const img = o.dataUrl ? this.getImage(o.dataUrl) : null;
      if (img) {
        const r = this._fitRect(img.naturalWidth, img.naturalHeight, X, Y, W, H, o.fit || 'center', o.vFit || 'middle', o.fitMode);
        ctx.save();
        ctx.beginPath(); ctx.rect(X, Y, W, H); ctx.clip();
        ctx.drawImage(img, r.x, r.y, r.w, r.h);
        ctx.restore();
      }
      if (!forExport) {
        const missing = !img && !!o.sourceField;
        this._frame(ctx, X, Y, W, H,
          img ? 'rgba(26,111,181,.28)' : (missing ? C('--warn', '#9A5B00') : 'rgba(107,114,128,.5)'), 1);
        if (!img) {
          const lbl = o.sourceField
            ? (o.fileName ? `이미지 없음: ${o.fileName}` : `이미지 슬롯 · ${o.sourceField}`)
            : '이미지 슬롯';
          this._ghost(ctx, X, Y, scale, lbl, missing ? C('--warn', '#9A5B00') : C('--ink3', '#6B7280'));
        }
      }

    } else if (o.type === 'barcode') {
      const data = LB.barcode.resolveData(o, this.barcodeCtx());
      const res = LB.barcode.draw(ctx, o, data, scale, ox, oy);
      if (!forExport) {
        const bad = !res.ok;
        this._frame(ctx, X, Y, W, H, bad ? C('--fail', '#9C0006') : 'rgba(46,158,91,.45)', bad ? 1.2 : 1);
        if (bad) {
          this._ghost(ctx, X, Y, scale, res.err || '값 없음', C('--fail', '#9C0006'));
        }
      }
    }

    // 잠금 표시
    if (!forExport && !this.previewMode && o.locked) {
      ctx.save();
      ctx.fillStyle = 'rgba(107,114,128,.55)';
      ctx.font = `${Math.max(9, Math.min(13, scale * 2.4))}px sans-serif`;
      ctx.textBaseline = 'top';
      ctx.fillText('🔒', X + 2, Y + 2);
      ctx.restore();
    }
  }

  _frame(ctx, X, Y, W, H, color, lw) {
    ctx.save();
    ctx.strokeStyle = color;
    ctx.setLineDash([3, 3]);
    ctx.lineWidth = lw || 1;
    ctx.strokeRect(Math.round(X) + .5, Math.round(Y) + .5, W, H);
    ctx.setLineDash([]);
    ctx.restore();
  }
  _ghost(ctx, X, Y, scale, label, color) {
    ctx.save();
    ctx.fillStyle = color;
    const px = Math.max(9, Math.min(14, scale * 2.2));
    ctx.font = `${px}px sans-serif`;
    ctx.textBaseline = 'top';
    ctx.fillText(label, X + 3, Y + 3);
    ctx.restore();
  }

  _fitRect(nw, nh, X, Y, W, H, hAlign, vAlign, fitMode) {
    if (fitMode === 'stretch') return { x: X, y: Y, w: W, h: H };
    const sc = (fitMode === 'cover') ? Math.max(W / nw, H / nh) : Math.min(W / nw, H / nh);
    const w = nw * sc, h = nh * sc;
    let x = X + (W - w) / 2;
    if (hAlign === 'left') x = X;
    else if (hAlign === 'right') x = X + W - w;
    let y = Y + (H - h) / 2;
    if (vAlign === 'top') y = Y;
    else if (vAlign === 'bottom') y = Y + H - h;
    return { x, y, w, h };
  }

  /** 객체의 표시용 이름 */
  labelOf(o) {
    if (o.name) return o.name;
    if (o.type === 'text') {
      const s = String(this.resolver(o.text || '')).replace(/\s+/g, ' ').trim();
      return s ? (s.length > 26 ? s.slice(0, 26) + '…' : s) : '(빈 텍스트)';
    }
    if (o.type === 'image') {
      if (!o.sourceField) return o.fileName || '이미지';
      return String(o.sourceField).startsWith('@')
        ? `이미지 · DB ${o.sourceField.slice(1)}열`
        : `이미지 · ${o.sourceField}`;
    }
    if (o.type === 'barcode') return LB.barcode.byId(o.symbology).n;
    return o.type;
  }
};
