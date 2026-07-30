import { KYC_DOCUMENT_TYPES, KYC_TYPE_LABELS, REQUIRED_KYC_TYPES, canSubmitKyc } from '@/lib/kyc';

describe('KYC_DOCUMENT_TYPES / REQUIRED_KYC_TYPES', () => {
  it('backend DocumentType enum ile senkron 5 tip listeler', () => {
    expect(KYC_DOCUMENT_TYPES).toEqual(['TaxLicense', 'SanitaryPermit', 'MykCertificate', 'IdentityCard', 'Other']);
  });

  it('yalnız TaxLicense + IdentityCard zorunlu (backend SubmitKycCommandValidator ile senkron)', () => {
    expect(REQUIRED_KYC_TYPES).toEqual(['TaxLicense', 'IdentityCard']);
  });

  it('her tip için Türkçe etiket tanımlı', () => {
    KYC_DOCUMENT_TYPES.forEach((type) => {
      expect(KYC_TYPE_LABELS[type]).toEqual(expect.any(String));
      expect(KYC_TYPE_LABELS[type].length).toBeGreaterThan(0);
    });
  });
});

describe('canSubmitKyc', () => {
  it('hiç belge yokken false döner', () => {
    expect(canSubmitKyc([])).toBe(false);
  });

  it('yalnız TaxLicense varken false döner', () => {
    expect(canSubmitKyc([{ documentType: 'TaxLicense' }])).toBe(false);
  });

  it('yalnız IdentityCard varken false döner', () => {
    expect(canSubmitKyc([{ documentType: 'IdentityCard' }])).toBe(false);
  });

  it('TaxLicense + IdentityCard varken true döner', () => {
    expect(canSubmitKyc([{ documentType: 'TaxLicense' }, { documentType: 'IdentityCard' }])).toBe(true);
  });

  it('zorunlulara ek opsiyonel belge (MykCertificate) eklense de true döner', () => {
    expect(
      canSubmitKyc([
        { documentType: 'TaxLicense' },
        { documentType: 'IdentityCard' },
        { documentType: 'MykCertificate' },
      ]),
    ).toBe(true);
  });
});
