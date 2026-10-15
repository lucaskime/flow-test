import http from 'k6/http';
import { check } from 'k6';
import { Counter, Rate } from 'k6/metrics';
import { BASE_URL, ACCUMULATOR_URL, SYMBOLS } from '../lib/config.js';

// Cada ordem vale 99.989.000,01 (qtd 99.999 x preco 999,99): so cabe UMA compra
// por simbolo dentro do limite de R$ 100 mi. Com varios VUs disparando em
// paralelo, valida que o limite e atomico: 1 aceita, o resto rejeitada, e que a
// exposicao nunca ultrapassa o limite nem e alterada por rejeicoes.
const ORDERS_PER_SYMBOL = parseInt(__ENV.ORDERS_PER_SYMBOL || '300', 10);
const QTY = 99999;
const PRICE = 999.99;
const ORDER_VALUE = QTY * PRICE;

const accepted = new Counter('orders_accepted');
const rejected = new Counter('orders_rejected');
const other = new Counter('orders_other');
const rejectedRate = new Rate('order_rejected_rate');

export const options = {
  scenarios: {
    rejections: {
      executor: 'shared-iterations',
      vus: parseInt(__ENV.VUS || '100', 10),
      iterations: ORDERS_PER_SYMBOL * SYMBOLS.length,
      maxDuration: '5m',
    },
  },
  thresholds: {
    orders_other: ['count==0'],               // Timeout/SessionUnavailable/erro
    http_req_failed: ['rate<0.01'],
    // no maximo 1 aceita por simbolo => ao menos (N-1)/N rejeitadas
    order_rejected_rate: [`rate>${((ORDERS_PER_SYMBOL - 1) / ORDERS_PER_SYMBOL - 0.001).toFixed(3)}`],
    orders_accepted: [`count<=${SYMBOLS.length}`],
  },
};

function state() {
  const r = http.get(`${ACCUMULATOR_URL}/api/state`);
  if (r.status !== 200) throw new Error(`Accumulator indisponivel (${r.status})`);
  const out = {};
  for (const s of r.json('symbols')) out[s.symbol] = s;
  return { symbols: out, limit: r.json('limit') };
}

export function setup() {
  const st = state();
  for (const sym of SYMBOLS) {
    const e = st.symbols[sym].exposure;
    if (e + ORDER_VALUE > st.limit) {
      throw new Error(`${sym} ja esta com exposicao ${e}; reinicie o Accumulator para um teste limpo.`);
    }
  }
  return { start: Object.fromEntries(SYMBOLS.map((s) => [s, st.symbols[s].exposure])) };
}

export default function () {
  const symbol = SYMBOLS[__ITER % SYMBOLS.length];
  const res = http.post(
    `${BASE_URL}/api/orders`,
    JSON.stringify({ symbol, side: 'Buy', quantity: QTY, price: PRICE }),
    { headers: { 'Content-Type': 'application/json' }, tags: { symbol, name: 'POST /api/orders' } },
  );

  const status = res.status === 200 ? res.json('status') : null;
  if (status === 'Accepted') accepted.add(1, { symbol });
  else if (status === 'Rejected') rejected.add(1, { symbol });
  else other.add(1, { symbol });
  rejectedRate.add(status === 'Rejected', { symbol });
}

export function teardown(data) {
  const st = state();
  for (const sym of SYMBOLS) {
    const s = st.symbols[sym];
    const delta = s.exposure - data.start[sym];
    check(s, {
      [`${sym}: exposicao dentro do limite`]: (x) => Math.abs(x.exposure) <= st.limit,
      [`${sym}: auditoria consistente`]: (x) => x.consistent === true,
      [`${sym}: rejeicoes nao alteraram exposicao (delta 0 ou 1 ordem)`]: () =>
        Math.abs(delta) < 0.005 || Math.abs(delta - ORDER_VALUE) < 0.005,
    });
    console.log(`${sym}: exposicao ${data.start[sym]} -> ${s.exposure} (delta ${delta})`);
  }
}
