/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** API base URL. Empty means same origin (dev proxy or nginx). */
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
