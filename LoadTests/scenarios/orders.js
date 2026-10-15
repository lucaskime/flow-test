import http from 'k6/http';
import { check } from 'k6';
import { Rate, Counter, Trend } from 'k6/metrics';
import { BASE_URL, ACCUMULATOR_URL, SYMBOLS, SIDES, profile, thresholds } from '../lib/config.js';
import { buildOrder, nextCombination } from '../lib/orders.js';

const accepted = new Rate('order_accepted');
const outcomes = new Counter('order_outcome');
const latency = new Trend('order_latency', true);

// Thresholds por simbolo/lado (tags) para ver a degradacao por combinacao.
const perCombo = {};
for (const s of SYMBOLS) {
  for (const d of SIDES) {
    perCombo[`http_req_duration{symbol:${s},side:${d}}`] = ['p(95)<800'];
  }
}

export const options = {
  scenarios: { orders: profile() },
  thresholds: { ...thresholds, ...perCombo },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

const params = (order) => ({
  headers: { 'Content-Type': 'application/json' },
  tags: { symbol: order.symbol, side: order.side, name: 'POST /api/orders' },
  timeout: '15s',
});

export function setup() {
  const h = http.get(`${BASE_URL}/api/status`);
  if (h.status !== 200) {
    throw new Error(`Generator indisponivel em ${BASE_URL} (status ${h.status})`);
  }
  console.log(`Generator OK: ${h.body}`);
}

export default function () {
  // Round-robin garante distribuicao uniforme nas 6 combinacoes.
  const combo = nextCombination();
  const order = buildOrder(combo.symbol, combo.side);

  const res = http.post(`${BASE_URL}/api/orders`, JSON.stringify(order), params(order));

  let status = null;
  try {
    status = res.json('status');
  } catch (_) { /* corpo invalido */ }

  const isAccepted = status === 'Accepted' || status === 0;
  const isRejected = status === 'Rejected' || status === 1;

  check(res, {
    'status 200': (r) => r.status === 200,
    'tem clOrdId': (r) => {
      try { return !!r.json('clOrdId'); } catch (_) { return false; }
    },
  });

  accepted.add(isAccepted, { symbol: order.symbol, side: order.side });
  outcomes.add(1, { outcome: String(status), symbol: order.symbol, side: order.side });
  latency.add(res.timings.duration, { symbol: order.symbol, side: order.side });
  // Rejeitada pelo limite e comportamento valido do negocio, nao falha de infra.
  if (!isAccepted && !isRejected && res.status === 200) {
    console.warn(`Resultado inesperado: ${res.body}`);
  }
}

export function teardown() {
  const r = http.get(`${ACCUMULATOR_URL}/api/state`);
  if (r.status === 200) {
    console.log(`Estado final do Accumulator: ${r.body.substring(0, 1500)}`);
  }
}
