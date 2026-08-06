import axios, { AxiosError } from 'axios';
import { classifyError } from '@/lib/classify-error';

function makeAxiosError(overrides: Partial<AxiosError> = {}): AxiosError {
  const err = new axios.AxiosError(
    overrides.message ?? 'error',
    overrides.code,
    undefined,
    undefined,
    overrides.response as never,
  );
  return Object.assign(err, overrides);
}

describe('classifyError — G12: 404 (kayıt yok) vs ağ hatası (retry mantıklı) ayrımı', () => {
  it('response.status 404 -> notFound', () => {
    const err = makeAxiosError({ response: { status: 404 } as never });
    expect(classifyError(err)).toBe('notFound');
  });

  it('response.status 500 -> network (sunucu cevap verdi ama hata; retry mantıklı)', () => {
    const err = makeAxiosError({ response: { status: 500 } as never });
    expect(classifyError(err)).toBe('network');
  });

  it('response.status 401 -> network (404 DEĞİL — yanlış kayıt-yok mesajı vermemeli)', () => {
    const err = makeAxiosError({ response: { status: 401 } as never });
    expect(classifyError(err)).toBe('network');
  });

  it('response.status 403 -> network', () => {
    const err = makeAxiosError({ response: { status: 403 } as never });
    expect(classifyError(err)).toBe('network');
  });

  it('response yok + code ERR_NETWORK (sunucuya ulaşılamadı / DNS / connection refused) -> network', () => {
    const err = makeAxiosError({ code: 'ERR_NETWORK', response: undefined });
    expect(classifyError(err)).toBe('network');
  });

  it('timeout (ECONNABORTED) -> network', () => {
    const err = makeAxiosError({ code: 'ECONNABORTED', response: undefined });
    expect(classifyError(err)).toBe('network');
  });

  it('timeout (ETIMEDOUT) -> network', () => {
    const err = makeAxiosError({ code: 'ETIMEDOUT', response: undefined });
    expect(classifyError(err)).toBe('network');
  });

  it('axios hatası değil (düz Error) -> network (güvenli varsayım)', () => {
    expect(classifyError(new Error('boom'))).toBe('network');
  });

  it('null/undefined -> network', () => {
    expect(classifyError(null)).toBe('network');
    expect(classifyError(undefined)).toBe('network');
  });

  it('string/number gibi tanınmayan şekiller -> network', () => {
    expect(classifyError('some error string')).toBe('network');
    expect(classifyError(42)).toBe('network');
  });

  it('isAxiosError bayrağı olmayan axios-benzeri obje -> network (duck-typing tuzağına düşmemeli)', () => {
    const fakeAxiosShaped = { response: { status: 404 }, message: 'looks real' };
    expect(classifyError(fakeAxiosShaped)).toBe('network');
  });

  it('status 200 ama body içinde "not found" metni geçiyor -> network (yalnızca HTTP status güvenilir, body içeriğine bakılmaz)', () => {
    const err = makeAxiosError({ response: { status: 200, data: { message: 'not found' } } as never });
    expect(classifyError(err)).toBe('network');
  });

  it('response.status 0 (ör. CORS/opak yanıt) -> network', () => {
    const err = makeAxiosError({ response: { status: 0 } as never });
    expect(classifyError(err)).toBe('network');
  });
});
