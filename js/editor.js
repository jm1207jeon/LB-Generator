/* editor.js — 캔버스 기반 라벨 편집기
 *  - 좌표계: 라벨 좌상단 원점, 단위 mm
 *  - 뷰: scale(px/mm), ox/oy(px 오프셋). 휠 줌 + 드래그 패닝
 *  - 객체: text / image(슬롯) / barcode. 드래그 이동, 핸들 리사이즈, 다중 선택
 */
window.LB = window.LB || {};

const PT2MM = 25.4 / 72;
const HANDLE = 7;           // 핸들 크기(px)

LB.Editor = class {
  constructor(canvas, state) {
    this.cv = canvas;
    this.ctx = canvas.getContext('2d');
    this.state = state;            // { label:{w,h}, objects:[...] }
    this.view = { scale: 6, ox: 40, oy: 40 };
    this.selection = new Set();    // object id
    this.imageCache = new Map();   // dataUrl -> HTMLImageElement
    this.barcodes = new Map();     // binding -> canvas
    this.resolver = (t) => t;      // 플레이스홀더 치환 함수 (app.js가 주입)
    this.onSelectionChange = () => {};
    this.onModelChange = () => {};
    this.drag = null;
    this._bind();
    this._resize();
    window.addEventListener('resize', () => this._resize());
  }

  /* ---------------- 좌표 변환 ---------------- */
  mm2px(x, y) { return [x * this.view.scale + this.view.ox, y * this.view.scale + this.view.oy]; }
  px2mm(x, y) { return [(x - this.view.ox) / this.view.scale, (y - this.view.oy) / this.view.scale]; }

  zoomFit() {
    const { w, h } = this.state.label;
    const cw = this.cv.clientWidth, ch = this.cv.clientHeight;
    const scale = Math.min((cw - 80) / w, (ch - 80) / h);
    this.view.scale = Math.max(0.5, scale);
    this.view.ox = (cw - w * this.view.scale) / 2;
    this.view.oy = (ch - h * this.view.scale) / 2;
    this.render();
  }

  /* ---------------- 객체 조작 ---------------- */
  byId(id) { return this.state.objects.find(o => o.id === id); }
  selectedObjects() { return this.state.objects.filter(o => this.selection.has(o.id)); }

  addObject(o) {
    o.id = o.id || ('o' + Date.now() + Math.random().toString(36).slice(2, 6));
    this.state.objects.push(o);
    this.select([o.id]);
    this.changed();
    return o;
  }

  deleteSelection() {
    this.state.objects = this.state.objects.filter(o => !this.selection.has(o.id));
    this.select([]);
    this.changed();
  }

  bringToFront() {
    const sel = this.selectedObjects();
    this.state.objects = this.state.objects.filter(o => !this.selection.has(o.id)).concat(sel);
    this.changed();
  }
  sendToBack() {
    const sel = this.selectedObjects();
    this.state.objects = sel.concat(this.state.objects.filter(o => !this.selection.has(o.id)));
    this.changed();
  }

  select(ids) {
    this.selection = new Set(ids);
    this.onSelectionChange();
    this.render();
  }

  changed() {
    this.onModelChange();
    this.render();
  }

  /* ---------------- 히트 테스트 ---------------- */
  hitObject(mx, my) {   // mm 좌표
    for (let i = this.state.objects.length - 1; i >= 0; i--) {
      const o = this.state.objects[i];
      if (mx >= o.x && mx <= o.x + o.w && my >= o.y && my <= o.y + o.h) return o;
    }
    return null;
  }

  hitHandle(px, py) {   // 스크린 px — 단일 선택시 8개 핸들
    if (this.selection.size !== 1) return null;
    const o = this.selectedObjects()[0];
    const hs = this._handlePositions(o);
    for (const h of hs) {
      if (Math.abs(px - h.x) <= HANDLE && Math.abs(py - h.y) <= HANDLE) return { obj: o, dir: h.dir };
    }
    return null;
  }

  _handlePositions(o) {
    const [x1, y1] = this.mm2px(o.x, o.y);
    const [x2, y2] = this.mm2px(o.x + o.w, o.y + o.h);
    const cx = (x1 + x2) / 2, cy = (y1 + y2) / 2;
    return [
      { dir: 'nw', x: x1, y: y1 }, { dir: 'n', x: cx, y: y1 }, { dir: 'ne', x: x2, y: y1 },
      { dir: 'w', x: x1, y: cy }, { dir: 'e', x: x2, y: cy },
      { dir: 'sw', x: x1, y: y2 }, { dir: 's', x: cx, y: y2 }, { dir: 'se', x: x2, y: y2 },
    ];
  }

  /* ---------------- 마우스/키보드 ---------------- */
  _bind() {
    const cv = this.cv;

    cv.addEventListener('wheel', (e) => {
      e.preventDefault();
      const factor = e.deltaY < 0 ? 1.15 : 1 / 1.15;
      const rect = cv.getBoundingClientRect();
      const px = e.clientX - rect.left, py = e.clientY - rect.top;
      const [mx, my] = this.px2mm(px, py);
      this.view.scale = Math.min(120, Math.max(0.5, this.view.scale * factor));
      this.view.ox = px - mx * this.view.scale;
      this.view.oy = py - my * this.view.scale;
      this.render();
    }, { passive: false });

    cv.addEventListener('pointerdown', (e) => {
      cv.setPointerCapture(e.pointerId);
      const rect = cv.getBoundingClientRect();
      const px = e.clientX - rect.left, py = e.clientY - rect.top;
      const [mx, my] = this.px2mm(px, py);

      // 중버튼 or 스페이스 = 패닝
      if (e.button === 1 || this.spaceDown) {
        this.drag = { mode: 'pan', sx: px, sy: py, ox: this.view.ox, oy: this.view.oy };
        return;
      }
      if (e.button !== 0) return;

      const handle = this.hitHandle(px, py);
      if (handle) {
        const o = handle.obj;
        this.drag = { mode: 'resize', dir: handle.dir, obj: o, start: { ...o }, sx: mx, sy: my };
        return;
      }

      const obj = this.hitObject(mx, my);
      if (obj) {
        if (e.shiftKey) {
          if (this.selection.has(obj.id)) this.selection.delete(obj.id);
          else this.selection.add(obj.id);
          this.select([...this.selection]);
        } else if (!this.selection.has(obj.id)) {
          this.select([obj.id]);
        }
        const starts = this.selectedObjects().map(o => ({ o, x: o.x, y: o.y }));
        this.drag = { mode: 'move', starts, sx: mx, sy: my, moved: false };
      } else {
        if (!e.shiftKey) this.select([]);
        this.drag = { mode: 'pan', sx: px, sy: py, ox: this.view.ox, oy: this.view.oy };
      }
    });

    cv.addEventListener('pointermove', (e) => {
      const rect = cv.getBoundingClientRect();
      const px = e.clientX - rect.left, py = e.clientY - rect.top;

      if (!this.drag) {
        // 커서 힌트
        const h = this.hitHandle(px, py);
        const [mx, my] = this.px2mm(px, py);
        cv.style.cursor = h ? ({ n: 'ns-resize', s: 'ns-resize', e: 'ew-resize', w: 'ew-resize', nw: 'nwse-resize', se: 'nwse-resize', ne: 'nesw-resize', sw: 'nesw-resize' })[h.dir]
          : (this.hitObject(mx, my) ? 'move' : 'default');
        return;
      }

      const d = this.drag;
      if (d.mode === 'pan') {
        this.view.ox = d.ox + (px - d.sx);
        this.view.oy = d.oy + (py - d.sy);
        this.render();
        return;
      }

      const [mx, my] = this.px2mm(px, py);
      const dx = mx - d.sx, dy = my - d.sy;

      if (d.mode === 'move') {
        d.moved = true;
        for (const s of d.starts) {
          s.o.x = Math.round((s.x + dx) * 10) / 10;
          s.o.y = Math.round((s.y + dy) * 10) / 10;
        }
        this.render();
      } else if (d.mode === 'resize') {
        const o = d.obj, s = d.start;
        let { x, y, w, h } = s;
        const dir = d.dir;
        if (dir.includes('e')) w = s.w + dx;
        if (dir.includes('s')) h = s.h + dy;
        if (dir.includes('w')) { x = s.x + dx; w = s.w - dx; }
        if (dir.includes('n')) { y = s.y + dy; h = s.h - dy; }
        // 모서리 핸들 + 이미지/바코드 = 종횡비 고정 (Shift로 해제)
        if ((o.type === 'image' || o.type === 'barcode') && dir.length === 2 && !e.shiftKey) {
          const ar = s.w / s.h;
          if (Math.abs(w / s.w) > Math.abs(h / s.h)) h = w / ar; else w = h * ar;
          if (dir.includes('n')) y = s.y + s.h - h;
          if (dir.includes('w')) x = s.x + s.w - w;
        }
        if (w > 0.5 && h > 0.5) {
          o.x = Math.round(x * 10) / 10; o.y = Math.round(y * 10) / 10;
          o.w = Math.round(w * 10) / 10; o.h = Math.round(h * 10) / 10;
        }
        this.render();
      }
    });

    const endDrag = () => {
      if (this.drag && (this.drag.mode === 'move' || this.drag.mode === 'resize')) {
        this.onSelectionChange();   // 속성 패널 좌표 갱신
        this.onModelChange();
      }
      this.drag = null;
    };
    cv.addEventListener('pointerup', endDrag);
    cv.addEventListener('pointercancel', endDrag);

    window.addEventListener('keydown', (e) => {
      if (e.target.matches('input, textarea, select')) return;
      if (e.code === 'Space') { this.spaceDown = true; e.preventDefault(); }
      if ((e.key === 'Delete' || e.key === 'Backspace') && this.selection.size) {
        this.deleteSelection(); e.preventDefault();
      }
      if (e.ctrlKey && e.key === '0') { this.zoomFit(); e.preventDefault(); }
      const step = e.shiftKey ? 1 : 0.1;
      const move = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[e.key];
      if (move && this.selection.size) {
        for (const o of this.selectedObjects()) {
          o.x = Math.round((o.x + move[0]) * 10) / 10;
          o.y = Math.round((o.y + move[1]) * 10) / 10;
        }
        this.onSelectionChange(); this.changed(); e.preventDefault();
      }
    });
    window.addEventListener('keyup', (e) => { if (e.code === 'Space') this.spaceDown = false; });
  }

  _resize() {
    const dpr = window.devicePixelRatio || 1;
    const w = this.cv.clientWidth, h = this.cv.clientHeight;
    if (!w || !h) return;
    this.cv.width = w * dpr;
    this.cv.height = h * dpr;
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.render();
  }

  /* ---------------- 이미지 캐시 ---------------- */
  getImage(dataUrl) {
    let img = this.imageCache.get(dataUrl);
    if (!img) {
      img = new Image();
      img.onload = () => this.render();
      img.src = dataUrl;
      this.imageCache.set(dataUrl, img);
    }
    return img.complete && img.naturalWidth ? img : null;
  }

  setBarcode(binding, canvas) {
    if (canvas) this.barcodes.set(binding, canvas);
    else this.barcodes.delete(binding);
    this.render();
  }

  /* ---------------- 렌더링 ---------------- */
  render() {
    const ctx = this.ctx;
    const cw = this.cv.clientWidth, ch = this.cv.clientHeight;
    ctx.clearRect(0, 0, cw, ch);

    const { w: LW, h: LH } = this.state.label;
    const [lx, ly] = this.mm2px(0, 0);
    const s = this.view.scale;

    // 라벨 용지
    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,0.4)'; ctx.shadowBlur = 14;
    ctx.fillStyle = '#fff';
    ctx.fillRect(lx, ly, LW * s, LH * s);
    ctx.restore();

    // 배경 템플릿 이미지
    if (this.state.label.bg) {
      const bgImg = this.getImage(this.state.label.bg);
      if (bgImg) ctx.drawImage(bgImg, lx, ly, LW * s, LH * s);
    }

    // 객체
    ctx.save();
    ctx.beginPath();
    ctx.rect(lx - 1000, ly - 1000, LW * s + 2000, LH * s + 2000); // 클리핑 안함(라벨 밖 객체도 보이게)
    for (const o of this.state.objects) this.drawObject(ctx, o, s, this.view.ox, this.view.oy, false);
    ctx.restore();

    // 라벨 외곽선
    ctx.strokeStyle = '#607d8b'; ctx.lineWidth = 1;
    ctx.strokeRect(lx - 0.5, ly - 0.5, LW * s + 1, LH * s + 1);

    // 선택 표시
    for (const o of this.selectedObjects()) {
      const [x1, y1] = this.mm2px(o.x, o.y);
      ctx.strokeStyle = '#1e88e5'; ctx.lineWidth = 1.5;
      ctx.setLineDash([4, 3]);
      ctx.strokeRect(x1, y1, o.w * s, o.h * s);
      ctx.setLineDash([]);
    }
    if (this.selection.size === 1) {
      const o = this.selectedObjects()[0];
      for (const h of this._handlePositions(o)) {
        ctx.fillStyle = '#fff'; ctx.strokeStyle = '#1e88e5'; ctx.lineWidth = 1.5;
        ctx.fillRect(h.x - HANDLE / 2, h.y - HANDLE / 2, HANDLE, HANDLE);
        ctx.strokeRect(h.x - HANDLE / 2, h.y - HANDLE / 2, HANDLE, HANDLE);
      }
    }

    const zi = document.getElementById('zoomInfo');
    if (zi) zi.textContent = `${LW}×${LH}mm | ${Math.round(this.view.scale / 3.7795 * 100)}%`;
  }

  /* 객체 하나 그리기 — export=true일 때는 슬롯 점선/플레이스홀더 표시 생략
   * (좌표계: px = mm * scale + o[xy]) */
  drawObject(ctx, o, scale, ox, oy, forExport) {
    const X = o.x * scale + ox, Y = o.y * scale + oy;
    const W = o.w * scale, H = o.h * scale;

    if (o.type === 'text') {
      const text = this.resolver(o.text || '');
      const sizePx = (o.sizePt || 8) * PT2MM * scale;
      const weight = o.bold ? 'bold ' : '';
      const style = o.italic ? 'italic ' : '';
      ctx.save();
      ctx.font = `${style}${weight}${sizePx}px "${o.font || 'Arial'}"`;
      ctx.fillStyle = o.color || '#000';
      ctx.textBaseline = 'alphabetic';
      try { ctx.letterSpacing = `${(o.letterSpacing || 0) * PT2MM * scale}px`; } catch (_) {}
      const lineH = sizePx * (o.lineHeight || 1.15);
      const lines = this._wrapText(ctx, text, W);
      let ty = Y + sizePx * 0.85;
      for (const ln of lines) {
        let tx = X;
        const tw = ctx.measureText(ln).width;
        if (o.align === 'center') tx = X + (W - tw) / 2;
        else if (o.align === 'right') tx = X + W - tw;
        ctx.fillText(ln, tx, ty);
        ty += lineH;
      }
      try { ctx.letterSpacing = '0px'; } catch (_) {}
      ctx.restore();

    } else if (o.type === 'image') {
      const img = o.dataUrl ? this.getImage(o.dataUrl) : null;
      if (img) {
        const r = this._fitRect(img.naturalWidth, img.naturalHeight, X, Y, W, H, o.fit || 'center');
        ctx.drawImage(img, r.x, r.y, r.w, r.h);
      }
      if (!forExport) {
        ctx.save();
        ctx.strokeStyle = img ? 'rgba(30,136,229,0.35)' : 'rgba(198,40,40,0.6)';
        ctx.setLineDash([3, 3]); ctx.lineWidth = 1;
        ctx.strokeRect(X, Y, W, H);
        ctx.setLineDash([]);
        if (!img) {
          ctx.fillStyle = 'rgba(198,40,40,0.7)';
          ctx.font = `${Math.max(9, 2.6 * scale)}px Arial`;
          ctx.fillText(o.sourceField ? `이미지 슬롯 [${o.sourceField}]` : '이미지 슬롯', X + 2, Y + Math.max(10, 3 * scale));
        }
        ctx.restore();
      }

    } else if (o.type === 'barcode') {
      const bc = this.barcodes.get(o.binding || 'UDI_FULL');
      if (bc) {
        const r = this._fitRect(bc.width, bc.height, X, Y, W, H, o.fit || 'center');
        ctx.imageSmoothingEnabled = false;
        ctx.drawImage(bc, r.x, r.y, r.w, r.h);
        ctx.imageSmoothingEnabled = true;
      }
      if (!forExport) {
        ctx.save();
        ctx.strokeStyle = 'rgba(46,125,50,0.5)';
        ctx.setLineDash([3, 3]); ctx.lineWidth = 1;
        ctx.strokeRect(X, Y, W, H);
        ctx.setLineDash([]);
        if (!bc) {
          ctx.fillStyle = 'rgba(46,125,50,0.8)';
          ctx.font = `${Math.max(9, 2.6 * scale)}px Arial`;
          ctx.fillText('DataMatrix (UDI)', X + 2, Y + Math.max(10, 3 * scale));
        }
        ctx.restore();
      }
    }
  }

  _fitRect(nw, nh, X, Y, W, H, align) {
    const sc = Math.min(W / nw, H / nh);
    const w = nw * sc, h = nh * sc;
    let x = X + (W - w) / 2;
    if (align === 'left') x = X;
    else if (align === 'right') x = X + W - w;
    const y = Y + (H - h) / 2;
    return { x, y, w, h };
  }

  _wrapText(ctx, text, maxW) {
    const out = [];
    for (const raw of String(text).split('\n')) {
      if (ctx.measureText(raw).width <= maxW || !raw.includes(' ')) { out.push(raw); continue; }
      let line = '';
      for (const word of raw.split(' ')) {
        const test = line ? line + ' ' + word : word;
        if (ctx.measureText(test).width > maxW && line) { out.push(line); line = word; }
        else line = test;
      }
      if (line) out.push(line);
    }
    return out;
  }
};
