/* imaging.js — 이미지 로딩 및 배경 자동 투명화
 *
 * 규칙 (엑셀 VBA UpdateImagesKeepAspectRatio 대응 + 요구사항):
 *  - 배경이 투명한 PNG → 그대로 사용
 *  - JPG 또는 배경이 불투명한 PNG → 가장자리 색을 배경색으로 감지하여
 *    가장자리에서 연결된(플러드필) 유사색 픽셀만 투명 처리
 *    (제품 내부의 흰색 영역은 유지됨)
 */
window.LB = window.LB || {};

LB.imaging = (() => {

  function blobToImage(blob) {
    return new Promise((resolve, reject) => {
      const url = URL.createObjectURL(blob);
      const img = new Image();
      img.onload = () => { URL.revokeObjectURL(url); resolve(img); };
      img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('이미지 디코딩 실패')); };
      img.src = url;
    });
  }

  function hasTransparency(data) {
    // 알파 < 250 픽셀이 일정 수 이상이면 이미 투명 배경으로 간주
    let count = 0;
    for (let i = 3; i < data.length; i += 4) {
      if (data[i] < 250) { count++; if (count > 16) return true; }
    }
    return false;
  }

  /* 가장자리 픽셀에서 배경색 후보(최빈 색)를 구함 */
  function detectBgColor(data, w, h) {
    const samples = [];
    const push = (x, y) => {
      const i = (y * w + x) * 4;
      samples.push([data[i], data[i + 1], data[i + 2]]);
    };
    for (let x = 0; x < w; x += Math.max(1, w >> 6)) { push(x, 0); push(x, h - 1); }
    for (let y = 0; y < h; y += Math.max(1, h >> 6)) { push(0, y); push(w - 1, y); }
    // 양자화(16단위) 후 최빈값
    const freq = new Map();
    for (const [r, g, b] of samples) {
      const key = `${r >> 4},${g >> 4},${b >> 4}`;
      freq.set(key, (freq.get(key) || 0) + 1);
    }
    let best = null, bestN = -1;
    for (const [k, n] of freq) if (n > bestN) { bestN = n; best = k; }
    const [r, g, b] = best.split(',').map(v => (parseInt(v, 10) << 4) + 8);
    return [r, g, b];
  }

  /* 가장자리에서 시작하는 BFS 플러드필로 배경 제거 */
  function removeBackground(imageData, tolerance) {
    const { data, width: w, height: h } = imageData;
    const [br, bg, bb] = detectBgColor(data, w, h);
    const tol2 = tolerance * tolerance * 3;
    const visited = new Uint8Array(w * h);
    const queue = new Int32Array(w * h);
    let qh = 0, qt = 0;

    const isBg = (p) => {
      const i = p * 4;
      const dr = data[i] - br, dg = data[i + 1] - bg, db = data[i + 2] - bb;
      return dr * dr + dg * dg + db * db <= tol2;
    };
    const tryPush = (p) => {
      if (!visited[p] && isBg(p)) { visited[p] = 1; queue[qt++] = p; }
    };

    for (let x = 0; x < w; x++) { tryPush(x); tryPush((h - 1) * w + x); }
    for (let y = 0; y < h; y++) { tryPush(y * w); tryPush(y * w + w - 1); }

    while (qh < qt) {
      const p = queue[qh++];
      data[p * 4 + 3] = 0;
      const x = p % w, y = (p / w) | 0;
      if (x > 0) tryPush(p - 1);
      if (x < w - 1) tryPush(p + 1);
      if (y > 0) tryPush(p - w);
      if (y < h - 1) tryPush(p + w);
    }
    return imageData;
  }

  /**
   * Blob → { dataUrl, w, h }
   * autoTransparent가 켜져 있으면 배경 투명화 처리
   */
  async function processBlob(blob, { autoTransparent = true, tolerance = 30 } = {}) {
    const img = await blobToImage(blob);
    const cv = document.createElement('canvas');
    cv.width = img.naturalWidth; cv.height = img.naturalHeight;
    const ctx = cv.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(img, 0, 0);

    if (autoTransparent) {
      const id = ctx.getImageData(0, 0, cv.width, cv.height);
      if (!hasTransparency(id.data)) {
        removeBackground(id, tolerance);
        ctx.putImageData(id, 0, 0);
      }
    }
    return { dataUrl: cv.toDataURL('image/png'), w: cv.width, h: cv.height };
  }

  return { processBlob };
})();
