export const BASE_URL = __ENV.BASE_URL || 'http://localhost:5029';
export const ACCUMULATOR_URL = __ENV.ACCUMULATOR_URL || 'http://localhost:5131';

export const SYMBOLS = ['PETR4', 'VALE3', 'VIIA4'];
export const SIDES = ['Buy', 'Sell'];

// Perfis de carga (taxa de ordens por segundo). Escolha com -e PROFILE=...
const MAX_VUS = parseInt(__ENV.MAX_VUS || '2000', 10);

const profiles = {
  smoke: {
    executor: 'constant-arrival-rate',
    rate: 5, timeUnit: '1s', duration: '30s',
    preAllocatedVUs: 10, maxVUs: 50,
  },
  load: {
    executor: 'ramping-arrival-rate',
    startRate: 10, timeUnit: '1s',
    preAllocatedVUs: 100, maxVUs: MAX_VUS,
    stages: [
      { target: 200, duration: '1m' },
      { target: 200, duration: '5m' },
      { target: 0, duration: '30s' },
    ],
  },
  stress: {
    executor: 'ramping-arrival-rate',
    startRate: 50, timeUnit: '1s',
    preAllocatedVUs: 200, maxVUs: MAX_VUS,
    stages: [
      { target: 200, duration: '2m' },
      { target: 500, duration: '2m' },
      { target: 1000, duration: '2m' },
      { target: 2000, duration: '2m' },
      { target: 0, duration: '1m' },
    ],
  },
  spike: {
    executor: 'ramping-arrival-rate',
    startRate: 20, timeUnit: '1s',
    preAllocatedVUs: 300, maxVUs: MAX_VUS,
    stages: [
      { target: 50, duration: '30s' },
      { target: 2000, duration: '15s' },
      { target: 2000, duration: '1m' },
      { target: 50, duration: '15s' },
      { target: 50, duration: '1m' },
    ],
  },
  soak: {
    executor: 'constant-arrival-rate',
    rate: 150, timeUnit: '1s', duration: '30m',
    preAllocatedVUs: 100, maxVUs: MAX_VUS,
  },
};

export function profile() {
  const name = __ENV.PROFILE || 'load';
  if (!profiles[name]) {
    throw new Error(`PROFILE invalido: ${name}. Use: ${Object.keys(profiles).join(', ')}`);
  }
  return profiles[name];
}

export const thresholds = {
  http_req_failed: ['rate<0.01'],
  http_req_duration: ['p(95)<500', 'p(99)<1000'],
  'order_accepted': ['rate>0.95'],
  'checks': ['rate>0.99'],
};
