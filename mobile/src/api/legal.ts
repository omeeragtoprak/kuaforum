import { apiClient } from './client';

/**
 * RFC-008 — legal metin slug'ları. Backend allowlist ile birebir
 * (`backend/src/Kuaforum.Api/Endpoints/LegalEndpoints.cs`); bilinmeyen slug 404 döner.
 */
export type LegalDocSlug = 'aydinlatma' | 'kullanim-kosullari';

export type LegalDocument = {
  doc: string;
  version: string;
  title: string;
  markdown: string;
};

export const LegalApi = {
  async get(doc: LegalDocSlug): Promise<LegalDocument> {
    const res = await apiClient.get<LegalDocument>(`/v1/legal/${doc}`);
    return res.data;
  },
};
