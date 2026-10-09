// GitHub Pages serves each immutable data chunk below the Git file size limit.
// Reassemble the original compressed bytes; Unity's loader handles decompression.
window.BattlePvpData = {
  async download(parts, onProgress) {
    const total = parts.reduce((sum, part) => sum + part.bytes, 0);
    let loaded = 0;
    const blobs = [];
    for (const part of parts) {
      const response = await fetch(part.url, {cache: 'force-cache'});
      if (!response.ok) throw new Error('게임 파일 다운로드 실패: ' + response.status);
      const pieces = [];
      let size = 0;
      if (response.body) {
        const reader = response.body.getReader();
        try {
          for (;;) {
            const result = await reader.read();
            if (result.done) break;
            pieces.push(result.value);
            size += result.value.byteLength;
            if (size > part.bytes) throw new Error('게임 파일 크기가 일치하지 않습니다.');
            onProgress((loaded + size) / total);
          }
        } finally {
          reader.releaseLock();
        }
      } else {
        const bytes = await response.arrayBuffer();
        pieces.push(bytes);
        size = bytes.byteLength;
      }
      if (size !== part.bytes) throw new Error('게임 파일 다운로드가 완료되지 않았습니다. 새로고침해 주세요.');
      blobs.push(new Blob(pieces));
      loaded += size;
      onProgress(loaded / total);
    }
    return URL.createObjectURL(new Blob(blobs, {type: 'application/octet-stream'}));
  }
};
