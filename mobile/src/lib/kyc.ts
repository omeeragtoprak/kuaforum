/** RFC-007 — backend SubmitKycCommandValidator ile senkron: TaxLicense + IdentityCard zorunlu. */
export const KYC_DOCUMENT_TYPES = ['TaxLicense', 'SanitaryPermit', 'MykCertificate', 'IdentityCard', 'Other'] as const;
export type KycDocumentType = (typeof KYC_DOCUMENT_TYPES)[number];

export const REQUIRED_KYC_TYPES: readonly KycDocumentType[] = ['TaxLicense', 'IdentityCard'];

export const OPTIONAL_KYC_TYPES: readonly KycDocumentType[] = KYC_DOCUMENT_TYPES.filter(
  (t) => !REQUIRED_KYC_TYPES.includes(t),
);

export const KYC_TYPE_LABELS: Record<KycDocumentType, string> = {
  TaxLicense: 'Vergi Levhası',
  IdentityCard: 'Kimlik',
  SanitaryPermit: 'İşyeri Açma / Ruhsat',
  MykCertificate: 'MYK Ustalık Belgesi',
  Other: 'Diğer',
};

export function canSubmitKyc(uploaded: ReadonlyArray<{ documentType: KycDocumentType }>): boolean {
  return REQUIRED_KYC_TYPES.every((t) => uploaded.some((d) => d.documentType === t));
}
