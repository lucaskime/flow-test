# Flowa — Ordens FIX 4.4

Duas aplicações .NET 10 que trocam ordens via FIX 4.4 ([QuickFIX/n](https://quickfixn.org/) 1.14.1) e controlam a exposição financeira por símbolo.

> This is a challenge by [Coodesh](https://coodesh.com/)

| App | Papel FIX | Interface | Porta HTTP |
|---|---|---|---|
| **OrderGenerator** | Initiator | Formulário de envio de ordens | `5029` |
| **OrderAccumulator** | Acceptor (porta FIX `5001`) | Painel de monitoramento em tempo real | `5131` |

```text
Navegador -> OrderGenerator -> NewOrderSingle (FIX) -> OrderAccumulator
Navegador <- OrderGenerator <- ExecutionReport (FIX) <- OrderAccumulator
```

## Rodar

Requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) e as portas `5001`, `5029` e `5131` livres. As telas não dependem de internet.

```bash
git clone <URL_DO_REPOSITORIO> && cd Flowa
```

**Terminal 1 — Accumulator (suba primeiro):**

```bash
dotnet run --project OrderAccumulator --launch-profile http
```

**Terminal 2 — Generator:**

```bash
dotnet run --project OrderGenerator --launch-profile http
```

Abra:

- **Enviar ordens:** <http://localhost:5029>
- **Monitorar exposição:** <http://localhost:5131>

Se o Generator subir antes do Accumulator, ele reconecta sozinho (a cada 5 s).

## Testes

```bash
dotnet test Flowa.slnx
```

Inclui testes de integração com acceptor e initiator reais em loopback (`FixLoopbackIntegrationTests`). O CI (`.github/workflows/ci.yml`) roda o mesmo comando.

## Testes de carga (k6)

Dispara ordens em `POST /api/orders` (OrderGenerator) cobrindo **3 símbolos** (`PETR4`, `VALE3`, `VIIA4`) × **2 lados** (`Buy`, `Sell`) = 6 combinações, em round-robin, com taxa de chegada fixa (modelo aberto: mede o sistema, não o cliente).

### Pré-requisitos
- `brew install k6`
- Accumulator (`:5131`) e Generator (`:5029`) rodando — veja [Rodar](#rodar).
  Para carga real, rode em Release: `dotnet run -c Release --project ... --launch-profile http`

### Executar
```bash
cd LoadTests
npm run smoke            # 5 req/s por 30s (valida o script)
npm run load             # sobe a 200 req/s, mantém 5 min
npm run stress           # 200 -> 2000 req/s até degradar
npm run spike            # pico súbito para 2000 req/s
npm run soak             # 150 req/s por 30 min
```
Sem npm: `k6 run -e PROFILE=load scenarios/orders.js`.

Variáveis: `BASE_URL` (Generator), `ACCUMULATOR_URL`, `MAX_VUS` (padrão 2000).

Os comandos `npm run` e `k6 run` partem de `LoadTests/`.

```bash
k6 run -e PROFILE=stress -e BASE_URL=http://host:5029 -e MAX_VUS=5000 scenarios/orders.js
k6 run --out json=resultado.json -e PROFILE=load scenarios/orders.js
```

### Métricas
| Métrica | Significado |
|---|---|
| `http_req_duration{symbol,side}` | latência por combinação |
| `order_accepted` | taxa de ordens aceitas (`Accepted`) |
| `order_outcome{outcome,symbol,side}` | contagem por resultado (Accepted/Rejected/Timeout/SessionUnavailable) |
| `dropped_iterations` | k6 não conseguiu sustentar a taxa → saturação |

### Observações
- Cada decisão é gravada em disco com flush (`decisions.jsonl`), então o disco entra na latência e no teto de vazão do Accumulator. Apague o arquivo entre execuções para não carregar exposição de testes anteriores.
- Quantidade 1–500 e preço 1–100 mantêm a exposição bem abaixo do limite de R$ 100 mi; compra/venda aleatórias a mantêm próxima de zero. Assim, rejeições por limite não mascaram o gargalo real.
- `Timeout`/`SessionUnavailable` indicam saturação da sessão FIX; observe também `GET :5131/api/state` (auditoria) ao fim — o `teardown` imprime o estado final.
- Se `dropped_iterations` aparecer ou o k6 reportar falta de VUs, aumente `MAX_VUS` e o limite de arquivos (`ulimit -n 65535`).
- Gere carga de outra máquina para stress alto; k6 local concorre por CPU com os apps.

### Rejeitadas e duplicadas
```bash
# Zere o estado do Accumulator: pare-o, apague OrderAccumulator/fix-data/decisions.jsonl
# (a exposição agora persiste entre reinícios) e suba-o de novo. Depois rode:
npm run rejections
```
Dispara 300 compras de R$ 99.989.000,01 por símbolo em paralelo (100 VUs). Esperado: no máximo 1 aceita por símbolo, o resto `Rejected`; exposição ≤ R$ 100 mi, auditoria consistente e rejeições sem alterar a exposição.

**Duplicadas:** o `ClOrdID` é um GUID gerado pelo Generator, então não dá para reenviar o mesmo ID pela API HTTP. A idempotência fica no FIX (Accumulator `:5001`) e é coberta em `OrderAccumulator.Tests` (unitários e `FixLoopbackIntegrationTests`, que reenvia o `ClOrdID` pelo protocolo).

## Regras

- **Símbolos:** `PETR4`, `VALE3`, `VIIA4`.
- **Lado:** compra ou venda.
- **Quantidade:** inteiro positivo `< 100.000`.
- **Preço:** `> 0`, múltiplo de `0,01` e `< 1.000`.
- **Exposição por símbolo:** `Σ compras (preço × qtd) − Σ vendas (preço × qtd)`.
- **Limite:** `|exposição| ≤ R$ 100.000.000`.
  - Aceita → `ExecutionReport` com `ExecType=New`; a ordem entra na exposição.
  - Rejeitada → `ExecType=Rejected`; a exposição não muda.
- **Idempotência:** o mesmo `ClOrdID` da mesma contraparte não é contabilizado duas vezes; reenvio idêntico repete a decisão, e reenvio com conteúdo diferente é rejeitado (`OrdRejReason=DuplicateOrder`).
- **Falhas:** se a ordem não puder ser processada ou persistida, o Accumulator responde `BusinessMessageReject` (nunca confirma a ordem).

## Telas

- **Generator:** formulário com validação, selo de saúde do Accumulator (envio bloqueado se offline), resultado da ordem e histórico da sessão.
- **Accumulator:** atualização em tempo real (SSE) com KPIs, exposição e uso do limite por símbolo, auditoria que recalcula a exposição a partir das ordens aceitas e tabela das últimas ordens.

## Endpoints

| App | Endpoint | Descrição |
|---|---|---|
| Generator | `POST /api/orders` | Envia uma ordem e devolve o resultado |
| Generator | `GET /api/status` | Estado da sessão FIX com o Accumulator |
| Accumulator | `GET /api/state` | Snapshot da exposição e das ordens |
| Accumulator | `GET /api/stream` | Stream SSE do estado |
| Ambos | `GET /health` | `fixLoggedOn` e sessões ativas |

```bash
curl -X POST localhost:5029/api/orders -H 'Content-Type: application/json' \
  -d '{"symbol":"PETR4","side":"Buy","quantity":100,"price":10.50}'
```

## Configuração

- **Sessões FIX:** `OrderAccumulator/Configuration/fix/acceptor.cfg` e `OrderGenerator/Configuration/fix/initiator.cfg` (CompIDs, host e porta; o dicionário `FIX44.xml` fica ao lado).
- **Arquivo `.cfg` por ambiente:** opção `Fix:ConfigFile` em `appsettings.{Ambiente}.json`. Development já aponta para `*.Development.cfg`.
- **Persistência das decisões:** opção `Persistence:DecisionsFile` (padrão `fix-data/decisions.jsonl`).
- **Sequências FIX:** persistem em `fix-data/` (ignorado pelo git).
  - **Development** (perfil `http`): `ResetOnLogon=Y`, então divergência entre os stores não trava o logon.
  - **Demais ambientes:** sem reset automático. Um erro `MsgSeqNum too low` exige apagar o `fix-data/` dos **dois** lados.

## Estrutura

```text
OrderGenerator/          Web (wwwroot, Program.cs) · Application · Domain · Infrastructure/Fix
OrderAccumulator/        Web (wwwroot, Program.cs) · Application · Domain · Infrastructure/Fix
OrderGenerator.Tests/    Validação de entrada, correlação de ExecutionReport e envio
OrderAccumulator.Tests/  Exposição, idempotência, persistência, diário, mapeamento FIX → ExecutionReport
                         e integração acceptor/initiator em loopback
LoadTests/               Cenários k6 (carga, stress, spike, soak, rejeições)
Flowa.slnx               Solution (os quatro projetos .NET)
```

O QuickFIX/n fica restrito a `Infrastructure/Fix`; Domain e Application não dependem dele.

## Estilos

O CSS das telas (`wwwroot/css/site.css`) é versionado e gerado com Tailwind. Só precisa regenerar se as classes dos `index.html` mudarem (requer Node):

```bash
npx tailwindcss@3.4.17 --content OrderGenerator/wwwroot/index.html   -o OrderGenerator/wwwroot/css/site.css   --minify
npx tailwindcss@3.4.17 --content OrderAccumulator/wwwroot/index.html -o OrderAccumulator/wwwroot/css/site.css --minify
```

## Limitações

- **Persistência:** cada decisão é gravada em `fix-data/decisions.jsonl` (append-only, flush por ordem) via `IDecisionStore`; ao iniciar, exposição, deduplicação e diário são reconstruídos. Mantenha `fix-data/` num volume persistente: apagar só o store FIX ou só o `decisions.jsonl` deixa os dois fora de sincronia. Para múltiplas instâncias seria preciso um backend compartilhado (outra implementação de `IDecisionStore`).
- Sem autenticação nem banco relacional (fora do escopo do desafio).
