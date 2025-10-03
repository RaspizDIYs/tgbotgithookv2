export const API_BASE_URL = 'https://tgbotgithookv2.onrender.com';

export async function apiGet<T>(path: string, init?: RequestInit): Promise<T> {
  const r = await fetch(`${API_BASE_URL}${path}`, { ...init });
  if (!r.ok) throw new Error(`HTTP ${r.status}`);
  return r.json() as Promise<T>;
}

export async function apiPost<T>(path: string, body: unknown, init?: RequestInit): Promise<T> {
  const r = await fetch(`${API_BASE_URL}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    ...init,
  });
  if (!r.ok) throw new Error(`HTTP ${r.status}`);
  return r.json() as Promise<T>;
}


