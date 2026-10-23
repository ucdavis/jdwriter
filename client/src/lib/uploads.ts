import { fetchJson } from './api.ts';

type StageResponse = { direct: true } | { slots: Array<{ blob: string; url: string }> };

/**
 * The body of an upload request. Where the server stages uploads (in Azure), each file is put
 * straight into blob storage with a short-lived, write-only link for one blob, and the request only names the
 * blobs — so files never pass through the campus front door's firewall, which refuses some
 * documents. Where it doesn't (local development), the files are the body, as before.
 */
export const uploadBody = async (files: File[], field = 'file'): Promise<FormData> => {
  const body = new FormData();
  const stage = await fetchJson<StageResponse>('/api/uploads/stage', {
    body: JSON.stringify({ count: files.length }),
    method: 'POST',
  });

  if ('direct' in stage) {
    for (const file of files) {
      body.append(field, file, file.name);
    }
    return body;
  }

  const staged: Array<{ blob: string; name: string; type: string }> = [];
  // One at a time: a folder of documents is a few dozen small files, and a failure names its file.
  for (const [i, file] of files.entries()) {
    const slot = stage.slots[i];
    const put = await fetch(slot.url, {
      body: file,
      headers: { 'Content-Type': file.type || 'application/octet-stream', 'x-ms-blob-type': 'BlockBlob' },
      method: 'PUT',
    });
    // Read the (empty) reply, so the browser records the request as finished rather than aborted.
    await put.text();
    if (!put.ok) {
      throw new Error(`${file.name} couldn't be uploaded (storage answered ${put.status}). Try again.`);
    }
    staged.push({ blob: slot.blob, name: file.name, type: file.type });
  }

  body.append('staged', JSON.stringify(staged));
  return body;
};
