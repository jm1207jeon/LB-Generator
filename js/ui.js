/* ui.js — 상태바 · 토스트 · 모달 · 확인 대화상자
 *
 * 상태바 색 규칙 (UDInspect와 동일)
 *   검정 = 안내, 갈색 굵게 = 경고, 붉은 굵게 = 오류
 */
window.LB = window.LB || {};

LB.ui = (() => {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const p2 = (n) => String(n).padStart(2, '0');

  /* ---------------- 상태바 ---------------- */
  const log = [];
  function status(msg, level) {
    const now = new Date();
    const stamped = `[${p2(now.getHours())}:${p2(now.getMinutes())}:${p2(now.getSeconds())}] ${msg}`;
    log.push({ at: now.toISOString(), msg, level: level || 'info' });
    if (log.length > 500) log.shift();
    const el = $('statusMsg');
    if (el) {
      el.textContent = stamped;
      el.className = level === 'error' ? 'err' : (level === 'warn' ? 'warn' : '');
      el.title = stamped;
    }
    return stamped;
  }
  function statusLog() { return log.slice(); }

  /* ---------------- 토스트 ---------------- */
  function toast(msg, level, ms) {
    const root = $('toasts');
    if (!root) return;
    const el = document.createElement('div');
    el.className = 'toast' + (level ? ' ' + level : '');
    el.textContent = msg;
    root.appendChild(el);
    setTimeout(() => {
      el.style.transition = 'opacity .2s';
      el.style.opacity = '0';
      setTimeout(() => el.remove(), 220);
    }, ms || (level === 'err' ? 5200 : 2800));
  }

  /* ---------------- 모달 ---------------- */
  let openCount = 0;

  /**
   * 모달을 띄운다.
   * @param {object} o {title, body(HTMLElement|string), buttons:[{label,value,cls,primary}],
   *                    wide, defaultValue, onMount(el)}
   * @returns Promise<value>   배경/Esc로 닫으면 defaultValue(기본 null)
   */
  function modal(o) {
    return new Promise((resolve) => {
      const back = document.createElement('div');
      back.className = 'modal-back';
      const box = document.createElement('div');
      box.className = 'modal' + (o.wide ? ' wide' : '');
      box.setAttribute('role', 'dialog');
      box.setAttribute('aria-modal', 'true');

      const head = document.createElement('div');
      head.className = 'modal-head';
      const h = document.createElement('h2');
      h.textContent = o.title || '';
      head.appendChild(h);
      const sp = document.createElement('span'); sp.className = 'spacer'; head.appendChild(sp);
      const x = document.createElement('button');
      x.className = 'btn sm ghost icon'; x.textContent = '✕'; x.title = '닫기 (Esc)';
      head.appendChild(x);

      const body = document.createElement('div');
      body.className = 'modal-body';
      if (typeof o.body === 'string') body.innerHTML = o.body;
      else if (o.body) body.appendChild(o.body);

      const foot = document.createElement('div');
      foot.className = 'modal-foot';
      const fsp = document.createElement('span'); fsp.className = 'spacer';
      foot.appendChild(fsp);

      let done = false;
      const close = (v) => {
        if (done) return;
        done = true;
        document.removeEventListener('keydown', onKey, true);
        back.remove();
        openCount--;
        resolve(v);
      };

      for (const b of (o.buttons || [{ label: '닫기', value: null }])) {
        const btn = document.createElement('button');
        btn.className = 'btn' + (b.primary ? ' primary' : '') + (b.cls ? ' ' + b.cls : '');
        btn.textContent = b.label;
        btn.onclick = () => {
          if (b.before && b.before(body) === false) return;
          close(b.value);
        };
        if (b.id) btn.id = b.id;
        foot.appendChild(btn);
        if (b.autofocus) setTimeout(() => btn.focus(), 30);
      }

      x.onclick = () => close(o.defaultValue === undefined ? null : o.defaultValue);
      back.onclick = (e) => { if (e.target === back) close(o.defaultValue === undefined ? null : o.defaultValue); };

      const onKey = (e) => {
        if (e.key === 'Escape') { e.stopPropagation(); close(o.defaultValue === undefined ? null : o.defaultValue); }
        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
          const pr = foot.querySelector('.btn.primary');
          if (pr) { e.preventDefault(); pr.click(); }
        }
      };
      document.addEventListener('keydown', onKey, true);

      box.append(head, body, foot);
      back.appendChild(box);
      $('modalRoot').appendChild(back);
      openCount++;
      if (o.onMount) o.onMount(body, { close });
      const first = body.querySelector('input,select,textarea,button');
      if (first && !o.noAutofocus) setTimeout(() => first.focus(), 30);
    });
  }

  function isModalOpen() { return openCount > 0; }

  /** 예/아니오. 파괴적 동작은 기본 버튼이 '아니오'다. */
  function confirm(msg, { title = '확인', okLabel = '예', cancelLabel = '아니오', danger = false, detail = '' } = {}) {
    const body = document.createElement('div');
    const p = document.createElement('p');
    p.style.margin = '0 0 6px';
    p.style.fontSize = '13.5px';
    p.textContent = msg;
    body.appendChild(p);
    if (detail) {
      const d = document.createElement('div');
      d.className = 'hint';
      d.textContent = detail;
      body.appendChild(d);
    }
    return modal({
      title, body, defaultValue: false,
      buttons: [
        { label: cancelLabel, value: false, autofocus: true },
        { label: okLabel, value: true, primary: !danger, cls: danger ? 'danger' : '' },
      ],
    });
  }

  /** 한 줄 입력 */
  function prompt(msg, { title = '입력', value = '', placeholder = '', okLabel = '확인' } = {}) {
    const body = document.createElement('div');
    const lab = document.createElement('div');
    lab.className = 'hint'; lab.style.marginBottom = '6px'; lab.textContent = msg;
    const inp = document.createElement('input');
    inp.type = 'text'; inp.value = value; inp.placeholder = placeholder;
    inp.onkeydown = (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        const pr = inp.closest('.modal').querySelector('.modal-foot .btn.primary');
        if (pr) pr.click();
      }
    };
    body.append(lab, inp);
    return modal({
      title, body, defaultValue: null,
      buttons: [
        { label: '취소', value: null },
        { label: okLabel, value: '__ok__', primary: true, before: () => true },
      ],
    }).then(v => (v === '__ok__' ? inp.value : null));
  }

  /* ---------------- 진행 표시 ---------------- */
  function progress(title) {
    const body = document.createElement('div');
    const txt = document.createElement('div');
    txt.className = 'hint'; txt.style.marginBottom = '8px';
    const bar = document.createElement('div');
    bar.className = 'progress'; bar.style.width = '100%';
    const fill = document.createElement('i');
    bar.appendChild(fill);
    body.append(txt, bar);
    let closeFn = null;
    const pr = modal({
      title, body, noAutofocus: true, defaultValue: 'cancel',
      buttons: [{ label: '중지', value: 'cancel', cls: 'danger' }],
      onMount: (_b, api) => { closeFn = api.close; },
    });
    return {
      promise: pr,
      set(i, total, label) {
        fill.style.width = total ? (i / total * 100).toFixed(1) + '%' : '0%';
        txt.textContent = label || `${i} / ${total}`;
      },
      close() { if (closeFn) closeFn('done'); },
    };
  }

  /* ---------------- 유틸 ---------------- */
  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }
  function clear(node) { while (node && node.firstChild) node.removeChild(node.firstChild); }

  /** 사용자가 복사할 수 있도록 텍스트 다운로드 */
  function downloadText(text, fileName, mime) {
    const blob = new Blob([text], { type: mime || 'text/plain;charset=utf-8' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = fileName;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 3000);
  }

  function fmtBytes(n) {
    if (!n) return '0 B';
    const u = ['B', 'KB', 'MB', 'GB'];
    const i = Math.min(u.length - 1, Math.floor(Math.log(n) / Math.log(1024)));
    return (n / Math.pow(1024, i)).toFixed(i ? 1 : 0) + ' ' + u[i];
  }

  return { status, statusLog, toast, modal, confirm, prompt, progress, isModalOpen, el, clear, downloadText, fmtBytes };
})();
