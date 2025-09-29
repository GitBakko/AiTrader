declare global {
  interface Window {
    NEBULA_PULSE_ORCHESTRATOR_URL?: string;
  }
}

const FALLBACK_BASE_URL = 'http://localhost:5010';

export function getOrchestratorBaseUrl(): string {
  if (typeof window === 'undefined') {
    return FALLBACK_BASE_URL;
  }

  const fromGlobal = window.NEBULA_PULSE_ORCHESTRATOR_URL;
  const fromMeta = document.head?.querySelector<HTMLMetaElement>('meta[name="nebula-orchestrator-url"]')?.content;

  const raw = (fromGlobal ?? fromMeta ?? '').trim();
  if (!raw) {
    return FALLBACK_BASE_URL;
  }

  try {
    const url = new URL(raw, window.location.origin);
    url.pathname = url.pathname.replace(/\/$/, '');
    url.search = '';
    url.hash = '';
    return url.toString().replace(/\/$/, '');
  } catch {
    return FALLBACK_BASE_URL;
  }
}

export function getWebSocketBaseUrl(): string {
  const httpBase = getOrchestratorBaseUrl();
  try {
    const url = new URL(httpBase);
    url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
    return url.toString();
  } catch {
    return httpBase.replace(/^http/, 'ws');
  }
}
