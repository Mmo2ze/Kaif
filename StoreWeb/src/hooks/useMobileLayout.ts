import { useEffect, useState } from 'react';
import { isMobileLayout } from '../utils/mobile';

export function useMobileLayout(): boolean {
  const [mobile, setMobile] = useState(isMobileLayout);

  useEffect(() => {
    const mq = window.matchMedia('(max-width: 767px)');
    const update = () => setMobile(mq.matches);
    update();
    mq.addEventListener('change', update);
    return () => mq.removeEventListener('change', update);
  }, []);

  return mobile;
}
