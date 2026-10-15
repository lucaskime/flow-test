import { SYMBOLS, SIDES } from './config.js';

let seq = 0;

export function randomInt(min, max) {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

// Preco multiplo de 0,01, entre 1,00 e 100,00 (regra: > 0, < 1000).
export function randomPrice() {
  return randomInt(100, 10000) / 100;
}

// Quantidade 1..500 (regra: inteiro positivo < 100.000). Valores baixos
// evitam estourar o limite de R$ 100 mi por simbolo durante testes longos;
// compra/venda aleatorias mantem a exposicao liquida proxima de zero.
export function randomQuantity() {
  return randomInt(1, 500);
}

export function buildOrder(symbol, side) {
  seq += 1;
  return {
    symbol: symbol || SYMBOLS[randomInt(0, SYMBOLS.length - 1)],
    side: side || SIDES[randomInt(0, SIDES.length - 1)],
    quantity: randomQuantity(),
    price: randomPrice(),
  };
}

// Combinacoes fixas: 3 simbolos x 2 lados = 6 tipos de ordem.
export const COMBINATIONS = SYMBOLS.flatMap((symbol) => SIDES.map((side) => ({ symbol, side })));

export function nextCombination() {
  seq += 1;
  return COMBINATIONS[seq % COMBINATIONS.length];
}
