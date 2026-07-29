/* exporter.js — 라벨을 실측 크기 PDF로 출력 */
window.LB = window.LB || {};

LB.exporter = (() => {

  /* 출력 전 배경/객체 이미지가 모두 디코딩되어 있도록 보장 */
  async function ensureImages(editor) {
    const urls = [];
    if (editor.state.label.bg) urls.push(editor.state.label.bg);
    for (const o of editor.state.objects) {
      if (o.type === 'image' && o.dataUrl) urls.push(o.dataUrl);
    }
    await Promise.all(urls.map(u => new Promise(res => {
      let img = editor.imageCache.get(u);
      if (!img) { img = new Image(); img.src = u; editor.imageCache.set(u, img); }
      if (img.complete) return res();
      img.addEventListener('load', res, { once: true });
      img.addEventListener('error', res, { once: true });
    })));
  }

  /* 라벨을 지정 DPI 캔버스로 렌더링 */
  function renderToCanvas(editor, dpi) {
    const { w: LW, h: LH } = editor.state.label;
    const pxPerMm = dpi / 25.4;
    const cv = document.createElement('canvas');
    cv.width = Math.round(LW * pxPerMm);
    cv.height = Math.round(LH * pxPerMm);
    const ctx = cv.getContext('2d');
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, cv.width, cv.height);
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, cv.width, cv.height);
    ctx.clip();
    if (editor.state.label.bg && editor.state.label.bgInclude !== false) {
      const bgImg = editor.getImage(editor.state.label.bg);
      if (bgImg) ctx.drawImage(bgImg, 0, 0, cv.width, cv.height);
    }
    for (const o of editor.state.objects) {
      editor.drawObject(ctx, o, pxPerMm, 0, 0, true);
    }
    ctx.restore();
    return cv;
  }

  /* 파일명 규칙 치환 */
  function buildFileName(pattern, fields, label) {
    const now = new Date();
    const p2 = (n) => String(n).padStart(2, '0');
    const map = {
      ...fields,
      DATE: `${String(now.getFullYear()).slice(2)}${p2(now.getMonth() + 1)}${p2(now.getDate())}`,
      TIME: `${p2(now.getHours())}${p2(now.getMinutes())}${p2(now.getSeconds())}`,
      W: label.w, H: label.h,
    };
    let name = pattern.replace(/\{(\w+)\}/g, (_, k) => (map[k] != null && map[k] !== '' ? String(map[k]) : ''));
    name = name.replace(/[\\/:*?"<>|]/g, '-').replace(/\s+/g, ' ').trim();
    if (!name) name = 'label';
    return name.endsWith('.pdf') ? name : name + '.pdf';
  }

  /**
   * PDF 생성 + 저장.
   * outDirHandle이 있으면 해당 폴더에 저장, 없으면 브라우저 다운로드.
   * @returns 저장된 파일명
   */
  async function exportPdf(editor, { dpi = 600, pattern, fields, outDirHandle }) {
    const { w: LW, h: LH } = editor.state.label;
    await ensureImages(editor);
    const canvas = renderToCanvas(editor, dpi);
    const png = canvas.toDataURL('image/png');

    const { jsPDF } = window.jspdf;
    const pdf = new jsPDF({
      unit: 'mm',
      format: [LW, LH],
      orientation: LW >= LH ? 'landscape' : 'portrait',
      compress: true,
    });
    pdf.addImage(png, 'PNG', 0, 0, LW, LH);

    const fileName = buildFileName(pattern || '{ITEM}_{LOT}_{DATE}', fields || {}, editor.state.label);

    if (outDirHandle) {
      const blob = pdf.output('blob');
      const fh = await outDirHandle.getFileHandle(fileName, { create: true });
      const ws = await fh.createWritable();
      await ws.write(blob);
      await ws.close();
    } else {
      pdf.save(fileName);
    }
    return fileName;
  }

  return { exportPdf, renderToCanvas, buildFileName };
})();
