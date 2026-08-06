import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react-native';
import axios from 'axios';
import { ErrorState } from '@/components/ErrorState';

function make404Error() {
  return new axios.AxiosError('Not Found', undefined, undefined, undefined, {
    status: 404,
  } as never);
}

function makeNetworkError() {
  return new axios.AxiosError('Network Error', 'ERR_NETWORK');
}

const baseProps = {
  notFoundTitle: 'Bu kuaför artık yayında değil',
  notFoundDescription: 'Bu salon kaldırılmış veya yayından kaldırılmış olabilir.',
  notFoundActionLabel: 'Geri dön',
  onNotFoundAction: jest.fn(),
};

describe('ErrorState — G12: 404 ekranında "Tekrar dene" YOK, ağ hatasında VAR', () => {
  afterEach(() => jest.clearAllMocks());

  it('404 hatasında ekrana özgü "kayıt yok" metni gösterilir, "Tekrar dene" GÖRÜNMEZ', () => {
    const onRetry = jest.fn();
    render(<ErrorState error={make404Error()} onRetry={onRetry} {...baseProps} />);

    expect(screen.getByText('Bu kuaför artık yayında değil')).toBeTruthy();
    expect(screen.queryByText('Tekrar dene')).toBeNull();

    fireEvent.press(screen.getByText('Geri dön'));
    expect(baseProps.onNotFoundAction).toHaveBeenCalledTimes(1);
    expect(onRetry).not.toHaveBeenCalled();
  });

  it('ağ hatasında "Bağlantı kurulamadı" + "Tekrar dene" GÖRÜNÜR ve refetch tetikler', () => {
    const onRetry = jest.fn();
    render(<ErrorState error={makeNetworkError()} onRetry={onRetry} {...baseProps} />);

    expect(screen.getByText('Bağlantı kurulamadı')).toBeTruthy();
    expect(screen.queryByText('Bu kuaför artık yayında değil')).toBeNull();

    fireEvent.press(screen.getByText('Tekrar dene'));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('500 hatası da network kolunda gösterilir (401/5xx notFound sanılmamalı)', () => {
    const err = new axios.AxiosError('Server Error', undefined, undefined, undefined, {
      status: 500,
    } as never);
    render(<ErrorState error={err} onRetry={jest.fn()} {...baseProps} />);

    expect(screen.getByText('Bağlantı kurulamadı')).toBeTruthy();
    expect(screen.getByText('Tekrar dene')).toBeTruthy();
  });

  it('ekrana özgü network metni override edilebilir', () => {
    render(
      <ErrorState
        error={makeNetworkError()}
        onRetry={jest.fn()}
        {...baseProps}
        networkTitle="Slotlar alınamadı"
        networkDescription="Bağlantı hatası — tekrar dene."
      />,
    );
    expect(screen.getByText('Slotlar alınamadı')).toBeTruthy();
    expect(screen.getByText('Bağlantı hatası — tekrar dene.')).toBeTruthy();
  });
});
