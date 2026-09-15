/* zpl.js — ZEBRA 프린터 출력 (ZPL II)
 *
 * 방식: 라벨을 프린터 해상도의 흑백 비트맵으로 만든 뒤 ^GFA 그래픽 필드로 보낸다.
 *   장점 — 화면·PDF와 **완전히 같은 그림**이 찍힌다. 배경 서식, 한글 글꼴,
 *          자간·장평까지 그대로 재현된다. 프린터에 글꼴을 올릴 필요가 없다.
 *   단점 — 순수 ZPL 텍스트 명령보다 전송량이 크다. 그래서 ZPL 표준
 *          ASCII 압축(G~Y, g~z, ':', ',')을 적용해 보통 10~50배로 줄인다.
 *
 * 보내는 경로 3가지
 *   1) Zebra Browser Print — 제브라가 배포하는 로컬 서비스(http://localhost:9100).
 *      USB·네트워크 프린터를 모두 잡아 주며 현장에서 가장 안정적이다.
 *   2) WebUSB — 크롬에서 USB 프린터에 직접 연결 (설치 불필요, 최초 1회 장치 허용).
 *   3) 파일 저장 — .zpl 파일로 내려받아 기존 출력 시스템에 넘긴다.
 */
window.LB = window.LB || {};

LB.zpl = (() => {
  'use strict';

  const DPI_CHOICES = [
    { v: 203, n: '203 dpi (8 dot/mm) — 대부분의 데스크톱 모델' },
    { v: 300, n: '300 dpi (12 dot/mm) — ZT/ZD 300 계열' },
    { v: 600, n: '600 dpi (24 dot/mm) — 고해상도 모델' },
  ];

  /* ---------------- 비트맵 변환 ---------------- */

  /**
   * 캔버스를 1비트 흑백 비트맵으로. 1 = 검정(인쇄).
   * @param {HTMLCanvasElement} cv
   * @param {object} opt {threshold 0~255, dither:boolean}
   * @returns {{bytes:Uint8Array, widthBytes:number, width:number, height:number}}
   */
  function toMonochrome(cv, opt = {}) {
    const threshold = opt.threshold == null ? 160 : opt.threshold;
    const dither = opt.dither !== false;
    const w = cv.width, h = cv.height;
    const ctx = cv.getContext('2d', { willReadFrequently: true });
    const img = ctx.getImageData(0, 0, w, h);
    const d = img.data;

    // 그레이스케일 (흰 배경 위에 합성된 상태를 가정)
    const gray = new Float32Array(w * h);
    for (let i = 0, p = 0; i < d.length; i += 4, p++) {
      const a = d[i + 3] / 255;
      const lum = 0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2];
      gray[p] = lum * a + 255 * (1 - a);      // 투명은 흰색으로
    }

    if (dither) {
      // Floyd–Steinberg — 제품 사진의 회색조를 살린다
      for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
          const p = y * w + x;
          const old = gray[p];
          const nv = old < threshold ? 0 : 255;
          gray[p] = nv;
          const err = old - nv;
          if (x + 1 < w) gray[p + 1] += err * 7 / 16;
          if (y + 1 < h) {
            if (x > 0) gray[p + w - 1] += err * 3 / 16;
            gray[p + w] += err * 5 / 16;
            if (x + 1 < w) gray[p + w + 1] += err * 1 / 16;
          }
        }
      }
    }

    const widthBytes = Math.ceil(w / 8);
    const bytes = new Uint8Array(widthBytes * h);
    for (let y = 0; y < h; y++) {
      const rowOff = y * widthBytes;
      for (let x = 0; x < w; x++) {
        const v = gray[y * w + x];
        const black = dither ? (v < 128) : (v < threshold);
        if (black) bytes[rowOff + (x >> 3)] |= (0x80 >> (x & 7));
      }
    }
    return { bytes, widthBytes, width: w, height: h };
  }

  /* ---------------- ZPL ASCII 압축 ----------------
   * G..Y = 1..20개 반복, g..z = 20..400개 반복 (20 단위)
   * ','  = 남은 줄을 0으로 채움,  '!' = 남은 줄을 1로 채움
   * ':'  = 바로 윗줄과 동일
   */
  const HEX = '0123456789ABCDEF';
  function repeatCode(n) {
    let s = '';
    const high = Math.floor(n / 20);
    if (high > 0) s += String.fromCharCode('f'.charCodeAt(0) + high);  // g=20 … z=400
    const low = n % 20;
    if (low > 0) s += String.fromCharCode('F'.charCodeAt(0) + low);    // G=1 … Y=20
    return s;
  }

  function compressRows(bytes, widthBytes, height) {
    const out = [];
    let prevRow = null;
    for (let y = 0; y < height; y++) {
      const off = y * widthBytes;
      const row = bytes.subarray(off, off + widthBytes);

      if (prevRow && sameRow(row, prevRow)) { out.push(':'); prevRow = row; continue; }

      // 니블 단위 hex 문자열
      let hex = '';
      for (let i = 0; i < widthBytes; i++) {
        hex += HEX[row[i] >> 4] + HEX[row[i] & 15];
      }
      // 뒤쪽 반복을 , 또는 ! 로
      let line = '';
      let i = 0;
      const n = hex.length;
      while (i < n) {
        const c = hex[i];
        let run = 1;
        while (i + run < n && hex[i + run] === c) run++;
        const rest = n - i;
        if (run === rest && (c === '0' || c === 'F')) {
          line += (c === '0' ? ',' : '!');
          break;
        }
        if (run > 1) line += repeatCode(run) + c;
        else line += c;
        i += run;
      }
      out.push(line);
      prevRow = row;
    }
    return out.join('');
  }
  function sameRow(a, b) {
    if (a.length !== b.length) return false;
    for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
    return true;
  }

  /** ^GFA 필드 문자열 */
  function toGFA(mono, { compress = true } = {}) {
    const total = mono.bytes.length;
    let data;
    if (compress) {
      data = compressRows(mono.bytes, mono.widthBytes, mono.height);
    } else {
      let hex = '';
      for (let i = 0; i < total; i++) hex += HEX[mono.bytes[i] >> 4] + HEX[mono.bytes[i] & 15];
      data = hex;
    }
    return `^GFA,${total},${total},${mono.widthBytes},${data}`;
  }

  /* ---------------- ZPL 조립 ---------------- */

  const DEFAULTS = {
    dpi: 203,
    darkness: null,       // ~MD / ^MD  -30~30 (null = 프린터 설정 유지)
    speed: null,          // ^PR 인치/초
    quantity: 1,
    mediaMode: null,      // 'T'=티어오프, 'P'=필오프, 'C'=커터
    homeX: 0, homeY: 0,   // ^LH
    invert: false,        // ^PO I (180도 회전)
    threshold: 160,
    dither: true,
    compress: true,
    labelWidthDots: null, // 자동
    labelLenDots: null,
  };

  /**
   * 라벨 캔버스 → ZPL 문자열
   * @param {HTMLCanvasElement} cv  프린터 해상도로 렌더된 캔버스
   * @param {object} opt
   */
  function build(cv, opt = {}) {
    const o = Object.assign({}, DEFAULTS, opt);
    const mono = toMonochrome(cv, { threshold: o.threshold, dither: o.dither });
    const gfa = toGFA(mono, { compress: o.compress });

    const pw = o.labelWidthDots || mono.width;
    const ll = o.labelLenDots || mono.height;

    const L = [];
    L.push('^XA');
    L.push('^CI28');                              // UTF-8 (텍스트 필드를 쓸 경우 대비)
    L.push(`^PW${pw}`);
    L.push(`^LL${ll}`);
    L.push(`^LH${o.homeX},${o.homeY}`);
    if (o.invert) L.push('^POI');
    if (o.darkness != null) L.push(`^MD${o.darkness}`);
    if (o.speed != null) L.push(`^PR${o.speed}`);
    if (o.mediaMode) L.push(`^MM${o.mediaMode}`);
    L.push(`^FO0,0${gfa}^FS`);
    L.push(`^PQ${Math.max(1, o.quantity | 0)}`);
    L.push('^XZ');
    return L.join('\n');
  }

  /** 라벨(mm) → 프린터 도트 */
  const mmToDots = (mm, dpi) => Math.round(mm / 25.4 * dpi);

  /**
   * 편집기 상태에서 곧바로 ZPL을 만든다.
   * @returns {{zpl:string, width:number, height:number, bytes:number, dots:string}}
   */
  async function fromEditor(editor, { objects, label, includeBg = true, dpi = 203, ...rest } = {}) {
    const objs = objects || editor.state.objects;
    const lab = label || editor.state.label;
    await LB.exporter.ensureImages(editor, objs, lab);
    const cv = LB.exporter.renderToCanvas(editor, { objects: objs, label: lab, dpi, includeBg });
    const zpl = build(cv, Object.assign({ dpi }, rest));
    const info = {
      zpl, width: cv.width, height: cv.height,
      bytes: zpl.length,
      dots: `${cv.width} × ${cv.height} dots @ ${dpi}dpi`,
    };
    cv.width = cv.height = 1;
    return info;
  }

  /* ---------------- 전송: Zebra Browser Print ---------------- */

  const BP_BASE = 'http://localhost:9100';

  /** Browser Print 서비스가 떠 있는지 */
  async function bpAvailable(timeoutMs = 1200) {
    try {
      const c = new AbortController();
      const t = setTimeout(() => c.abort(), timeoutMs);
      const r = await fetch(BP_BASE + '/available', { signal: c.signal });
      clearTimeout(t);
      if (!r.ok) return null;
      return await r.json();
    } catch (_) { return null; }
  }

  /** 연결된 프린터 목록 */
  async function bpPrinters() {
    const a = await bpAvailable();
    if (!a) return [];
    const list = [].concat(a.printer || [], a.device || []).filter(Boolean);
    return list.map(d => ({
      uid: d.uid || d.name, name: d.name || d.uid,
      connection: d.connection, deviceType: d.deviceType, manufacturer: d.manufacturer,
      raw: d,
    }));
  }

  /** Browser Print로 전송 */
  async function bpSend(device, data) {
    const r = await fetch(BP_BASE + '/write', {
      method: 'POST',
      headers: { 'Content-Type': 'text/plain;charset=UTF-8' },
      body: JSON.stringify({ device: device.raw || device, data }),
    });
    if (!r.ok) throw new Error(`Browser Print 오류 (HTTP ${r.status})`);
    return true;
  }

  /* ---------------- 전송: WebUSB ---------------- */

  const ZEBRA_VID = 0x0a5f;

  function usbSupported() { return typeof navigator !== 'undefined' && !!navigator.usb; }

  /** 사용자에게 USB 장치를 고르게 한다 (클릭 핸들러 안에서만 호출) */
  async function usbRequest() {
    if (!usbSupported()) throw new Error('이 브라우저는 WebUSB를 지원하지 않습니다. Chrome 또는 Edge를 사용하세요.');
    const dev = await navigator.usb.requestDevice({ filters: [{ vendorId: ZEBRA_VID }, { classCode: 7 }] });
    return dev;
  }

  /** 이미 허용된 장치 목록 */
  async function usbList() {
    if (!usbSupported()) return [];
    try { return await navigator.usb.getDevices(); } catch (_) { return []; }
  }

  /** USB 프린터로 전송 */
  async function usbSend(device, data) {
    if (!device) throw new Error('USB 프린터가 선택되지 않았습니다.');
    if (!device.opened) await device.open();
    if (device.configuration === null) await device.selectConfiguration(1);

    // 프린터 클래스(7) 인터페이스의 bulk OUT 엔드포인트를 찾는다
    let ifaceNum = null, epOut = null;
    for (const cfg of device.configurations) {
      for (const iface of cfg.interfaces) {
        for (const alt of iface.alternates) {
          if (alt.interfaceClass === 7) {
            const ep = alt.endpoints.find(e => e.direction === 'out' && e.type === 'bulk');
            if (ep) { ifaceNum = iface.interfaceNumber; epOut = ep.endpointNumber; break; }
          }
        }
        if (epOut != null) break;
      }
      if (epOut != null) break;
    }
    if (epOut == null) throw new Error('프린터의 USB 출력 엔드포인트를 찾지 못했습니다.');

    try { await device.claimInterface(ifaceNum); }
    catch (e) { throw new Error('USB 인터페이스를 사용할 수 없습니다. 다른 프로그램이 프린터를 점유하고 있을 수 있습니다. (' + e.message + ')'); }

    const buf = new TextEncoder().encode(data);
    const CHUNK = 16 * 1024;
    for (let i = 0; i < buf.length; i += CHUNK) {
      await device.transferOut(epOut, buf.subarray(i, Math.min(buf.length, i + CHUNK)));
    }
    try { await device.releaseInterface(ifaceNum); } catch (_) {}
    return true;
  }

  /* ---------------- 진단 ---------------- */

  /** 프린터 상태/설정을 확인할 수 있는 표준 ZPL 조각들 */
  const DIAGNOSTIC = {
    config: '^XA^HH^XZ',                  // 설정 라벨을 호스트로
    printConfig: '~WC',                   // 설정 라벨 인쇄
    calibrate: '~JC',                     // 미디어 캘리브레이션
    status: '~HS',                        // 상태 질의
    testLabel: '^XA^CI28^FO40,40^A0N,40,40^FDLB Generator TEST^FS' +
               '^FO40,100^BXN,6,200^FD(01)08806367087911(10)TEST0001^FS' +
               '^FO40,320^A0N,28,28^FDZPL OK^FS^PQ1^XZ',
  };

  /** 전송 전에 크기가 현실적인지 확인 */
  function sanityCheck(info, dpi, labelMm) {
    const issues = [];
    const mb = info.bytes / 1048576;
    if (mb > 4) {
      issues.push({ level: 'warn', msg: `ZPL 크기가 ${mb.toFixed(1)}MB입니다. 프린터 메모리를 넘거나 전송이 느릴 수 있습니다. 해상도를 낮추거나 라벨을 나누세요.` });
    }
    if (labelMm && labelMm.w > 110) {
      issues.push({ level: 'warn', msg: `라벨 폭이 ${labelMm.w}mm입니다. 일반 데스크톱 제브라 모델의 최대 인쇄 폭(약 104mm)을 넘습니다.` });
    }
    if (info.width > 1400 && dpi === 203) {
      issues.push({ level: 'info', msg: '폭이 넓습니다. 산업용 모델(ZT 계열)인지 확인하세요.' });
    }
    return issues;
  }

  return {
    DPI_CHOICES, DEFAULTS, DIAGNOSTIC,
    toMonochrome, toGFA, build, fromEditor, mmToDots, sanityCheck,
    bpAvailable, bpPrinters, bpSend,
    usbSupported, usbRequest, usbList, usbSend,
  };
})();
