import React from 'react';
import { act, render } from '@testing-library/react-native';
import { AccessibilityInfo } from 'react-native';
import {
  Skeleton,
  SkeletonBarberDetail,
  SkeletonShopCard,
  SkeletonBookingRow,
  SkeletonBookingDetail,
} from '@/components/Skeleton';

describe('Skeleton bileşenleri', () => {
  it('crash olmadan render edilir (kuaför detay / keşfet / randevularım iskeletleri)', async () => {
    render(<SkeletonBarberDetail />);
    render(<SkeletonShopCard />);
    render(<SkeletonBookingRow />);
    render(<SkeletonBookingDetail />);
    // useReduceMotion içindeki AccessibilityInfo.isReduceMotionEnabled() promise'ini
    // act() içinde flush ediyoruz ki "update not wrapped in act" uyarısı çıkmasın.
    await act(async () => {
      await Promise.resolve();
    });
  });

  it('reduce motion açıkken de crash olmadan render edilir (AccessibilityInfo.isReduceMotionEnabled=true)', async () => {
    const spy = jest
      .spyOn(AccessibilityInfo, 'isReduceMotionEnabled')
      .mockResolvedValue(true);
    render(<Skeleton />);
    await act(async () => {
      await Promise.resolve();
    });
    spy.mockRestore();
  });
});
