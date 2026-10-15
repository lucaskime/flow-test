using System.Globalization;
using OrderAccumulator.Application;
using OrderAccumulator.Domain;
using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;
using DomainSymbol = OrderAccumulator.Domain.Symbol;
using Message = QuickFix.Message;

namespace OrderAccumulator.Infrastructure.Fix;

/// <summary>
/// Ponte entre o QuickFIX/n e a aplicação. NewOrderSingle é traduzido para <see cref="OrderRequest"/>
/// e a decisão de negócio volta como ExecutionReport. Tipos sem handler geram BusinessMessageReject
/// pelo próprio QuickFIX/n; mensagens estruturalmente inválidas são tratadas pelo Data Dictionary.
/// </summary>
internal sealed class AcceptorApplication(
    FixSessionMonitor monitor,
    IOrderAcceptanceService acceptance,
    OrderJournal journal,
    ILogger<AcceptorApplication> logger) : MessageCracker, IApplication
{
    public void OnCreate(SessionID sessionID) =>
        logger.LogInformation("FIX session created {SessionId}", sessionID);

    public void OnLogon(SessionID sessionID)
    {
        monitor.MarkLoggedOn(sessionID);
        logger.LogInformation("FIX logon {SessionId}", sessionID);
    }

    public void OnLogout(SessionID sessionID)
    {
        monitor.MarkLoggedOut(sessionID);
        logger.LogInformation("FIX logout {SessionId}", sessionID);
    }

    public void ToAdmin(Message message, SessionID sessionID) { }

    public void FromAdmin(Message message, SessionID sessionID) { }

    public void ToApp(Message message, SessionID sessionID) { }

    public void FromApp(Message message, SessionID sessionID) => Crack(message, sessionID);

    // Descoberto por convenção pelo MessageCracker.
    public void OnMessage(NewOrderSingle message, SessionID sessionID)
    {
        Message response;
        try
        {
            response = Process(message, sessionID);
        }
        catch (Exception ex)
        {
            // Campo obrigatório ausente, falha ao persistir etc.: nunca deixar o remetente sem resposta
            // e nunca confirmar uma ordem cujo efeito não foi garantido.
            logger.LogError(ex, "Failed to process NewOrderSingle");
            response = BuildBusinessReject(message, ex);
        }

        Session.SendToTarget(response, sessionID);
    }

    internal static BusinessMessageReject BuildBusinessReject(Message source, Exception error)
    {
        var reject = new BusinessMessageReject(
            new RefMsgType(MsgType.NEWORDERSINGLE),
            new BusinessRejectReason(error is FieldNotFoundException or FieldConvertError
                ? BusinessRejectReason.CONDITIONALLY_REQUIRED_FIELD_MISSING
                : BusinessRejectReason.APPLICATION_NOT_AVAILABLE));
        reject.Set(new Text(error is FieldNotFoundException or FieldConvertError
            ? error.Message
            : "Order could not be processed"));

        if (source.IsSetField(Tags.ClOrdID))
        {
            reject.Set(new BusinessRejectRefID(source.GetString(Tags.ClOrdID)));
        }

        if (source.Header.IsSetField(Tags.MsgSeqNum))
        {
            reject.Set(new RefSeqNum((ulong)source.Header.GetInt(Tags.MsgSeqNum)));
        }

        return reject;
    }

    /// <summary>Decide e registra a ordem; devolve o ExecutionReport a enviar (separado do envio para facilitar testes).</summary>
    internal ExecutionReport Process(NewOrderSingle message, SessionID sessionID)
    {
        var clOrdId = message.ClOrdID.Value;
        var symbolText = message.Symbol.Value;
        var quantity = message.OrderQty.Value;
        var price = message.Price.Value;

        OrderDecision decision;
        if (!TryMap(message, out var order))
        {
            decision = new OrderDecision(false, OrderRejectReason.InvalidOrder, "Unsupported symbol, side or quantity");
            order = new OrderRequest(clOrdId, default, default, 0, price);
        }
        else
        {
            decision = acceptance.Evaluate(sessionID.TargetCompID, order);
        }

        journal.Record(new OrderJournalEntry(
            DateTimeOffset.UtcNow, sessionID.TargetCompID, clOrdId, symbolText,
            message.Side.Value == Side.BUY ? "Buy" : message.Side.Value == Side.SELL ? "Sell" : "?",
            quantity, price, quantity * price,
            decision.Accepted ? "Accepted" : "Rejected", decision.RejectReason?.ToString(), decision.Text,
            decision.IsDuplicate, decision.ExposureAfter));

        logger.LogInformation(
            "Order {ClOrdId} {Symbol} {Side} {Quantity}@{Price} -> {Decision} {Reason}",
            clOrdId, symbolText, message.Side.Value, quantity, price.ToString(CultureInfo.InvariantCulture),
            decision.Accepted ? "New" : "Rejected", decision.RejectReason);

        return BuildExecutionReport(message, decision);
    }

    private static bool TryMap(NewOrderSingle message, out OrderRequest order)
    {
        order = default!;
        var quantity = message.OrderQty.Value;

        if (!Enum.TryParse<DomainSymbol>(message.Symbol.Value, ignoreCase: false, out var symbol)
            || !Enum.IsDefined(symbol)
            || quantity != decimal.Truncate(quantity)
            || quantity is <= 0 or > int.MaxValue)
        {
            return false;
        }

        OrderSide side;
        switch (message.Side.Value)
        {
            case Side.BUY: side = OrderSide.Buy; break;
            case Side.SELL: side = OrderSide.Sell; break;
            default: return false;
        }

        order = new OrderRequest(message.ClOrdID.Value, symbol, side, (int)quantity, message.Price.Value);
        return true;
    }

    private static ExecutionReport BuildExecutionReport(NewOrderSingle source, OrderDecision decision)
    {
        var accepted = decision.Accepted;
        var execType = accepted ? ExecType.NEW : ExecType.REJECTED;
        var ordStatus = accepted ? OrdStatus.NEW : OrdStatus.REJECTED;
        var quantity = source.OrderQty.Value;

        var report = new ExecutionReport(
            new OrderID(accepted ? Guid.NewGuid().ToString("N") : "NONE"),
            new ExecID(Guid.NewGuid().ToString("N")),
            new ExecType(execType),
            new OrdStatus(ordStatus),
            source.Symbol,
            source.Side,
            new LeavesQty(accepted ? quantity : 0m),
            new CumQty(0m),
            new AvgPx(0m));

        report.Set(source.ClOrdID);
        report.Set(source.OrderQty);
        report.Set(source.Price);

        if (!accepted)
        {
            report.Set(new OrdRejReason(decision.RejectReason switch
            {
                OrderRejectReason.ExposureLimitExceeded => OrdRejReason.ORDER_EXCEEDS_LIMIT,
                OrderRejectReason.DuplicateOrder => OrdRejReason.DUPLICATE_ORDER,
                _ => OrdRejReason.OTHER
            }));
            report.Set(new Text(decision.Text ?? "Rejected"));
        }

        return report;
    }
}
